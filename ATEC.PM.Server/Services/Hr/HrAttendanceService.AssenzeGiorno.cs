using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

// Parte «assenze giorno per giorno» di HrAttendanceService (08/09/2026): lo specchio di
// PeopleAbsenceRequestRefineWorkAll in `hr_absence_days` e la sua lettura aggregata per
// (dipendente, giorno), che calendario, cartellino e quadratura preferiscono alla nostra
// espansione dell'intervallo della richiesta. Manuale: docs/guide/ECOS-API-MANUALE.md §9.7.
public partial class HrAttendanceService
{
    /// <summary>
    /// La finestra che l'import automatico rilegge a ogni giro: indietro quanto basta a
    /// coprire i cartellini ancora aperti, avanti quanto si prenotano le ferie. Fuori dalla
    /// finestra le righe restano come sono (le toglie solo una finestra che le comprende).
    /// </summary>
    internal static readonly TimeSpan AssenzeGiornoIndietro = TimeSpan.FromDays(60);
    internal static readonly TimeSpan AssenzeGiornoAvanti = TimeSpan.FromDays(180);

    /// <summary>Tolleranza sotto le ore di contratto entro cui un giorno di assenza conta come intero.</summary>
    private const int TolleranzaGiornataInteraMinuti = 15;

    /// <summary>Da <c>CategoryCode</c> di Ecos alla nostra causale.</summary>
    internal static string TipoAssenza(string categoryCode) => categoryCode switch
    {
        "F" => "VACATION",
        "P" => "PERMIT",
        "M" or "MA" => "SICKNESS",
        "I" or "IN" => "INJURY",
        _ => "OTHER",
    };

    /// <summary>
    /// Da <c>StatusCode</c> di Ecos (scheda di <c>PeopleAbsenceRequestGetAll</c>: ACCEPTED,
    /// REQUEST, REJECT) al nostro stato. Una riga marcata <c>Delete</c> è CANCELLED.
    /// </summary>
    internal static string StatoRichiesta(string statusCode, bool deleted) => deleted
        ? "CANCELLED"
        : statusCode switch
        {
            "ACCEPTED" => "APPROVED",
            "REJECT" or "REJECTED" => "REJECTED",
            "CANCELLED" => "CANCELLED",
            _ => "PENDING",
        };

    /// <summary>La finestra di date dell'import automatico intorno a <paramref name="oggi"/>.</summary>
    internal static (DateTime Dal, DateTime Al) FinestraAssenzeGiorno(DateTime oggi) =>
        ((oggi - AssenzeGiornoIndietro).Date, (oggi + AssenzeGiornoAvanti).Date);

