using System.Globalization;
using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

// Parte «richieste verso Ecos» di HrAttendanceService (#151, 09/09/2026): dall'08/09 pomeriggio
// l'utente api.it può scrivere le richieste di assenza (PeopleAbsenceRequestPost, manuale §9.9).
//
//  • Decisioni: Approva / Rifiuta / Annulla su una richiesta che vive su Ecos (nata là, o nata
//    qui e già mandata) vanno PRIMA su Ecos e poi qui. Se Ecos dice di no, qui non cambia
//    niente: al prossimo import vincerebbe comunque Ecos.
//  • Nascite: una richiesta nuova inserita qui nasce anche su Ecos come REQUEST (il
//    responsabile la vede là come qui); una giustificazione dal cartellino nasce già ACCEPTED
//    (la mette chi ha la scrittura). L'id di Ecos si salva subito (mai ritentare alla cieca);
//    se Ecos non risponde la richiesta resta valida qui, con un avviso e una nota.
//  • CategoryID SEMPRE da AnagTSCategoryGetAll per CategoryCode (F ferie, P ROL, M malattia,
//    I1 infortunio, F_ND assenza): gli id non si cablano.
public partial class HrAttendanceService
{
    /// <summary>Il <c>CategoryCode</c> di Ecos per i nostri tipi di assenza.</summary>
    internal static string CategoriaEcos(string? absenceType) => (absenceType ?? "").Trim().ToUpperInvariant() switch
    {
        "VACATION" => "F",
        "PERMIT" => "P",
        "SICKNESS" => "M",
        "INJURY" => "I1",
        _ => "F_ND",
    };

    internal sealed class RichiestaLocale
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
        public string? EcosId { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal? Hours { get; set; }
        public bool IsFullDay { get; set; }
        public TimeSpan? HourFrom { get; set; }
        public TimeSpan? HourTo { get; set; }
        public string AbsenceType { get; set; } = "";
        public string? Notes { get; set; }
        public int? EmplId { get; set; }
        public string EmployeeName { get; set; } = "";
    }

    private static RichiestaLocale? CaricaRichiesta(MySqlConnection c, int absenceId) =>
        c.QueryFirstOrDefault<RichiestaLocale>(@"
            SELECT a.id AS Id, a.employee_id AS EmployeeId, a.status AS Status, a.source AS Source,
                   a.ecos_absence_id AS EcosId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.hour_from AS HourFrom, a.hour_to AS HourTo,
                   a.absence_type AS AbsenceType, a.notes AS Notes, e.ecos_empl_id AS EmplId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName
            FROM hr_absences a
            JOIN employees e ON e.id = a.employee_id
            WHERE a.id = @Id",
            new { Id = absenceId });

    // ── DECISIONI ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Approva o rifiuta: se la richiesta vive su Ecos, prima là (<c>ACCEPTED</c>/<c>REJECT</c>,
    /// motivo in <c>ApproveReply</c>), poi qui. I controlli locali (stato, permessi) si fanno
    /// PRIMA di scrivere su Ecos, così un «non sei responsabile» non lascia Ecos avanti.
    /// </summary>
    public async Task<string?> ApproveAbsenceRequestAsync(
        int absenceId, bool approved, string? rejectionReason, int approverId, bool isManagerOrAdmin,
        CancellationToken ct = default)
    {
        string? ecosId;
        using (MySqlConnection c = _db.Open())
        {
            RichiestaLocale? r = CaricaRichiesta(c, absenceId);
            if (r == null) return "Richiesta non trovata.";
            string? errore = ControlloApprovazione(c, r.Status, r.EmployeeId, approverId, isManagerOrAdmin);
            if (errore != null) return errore;
            ecosId = r.EcosId;
        }

        if (!string.IsNullOrEmpty(ecosId))
        {
            string? erroreEcos = await ScriviStatoSuEcosAsync(
                ecosId, approved ? "ACCEPTED" : "REJECT", approved ? null : rejectionReason, ct);
            if (erroreEcos != null) return erroreEcos;
        }

        return ApproveAbsenceRequest(absenceId, approved, rejectionReason, approverId, isManagerOrAdmin, ecosAllineato: true);
    }

