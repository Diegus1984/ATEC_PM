using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Invia a Ecos» di HrAttendanceService (08/09/2026, ordine di Diego): gli orari
// ARROTONDATI tornano su Ecos con PeopleStampPost Edit=true (manuale Ecos §4.2 e §9.3).
//
// Le tre regole che reggono tutto:
//  1. L'ora timbrata non sparisce mai: hr_punches.punched_at resta quella; quello che Ecos ha
//     per colpa nostra sta in ecos_punched_at, e ogni invio finisce in hr_ecos_sends con ora
//     originale, ora inviata, esito e autore. Ecos non tiene la storia: la teniamo noi.
//  2. Si manda solo ciò che differisce: una timbratura già allineata non parte. È un update
//     (Edit=true + StampID): rimandarlo è innocuo, a differenza degli inserimenti.
//  3. L'import riconosce l'eco: quando Ecos rimanda l'orario che gli abbiamo scritto noi,
//     non è una modifica (ImportPunches). Se invece su Ecos l'orario è cambiato per mano
//     d'altri, vince Ecos: punched_at si aggiorna e l'invio decade.
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

        private static DateTime AlMinuto(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
    }

    /// <summary>Lo stesso arrotondamento del motore (<c>TimesheetEngine.Norm</c>): una regola sola.</summary>
    internal static DateTime OraArrotondata(DateTime punchedAt, string direction) =>
        TimesheetRules.RoundTime(punchedAt, NightShift.IsEntry(direction));

    private sealed class PunchEcosRow
    {
        public long Id { get; set; }
        public string ExternalId { get; set; } = "";
        public string Direction { get; set; } = "";
        public DateTime PunchedAt { get; set; }
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

    /// <summary>
    /// Il pulsante «Invia a Ecos» di una giornata: per ogni timbratura di Ecos il cui orario
    /// arrotondato differisce da quello che Ecos ha, una <c>PeopleStampPost Edit=true</c>.
    /// Al primo errore ci si ferma: il registro dice fin dove si è arrivati e il pulsante si
    /// può ripremere (sono update, non inserimenti). L'esito è sempre un dato, anche quando
    /// è un fallimento: i contatori servono a video.
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
        List<TimbraturaEcos> daInviare = tutte.Where(t => t.DaInviare).ToList();
        esito.Total = tutte.Count;
        esito.Skipped = tutte.Count(t => !t.Inviabile);

        if (daInviare.Count == 0)
        {
            esito.Success = true;
            esito.Message = tutte.Count == 0
                ? "Nessuna timbratura di Ecos in questa giornata: niente da inviare."
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

        foreach (TimbraturaEcos t in daInviare)
        {
            string outcome;
            string? messaggio;
            try
            {
                messaggio = await _ecos.UpdateStampTimeAsync(token, t.StampId, t.Arrotondata, ct);
                outcome = "OK";
                c.Execute(
                    "UPDATE hr_punches SET ecos_punched_at = @Ora, ecos_sent_at = NOW() WHERE id = @Id",
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
                    t.PunchId,
                    t.StampId,
                    t.Direction,
                    t.PunchedAt,
                    SentTime = t.Arrotondata,
                    PreviousTime = t.OraSuEcos,
                    Outcome = outcome,
                    Message = messaggio is { Length: > 500 } lungo ? lungo[..500] : messaggio,
                    SentBy = autoreId,
                });

            // 🪤 Al primo errore ci si ferma: se è il token o la rete, insistere fa solo altre
            // righe rosse nel registro. Gli update sono innocui da ripetere: si ripreme il pulsante.
            if (outcome == "ERROR") break;
        }

        esito.Success = esito.Failed == 0;
        int nonTentate = daInviare.Count - esito.Sent - esito.Failed;
        esito.Message = esito.Success
            ? $"Inviate a Ecos {esito.Sent} timbrature con l'orario arrotondato"
              + (esito.Skipped > 0 ? $" ({esito.Skipped} non inviabili: l'arrotondamento cambia giorno)" : "") + "."
            : $"Inviate {esito.Sent}, una fallita"
              + (nonTentate > 0 ? $", {nonTentate} non tentate" : "") + ": " + esito.Errors[0];

        _logger.LogInformation(
            "[HR] Invio a Ecos: dipendente {Dip}, {Giorno:yyyy-MM-dd}, autore {Autore}: {Msg}",
            employeeId, workDate, autoreId, esito.Message);
        return esito;
    }

    private static string Verso(string direction) => NightShift.IsEntry(direction) ? "Entrata" : "Uscita";

    private sealed class InvioRow
    {
        public long Id { get; set; }
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
            lista.Add(new HrEcosSendDto
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
            });
        }
        return perGiorno;
    }

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
        bool diEcos = string.Equals(t.Source, "ECOS", StringComparison.OrdinalIgnoreCase)
                      && !string.IsNullOrWhiteSpace(t.ExternalId);
        if (diEcos)
        {
            var te = new TimbraturaEcos(t.Id, t.ExternalId!, t.Direction, t.PunchedAt, t.EcosPunchedAt, t.EcosSentAt);
            dto.RoundedAt = te.Arrotondata;
            dto.CanSendToEcos = te.Inviabile;
            dto.ToSendToEcos = te.DaInviare;
        }
        return dto;
    }
}