    /// <summary>
    /// Specchia in <c>hr_absence_days</c> i giorni di assenza ricevuti per la finestra
    /// [<paramref name="dal"/>, <paramref name="al"/>]: inserisce i nuovi, aggiorna i cambiati,
    /// toglie quelli che nella finestra Ecos non manda più (richiesta annullata o accorciata).
    /// Le righe di persone non collegate si saltano.
    ///
    /// <para>🪤 Con zero righe non si toglie niente: è più probabile un filtro che non ha
    /// funzionato di una finestra davvero vuota, e cancellare in silenzio costa più che
    /// aspettare il giro dopo.</para>
    /// </summary>
    internal (int Nuove, int Aggiornate, int Rimosse) SyncAbsenceDays(
        MySqlConnection c, IReadOnlyList<EcosAbsenceDay> giorni, DateTime dal, DateTime al)
    {
        if (giorni.Count == 0) return (0, 0, 0);

        Dictionary<string, int> mappa = MappaEcos(c);

        // 🪤 La chiave è (richiesta, GIORNO, tratto): il progressivo del tratto si ripete per
        // ogni giorno di una richiesta a più giorni (1 = mattina, 2 = pomeriggio). Al primo
        // import in produzione la chiave senza giorno è saltata su «Duplicate entry
        // '133095-2'». Se lo stesso tratto arrivasse due volte, l'ultimo vince: si deduplica
        // qui e l'import non si ferma.
        var esistenti = new Dictionary<string, RigaAssenzaGiorno>(StringComparer.OrdinalIgnoreCase);
        foreach (RigaAssenzaGiorno r in c.Query<RigaAssenzaGiorno>(
                     @"SELECT id AS Id, ecos_absence_id AS EcosAbsenceId, ecos_refine_id AS EcosRefineId,
                              employee_id AS EmployeeId, work_date AS WorkDate, category_code AS CategoryCode,
                              status AS Status, minutes AS Minutes, hour_begin AS HourBegin, hour_end AS HourEnd
                       FROM hr_absence_days
                       WHERE work_date BETWEEN @Dal AND @Al
                       ORDER BY id",
                     new { Dal = dal.Date, Al = al.Date }))
        {
            esistenti[Chiave(r.EcosAbsenceId, r.WorkDate, r.EcosRefineId)] = r;
        }

        var ricevuti = new Dictionary<string, EcosAbsenceDay>(StringComparer.OrdinalIgnoreCase);
        foreach (EcosAbsenceDay g in giorni)
        {
            if (g.Date.Date < dal.Date || g.Date.Date > al.Date) continue;
            ricevuti[Chiave(g.AbsenceRequestId, g.Date, g.RefineId)] = g;
        }

        int nuove = 0, aggiornate = 0;
        var visti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (MySqlTransaction tran = c.BeginTransaction())
        {
            foreach ((string chiave, EcosAbsenceDay g) in ricevuti)
            {
                if (!mappa.TryGetValue(g.EmplCode, out int employeeId)) continue;
                visti.Add(chiave);

                var valori = new
                {
                    EmployeeId = employeeId,
                    WorkDate = g.Date.Date,
                    EcosAbsenceId = g.AbsenceRequestId,
                    EcosRefineId = g.RefineId,
                    g.CategoryCode,
                    CategoryDesc = string.IsNullOrWhiteSpace(g.CategoryDesc) ? null : g.CategoryDesc,
                    AbsenceType = TipoAssenza(g.CategoryCode),
                    Status = StatoRichiesta(g.StatusCode, deleted: false),
                    HourBegin = OraSql(g.HourBegin),
                    HourEnd = OraSql(g.HourEnd),
                    g.Minutes,
                    g.UpdateDate,
                };

                if (!esistenti.TryGetValue(chiave, out RigaAssenzaGiorno? vecchia))
                {
                    c.Execute(@"
                        INSERT INTO hr_absence_days
                            (employee_id, work_date, ecos_absence_id, ecos_refine_id, category_code, category_desc,
                             absence_type, status, hour_begin, hour_end, minutes, ecos_update_date)
                        VALUES
                            (@EmployeeId, @WorkDate, @EcosAbsenceId, @EcosRefineId, @CategoryCode, @CategoryDesc,
                             @AbsenceType, @Status, @HourBegin, @HourEnd, @Minutes, @UpdateDate)",
                        valori, tran);
                    nuove++;
                    continue;
                }

                bool cambiata = vecchia.EmployeeId != employeeId
                                || vecchia.WorkDate.Date != g.Date.Date
                                || vecchia.CategoryCode != g.CategoryCode
                                || vecchia.Status != valori.Status
                                || vecchia.Minutes != g.Minutes
                                || vecchia.HourBegin != valori.HourBegin
                                || vecchia.HourEnd != valori.HourEnd;
                if (!cambiata) continue;

                c.Execute(@"
                    UPDATE hr_absence_days
                    SET employee_id = @EmployeeId, work_date = @WorkDate, category_code = @CategoryCode,
                        category_desc = @CategoryDesc, absence_type = @AbsenceType, status = @Status,
                        hour_begin = @HourBegin, hour_end = @HourEnd, minutes = @Minutes, ecos_update_date = @UpdateDate
                    WHERE id = @Id",
                    new
                    {
                        vecchia.Id, valori.EmployeeId, valori.WorkDate, valori.CategoryCode, valori.CategoryDesc,
                        valori.AbsenceType, valori.Status, valori.HourBegin, valori.HourEnd, valori.Minutes, valori.UpdateDate,
                    }, tran);
                aggiornate++;
            }

            // Dentro la finestra lo scarico è la fotografia intera: quello che non c'è più si toglie.
            long[] sparite = esistenti
                .Where(kv => !visti.Contains(kv.Key))
                .Select(kv => kv.Value.Id)
                .ToArray();
            foreach (long[] blocco in ABlocchi(sparite, 500))
                c.Execute("DELETE FROM hr_absence_days WHERE id IN @Ids", new { Ids = blocco }, tran);

            tran.Commit();

            if (nuove + aggiornate + sparite.Length > 0)
            {
                _logger.LogInformation(
                    "[HR] Assenze per giorno {Dal:dd/MM/yyyy}-{Al:dd/MM/yyyy}: {N} nuove, {A} aggiornate, {R} tolte.",
                    dal, al, nuove, aggiornate, sparite.Length);
            }
            return (nuove, aggiornate, sparite.Length);
        }
    }

    /// <summary>
    /// Un giorno di assenza come lo vede chi legge: i tratti di Ecos sommati, la causale che
    /// pesa di più, lo stato migliore (APPROVED batte PENDING).
    /// </summary>
    internal sealed record AssenzaEcosGiorno(
        int EmployeeId, DateTime WorkDate, string AbsenceType, string Status, int? Minutes,
        string CategoryCode, string? CategoryDesc);

