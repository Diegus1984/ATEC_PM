using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Cartellino» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── CARTELLINO MENSILE ────────────────────────────────────────────────────

    public HrMonthlyTimesheetDto GetMonthlyTimesheet(int employeeId, int year, int month)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var dipendente = c.QueryFirstOrDefault<(string Name, string? EcosCode, bool MustPunch, decimal DailyHours)>(
            @"SELECT CONCAT_WS(' ', first_name, last_name) AS Name, ecos_empl_code AS EcosCode,
                     hr_must_punch AS MustPunch, hr_daily_hours AS DailyHours
              FROM employees WHERE id = @Id", new { Id = employeeId });

        // 🪤 Ogni colonna vuole il suo alias: Dapper NON abbina `work_date` a `WorkDate`
        // (`MatchNamesWithUnderscores` qui non è attivo). Senza alias la data resta a
        // DateTime.MinValue su OGNI riga, e il `ToDictionary` qui sotto muore al secondo
        // giorno con «An item with the same key has already been added: 01/01/0001» —
        // cioè il cartellino risponde 500 appena una persona ha due giornate.
        var giornate = c.Query<DayRow>(
                @"SELECT work_date AS WorkDate,
                         clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1,
                         clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                         regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                         break_minutes AS BreakMinutes, bands_json AS BandsJson,
                         note AS Note, has_anomaly AS HasAnomaly
                  FROM hr_days
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(g => g.WorkDate.Date);

        var timbrature = c.Query<PunchRow>(
                @"SELECT t.id AS Id, t.work_date AS WorkDate, t.punched_at AS PunchedAt,
                         t.direction AS Direction, t.source AS Source, t.reason AS Reason,
                         t.external_id AS ExternalId, t.ecos_punched_at AS EcosPunchedAt, t.ecos_sent_at AS EcosSentAt,
                         CONCAT_WS(' ', e.first_name, e.last_name) AS CreatedBy
                  FROM hr_punches t
                  LEFT JOIN employees e ON e.id = t.created_by
                  WHERE t.employee_id = @Id AND t.work_date BETWEEN @Da AND @A
                  ORDER BY t.punched_at",
                // 🪤 Un giorno in più da ogni parte: serve a riconoscere il turno di notte
                // a cavallo del 1° e dell'ultimo del mese. Il ciclo resta sulle sole
                // giornate del mese, quindi i due giorni in più non finiscono a video.
                new { Id = employeeId, Da = primo.AddDays(-1), A = ultimo.AddDays(1) })
            .GroupBy(t => t.WorkDate.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Il registro degli invii a Ecos del mese (poche righe): sta nella giornata.
        Dictionary<DateTime, List<HrEcosSendDto>> inviiEcos = InviiEcosPerGiorno(c, employeeId, primo, ultimo);

        // Assenze approvate del mese
        var assenze = c.Query<HrAbsenceDto>(
                @"SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                         a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType, a.status AS Status
                  FROM hr_absences a
                  WHERE a.employee_id = @Id AND a.status = 'APPROVED'
                    AND a.date_from <= @A AND a.date_to >= @Da",
                new { Id = employeeId, Da = primo, A = ultimo }).ToList();

        var assenzeGiorno = new Dictionary<DateTime, HrAbsenceDto>();
        foreach (var a in assenze)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                assenzeGiorno[dt.Date] = a;
        }

        // Dove Ecos ha spezzato la richiesta giorno per giorno (hr_absence_days) vincono le sue
        // ore: «PERMIT (0.75h)» è il tratto vero del giorno, non la durata dell'intera richiesta.
        foreach (var (chiave, g) in AssenzeEcosPerGiorno(c, primo, ultimo, anchePending: false, employeeId))
        {
            assenzeGiorno[chiave.WorkDate] = new HrAbsenceDto
            {
                EmployeeId = employeeId,
                DateFrom = chiave.WorkDate,
                DateTo = chiave.WorkDate,
                Hours = OreAssenzaGiorno(g, dipendente.DailyHours),
                IsFullDay = GiornataIntera(g, dipendente.DailyHours),
                AbsenceType = g.AbsenceType,
                Status = g.Status,
                Source = "ECOS",
            };
        }

        // Solleciti già chiesti nel mese: il tooltip del pulsante 📧 dice QUANDO (come
        // GetLastMailSent nell'originale), non solo che è già stato mandato.
        var solleciti = c.Query<(DateTime WorkDate, DateTime SentAt)>(
                @"SELECT work_date AS WorkDate, sent_at AS SentAt
                  FROM hr_reminders
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(x => x.WorkDate.Date, x => x.SentAt);

        // Le entrate anticipate già decise nel mese: senza riga vale il no.
        var anticipi = c.Query<(DateTime WorkDate, bool Authorized)>(
                @"SELECT work_date AS WorkDate, authorized AS Authorized
                  FROM hr_early_entries
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(x => x.WorkDate.Date, x => x.Authorized);

        DateTime oggi = DateTime.Today;

        var dto = new HrMonthlyTimesheetDto
        {
            EmployeeId = employeeId,
            EmployeeName = dipendente.Name ?? "",
            Year = year,
            Month = month,
            EcosLinked = !string.IsNullOrWhiteSpace(dipendente.EcosCode),
        };

        var profilo = new ProfiloCartellino(dipendente.MustPunch, dipendente.DailyHours);
        for (DateTime work_date = primo; work_date <= ultimo; work_date = work_date.AddDays(1))
        {
            dto.Days.Add(CostruisciGiornata(
                work_date, oggi, profilo, giornate, timbrature, assenzeGiorno, inviiEcos, solleciti, anticipi));
        }

        return dto;
    }

    /// <summary>Quanto della persona serve a comporre una giornata: chi non timbra ha il forfait sui giorni passati.</summary>
    internal sealed record ProfiloCartellino(bool MustPunch, decimal DailyHours);

    /// <summary>
    /// Una giornata come la legge la pagina: il risultato del motore (<c>hr_days</c>), oppure
    /// l'assenza approvata, oppure il forfait di chi non timbra; sotto, le timbrature grezze coi
    /// due stadi ricalcolati al volo, il registro degli invii a Ecos e il sollecito già mandato.
    /// La usano il cartellino mensile di una persona e il «Controllo di ieri» di tutti: la regola
    /// è una sola, e le due pagine non possono dire due cose diverse dello stesso giorno.
    /// I dizionari sono quelli della persona, per giorno.
    /// </summary>
    private static HrDayDto CostruisciGiornata(
        DateTime work_date,
        DateTime oggi,
        ProfiloCartellino dipendente,
        IReadOnlyDictionary<DateTime, DayRow> giornate,
        IReadOnlyDictionary<DateTime, List<PunchRow>> timbrature,
        IReadOnlyDictionary<DateTime, HrAbsenceDto> assenzeGiorno,
        IReadOnlyDictionary<DateTime, List<HrEcosSendDto>> inviiEcos,
        IReadOnlyDictionary<DateTime, DateTime> solleciti,
        IReadOnlyDictionary<DateTime, bool> anticipiDecisi)
    {
        bool isHoliday = TimesheetRules.IsHoliday(work_date);
        var riga = new HrDayDto
        {
            WorkDate = work_date,
            IsHoliday = isHoliday,
        };

        if (giornate.TryGetValue(work_date, out DayRow? g))
        {
            bool nonCalcolabile = g.Note.StartsWith("⚠ ERR");
            riga.HasData = true;
            riga.ClockIn1 = g.ClockIn1 ?? "";
            riga.ClockOut1 = g.ClockOut1 ?? "";
            riga.ClockIn2 = g.ClockIn2 ?? "";
            riga.ClockOut2 = g.ClockOut2 ?? "";
            riga.RegularHours = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.RegularMinutes);
            riga.Overtime = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.OvertimeMinutes);
            riga.BreakTime = TimesheetRules.FormatDuration(g.BreakMinutes);
            riga.Bands = LeggiFasce(g.BandsJson);
            riga.Note = g.Note;
            riga.HasAnomaly = g.HasAnomaly;
        }
        else if (assenzeGiorno.TryGetValue(work_date, out HrAbsenceDto? abs))
        {
            riga.HasData = true;
            riga.RegularHours = "0h 0m";
            riga.Overtime = "0h 0m";
            riga.BreakTime = "0h 0m";
            riga.Note = abs.IsFullDay ? abs.AbsenceType : $"{abs.AbsenceType} ({abs.Hours}h)";
        }
        else if (!dipendente.MustPunch && !isHoliday && work_date < oggi)
        {
            // Forfait su giorno passato
            riga.HasData = true;
            int minutiForfait = (int)(dipendente.DailyHours * 60m);
            riga.RegularHours = TimesheetRules.FormatDuration(minutiForfait);
            riga.Overtime = "0h 0m";
            riga.BreakTime = "0h 0m";
            riga.Note = "FORFAIT";
        }

        if (inviiEcos.TryGetValue(work_date, out List<HrEcosSendDto>? invii))
            riga.EcosSends = invii;

        if (timbrature.TryGetValue(work_date, out List<PunchRow>? grezze))
        {
            riga.Punches = grezze.Select(PunchDto).ToList();

            // Grezzo e normalizzato non stanno su hr_days — là c'è il risultato — ma
            // si ottengono ripassando le timbrature nel motore, che è puro: nessuna
            // scrittura, e per un mese sono trentun giornate.
            // La configurazione della persona qui non serve: incide sullo straordinario,
            // e i due stadi sono solo orari e somme.
            TimesheetDay stadi = TimesheetEngine.Calcola(
                work_date,
                grezze.Select(t => new RawPunch(t.PunchedAt, t.Direction, null)),
                oggi,
                null,
                ContestoNotte(timbrature, work_date));

            riga.Raw = new HrDayStageDto
            {
                ClockIn1 = stadi.RawEntrata1,
                ClockOut1 = stadi.RawUscita1,
                ClockIn2 = stadi.RawEntrata2,
                ClockOut2 = stadi.RawUscita2,
                BreakTime = stadi.RawBreak,
                TotalHours = stadi.RawTotal,
            };
            // L'entrata prima delle 8: quanto anticipo e se qualcuno l'ha già deciso. Si guarda
            // l'orario ARROTONDATO, che è quello su cui vale la regola (Diego, 10/09/2026).
            riga.EarlyEntryMinutes = MinutiPrimaDelleOtto(stadi.NormEntrata1, stadi.Note);
            if (anticipiDecisi.TryGetValue(work_date, out bool deciso)) riga.EarlyEntryAuthorized = deciso;

            riga.Normalized = new HrDayStageDto
            {
                ClockIn1 = stadi.NormEntrata1,
                ClockOut1 = stadi.NormUscita1,
                ClockIn2 = stadi.NormEntrata2,
                ClockOut2 = stadi.NormUscita2,
                BreakTime = stadi.NormBreak,
                TotalHours = stadi.NormTotal,
            };
        }

        // La pausa dedotta dal motore che su Ecos non c'è: la stessa regola di «Allinea Ecos»
        // (TimbratureDedotte), così il pulsante sulla riga e il resoconto dicono la stessa cosa.
        if (giornate.TryGetValue(work_date, out DayRow? calcolata) && PausaDedotta(calcolata.Note))
        {
            IEnumerable<(string Direction, DateTime Arrotondata)> esistenti = timbrature.TryGetValue(work_date, out List<PunchRow>? del)
                ? del.Select(t => (t.Direction, OraArrotondata(t.PunchedAt, t.Direction)))
                : Enumerable.Empty<(string, DateTime)>();
            riga.EcosBreakToInsert = TimbratureDedotte(
                work_date, calcolata.Note, calcolata.ClockOut1, calcolata.ClockIn2, esistenti).Count > 0;
        }

        // La timbratura che manca (l'uscita): HR la scrive nel dettaglio, «Scrivi su Ecos» la inserisce.
        riga.EcosMissingToInsert = VersoMancante(riga.Note);

        // Le ore del contratto: una giornata più corta non è «tutto regolare».
        riga.ShortMinutes = MinutiMancanti(riga, dipendente, giornate.ContainsKey(work_date),
            assenzeGiorno.TryGetValue(work_date, out HrAbsenceDto? assenzaDelGiorno) ? assenzaDelGiorno : null);

        // La regola sta in un posto solo (HrDayReminder): la usano il pulsante 📧 sulla
        // riga e il filtro «📧 Da segnalare», che così non possono divergere.
        riga.CanRemind = HrDayReminder.Serve(riga.Note, work_date, oggi);
        if (solleciti.TryGetValue(work_date.Date, out DateTime quando))
            riga.LastReminderAt = quando;

        return riga;
    }

    /// <summary>
    /// Quanti minuti mancano alle ore previste dal contratto (0 = giornata piena). Le ore
    /// previste sono quelle dell'anagrafica (<c>employees.hr_daily_hours</c>): chi ha otto ore
    /// non può farne sette e mezza e passare per «tutto regolare» (Diego, 10/09/2026, sulle
    /// giornate di Maracich e Saffioti del 09/09).
    ///
    /// <para>Vale solo dove la persona ha davvero lavorato, cioè dove il motore ha prodotto
    /// una giornata. Restano fuori: chi non timbra (forfait), i giorni senza timbrature
    /// (riposo, festivi e assenze hanno già la loro parola), le giornate già rosse — un
    /// secondo avviso non aiuta chi deve sistemare un buco — e quella ancora in corso.</para>
    ///
    /// <para>Le ore coperte da un permesso o da una ferie di mezza giornata contano come
    /// fatte: chi lavora quattro ore e ne ha quattro di permesso ha fatto la sua giornata.
    /// Lo straordinario invece non copre nulla, perché è la <b>parte ordinaria</b> a dover
    /// arrivare alle ore del contratto.</para>
    /// </summary>
    /// <summary>
    /// Di quanti minuti l'entrata arrotondata sta prima delle 8 (0 = nessun anticipo). Fuori
    /// dal conto i turni di notte e chi comincia prima delle 5: là «prima delle 8» non vuol
    /// dire arrivare in anticipo, sono altri turni. Stessa soglia del motore.
    /// </summary>
    private static int MinutiPrimaDelleOtto(string? entrataArrotondata, string? nota)
    {
        if (nota is not null && nota.Contains(NightShift.NoteMarker, StringComparison.Ordinal)) return 0;
        if (!TimeSpan.TryParse(entrataArrotondata, CultureInfo.InvariantCulture, out TimeSpan ora)) return 0;

        int minuti = (int)ora.TotalMinutes;
        return minuti >= TimesheetRules.EarlyEntryEarliestMinutes && minuti < TimesheetRules.StandardStartMinutes
            ? TimesheetRules.StandardStartMinutes - minuti
            : 0;
    }

    private static int MinutiMancanti(
        HrDayDto riga, ProfiloCartellino dipendente, bool calcolataDalMotore, HrAbsenceDto? assenza)
    {
        if (!dipendente.MustPunch || !calcolataDalMotore || riga.HasAnomaly) return 0;
        if (riga.Note.StartsWith("Giornata in corso", StringComparison.Ordinal)) return 0;

        int previsti = (int)(dipendente.DailyHours * 60m);
        if (previsti <= 0) return 0;

        int coperti = TimesheetRules.MinutesFromDuration(riga.RegularHours);
        if (assenza is not null)
            coperti += assenza.IsFullDay ? previsti : (int)Math.Round((assenza.Hours ?? 0m) * 60m);

        return Math.Max(0, previsti - coperti);
    }

    // ── ENTRATA PRIMA DELLE 8 (Diego, 10/09/2026) ─────────────────────────────

    /// <summary>
    /// Decide se l'entrata anticipata di una giornata vale l'orario timbrato o parte dalle 8,
    /// e rifà subito il conto della giornata. Diego: «c'è gente che arriva, timbra alle 7:30 e
    /// si fa mezz'ora di straordinario non autorizzato tutti i giorni».
    /// </summary>
    /// <returns>Il motivo del rifiuto, o null se è andata.</returns>
    public string? SetEarlyEntry(int employeeId, DateTime workDate, bool authorized, int autoreId)
    {
        if (employeeId <= 0) return "Dipendente non indicato.";
        if (workDate.Date > DateTime.Today) return "Giornata futura: non c'è ancora niente da autorizzare.";

        using MySqlConnection c = _db.Open();
        if (c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id AND work_date = @Giorno",
                new { Id = employeeId, Giorno = workDate.Date }) == 0)
            return "Questa giornata non ha timbrature.";

        c.Execute(@"
            INSERT INTO hr_early_entries (employee_id, work_date, authorized, decided_by)
            VALUES (@Id, @Giorno, @Ok, @Autore)
            ON DUPLICATE KEY UPDATE authorized = VALUES(authorized), decided_by = VALUES(decided_by),
                                    decided_at = NOW()",
            new { Id = employeeId, Giorno = workDate.Date, Ok = authorized, Autore = autoreId });

        _logger.LogInformation(
            "[HR] Entrata anticipata del {Giorno:yyyy-MM-dd} di {Dip}: {Esito} da {Autore}.",
            workDate, employeeId, authorized ? "AUTORIZZATA" : "non autorizzata", autoreId);

        RicalcolaConVicine(c, employeeId, workDate.Date);
        return null;
    }

    // ── SOLLECITO DELLA SINGOLA GIORNATA (voce 1 del port) ────────────────────

    /// <summary>
    /// Il sollecito pronto per una giornata: destinatario, oggetto e corpo integrale, più lo
    /// stato («già chiesto il …»). Il testo lo compone il server, come per quello mensile: la
    /// pagina lo mostra e basta.
    /// </summary>
    /// <param name="firma">
    /// Nome del mittente da mettere in fondo (vuoto = solo la riga dell'ufficio).
    /// </param>
    public HrDayReminderDto GetDayReminder(int employeeId, DateTime date, string firma)
    {
        DateTime giorno = date.Date;
        HrMonthlyTimesheetDto mese = GetMonthlyTimesheet(employeeId, giorno.Year, giorno.Month);
        HrDayDto? giornata = mese.Days.FirstOrDefault(g => g.WorkDate.Date == giorno);

        var dto = new HrDayReminderDto
        {
            EmployeeId = employeeId,
            EmployeeName = mese.EmployeeName,
            Date = giorno,
        };

        if (giornata == null)
        {
            dto.Blocco = "Giornata fuori dal mese richiesto.";
            return dto;
        }

        using MySqlConnection c = _db.Open();

        // Il saluto usa il nome di battesimo dalla colonna, come fa il sollecito mensile:
        // ricavarlo tagliando il nome completo al primo spazio sbaglierebbe su «Maria Grazia».
        var recapito = c.QueryFirstOrDefault<(string? Email, string FirstName)>(
            "SELECT email AS Email, COALESCE(first_name, '') AS FirstName FROM employees WHERE id = @Id",
            new { Id = employeeId });
        dto.Email = recapito.Email;
        string saluto = string.IsNullOrWhiteSpace(recapito.FirstName)
            ? mese.EmployeeName
            : recapito.FirstName;

        dto.CanRemind = giornata.CanRemind;
        dto.LastReminderAt = giornata.LastReminderAt;
        dto.Subject = HrDayReminder.Oggetto(giorno);
        dto.Body = HrDayReminder.Corpo(saluto, giorno, giornata, firma);

        if (!giornata.CanRemind)
        {
            dto.Blocco = giorno == DateTime.Today
                ? "La giornata di oggi non si sollecita: è ancora aperta."
                : "Questa giornata non ha anomalie da segnalare.";
        }
        else if (string.IsNullOrWhiteSpace(dto.Email))
        {
            // Stesso messaggio dell'originale («Nessuna email configurata per …»).
            dto.Blocco = $"Nessuna email configurata per {mese.EmployeeName}.";
        }

        return dto;
    }

    /// <summary>
    /// Segna la giornata come sollecitata, conservando anche il testo (M117): è la riga che
    /// la Cronologia Email rilegge. Un secondo sollecito sulla stessa giornata aggiorna.
    /// </summary>
    public void MarkDayReminder(
        int employeeId, DateTime date, string? email, string subject, string body,
        int sentBy, string channel)
    {
        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel, email, subject, body)
            VALUES (@EmployeeId, @WorkDate, @SentBy, @Channel, @Email, @Subject, @Body)
            ON DUPLICATE KEY UPDATE sent_at = NOW(), sent_by = VALUES(sent_by), channel = VALUES(channel),
                                    email = VALUES(email), subject = VALUES(subject), body = VALUES(body)",
            new
            {
                EmployeeId = employeeId,
                WorkDate = date.Date,
                SentBy = sentBy,
                Channel = channel,
                Email = email,
                Subject = subject,
                Body = body,
            });
    }

    // ── CRONOLOGIA EMAIL (voce 6 del port) ───────────────────────────────────

    /// <summary>
    /// Le mail di sollecito di un mese. 🪤 Il mese è quello del <b>giorno di riferimento</b>
    /// (<c>work_date</c>), non della spedizione: come nell'originale, una mail mandata a
    /// settembre per un buco di agosto si cerca sotto agosto.
    /// </summary>
    public HrReminderLogDto GetReminderLog(int year, int month, int? employeeId)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        string filtro = employeeId.HasValue ? " AND r.employee_id = @EmployeeId" : "";

        using MySqlConnection c = _db.Open();
        List<HrReminderLogRowDto> righe = c.Query<HrReminderLogRowDto>(
            @"SELECT r.id AS Id, r.sent_at AS SentAt, r.employee_id AS EmployeeId,
                     CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                     r.email AS Email, r.work_date AS WorkDate, r.subject AS Subject,
                     r.body AS Body, r.channel AS Channel,
                     CONCAT_WS(' ', a.first_name, a.last_name) AS SentByName
              FROM hr_reminders r
              JOIN employees e ON e.id = r.employee_id
              LEFT JOIN employees a ON a.id = r.sent_by
              WHERE r.work_date BETWEEN @Primo AND @Ultimo" + filtro + @"
              ORDER BY r.sent_at DESC, r.work_date DESC",
            new { Primo = primo, Ultimo = ultimo, EmployeeId = employeeId }).ToList();

        return new HrReminderLogDto { Year = year, Month = month, Rows = righe };
    }
}
