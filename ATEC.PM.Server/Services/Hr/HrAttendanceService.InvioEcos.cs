using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Invia a Ecos» di HrAttendanceService (08/09/2026, ordine di Diego): gli orari
// ARROTONDATI tornano su Ecos con PeopleStampPost (manuale Ecos §4 e §9.3).
//  • Le timbrature di Ecos si MODIFICANO (Edit=true + StampID): update parziale, innocuo da
//    ripetere.
//  • Le rettifiche (timbrature aggiunte qui, che su Ecos non esistono) si INSERISCONO
//    (senza Edit): la persona si identifica col BadgeCode attivo — l'EmplID nel corpo viene
//    ignorato e nasce un record senza persona, invisibile (provato l'08/09). Non idempotente:
//    un timeout dopo la scrittura ha già creato il record, e la lettura di massa NON lo
//    mostra subito. Quindi: esito incerto = si marca e non si ritenta alla cieca.
//
// Le tre regole che reggono tutto:
//  1. Ecos è la bibbia (Diego, 09/09/2026 sera): dopo una scrittura riuscita hr_punches è lo
//     SPECCHIO di Ecos — punched_at prende l'orario scritto, e la giornata si ricalcola. L'ora
//     timbrata in origine non sparisce ma sta nel registro hr_ecos_sends (ora originale, ora
//     inviata, esito e autore): Ecos non tiene la storia, la teniamo noi lì, non nella griglia.
//     (Fino al 09/09 pomeriggio punched_at restava quello timbrato e il dettaglio mostrava
//     07:48 anche a Ecos allineato: «chissene frega di come sono arrivate».)
//  2. Si manda solo ciò che differisce: una timbratura già allineata non parte.
//  3. L'import riconosce l'eco: quando Ecos rimanda l'orario che gli abbiamo scritto noi,
//     non è una modifica (ImportPunches). Se invece su Ecos l'orario è cambiato per mano
//     d'altri, vince Ecos: punched_at si aggiorna e l'invio decade. Una rettifica inserita con
//     esito incerto viene ADOTTATA dall'import se Ecos poi la restituisce con quell'orario.
public partial class HrAttendanceService
{
    /// <summary>Una timbratura di Ecos vista dal pulsante: com'è, com'è su Ecos, come dovrebbe essere.</summary>
    internal sealed record TimbraturaEcos(
        long PunchId, string StampId, string Direction, DateTime PunchedAt, DateTime? EcosPunchedAt, DateTime? EcosSentAt)
    {
        /// <summary>L'orario arrotondato dal motore (scatto 30', tolleranza 10': entrata su, uscita giù).</summary>
        public DateTime Arrotondata => OraArrotondata(PunchedAt, Direction);

        /// <summary>Quello che Ecos ha adesso, per quanto ne sappiamo: l'ultimo inviato, altrimenti il timbrato.</summary>
        public DateTime OraSuEcos => EcosPunchedAt ?? PunchedAt;

        /// <summary>
        /// Si può mandare solo se l'arrotondamento resta nello stesso giorno: un'entrata alle
        /// 23:55 arrotonda a mezzanotte del giorno dopo, e su Ecos la timbratura cambierebbe
        /// cartellino. Quelle restano da guardare a mano.
        /// </summary>
        public bool Inviabile => Arrotondata.Date == PunchedAt.Date;

        /// <summary>
        /// Si confronta al minuto: una timbratura alle 08:00:52 è già sullo scatto, e mandare
        /// 08:00:00 sarebbe una riga «08:00 diventa 08:00» nel registro (vista l'08/09/2026 alla
        /// prima prova in produzione). I secondi non contano né qui né su Ecos.
        /// </summary>
        public bool DaInviare => Inviabile && Arrotondata != AlMinuto(OraSuEcos);
    }

    /// <summary>
    /// Una rettifica (timbratura aggiunta qui con motivo e autore) che su Ecos non esiste
    /// ancora: si inserisce con l'orario arrotondato. Dopo l'inserimento la riga diventa una
    /// timbratura di Ecos a tutti gli effetti (source ECOS + StampID), motivo e autore restano.
    /// </summary>
    internal sealed record RettificaEcos(
        long PunchId, string Direction, DateTime PunchedAt, string? Reason, string? CreatedBy,
        DateTime? EcosPunchedAt, DateTime? EcosSentAt)
    {
        public DateTime Arrotondata => OraArrotondata(PunchedAt, Direction);
        public bool Inviabile => Arrotondata.Date == PunchedAt.Date;

        /// <summary>
        /// Un tentativo senza risposta certa (timeout dopo la scrittura): Ecos può averla creata
        /// e la lettura di massa non la mostra subito. Non si rimanda alla cieca: o l'import
        /// la adotta quando Ecos la restituisce, oppure si toglie la rettifica e la si rifà.
        /// </summary>
        public bool Incerta => EcosSentAt != null;

        public bool DaInviare => Inviabile && !Incerta;

        /// <summary>La nota che va su Ecos: dice che viene da qui, con motivo e autore.</summary>
        public string Nota
        {
            get
            {
                string motivo = string.IsNullOrWhiteSpace(Reason) ? "rettifica" : Reason.Trim();
                string chi = string.IsNullOrWhiteSpace(CreatedBy) ? "" : $" ({CreatedBy.Trim()})";
                string nota = $"ATEC PM: {motivo}{chi}";
                return nota.Length > 200 ? nota[..200] : nota;
            }
        }
    }