    /// <summary>Annulla: se vive su Ecos, prima la cancellazione logica là (<c>Delete=1</c>), poi qui.</summary>
    public async Task<string?> CancelAbsenceRequestAsync(
        int absenceId, int currentUserId, bool isAdmin, CancellationToken ct = default)
    {
        string? ecosId;
        using (MySqlConnection c = _db.Open())
        {
            RichiestaLocale? r = CaricaRichiesta(c, absenceId);
            if (r == null) return "Richiesta non trovata.";
            string? errore = ControlloAnnullamento(r.Status, r.EmployeeId, currentUserId, isAdmin, CreatoDa(c, absenceId));
            if (errore != null) return errore;
            ecosId = r.EcosId;
        }

        if (!string.IsNullOrEmpty(ecosId))
        {
            string? erroreEcos = await CancellaSuEcosAsync(ecosId, ct);
            if (erroreEcos != null) return erroreEcos;
        }

        return CancelAbsenceRequest(absenceId, currentUserId, isAdmin, ecosAllineato: true);
    }

    private static int? CreatoDa(MySqlConnection c, int absenceId) =>
        c.ExecuteScalar<int?>("SELECT created_by FROM hr_absences WHERE id = @Id", new { Id = absenceId });

    private async Task<string?> ScriviStatoSuEcosAsync(string ecosId, string stato, string? motivo, CancellationToken ct)
    {
        if (!_ecos.Configured)
            return "Credenziali Ecos non configurate: la richiesta vive su Ecos e la decisione non può partire.";
        try
        {
            string token = await _ecos.TokenAsync(ct);
            await _ecos.SetAbsenceRequestStatusAsync(token, ecosId, stato, motivo, ct);
            _logger.LogInformation("[HR] Richiesta Ecos {Id}: stato {Stato} scritto su Ecos.", ecosId, stato);
            return null;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] Richiesta Ecos {Id}: stato {Stato} NON scritto: {Msg}", ecosId, stato, ex.Message);
            return $"Ecos non ha accettato la decisione: {ex.Message}";
        }
    }

    private async Task<string?> CancellaSuEcosAsync(string ecosId, CancellationToken ct)
    {
        if (!_ecos.Configured)
            return "Credenziali Ecos non configurate: la richiesta vive su Ecos e non si può annullare da qui.";
        try
        {
            string token = await _ecos.TokenAsync(ct);
            await _ecos.DeleteAbsenceRequestAsync(token, ecosId, ct);
            _logger.LogInformation("[HR] Richiesta Ecos {Id}: cancellata su Ecos.", ecosId);
            return null;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] Richiesta Ecos {Id}: NON cancellata: {Msg}", ecosId, ex.Message);
            return $"Ecos non ha accettato l'annullamento: {ex.Message}";
        }
    }

    // ── NASCITE ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Nuova richiesta da ATEC PM: qui subito, poi su Ecos come <c>REQUEST</c>. L'esito di Ecos
    /// è un <b>avviso</b>, non un errore: la richiesta è valida anche se Ecos non risponde.
    /// </summary>
    public async Task<(int? Id, string? Error, string? Avviso)> CreateAbsenceRequestAsync(
        HrCreateAbsenceRequest req, int currentUserId, bool isManagerOrAdmin, CancellationToken ct = default)
    {
        (int? id, string? error) = CreateAbsenceRequest(req, currentUserId, isManagerOrAdmin);
        if (error != null || id == null) return (id, error, null);
        string? avviso = await InviaRichiestaSuEcosAsync(id.Value, "REQUEST", ct);
        return (id, null, avviso);
    }

    /// <summary>
    /// Giustificazione dal cartellino: qui come sempre, poi su Ecos già <c>ACCEPTED</c>. Se la
    /// giornata aveva già una nostra richiesta su Ecos (causale cambiata o tolta), quella là si
    /// cancella prima: su Ecos ne resta una sola, quella nuova.
    /// </summary>
    public async Task<(string? Error, string? Avviso)> SaveGiustificaAsync(
        HrGiustificaRequest req, int autoreId, CancellationToken ct = default)
    {
        DateTime giorno = req.Date.Date;
        bool rimozione = string.IsNullOrWhiteSpace(req.Causale);

        List<string> vecchieSuEcos;
        using (MySqlConnection c = _db.Open())
        {
            vecchieSuEcos = c.Query<string>(@"
                SELECT ecos_absence_id FROM hr_absences
                WHERE employee_id = @Id AND date_from = @G AND date_to = @G
                  AND source <> 'ECOS' AND ecos_absence_id IS NOT NULL",
                new { Id = req.EmployeeId, G = giorno }).ToList();
        }

        string? errore = SaveGiustifica(req, autoreId);
        if (errore != null) return (errore, null);

        var avvisi = new List<string>();
        foreach (string vecchia in vecchieSuEcos)
        {
            string? err = await CancellaSuEcosAsync(vecchia, ct);
            if (err != null) avvisi.Add(err);
        }

        int? absenceId;
        using (MySqlConnection c = _db.Open())
        {
            if (vecchieSuEcos.Count > 0)
            {
                // La riga riscritta da SaveGiustifica porta ancora l'id vecchio: quella richiesta su
                // Ecos non c'è più, se ne fa una nuova.
                c.Execute(@"UPDATE hr_absences SET ecos_absence_id = NULL
                            WHERE employee_id = @Id AND date_from = @G AND date_to = @G AND source <> 'ECOS'",
                    new { Id = req.EmployeeId, G = giorno });
            }
            absenceId = rimozione ? null : c.ExecuteScalar<int?>(@"
                SELECT id FROM hr_absences
                WHERE employee_id = @Id AND date_from = @G AND date_to = @G AND source = 'MANUAL'
                ORDER BY id DESC LIMIT 1",
                new { Id = req.EmployeeId, G = giorno });
        }

        if (absenceId is int nuova)
        {
            string? avviso = await InviaRichiestaSuEcosAsync(nuova, "ACCEPTED", ct);
            if (avviso != null) avvisi.Add(avviso);
        }

        return (null, avvisi.Count > 0 ? string.Join(" ", avvisi) : null);
    }

    /// <summary>
    /// Manda su Ecos una richiesta nostra che là non c'è ancora. Torna un avviso (mai un
    /// errore): se non parte, la richiesta resta valida qui con una nota che dice perché.
    /// </summary>
    internal async Task<string?> InviaRichiestaSuEcosAsync(int absenceId, string statusCode, CancellationToken ct)
    {
        using MySqlConnection c = _db.Open();
        RichiestaLocale? r = CaricaRichiesta(c, absenceId);
        if (r == null || !string.IsNullOrEmpty(r.EcosId)) return null;
        if (!_ecos.Configured) return "Ecos non configurato: la richiesta resta solo qui.";
        if (r.EmplId is not int emplId)
            return "La persona non ha ancora l'EmplID di Ecos: la richiesta resta solo qui.";

        TimeSpan? inizio = null, fine = null;
        if (!r.IsFullDay)
        {
            (inizio, fine) = FasciaOraria(c, r);
            if (inizio == null || fine == null)
                return "Manca la fascia oraria: la richiesta resta solo qui.";
            if (r.HourFrom == null || r.HourTo == null)
                c.Execute("UPDATE hr_absences SET hour_from = @Da, hour_to = @A WHERE id = @Id",
                    new { Da = inizio, A = fine, Id = r.Id });
        }

        try
        {
            string token = await _ecos.TokenAsync(ct);
            Dictionary<string, int> categorie = await _ecos.AbsenceCategoriesAsync(token, ct);
            string codice = CategoriaEcos(r.AbsenceType);
            if (!categorie.TryGetValue(codice, out int categoryId))
                return $"Su Ecos non c'è la causale {codice}: la richiesta resta solo qui.";

            EcosAbsenceRequestInserted ins = await _ecos.InsertAbsenceRequestAsync(
                token, emplId, r.DateFrom, r.DateTo, r.IsFullDay, inizio, fine, categoryId, statusCode, NotaPerEcos(r), ct);
            c.Execute("UPDATE hr_absences SET ecos_absence_id = @E WHERE id = @Id", new { E = ins.AbsenceRequestId, Id = r.Id });
            _logger.LogInformation(
                "[HR] Richiesta {Id} di {Chi} mandata su Ecos come {Stato}: AbsenceRequestID {Ecos}.",
                r.Id, r.EmployeeName, statusCode, ins.AbsenceRequestId);
            return null;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] Richiesta {Id} NON mandata su Ecos: {Msg}", r.Id, ex.Message);
            string nota = $"Ecos: non inviata ({ex.Message})";
            c.Execute("UPDATE hr_absences SET notes = CONCAT_WS(' - ', NULLIF(notes, ''), @N) WHERE id = @Id",
                new { N = nota.Length > 250 ? nota[..250] : nota, Id = r.Id });
            return $"Richiesta registrata qui, ma non su Ecos: {ex.Message}";
        }
    }

    private static string NotaPerEcos(RichiestaLocale r)
    {
        string nota = string.IsNullOrWhiteSpace(r.Notes) ? "ATEC PM" : $"ATEC PM: {r.Notes.Trim()}";
        return nota.Length > 200 ? nota[..200] : nota;
    }

    private static readonly TimeSpan InizioGiornata = new(8, 0, 0);

    /// <summary>
    /// La fascia oraria di una richiesta a ore: quella scritta, altrimenti ricavata dalle
    /// timbrature della giornata (è il caso della giustificazione dal cartellino, che conosce
    /// solo le ore mancanti). Se la mattina è libera abbastanza, il buco sta prima della prima
    /// timbratura; altrimenti dopo l'ultima; senza timbrature, dalle 08:00.
    /// </summary>
    internal static (TimeSpan? Inizio, TimeSpan? Fine) FasciaOraria(MySqlConnection c, RichiestaLocale r)
    {
        if (r.HourFrom is { } da && r.HourTo is { } a && a > da) return (da, a);
        if (r.Hours is not > 0m) return (null, null);
        var timbrature = c.Query<(DateTime PunchedAt, string Direction)>(@"
            SELECT punched_at, direction FROM hr_punches
            WHERE employee_id = @Id AND work_date = @G ORDER BY punched_at",
            new { Id = r.EmployeeId, G = r.DateFrom.Date }).ToList();
        return FasciaDaTimbrature(r.Hours.Value, timbrature);
    }

    internal static (TimeSpan? Inizio, TimeSpan? Fine) FasciaDaTimbrature(
        decimal ore, IReadOnlyList<(DateTime PunchedAt, string Direction)> timbrature)
    {
        var durata = TimeSpan.FromMinutes((double)Math.Round(ore * 60m));
        if (durata <= TimeSpan.Zero) return (null, null);

        TimeSpan inizio;
        if (timbrature.Count == 0)
        {
            inizio = InizioGiornata;
        }
        else
        {
            TimeSpan prima = OraArrotondata(timbrature[0].PunchedAt, timbrature[0].Direction).TimeOfDay;
            TimeSpan ultima = OraArrotondata(timbrature[^1].PunchedAt, timbrature[^1].Direction).TimeOfDay;
            inizio = prima - InizioGiornata >= durata ? prima - durata : ultima;
        }

        TimeSpan fine = inizio + durata;
        if (inizio < TimeSpan.Zero || fine > new TimeSpan(23, 59, 0)) return (null, null);
        return (inizio, fine);
    }

    /// <summary>«08:30» da un TimeSpan, per i DTO.</summary>
    internal static string? OraBreve(TimeSpan? t) =>
        t is { } v ? v.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : null;
}
