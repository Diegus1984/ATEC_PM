using System.Data;

using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// «Verifica effettuata» sullo scarico ore (segnalazioni #102 e #109).
///
/// <para>Il PM deve sapere a colpo d'occhio su quali commesse sono arrivate ore nuove
/// dal Timesheet dall'ultima volta che le ha guardate. Il confronto è fra l'ultimo id di
/// <c>timesheet_entries</c> della commessa e la filigrana salvata in
/// <c>project_hours_checks</c> (migrazione v99): tutto ciò che sta sopra la filigrana è
/// nuovo. Niente filigrana = mai verificata = tutto nuovo.</para>
///
/// <para>Due letture della stessa commessa, tenute separate dallo <c>scope</c>: la pagina
/// Ore Commessa guarda TUTTE le ore, la pagina Trasferta solo quelle su fasi «da cliente»
/// — le uniche che generano righe di trasferta (stesso perimetro di
/// <c>TravelFromTimesheet</c>). Verificare l'una non spegne l'altra.</para>
///
/// <para>Il conteggio del pallino di menu è di <b>persone</b>, non di commesse o di righe:
/// una persona che ha scaricato su tre commesse conta una volta sola, perché è quello che
/// dice la segnalazione («numero totale di persone che hanno scaricato ore non ancora
/// verificate»).</para>
/// </summary>
public static class ScaricoOre
{
    public const string ScopeOre = "HOURS";
    public const string ScopeTrasferta = "TRAVEL";

    /// <summary>Le ore che contano per l'argomento: la trasferta guarda solo il «da cliente».</summary>
    private static string FiltroScope(string scope) =>
        scope == ScopeTrasferta
            ? @"JOIN cost_section_templates cst ON cst.id = pp.cost_section_template_id
                   AND cst.section_type = 'DA_CLIENTE'"
            : "";

    /// <summary>
    /// Righe di timesheet oltre la filigrana, per commessa. Il perimetro delle commesse è
    /// quello delle due pagine: fuori bozze e annullate, e le chiuse solo se richieste.
    /// </summary>
    private static string SqlPendenti(string scope) => $@"
        SELECT pp.project_id AS ProjectId,
               COUNT(DISTINCT te.employee_id) AS Persone,
               COALESCE(SUM(te.hours), 0) AS Ore,
               MIN(te.work_date) AS DalGiorno,
               MAX(te.work_date) AS AlGiorno
        FROM timesheet_entries te
        JOIN project_phases pp ON pp.id = te.project_phase_id
        {FiltroScope(scope)}
        LEFT JOIN project_hours_checks chk
               ON chk.project_id = pp.project_id AND chk.scope = @Scope
        JOIN projects p ON p.id = pp.project_id
        WHERE te.id > COALESCE(chk.last_entry_id, 0)
          AND p.status NOT IN ('DRAFT','CANCELLED')
        GROUP BY pp.project_id";

    public sealed class Pendenza
    {
        public int ProjectId { get; set; }
        public int Persone { get; set; }
        public decimal Ore { get; set; }
        public DateTime? DalGiorno { get; set; }
        public DateTime? AlGiorno { get; set; }
    }

    /// <summary>Scarico non ancora verificato, commessa per commessa.</summary>
    public static Dictionary<int, Pendenza> PerCommessa(IDbConnection c, string scope) =>
        c.Query<Pendenza>(SqlPendenti(scope), new { Scope = scope })
            .ToDictionary(r => r.ProjectId);

    /// <summary>
    /// Persone distinte con ore non verificate su TUTTE le commesse: è il numero del
    /// pallino rosso accanto alla voce di menu. Va contato una volta sola sull'insieme,
    /// non sommando i per-commessa, o chi lavora su tre cantieri varrebbe tre.
    /// </summary>
    public static int PersoneInAttesa(IDbConnection c, string scope) =>
        c.ExecuteScalar<int>($@"
            SELECT COUNT(DISTINCT te.employee_id)
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            {FiltroScope(scope)}
            LEFT JOIN project_hours_checks chk
                   ON chk.project_id = pp.project_id AND chk.scope = @Scope
            JOIN projects p ON p.id = pp.project_id
            WHERE te.id > COALESCE(chk.last_entry_id, 0)
              AND p.status NOT IN ('DRAFT','CANCELLED')", new { Scope = scope });

    public sealed class Verifica
    {
        public int ProjectId { get; set; }
        public DateTime VerifiedAt { get; set; }
        public string VerifiedByName { get; set; } = "";
    }

    /// <summary>Chi ha verificato e quando, per le commesse che sono già state guardate.</summary>
    public static Dictionary<int, Verifica> VerificheFatte(IDbConnection c, string scope) =>
        c.Query<Verifica>(@"
            SELECT chk.project_id AS ProjectId,
                   chk.verified_at AS VerifiedAt,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS VerifiedByName
            FROM project_hours_checks chk
            LEFT JOIN employees e ON e.id = chk.verified_by
            WHERE chk.scope = @Scope", new { Scope = scope })
            .ToDictionary(r => r.ProjectId);

    /// <summary>
    /// Segna verificato: la filigrana sale all'ultima riga di timesheet della commessa in
    /// questo momento. Le ore che arriveranno dopo faranno tornare rossa la card.
    /// </summary>
    public static void SegnaVerificato(IDbConnection c, int projectId, string scope, int? userId)
    {
        int ultimo = c.ExecuteScalar<int?>($@"
            SELECT MAX(te.id)
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            {FiltroScope(scope)}
            WHERE pp.project_id = @Pid", new { Pid = projectId }) ?? 0;

        c.Execute(@"
            INSERT INTO project_hours_checks (project_id, scope, last_entry_id, verified_at, verified_by)
            VALUES (@Pid, @Scope, @Last, NOW(), @Uid)
            ON DUPLICATE KEY UPDATE
                last_entry_id = VALUES(last_entry_id),
                verified_at   = VALUES(verified_at),
                verified_by   = VALUES(verified_by)",
            new { Pid = projectId, Scope = scope, Last = ultimo, Uid = userId });
    }
}