    /// <summary>
    /// Le assenze di Ecos giorno per giorno nella finestra, aggregate per (dipendente, giorno).
    /// <paramref name="anchePending"/>: il calendario mostra anche le richieste in attesa,
    /// il cartellino solo le approvate. <paramref name="employeeId"/> null = tutti.
    /// </summary>
    internal static Dictionary<(int EmployeeId, DateTime WorkDate), AssenzaEcosGiorno> AssenzeEcosPerGiorno(
        MySqlConnection c, DateTime dal, DateTime al, bool anchePending, int? employeeId = null)
    {
        string sql = @"
            SELECT id AS Id, ecos_absence_id AS EcosAbsenceId, ecos_refine_id AS EcosRefineId,
                   employee_id AS EmployeeId, work_date AS WorkDate, category_code AS CategoryCode,
                   category_desc AS CategoryDesc, absence_type AS AbsenceType, status AS Status,
                   minutes AS Minutes, hour_begin AS HourBegin, hour_end AS HourEnd
            FROM hr_absence_days
            WHERE work_date BETWEEN @Dal AND @Al
              AND status IN ('APPROVED'" + (anchePending ? ", 'PENDING'" : "") + ")"
            + (employeeId.HasValue ? " AND employee_id = @EmployeeId" : "");

        var righe = c.Query<RigaAssenzaGiorno>(sql, new { Dal = dal.Date, Al = al.Date, EmployeeId = employeeId });

        var risultato = new Dictionary<(int, DateTime), AssenzaEcosGiorno>();
        foreach (var gruppo in righe.GroupBy(r => (r.EmployeeId, r.WorkDate.Date)))
        {
            // I minuti si sommano solo se ogni tratto li ha: un tratto senza orari rende
            // ignoto il totale, e «ignoto» vale «giornata intera» per chi legge.
            int? minuti = gruppo.All(r => r.Minutes.HasValue) ? gruppo.Sum(r => r.Minutes!.Value) : null;

            RigaAssenzaGiorno prevalente = gruppo
                .OrderByDescending(r => r.Minutes ?? int.MaxValue)
                .ThenBy(r => r.Id)
                .First();

            string stato = gruppo.Any(r => r.Status == "APPROVED") ? "APPROVED" : "PENDING";

            risultato[gruppo.Key] = new AssenzaEcosGiorno(
                gruppo.Key.EmployeeId, gruppo.Key.Date, prevalente.AbsenceType, stato, minuti,
                prevalente.CategoryCode, prevalente.CategoryDesc);
        }
        return risultato;
    }

    /// <summary>Le ore del giorno per chi legge: i minuti in ore, o le ore di contratto se Ecos non dà gli orari.</summary>
    internal static decimal OreAssenzaGiorno(AssenzaEcosGiorno g, decimal oreGiornaliere) =>
        g.Minutes is { } m ? Math.Round(m / 60m, 2) : oreGiornaliere;

    /// <summary>Giornata intera quando i tratti coprono le ore di contratto (meno una tolleranza).</summary>
    internal static bool GiornataIntera(AssenzaEcosGiorno g, decimal oreGiornaliere) =>
        g.Minutes is not { } m || m >= (int)(oreGiornaliere * 60m) - TolleranzaGiornataInteraMinuti;

    private static string Chiave(string absenceId, DateTime giorno, string refineId) =>
        absenceId + "/" + giorno.ToString("yyyyMMdd") + "/" + refineId;

    /// <summary>«16:15:00» come lo manda Ecos, o «16:15»: un TIME per MySQL; null se manca o non si legge.</summary>
    private static TimeSpan? OraSql(string? ora) =>
        TimeSpan.TryParseExact(ora?.Trim() ?? "", new[] { "hh\\:mm\\:ss", "hh\\:mm", "h\\:mm\\:ss", "h\\:mm" },
            System.Globalization.CultureInfo.InvariantCulture, out TimeSpan t)
            ? t
            : null;

    private sealed class RigaAssenzaGiorno
    {
        public long Id { get; set; }
        public string EcosAbsenceId { get; set; } = "";
        public string EcosRefineId { get; set; } = "";
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string CategoryCode { get; set; } = "";
        public string? CategoryDesc { get; set; }
        public string AbsenceType { get; set; } = "";
        public string Status { get; set; } = "";
        public int? Minutes { get; set; }
        public TimeSpan? HourBegin { get; set; }
        public TimeSpan? HourEnd { get; set; }
    }
}