    /// <summary>
    /// Una timbratura della pausa pranzo che il motore ha DEDOTTO e che su Ecos non esiste
    /// (09/09/2026, Diego: «se l'ora di pausa non esiste, la devo inserire»): con «Allinea Ecos»
    /// si inserisce come timbratura vera, così Ecos ha la stessa giornata che abbiamo calcolato
    /// noi e al prossimo import la pausa è timbrata, non più dedotta.
    /// </summary>
    internal sealed record TimbraturaDedotta(string Direction, DateTime Quando)
    {
        public const string Motivo = "Pausa pranzo dedotta dal motore, inserita su Ecos";
        public string Nota => "ATEC PM: pausa pranzo dedotta dal motore";
    }

    /// <summary>
    /// Le sole note del motore in cui la pausa è dedotta E NON timbrata. 🪤 «Pausa 1h forzata»
    /// resta fuori: lì le quattro timbrature ci sono (pausa troppo corta) e inserirne altre due
    /// farebbe sei strisciate e una giornata «da verificare».
    /// </summary>
    internal static bool PausaDedotta(string? nota) =>
        nota != null
        && (nota.StartsWith("AUTO_P: Pausa 1h detratta", StringComparison.Ordinal)
            || nota.StartsWith("AUTO_P: Pausa implicita (1 IN / 2 OUT)", StringComparison.Ordinal));

    /// <summary>
    /// Le timbrature della pausa da inserire su Ecos: gli orari con l'asterisco della giornata
    /// calcolata (uscita per la pausa, rientro) che nessuna timbratura esistente — di Ecos o
    /// rettifica — copre già allo stesso minuto e nello stesso verso. Nel caso «1 IN / 2 OUT»
    /// l'uscita con l'asterisco è una timbratura vera e resta coperta: parte solo il rientro.
    /// </summary>
    internal static List<TimbraturaDedotta> TimbratureDedotte(
        DateTime workDate, string? nota, string? clockOut1, string? clockIn2,
        IEnumerable<(string Direction, DateTime Arrotondata)> esistenti)
    {
        var esito = new List<TimbraturaDedotta>();
        if (!PausaDedotta(nota)) return esito;
        List<(string Direction, DateTime Arrotondata)> coperte = esistenti.ToList();

        foreach ((string verso, string? orario) in new[] { ("OUT", clockOut1), ("IN", clockIn2) })
        {
            if (orario is null || !orario.EndsWith('*')) continue;
            // «24:00» non è un'ora di pausa e non si legge: resta fuori da sé.
            if (!TimeSpan.TryParseExact(orario.TrimEnd('*'), "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out TimeSpan ora))
                continue;
            DateTime quando = workDate.Date + ora;
            bool entrata = verso == "IN";
            bool coperta = coperte.Any(e => NightShift.IsEntry(e.Direction) == entrata && AlMinuto(e.Arrotondata) == quando);
            if (!coperta) esito.Add(new TimbraturaDedotta(verso, quando));
        }
        return esito;
    }

    private static DateTime AlMinuto(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);

    /// <summary>Lo stesso arrotondamento del motore (<c>TimesheetEngine.Norm</c>): una regola sola.</summary>
    internal static DateTime OraArrotondata(DateTime punchedAt, string direction) =>
        TimesheetRules.RoundTime(punchedAt, NightShift.IsEntry(direction));

    private sealed class PunchEcosRow
    {
        public long Id { get; set; }
        public string ExternalId { get; set; } = "";
        public string Direction { get; set; } = "";
        public DateTime PunchedAt { get; set; }
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? EcosPunchedAt { get; set; }
        public DateTime? EcosSentAt { get; set; }
    }

    internal static List<TimbraturaEcos> TimbratureEcosDelGiorno(MySqlConnection c, int employeeId, DateTime workDate) =>
        c.Query<PunchEcosRow>(@"
                SELECT id AS Id, external_id AS ExternalId, direction AS Direction, punched_at AS PunchedAt,
                       ecos_punched_at AS EcosPunchedAt, ecos_sent_at AS EcosSentAt
                FROM hr_punches
                WHERE employee_id = @Id AND work_date = @Giorno AND source = 'ECOS' AND external_id IS NOT NULL
                ORDER BY punched_at",
                new { Id = employeeId, Giorno = workDate.Date })
            .Select(r => new TimbraturaEcos(r.Id, r.ExternalId, r.Direction, r.PunchedAt, r.EcosPunchedAt, r.EcosSentAt))
            .ToList();

    internal static List<RettificaEcos> RettificheDelGiorno(MySqlConnection c, int employeeId, DateTime workDate) =>
        c.Query<PunchEcosRow>(@"
                SELECT t.id AS Id, t.direction AS Direction, t.punched_at AS PunchedAt, t.reason AS Reason,
                       CONCAT_WS(' ', e.first_name, e.last_name) AS CreatedBy,
                       t.ecos_punched_at AS EcosPunchedAt, t.ecos_sent_at AS EcosSentAt
                FROM hr_punches t
                LEFT JOIN employees e ON e.id = t.created_by
                WHERE t.employee_id = @Id AND t.work_date = @Giorno AND t.source = 'ADJUSTMENT' AND t.external_id IS NULL
                ORDER BY t.punched_at",
                new { Id = employeeId, Giorno = workDate.Date })
            .Select(r => new RettificaEcos(r.Id, r.Direction, r.PunchedAt, r.Reason, r.CreatedBy, r.EcosPunchedAt, r.EcosSentAt))
            .ToList();

    /// <summary>La giornata calcolata (hr_days) di una persona, o null se non c'è.</summary>
    private static DayRow? GiornataCalcolata(MySqlConnection c, int employeeId, DateTime workDate) =>
        c.QuerySingleOrDefault<DayRow>(@"
            SELECT employee_id AS EmployeeId, work_date AS WorkDate,
                   clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1, clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                   regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes, break_minutes AS BreakMinutes,
                   bands_json AS BandsJson, note AS Note, has_anomaly AS HasAnomaly
            FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno = workDate.Date });

    /// <summary>
    /// Le timbrature della pausa dedotta già tentate senza risposta certa (timeout dopo la
    /// scrittura): nel registro stanno senza timbratura (punch_id NULL) con l'esito incerto.
    /// Non si rimandano alla cieca: se Ecos le ha create, l'import le porta qui da sé.
    /// </summary>
    private static HashSet<(string Direction, DateTime Quando)> PauseIncerte(MySqlConnection c, int employeeId, DateTime workDate) =>
        c.Query<(string Direction, DateTime SentTime)>(@"
                SELECT direction AS Direction, sent_time AS SentTime
                FROM hr_ecos_sends
                WHERE employee_id = @Id AND work_date = @Giorno AND punch_id IS NULL
                  AND outcome = 'ERROR' AND message LIKE 'Esito incerto%'",
                new { Id = employeeId, Giorno = workDate.Date })
            .Select(r => (r.Direction, AlMinuto(r.SentTime)))
            .ToHashSet();

    /// <summary>Le timbrature della pausa dedotta della giornata (non ancora tentate con esito incerto).</summary>
    private static (List<TimbraturaDedotta> DaInserire, int Incerte) PausaDaInserire(
        MySqlConnection c, int employeeId, DateTime workDate, DayRow? giornata,
        IEnumerable<TimbraturaEcos> tutte, IEnumerable<RettificaEcos> rettifiche)
    {
        if (giornata == null) return (new List<TimbraturaDedotta>(), 0);
        List<TimbraturaDedotta> dedotte = TimbratureDedotte(
            workDate, giornata.Note, giornata.ClockOut1, giornata.ClockIn2,
            tutte.Select(t => (t.Direction, t.Arrotondata)).Concat(rettifiche.Select(r => (r.Direction, r.Arrotondata))));
        if (dedotte.Count == 0) return (dedotte, 0);
        HashSet<(string, DateTime)> incerte = PauseIncerte(c, employeeId, workDate);
        int quanteIncerte = dedotte.Count(d => incerte.Contains((d.Direction, d.Quando)));
        // 🪤 Una pausa a metà non si completa alla cieca: se l'uscita delle 12:30 è incerta e si
        // inserisse solo il rientro, Ecos potrebbe restare con tre strisciate e la giornata
        // diventerebbe «uscita mancante». Prima si verifica su Ecos (o si aspetta l'import).
        return quanteIncerte > 0 ? (new List<TimbraturaDedotta>(), quanteIncerte) : (dedotte, 0);
    }

    /// <summary>
    /// Il resoconto di «Allinea Ecos» PRIMA di scrivere: la giornata calcolata e, riga per
    /// riga, le modifiche (orario arrotondato al posto di quello che Ecos ha), gli inserimenti
    /// (rettifiche e pausa dedotta) e ciò che resta fuori con il perché. È la stessa lettura
    /// di <see cref="SendDayToEcosAsync"/>: quello che si conferma è quello che parte.
    /// </summary>
    public HrEcosPlanDto GetEcosPlan(int employeeId, DateTime workDate)
    {
        using MySqlConnection c = _db.Open();
        var dip = c.QuerySingleOrDefault<(string Name, string? EcosCode)>(
            "SELECT CONCAT_WS(' ', first_name, last_name) AS Name, ecos_empl_code AS EcosCode FROM employees WHERE id = @Id",
            new { Id = employeeId });
        DayRow? giornata = GiornataCalcolata(c, employeeId, workDate);
        List<TimbraturaEcos> tutte = TimbratureEcosDelGiorno(c, employeeId, workDate);
        List<RettificaEcos> rettifiche = RettificheDelGiorno(c, employeeId, workDate);
        (List<TimbraturaDedotta> pausa, int pauseIncerte) = PausaDaInserire(c, employeeId, workDate, giornata, tutte, rettifiche);

        var piano = new HrEcosPlanDto
        {
            EmployeeId = employeeId,
            EmployeeName = dip.Name ?? "",
            WorkDate = workDate.Date,
            Configured = _ecos.Configured,
            Note = giornata?.Note ?? "",
            HasAnomaly = giornata?.HasAnomaly ?? false,
            ClockIn1 = giornata?.ClockIn1 ?? "",
            ClockOut1 = giornata?.ClockOut1 ?? "",
            ClockIn2 = giornata?.ClockIn2 ?? "",
            ClockOut2 = giornata?.ClockOut2 ?? "",
            RegularHours = giornata == null ? "" : TimesheetRules.FormatDuration(giornata.RegularMinutes),
            Overtime = giornata == null ? "" : TimesheetRules.FormatDuration(giornata.OvertimeMinutes),
            BreakTime = giornata == null ? "" : TimesheetRules.FormatDuration(giornata.BreakMinutes),
        };

        foreach (TimbraturaEcos t in tutte)
        {
            if (t.DaInviare)
            {
                piano.Operations.Add(new HrEcosPlannedOpDto
                {
                    Kind = "UPDATE", Direction = t.Direction, From = t.OraSuEcos, To = t.Arrotondata,
                    Label = $"{Verso(t.Direction)} {t.OraSuEcos:HH:mm} → {t.Arrotondata:HH:mm}",
                    Detail = "orario arrotondato al posto di quello timbrato",
                });
            }
            else if (!t.Inviabile)
            {
                piano.Operations.Add(new HrEcosPlannedOpDto
                {
                    Kind = "SKIP", Direction = t.Direction, From = t.OraSuEcos, To = t.Arrotondata,
                    Label = $"{Verso(t.Direction)} {t.PunchedAt:HH:mm}",
                    Detail = "non inviabile: l'arrotondamento cambierebbe giorno",
                });
            }
        }
        foreach (RettificaEcos r in rettifiche)
        {
            if (r.Incerta)
            {
                piano.Operations.Add(new HrEcosPlannedOpDto
                {
                    Kind = "UNCERTAIN", Direction = r.Direction, To = r.Arrotondata,
                    Label = $"{Verso(r.Direction)} {r.Arrotondata:HH:mm} (rettifica)",
                    Detail = "già mandata senza risposta certa: verificare su Ecos, non riparte da sola",
                });
            }
            else if (r.DaInviare)
            {
                piano.Operations.Add(new HrEcosPlannedOpDto
                {
                    Kind = "INSERT", Direction = r.Direction, To = r.Arrotondata,
                    Label = $"{Verso(r.Direction)} {r.Arrotondata:HH:mm} (rettifica)",
                    Detail = $"nuova timbratura su Ecos — {r.Nota}",
                });
            }
            else
            {
                piano.Operations.Add(new HrEcosPlannedOpDto
                {
                    Kind = "SKIP", Direction = r.Direction, To = r.Arrotondata,
                    Label = $"{Verso(r.Direction)} {r.PunchedAt:HH:mm} (rettifica)",
                    Detail = "non inviabile: l'arrotondamento cambierebbe giorno",
                });
            }
        }
        foreach (TimbraturaDedotta d in pausa)
        {
            piano.Operations.Add(new HrEcosPlannedOpDto
            {
                Kind = "INSERT_BREAK", Direction = d.Direction, To = d.Quando,
                Label = $"{Verso(d.Direction)} {d.Quando:HH:mm} (pausa)",
                Detail = "nuova timbratura su Ecos — pausa pranzo dedotta dal motore, su Ecos non c'è",
            });
        }
        if (pauseIncerte > 0)
        {
            piano.Operations.Add(new HrEcosPlannedOpDto
            {
                Kind = "UNCERTAIN", Direction = "",
                Label = pauseIncerte == 1 ? "Una timbratura di pausa" : $"{pauseIncerte} timbrature di pausa",
                Detail = "già mandate senza risposta certa: verificare su Ecos, l'import le porta qui se ci sono",
            });
        }

        piano.ToWrite = piano.Operations.Count(o => o.Kind is "UPDATE" or "INSERT" or "INSERT_BREAK");
        if (!piano.Configured)
            piano.Message = "Credenziali Ecos non configurate sul server: impossibile scrivere.";
        else if (giornata == null)
            piano.Message = "Nessuna giornata calcolata: niente da allineare.";
        else if (piano.HasAnomaly)
            piano.Message = "La giornata ha un'anomalia: prima si sistema (rettifica o rilettura da Ecos), poi si allinea Ecos.";
        else if (piano.ToWrite == 0)
            piano.Message = pauseIncerte > 0
                ? "Niente da scrivere: la pausa è già stata mandata senza risposta certa, da verificare su Ecos."
                : "Ecos ha già gli orari di questa giornata: niente da scrivere.";
        piano.CanSend = piano.Configured && giornata != null && !piano.HasAnomaly && piano.ToWrite > 0;
        return piano;
    }

    /// <summary>
    /// Il pulsante «Invia a Ecos» / «Allinea Ecos» di una giornata: prima le modifiche
    /// (timbrature di Ecos con l'orario arrotondato diverso da quello che Ecos ha), poi gli
    /// inserimenti (le rettifiche, e dal 09/09/2026 la pausa dedotta). Al primo errore ci si
    /// ferma: il registro dice fin dove si è arrivati e il pulsante si può ripremere (le
    /// modifiche sono innocue da ripetere; gli inserimenti incerti non ripartono). L'esito è
    /// sempre un dato, anche quando è un fallimento.
    /// </summary>
    public async Task<HrEcosSendResultDto> SendDayToEcosAsync(
        int employeeId, DateTime workDate, int autoreId, CancellationToken ct = default)
    {
        var esito = new HrEcosSendResultDto();
        if (!_ecos.Configured)
        {
            esito.Message = "Credenziali Ecos non configurate: impossibile inviare.";
            return esito;
        }

        using MySqlConnection c = _db.Open();
        List<TimbraturaEcos> tutte = TimbratureEcosDelGiorno(c, employeeId, workDate);
        List<RettificaEcos> rettifiche = RettificheDelGiorno(c, employeeId, workDate);
        List<TimbraturaEcos> daInviare = tutte.Where(t => t.DaInviare).ToList();
        List<RettificaEcos> daInserire = rettifiche.Where(r => r.DaInviare).ToList();
        (List<TimbraturaDedotta> pausaDaInserire, int pauseIncerte) = PausaDaInserire(
            c, employeeId, workDate, GiornataCalcolata(c, employeeId, workDate), tutte, rettifiche);
        esito.Total = tutte.Count + rettifiche.Count;
        esito.Skipped = tutte.Count(t => !t.Inviabile) + rettifiche.Count(r => !r.Inviabile);
        int incerte = rettifiche.Count(r => r.Incerta) + pauseIncerte;

        if (daInviare.Count == 0 && daInserire.Count == 0 && pausaDaInserire.Count == 0)
        {
            esito.Success = true;
            esito.Message = esito.Total == 0
                ? "Nessuna timbratura in questa giornata: niente da inviare."
                : incerte > 0
                    ? $"Niente da inviare: {incerte} timbrature con esito incerto, da verificare su Ecos."
                    : "Ecos ha già gli orari arrotondati di questa giornata: niente da inviare.";
            return esito;
        }

        string token;
        try
        {
            token = await _ecos.TokenAsync(ct);
        }
        catch (EcosApiException ex)
        {
            esito.Message = ex.Message;
            return esito;
        }

        bool fermato = false;
        foreach (TimbraturaEcos t in daInviare)
        {
            string outcome;
            string? messaggio;
            try
            {
                messaggio = await _ecos.UpdateStampTimeAsync(token, t.StampId, t.Arrotondata, ct);
                outcome = "OK";
                // Specchio di Ecos: anche punched_at prende l'orario scritto (l'originale è nel registro).
                c.Execute(
                    "UPDATE hr_punches SET punched_at = @Ora, ecos_punched_at = @Ora, ecos_sent_at = NOW() WHERE id = @Id",
                    new { Ora = t.Arrotondata, Id = t.PunchId });
                esito.Sent++;
            }
            catch (EcosApiException ex)
            {
                outcome = "ERROR";
                messaggio = ex.Message;
                esito.Failed++;
                esito.Errors.Add($"{Verso(t.Direction)} {t.PunchedAt:HH:mm}: {ex.Message}");
                _logger.LogWarning(
                    "[HR] Invio a Ecos fallito: dipendente {Dip}, {Giorno:yyyy-MM-dd}, StampID {Stamp} verso {Ora:HH:mm}: {Msg}",
                    employeeId, workDate, t.StampId, t.Arrotondata, ex.Message);
            }

            RegistraInvio(c, employeeId, workDate, t.PunchId, t.StampId, t.Direction, t.PunchedAt, t.Arrotondata,
                t.OraSuEcos, outcome, messaggio, autoreId);

            // 🪤 Al primo errore ci si ferma: se è il token o la rete, insistere fa solo altre
            // righe rosse nel registro. Le modifiche sono innocue da ripetere: si ripreme il pulsante.
            if (outcome == "ERROR") { fermato = true; break; }
        }

        if (!fermato && daInserire.Count > 0)
            await InserisciRettificheAsync(c, token, employeeId, workDate, daInserire, autoreId, esito, ct);

        // La pausa dedotta parte per ultima e solo se fin qui è andato tutto bene: al primo
        // errore ci si ferma, come per il resto.
        if (!fermato && esito.Failed == 0 && pausaDaInserire.Count > 0)
            await InserisciPausaDedottaAsync(c, token, employeeId, workDate, pausaDaInserire, autoreId, esito, ct);

        esito.Success = esito.Failed == 0;
        int tentate = esito.Sent + esito.Inserted + esito.BreakInserted + esito.Failed;
        int nonTentate = daInviare.Count + daInserire.Count + pausaDaInserire.Count - tentate;
        var pezzi = new List<string>();
        if (esito.Sent > 0) pezzi.Add($"{esito.Sent} orari modificati");
        if (esito.Inserted > 0) pezzi.Add($"{esito.Inserted} timbrature inserite");
        if (esito.BreakInserted > 0) pezzi.Add($"{esito.BreakInserted} timbrature di pausa inserite");
        string fatto = pezzi.Count > 0 ? string.Join(", ", pezzi) : "niente inviato";
        esito.Message = esito.Success
            ? $"Inviato a Ecos: {fatto}"
              + (esito.Skipped > 0 ? $" ({esito.Skipped} non inviabili: l'arrotondamento cambia giorno)" : "") + "."
            : $"Inviato a Ecos: {fatto}; una fallita"
              + (nonTentate > 0 ? $", {nonTentate} non tentate" : "") + ": " + esito.Errors[0];

        _logger.LogInformation(
            "[HR] Invio a Ecos: dipendente {Dip}, {Giorno:yyyy-MM-dd}, autore {Autore}: {Msg}",
            employeeId, workDate, autoreId, esito.Message);

        // Scritto qualcosa? Le timbrature qui sono già lo specchio di Ecos: si ricalcola la
        // giornata (vicine comprese, per le notti) e poi la si rilegge da Ecos (Diego,
        // 09/09/2026: «una volta che ho scritto devi risincronizzare le righe interessate») —
        // gli orari modificati tornano come eco. Le timbrature appena INSERITE Ecos non le
        // restituisce ancora: la rilettura non le tocca (ProtezioneInviiRecenti). Un fallimento
        // della rilettura non cancella la scrittura riuscita: si dice e basta.
        if (esito.Sent + esito.Inserted + esito.BreakInserted > 0)
        {
            RicalcolaConVicine(c, employeeId, workDate);
            HrImportResultDto rilettura = await ImportWindowAsync(employeeId, workDate, workDate, ct);
            esito.Resynced = rilettura.Success;
            esito.Message += rilettura.Success
                ? $" Riletta da Ecos: {rilettura.Message}"
                : $" Rilettura da Ecos non riuscita ({rilettura.Message}): riprovare con «Rileggi da Ecos».";
        }
        return esito;
    }

    /// <summary>
    /// Gli inserimenti: badge attivo della persona, una <c>PeopleStampPost</c> per rettifica,
    /// verifica che Ecos l'abbia attaccata alla persona giusta (altrimenti la si cancella
    /// subito), conversione della riga in timbratura di Ecos. Al primo errore ci si ferma.
    /// </summary>
    /// <summary>
    /// Chi è la persona su Ecos e con quale badge attivo si inserisce: la stessa ricerca per le
    /// rettifiche e per la pausa dedotta. Null (con l'errore già nell'esito) quando manca
    /// l'EmplID o il badge, o quando Ecos non risponde.
    /// </summary>
    private async Task<(int EmplId, string? EmplCode, string Badge)?> PersonaEBadgeAsync(
        MySqlConnection c, string token, int employeeId, HrEcosSendResultDto esito, CancellationToken ct)
    {
        var persona = c.QuerySingleOrDefault<(int? EmplId, string? EmplCode)>(
            "SELECT ecos_empl_id, ecos_empl_code FROM employees WHERE id = @Id", new { Id = employeeId });
        if (persona.EmplId is not int emplId)
        {
            esito.Failed++;
            esito.Errors.Add("La persona non ha ancora l'EmplID di Ecos: l'import lo impara dai badge, riprovare dopo.");
            return null;
        }

        string? badge;
        try
        {
            badge = await _ecos.ActiveBadgeCodeAsync(token, emplId, ct);
        }
        catch (EcosApiException ex)
        {
            esito.Failed++;
            esito.Errors.Add($"Lettura del badge non riuscita: {ex.Message}");
            return null;
        }
        if (string.IsNullOrEmpty(badge))
        {
            esito.Failed++;
            esito.Errors.Add("La persona non ha un badge attivo su Ecos: senza badge la timbratura non si può inserire.");
            return null;
        }
        return (emplId, persona.EmplCode, badge);
    }

    /// <summary>
    /// La pausa dedotta dal motore diventa timbrature vere su Ecos: una <c>PeopleStampPost</c>
    /// per strisciata (uscita, rientro), verifica della persona, e la stessa timbratura nasce
    /// anche qui come timbratura di Ecos (StampID), così la giornata si ricalcola subito con la
    /// pausa timbrata e l'import di poi riconosce l'eco. Esito incerto = riga nel registro senza
    /// timbratura, e non si riprova da soli. Al primo errore ci si ferma.
    /// </summary>
    private async Task InserisciPausaDedottaAsync(
        MySqlConnection c, string token, int employeeId, DateTime workDate, List<TimbraturaDedotta> pausa,
        int autoreId, HrEcosSendResultDto esito, CancellationToken ct)
    {
        (int EmplId, string? EmplCode, string Badge)? chi = await PersonaEBadgeAsync(c, token, employeeId, esito, ct);
        if (chi is not { } persona) return;

        foreach (TimbraturaDedotta d in pausa)
        {
            string outcome = "ERROR";
            string? messaggio = null;
            string stampId = "";
            long? punchId = null;
            try
            {
                EcosStampInserted ins = await _ecos.InsertStampAsync(token, persona.Badge, d.Quando, d.Direction, d.Nota, ct);
                stampId = ins.StampId;
                bool personaGiusta = ins.EmplId == persona.EmplId.ToString()
                    || (!string.IsNullOrEmpty(persona.EmplCode) && ins.EmplCode == persona.EmplCode);
                if (!personaGiusta)
                {
                    try { await _ecos.DeleteStampAsync(token, ins.StampId, ct); }
                    catch (EcosApiException exDel)
                    {
                        _logger.LogWarning("[HR] Timbratura di pausa {Stamp} attaccata alla persona sbagliata e NON cancellabile: {Msg}", ins.StampId, exDel.Message);
                    }
                    messaggio = $"Ecos ha attaccato la timbratura a un'altra persona (EmplID {ins.EmplId}): cancellata subito.";
                    esito.Failed++;
                }
                else
                {
                    c.Execute(@"
                        INSERT INTO hr_punches
                            (employee_id, work_date, punched_at, direction, source, external_id, reason, created_by,
                             ecos_punched_at, ecos_sent_at)
                        VALUES (@Id, @Giorno, @Quando, @Verso, 'ECOS', @Stamp, @Motivo, @Autore, @Quando, NOW())",
                        new
                        {
                            Id = employeeId, Giorno = workDate.Date, Quando = d.Quando, Verso = d.Direction,
                            Stamp = ins.StampId, Motivo = TimbraturaDedotta.Motivo, Autore = autoreId,
                        });
                    punchId = c.ExecuteScalar<long>("SELECT LAST_INSERT_ID()");
                    outcome = "OK";
                    messaggio = "Correct Record Insert (pausa dedotta)";
                    esito.BreakInserted++;
                }
            }
            catch (EcosApiException ex) when (ex.EsitoIncerto)
            {
                // Può essere passata: resta nel registro (senza timbratura) e non si riprova.
                // Se Ecos l'ha creata, l'import la porta qui come timbratura nuova.
                messaggio = $"Esito incerto (nessuna risposta): {ex.Message}. Verificare su Ecos; non si rimanda da sola.";
                esito.Failed++;
            }
            catch (EcosApiException ex)
            {
                messaggio = ex.Message;
                esito.Failed++;
            }

            if (outcome == "ERROR")
            {
                esito.Errors.Add($"{Verso(d.Direction)} {d.Quando:HH:mm} (pausa dedotta): {messaggio}");
                _logger.LogWarning(
                    "[HR] Inserimento della pausa su Ecos fallito: dipendente {Dip}, {Giorno:yyyy-MM-dd}, {Verso} {Ora:HH:mm}: {Msg}",
                    employeeId, workDate, d.Direction, d.Quando, messaggio);
            }

            RegistraInvio(c, employeeId, workDate, punchId, stampId, d.Direction, d.Quando, d.Quando,
                null, outcome, messaggio, autoreId);
            if (outcome == "ERROR") break;
        }

    }

    private async Task InserisciRettificheAsync(
        MySqlConnection c, string token, int employeeId, DateTime workDate, List<RettificaEcos> daInserire,
        int autoreId, HrEcosSendResultDto esito, CancellationToken ct)
    {
        (int EmplId, string? EmplCode, string Badge)? chi = await PersonaEBadgeAsync(c, token, employeeId, esito, ct);
        if (chi is not { } persona) return;
        int emplId = persona.EmplId;
        string badge = persona.Badge;

        foreach (RettificaEcos r in daInserire)
        {
            string outcome = "ERROR";
            string? messaggio = null;
            string stampId = "";
            try
            {
                EcosStampInserted ins = await _ecos.InsertStampAsync(token, badge, r.Arrotondata, r.Direction, r.Nota, ct);
                stampId = ins.StampId;
                bool personaGiusta = ins.EmplId == emplId.ToString()
                    || (!string.IsNullOrEmpty(persona.EmplCode) && ins.EmplCode == persona.EmplCode);
                if (!personaGiusta)
                {
                    // 🪤 Il badge ha portato a un'altra persona: la timbratura non deve restare.
                    try { await _ecos.DeleteStampAsync(token, ins.StampId, ct); }
                    catch (EcosApiException exDel)
                    {
                        _logger.LogWarning("[HR] Timbratura {Stamp} attaccata alla persona sbagliata e NON cancellabile: {Msg}", ins.StampId, exDel.Message);
                    }
                    messaggio = $"Ecos ha attaccato la timbratura a un'altra persona (EmplID {ins.EmplId}): cancellata subito.";
                    esito.Failed++;
                }
                else
                {
                    // Specchio di Ecos: la rettifica diventa la timbratura di Ecos con l'orario
                    // che Ecos ha (quello arrotondato); l'orario battuto a mano resta nel registro.
                    c.Execute(@"
                        UPDATE hr_punches
                        SET source = 'ECOS', external_id = @Stamp, punched_at = @Ora, ecos_punched_at = @Ora, ecos_sent_at = NOW()
                        WHERE id = @Id",
                        new { Stamp = ins.StampId, Ora = r.Arrotondata, Id = r.PunchId });
                    outcome = "OK";
                    messaggio = "Correct Record Insert";
                    esito.Inserted++;
                }
            }
            catch (EcosApiException ex) when (ex.EsitoIncerto)
            {
                // La richiesta può essere passata: si marca e NON si ritenta. L'import adotta la
                // timbratura se Ecos la restituisce con quest'orario (ImportPunches).
                c.Execute(
                    "UPDATE hr_punches SET ecos_punched_at = @Ora, ecos_sent_at = NOW() WHERE id = @Id",
                    new { Ora = r.Arrotondata, Id = r.PunchId });
                messaggio = $"Esito incerto (nessuna risposta): {ex.Message}. Verificare su Ecos; non si rimanda da sola.";
                esito.Failed++;
            }
            catch (EcosApiException ex)
            {
                messaggio = ex.Message;
                esito.Failed++;
            }

            if (outcome == "ERROR")
            {
                esito.Errors.Add($"{Verso(r.Direction)} {r.PunchedAt:HH:mm} (rettifica): {messaggio}");
                _logger.LogWarning(
                    "[HR] Inserimento su Ecos fallito: dipendente {Dip}, {Giorno:yyyy-MM-dd}, {Verso} {Ora:HH:mm}: {Msg}",
                    employeeId, workDate, r.Direction, r.Arrotondata, messaggio);
            }

            RegistraInvio(c, employeeId, workDate, r.PunchId, stampId, r.Direction, r.PunchedAt, r.Arrotondata,
                null, outcome, messaggio, autoreId);
            if (outcome == "ERROR") break;
        }
    }

    /// <summary>
    /// Una riga del registro: <c>previous_time</c> NULL = inserimento, altrimenti modifica;
    /// <c>punch_id</c> NULL = pausa dedotta che non è (ancora) una timbratura qui.
    /// </summary>
    private static void RegistraInvio(
        MySqlConnection c, int employeeId, DateTime workDate, long? punchId, string stampId, string direction,
        DateTime punchedAt, DateTime sentTime, DateTime? previousTime, string outcome, string? messaggio, int autoreId)
    {
        c.Execute(@"
            INSERT INTO hr_ecos_sends
                (employee_id, work_date, punch_id, ecos_stamp_id, direction, punched_at,
                 sent_time, previous_time, outcome, message, sent_by)
            VALUES (@EmployeeId, @WorkDate, @PunchId, @StampId, @Direction, @PunchedAt,
                    @SentTime, @PreviousTime, @Outcome, @Message, @SentBy)",
            new
            {
                EmployeeId = employeeId,
                WorkDate = workDate.Date,
                PunchId = punchId,
                StampId = stampId,
                Direction = direction,
                PunchedAt = punchedAt,
                SentTime = sentTime,
                PreviousTime = previousTime,
                Outcome = outcome,
                Message = messaggio is { Length: > 500 } lungo ? lungo[..500] : messaggio,
                SentBy = autoreId,
            });
    }

    private static string Verso(string direction) => NightShift.IsEntry(direction) ? "Entrata" : "Uscita";

    private sealed class InvioRow
    {
        public long Id { get; set; }
        /// <summary>Letto solo dalla query di tutti (Controllo di ieri); in quella di una persona resta 0.</summary>
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public long? PunchId { get; set; }
        public string EcosStampId { get; set; } = "";
        public string Direction { get; set; } = "";
        public DateTime PunchedAt { get; set; }
        public DateTime SentTime { get; set; }
        public DateTime? PreviousTime { get; set; }
        public string Outcome { get; set; } = "";
        public string? Message { get; set; }
        public string? SentBy { get; set; }
        public DateTime SentAt { get; set; }
    }

    /// <summary>Il registro degli invii di un intervallo, per giornata: va nel cartellino del mese.</summary>
    internal static Dictionary<DateTime, List<HrEcosSendDto>> InviiEcosPerGiorno(
        MySqlConnection c, int employeeId, DateTime da, DateTime a)
    {
        var perGiorno = new Dictionary<DateTime, List<HrEcosSendDto>>();
        foreach (InvioRow r in c.Query<InvioRow>(@"
            SELECT s.id AS Id, s.work_date AS WorkDate, s.punch_id AS PunchId, s.ecos_stamp_id AS EcosStampId,
                   s.direction AS Direction, s.punched_at AS PunchedAt, s.sent_time AS SentTime,
                   s.previous_time AS PreviousTime, s.outcome AS Outcome, s.message AS Message,
                   s.sent_at AS SentAt, CONCAT_WS(' ', e.first_name, e.last_name) AS SentBy
            FROM hr_ecos_sends s
            LEFT JOIN employees e ON e.id = s.sent_by
            WHERE s.employee_id = @Id AND s.work_date BETWEEN @Da AND @A
            ORDER BY s.sent_at DESC, s.id DESC",
            new { Id = employeeId, Da = da, A = a }))
        {
            if (!perGiorno.TryGetValue(r.WorkDate.Date, out List<HrEcosSendDto>? lista))
                perGiorno[r.WorkDate.Date] = lista = new List<HrEcosSendDto>();
            lista.Add(InvioDto(r));
        }
        return perGiorno;
    }

    /// <summary>
    /// Lo stesso registro per <b>tutti</b> i dipendenti, per persona e per giornata: lo usa il
    /// «Controllo di ieri», che compone le giornate di tutti con le stesse letture del cartellino.
    /// </summary>
    internal static Dictionary<int, Dictionary<DateTime, List<HrEcosSendDto>>> InviiEcosPerDipendenteEGiorno(
        MySqlConnection c, DateTime da, DateTime a)
    {
        var perDipendente = new Dictionary<int, Dictionary<DateTime, List<HrEcosSendDto>>>();
        foreach (InvioRow r in c.Query<InvioRow>(@"
            SELECT s.id AS Id, s.employee_id AS EmployeeId, s.work_date AS WorkDate, s.punch_id AS PunchId,
                   s.ecos_stamp_id AS EcosStampId, s.direction AS Direction, s.punched_at AS PunchedAt,
                   s.sent_time AS SentTime, s.previous_time AS PreviousTime, s.outcome AS Outcome,
                   s.message AS Message, s.sent_at AS SentAt,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS SentBy
            FROM hr_ecos_sends s
            LEFT JOIN employees e ON e.id = s.sent_by
            WHERE s.work_date BETWEEN @Da AND @A
            ORDER BY s.sent_at DESC, s.id DESC",
            new { Da = da, A = a }))
        {
            if (!perDipendente.TryGetValue(r.EmployeeId, out Dictionary<DateTime, List<HrEcosSendDto>>? perGiorno))
                perDipendente[r.EmployeeId] = perGiorno = new Dictionary<DateTime, List<HrEcosSendDto>>();
            if (!perGiorno.TryGetValue(r.WorkDate.Date, out List<HrEcosSendDto>? lista))
                perGiorno[r.WorkDate.Date] = lista = new List<HrEcosSendDto>();
            lista.Add(InvioDto(r));
        }
        return perDipendente;
    }

    private static HrEcosSendDto InvioDto(InvioRow r) => new()
    {
        Id = r.Id,
        PunchId = r.PunchId,
        EcosStampId = r.EcosStampId,
        Direction = r.Direction,
        PunchedAt = r.PunchedAt,
        SentTime = r.SentTime,
        PreviousTime = r.PreviousTime,
        Outcome = r.Outcome,
        Message = r.Message,
        SentBy = string.IsNullOrWhiteSpace(r.SentBy) ? null : r.SentBy,
        SentAt = r.SentAt,
    };

    /// <summary>Il DTO di una timbratura, con quello che serve al pulsante «Invia a Ecos».</summary>
    private static HrPunchDto PunchDto(PunchRow t)
    {
        var dto = new HrPunchDto
        {
            Id = t.Id,
            PunchedAt = t.PunchedAt,
            Direction = t.Direction,
            Source = t.Source,
            Reason = t.Reason,
            CreatedBy = t.CreatedBy,
            EcosStampId = t.ExternalId,
            EcosPunchedAt = t.EcosPunchedAt,
            EcosSentAt = t.EcosSentAt,
        };
        bool conStampId = !string.IsNullOrWhiteSpace(t.ExternalId);
        if (string.Equals(t.Source, "ECOS", StringComparison.OrdinalIgnoreCase) && conStampId)
        {
            var te = new TimbraturaEcos(t.Id, t.ExternalId!, t.Direction, t.PunchedAt, t.EcosPunchedAt, t.EcosSentAt);
            dto.RoundedAt = te.Arrotondata;
            dto.CanSendToEcos = te.Inviabile;
            dto.ToSendToEcos = te.DaInviare;
        }
        else if (string.Equals(t.Source, "ADJUSTMENT", StringComparison.OrdinalIgnoreCase) && !conStampId)
        {
            var r = new RettificaEcos(t.Id, t.Direction, t.PunchedAt, t.Reason, t.CreatedBy, t.EcosPunchedAt, t.EcosSentAt);
            dto.RoundedAt = r.Arrotondata;
            dto.EcosInsert = true;
            dto.EcosUncertain = r.Incerta;
            dto.CanSendToEcos = r.Inviabile && !r.Incerta;
            dto.ToSendToEcos = r.DaInviare;
        }
        return dto;
    }
}
