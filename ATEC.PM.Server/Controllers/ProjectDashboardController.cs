using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;


/// <summary>
/// Cruscotto della commessa (<c>GET api/projects/{id}/dashboard</c>): ore, costi, reparti,
/// avanzamento. Spostato da <c>ProjectsController</c> il 04/09/2026, nessun percorso cambiato.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura riguarda UNA commessa (l'id sta nella rotta), quindi il cancello si mette
// una volta sola sulla classe: una commessa in bozza, in stand-by o chiusa si consulta ma non si
// modifica, salvo il permesso di scavalco. E una bozza non si VEDE proprio, letture comprese.
[RequireProjectWritable]
[RequireProjectVisible]
public class ProjectDashboardController : ProjectsControllerBase
{
    private readonly ILogger<ProjectDashboardController> _logger;
    public ProjectDashboardController(
        DbService db,
        NotificationService notif,
        ILogger<ProjectDashboardController> logger,
        IHubContext<ProjectHub> hub,
        FeatureAccessService access,
        AnagraficheCache cache) : base(db, hub, notif, access, cache)
    {
        _logger = logger;
    }

    /// <summary>
    /// La Dashboard della commessa: <c>revenue</c>, budget, costo consuntivo, materiali,
    /// trasferta e totale. È la stessa roba che <see cref="GetAll"/> tiene dietro
    /// <c>nav.commesse</c>, quindi sta dietro la stessa chiave.
    ///
    /// <para>🪤 Trovata da una revisione il 20/08, un'ora dopo aver chiuso l'elenco: qui non
    /// c'era nessun <c>[RequireFeature]</c>, e gli attributi di classe non coprono
    /// (<c>RequireProjectWritable</c> esce sulle letture, <c>RequireProjectVisible</c> filtra
    /// solo le bozze). Chiusa la porta principale, i soldi uscivano da quella di servizio: un
    /// ciclo su <c>{id}</c> e si riprendeva il valore di ogni commessa, margine compreso.
    /// La lezione: quando si chiude un dato, si chiudono TUTTE le strade che lo portano, non
    /// quella da cui lo si è visto passare.</para>
    ///
    /// <para>Unico chiamante: <c>ProjectDetailsSection</c> dentro <c>CommessePage</c>, già
    /// dietro <c>nav.commesse</c> — chiuderla non toglie niente a nessuno che l'usasse.</para>
    /// </summary>
    [HttpGet("{id}/dashboard")]
    [RequireFeature("nav.commesse")]
    public IActionResult GetDashboard(int id)
    {
        try
        {
            ProjectDashboardData? data = BuildProjectDashboard(id);
            if (data == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));
            return Ok(ApiResponse<ProjectDashboardData>.Ok(data));
        }
        catch (Exception ex)
        {
            // Mai un 500 nudo: il client mostrerebbe solo "Internal Server Error" e il motivo
            // resterebbe sepolto nel log del server. Meglio il messaggio vero in pagina.
            _logger.LogError(ex, "[Dashboard] Commessa {ProjectId}: calcolo dashboard fallito", id);
            return Ok(ApiResponse<ProjectDashboardData>.Fail($"Dashboard non disponibile: {ex.Message}"));
        }
    }

    /// <summary>Calcola la dashboard della commessa. <c>null</c> se la commessa non esiste.</summary>
    private ProjectDashboardData? BuildProjectDashboard(int id)
    {
        using var c = _db.Open();

        // Info commessa + cliente + PM
        var data = c.QueryFirstOrDefault<ProjectDashboardData>(@"
            SELECT p.code AS Code, p.title AS Title, p.status, p.priority,
                   p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.budget_total AS BudgetTotal,
                   COALESCE((SELECT SUM(pa.planned_hours) FROM phase_assignments pa JOIN project_phases pp2 ON pp2.id = pa.project_phase_id WHERE pp2.project_id = p.id), 0) AS BudgetHoursTotal,
                   p.revenue AS Revenue, p.description AS Description,
                   p.server_path AS ServerPath, p.notes AS Notes,
                   COALESCE(cust.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(pm.first_name,' ',pm.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cust ON cust.id = p.customer_id
            LEFT JOIN employees pm ON pm.id = p.pm_id
            WHERE p.id = @Id", new { Id = id });

        if (data == null) return null;

        // Ore lavorate totali + costo consuntivo
        // Fallback robusto: priorità is_primary, poi is_responsible, poi qualsiasi reparto (MIN id).
        // Extra Lavoro (#39): fuori le ore che il PM ha tolto dalla contabilità della commessa,
        // o questa card direbbe un numero e il Bilancio un altro — sulla stessa schermata.
        var totals = c.QueryFirstOrDefault<dynamic>($@"
    SELECT COALESCE(SUM(te.hours), 0) AS HoursWorked,
           COALESCE(SUM(te.hours * COALESCE(d.hourly_cost, 0)), 0) AS CostWorked
    FROM timesheet_entries te
    JOIN employees e ON e.id = te.employee_id
    JOIN project_phases pp ON pp.id = te.project_phase_id
    {ProjectEconomics.ExtraWorkJoin}
    LEFT JOIN (
        SELECT employee_id, department_id,
               ROW_NUMBER() OVER (PARTITION BY employee_id
                                  ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
        FROM employee_departments
    ) ed ON ed.employee_id = e.id AND ed.rn = 1
    LEFT JOIN departments d ON d.id = ed.department_id
    WHERE pp.project_id = @Id AND {ProjectEconomics.ExtraWorkCounts}", new { Id = id });

        data.HoursWorked = (decimal)(totals?.HoursWorked ?? 0m);
        data.CostWorked = (decimal)(totals?.CostWorked ?? 0m);

        // Costo materiali DDP — esclude solo gli stati «esclusi da totale» (aggregazione A9);
        // i materiali consegnati (A2) SONO un costo reale e restano nel totale.
        string[] ddpExcluded = DdpAggregationSet.Load(c, "A9", _cache);
        // La somma commerciale NON si ricopia: la fa ProjectEconomics, che porta con sé le due
        // regole del Bilancio — dedup dei padri di composizione (#119) e quota dei grezzi
        // (#135). Fino al 28/08/2026 qui c'era una terza copia della query, senza la dedup:
        // su ogni commessa con un gruppo Codex importato questa card sommava anche
        // l'intestazione ai suoi figli e diceva un materiale più alto del Bilancio.
        decimal materialCostCommercial =
            ProjectEconomics.GetCommercialMaterialCost(c, id, ddpExcluded);

        decimal materialCostOfficina = c.ExecuteScalar<decimal>($@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM ddp_officina_items
            WHERE project_id = @Id AND COALESCE(item_status,'') NOT IN @Excluded
              AND {ProjectEconomics.OfficinaParentDedup}",
            new { Id = id, Excluded = ddpExcluded });

        data.MaterialCostCommercial = materialCostCommercial;
        data.MaterialCostOfficina = materialCostOfficina;
        data.MaterialCost = materialCostCommercial + materialCostOfficina;

        // La trasferta a consuntivo fa parte del costo totale: senza, il MARGINE del tab
        // Dettagli risultava più alto della Redditività del conto economico, che la include.
        // Stessa regola del conto economico e di /bilancio: se la trasferta è compilata a righe
        // (blocco 6) il costo lo dice il suo foglio, altrimenti il valore digitato a mano.
        data.TravelCost = c.ExecuteScalar<decimal?>(
            $"SELECT {TravelPlanService.ActualTravelCostSql} FROM projects p WHERE p.id=@Id",
            new { Id = id }) ?? 0;
        data.TotalCost = data.CostWorked + data.MaterialCost + data.TravelCost;

        // Conteggio fasi
        var phaseCounts = c.QueryFirstOrDefault<dynamic>(@"
            -- Le fasi spente (#51) stanno fuori dall'elenco e dal Timesheet: contarle
            -- al denominatore dell'avanzamento vorrebbe dire chiedere di completare
            -- fasi che nessuno vede più.
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN status='COMPLETED' THEN 1 ELSE 0 END) AS Completed
            FROM project_phases WHERE is_off = 0 AND project_id = @Id", new { Id = id });

        data.TotalPhases = (int)(phaseCounts?.Total ?? 0);
        data.CompletedPhases = (int)(phaseCounts?.Completed ?? 0);

        // Riepilogo per reparto — 3 livelli: Preventivate / Assegnate / Lavorate
        // Ore preventivate: le ore della sezione si SPEZZANO sui reparti collegati allo
        // snapshot di sezione (fallback al template). Prima il JOIN le ripeteva intere su
        // ogni reparto (segnalazione #74): Prev MEC+INS sulla stessa sezione valeva 2×.
        var costingByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT d.code, d.name,
                   ROUND(SUM(r.work_days * r.hours_per_day / NULLIF(dc.cnt, 0)), 2) AS Hours
            FROM project_cost_resources r
            JOIN project_cost_sections pcs ON pcs.id = r.section_id
            JOIN (
                SELECT section_id, department_id, cnt FROM (
                    SELECT pcsd.project_cost_section_id AS section_id,
                           pcsd.department_id,
                           COUNT(*) OVER (PARTITION BY pcsd.project_cost_section_id) AS cnt
                    FROM project_cost_section_departments pcsd
                ) proj
                UNION ALL
                SELECT pcs2.id AS section_id, cstd.department_id,
                       COUNT(*) OVER (PARTITION BY pcs2.id) AS cnt
                FROM project_cost_sections pcs2
                JOIN cost_section_template_departments cstd
                  ON cstd.section_template_id = pcs2.template_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM project_cost_section_departments x
                    WHERE x.project_cost_section_id = pcs2.id
                )
            ) dc ON dc.section_id = pcs.id
            JOIN departments d ON d.id = dc.department_id
            WHERE pcs.project_id = @Id AND pcs.is_enabled = 1
            GROUP BY d.code, d.name", new { Id = id }).ToList();

        // Ore assegnate (dalle phase_assignments, raggruppate per reparto fase o reparto dipendente).
        // Per il reparto del dipendente: priorità is_primary, poi is_responsible, poi qualsiasi (MIN id).
        var assignedByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT COALESCE(d.code, ed.code, 'TRASV') AS code,
                   COALESCE(d.name, ed.name, 'Trasversale') AS name,
                   SUM(pa.planned_hours) AS Hours
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN departments d ON d.id = pp.department_id
            LEFT JOIN (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) empd ON empd.employee_id = pa.employee_id AND empd.rn = 1
            LEFT JOIN departments ed ON ed.id = empd.department_id
            WHERE pp.project_id = @Id
            GROUP BY COALESCE(d.code, ed.code, 'TRASV'), COALESCE(d.name, ed.name, 'Trasversale')", new { Id = id }).ToList();

        // Fasi, completamento, ore lavorate, materiali
        // Extra Lavoro (#39): le ore tolte dalla commessa escono anche da qui, o il grafico
        // «Lavorato per reparto» non somma più al totale della card «Ore totali».
        var phasesByDept = c.Query<DeptSummary>($@"
            SELECT dept_code AS DepartmentCode, dept_name AS DepartmentName,
                   SUM(HoursWorked) AS HoursWorked,
                   SUM(TotalPhases) AS TotalPhases, SUM(CompletedPhases) AS CompletedPhases,
                   SUM(MaterialCost) AS MaterialCost
            FROM (
                -- Fasi con reparto
                SELECT COALESCE(d.code, 'TRASV') AS dept_code,
                       COALESCE(d.name, 'Trasversale') AS dept_name,
                       COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                                 {ProjectEconomics.ExtraWorkJoin}
                                 WHERE te.project_phase_id = pp.id
                                   AND {ProjectEconomics.ExtraWorkCounts}), 0) AS HoursWorked,
                       -- Le fasi spente restano fuori dal conteggio, come nella card
                       -- «Avanzamento»: le ore però ci sono ancora, e restano contate.
                       CASE WHEN pp.is_off = 1 THEN 0 ELSE 1 END AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' AND pp.is_off = 0 THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                LEFT JOIN departments d ON d.id = pp.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NOT NULL

                UNION ALL

                -- Fasi senza department_id (casi tipici in produzione): non usare MIN(department_id)
                -- della sezione — con sezioni multi-reparto scaricava tutto sul primo id
                -- (segnalazione #74: «Installazione elettrica» di Tomasi/INS finiva in MEC).
                -- Ordine: reparto primario del dipendente SE è tra quelli della sezione;
                -- altrimenti il primario comunque; altrimenti il primo della sezione; altrimenti TRASV.
                SELECT COALESCE(d_match.code, ed.code, dsec.code, 'TRASV') AS dept_code,
                       COALESCE(d_match.name, ed.name, dsec.name, 'Trasversale') AS dept_name,
                       te.hours AS HoursWorked,
                       0 AS TotalPhases, 0 AS CompletedPhases, 0 AS MaterialCost
                FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                LEFT JOIN (
                    SELECT employee_id, department_id,
                           ROW_NUMBER() OVER (PARTITION BY employee_id
                                              ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                    FROM employee_departments
                ) empd ON empd.employee_id = te.employee_id AND empd.rn = 1
                LEFT JOIN departments ed ON ed.id = empd.department_id
                LEFT JOIN project_cost_sections pcs
                     ON pcs.project_id = pp.project_id
                    AND pcs.template_id = pp.cost_section_template_id
                LEFT JOIN project_cost_section_departments pcsd_match
                     ON pcsd_match.project_cost_section_id = pcs.id
                    AND pcsd_match.department_id = empd.department_id
                LEFT JOIN departments d_match ON d_match.id = pcsd_match.department_id
                LEFT JOIN (
                    SELECT pcsd.project_cost_section_id, MIN(pcsd.department_id) AS department_id
                    FROM project_cost_section_departments pcsd
                    GROUP BY pcsd.project_cost_section_id
                ) firstdept ON firstdept.project_cost_section_id = pcs.id
                LEFT JOIN departments dsec ON dsec.id = firstdept.department_id
                {ProjectEconomics.ExtraWorkJoin}
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
                  AND {ProjectEconomics.ExtraWorkCounts}

                UNION ALL

                -- Fasi senza reparto proprio: il reparto si ricava con la STESSA cascata
                -- del ramo delle ore qui sopra (segnalazione #107), così fasi e ore di
                -- una stessa fase cadono sullo stesso reparto.
                -- Prima finivano tutte su 'TRASV': con l'anagrafica attività di oggi
                -- `project_phases.department_id` nasce NULL (BulkCreate non lo scrive e
                -- `phase_templates.department_id` è vuoto su tutti i template), quindi il
                -- conteggio fasi di OGNI reparto risultava 0/0 e un reparto che in
                -- anagrafica non esiste si prendeva tutte le fasi della commessa, con
                -- zero ore — mentre le ore andavano ai reparti veri.
                -- Ordine: reparto della persona assegnata alla fase se è fra quelli della
                -- sezione; altrimenti il suo reparto principale; altrimenti il primo
                -- reparto della sezione; altrimenti TRASV (fase senza sezione e senza
                -- nessuno assegnato). Una fase conta SEMPRE una volta sola: spezzarla in
                -- frazioni sui reparti della sezione scriverebbe «3,5 fasi su 7».
                SELECT COALESCE(d_match2.code, ed2.code, dsec2.code, 'TRASV') AS dept_code,
                       COALESCE(d_match2.name, ed2.name, dsec2.name, 'Trasversale') AS dept_name,
                       0 AS HoursWorked,
                       CASE WHEN pp.is_off = 1 THEN 0 ELSE 1 END AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' AND pp.is_off = 0 THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                LEFT JOIN (
                    SELECT project_phase_id, employee_id,
                           ROW_NUMBER() OVER (PARTITION BY project_phase_id ORDER BY id) AS rn
                    FROM phase_assignments
                ) pa1 ON pa1.project_phase_id = pp.id AND pa1.rn = 1
                LEFT JOIN (
                    SELECT employee_id, department_id,
                           ROW_NUMBER() OVER (PARTITION BY employee_id
                                              ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                    FROM employee_departments
                ) empd2 ON empd2.employee_id = pa1.employee_id AND empd2.rn = 1
                LEFT JOIN departments ed2 ON ed2.id = empd2.department_id
                LEFT JOIN project_cost_sections pcs2b
                       ON pcs2b.project_id = pp.project_id
                      AND pcs2b.template_id = pp.cost_section_template_id
                LEFT JOIN project_cost_section_departments pcsd_match2
                       ON pcsd_match2.project_cost_section_id = pcs2b.id
                      AND pcsd_match2.department_id = empd2.department_id
                LEFT JOIN departments d_match2 ON d_match2.id = pcsd_match2.department_id
                LEFT JOIN (
                    SELECT pcsd.project_cost_section_id, MIN(pcsd.department_id) AS department_id
                    FROM project_cost_section_departments pcsd
                    GROUP BY pcsd.project_cost_section_id
                ) firstdept2 ON firstdept2.project_cost_section_id = pcs2b.id
                LEFT JOIN departments dsec2 ON dsec2.id = firstdept2.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
            ) sub
            GROUP BY dept_code, dept_name", new { Id = id, Excluded = ddpExcluded }).ToList();

        // Merge: unisci costing + assigned + fasi in un unico elenco per reparto.
        // Tollerante ai duplicati: due righe con lo STESSO codice reparto (capita con reparti
        // dal codice NULL/vuoto, che ricadono su 'TRASV' insieme alle fasi trasversali) vanno
        // sommate — con ToDictionary sarebbero un'eccezione e quindi tutta la dashboard in errore.
        static string DeptKey(string? code) =>
            string.IsNullOrWhiteSpace(code) ? "TRASV" : code;

        Dictionary<string, DeptSummary> deptMap = new();
        foreach (DeptSummary row in phasesByDept)
        {
            string code = DeptKey(row.DepartmentCode);
            if (deptMap.TryGetValue(code, out DeptSummary? existing))
            {
                existing.HoursWorked += row.HoursWorked;
                existing.TotalPhases += row.TotalPhases;
                existing.CompletedPhases += row.CompletedPhases;
                existing.MaterialCost += row.MaterialCost;
                if (string.IsNullOrWhiteSpace(existing.DepartmentName))
                    existing.DepartmentName = row.DepartmentName ?? code;
            }
            else
            {
                row.DepartmentCode = code;
                row.DepartmentName = string.IsNullOrWhiteSpace(row.DepartmentName) ? code : row.DepartmentName;
                deptMap[code] = row;
            }
        }
        foreach (var (code, name, _) in costingByDept.Concat(assignedByDept))
        {
            string key = DeptKey(code);
            if (!deptMap.ContainsKey(key))
                deptMap[key] = new DeptSummary { DepartmentCode = key, DepartmentName = name ?? key };
        }
        foreach (var (code, _, hours) in costingByDept)
            if (deptMap.TryGetValue(DeptKey(code), out DeptSummary? ds)) ds.CostingHours += hours;
        foreach (var (code, _, hours) in assignedByDept)
            if (deptMap.TryGetValue(DeptKey(code), out DeptSummary? ds)) ds.AssignedHours += hours;

        // BudgetHours = costing come riferimento principale
        foreach (DeptSummary ds in deptMap.Values)
            ds.BudgetHours = ds.CostingHours;

        data.DepartmentSummaries = deptMap.Values.OrderBy(d2 => d2.DepartmentCode).ToList();

        // Ultimi 10 inserimenti timesheet
        data.RecentEntries = c.Query<RecentTimesheetEntry>(@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType,
                   COALESCE(te.notes, '') AS Notes
            FROM timesheet_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.project_id = @Id
            ORDER BY te.work_date DESC, te.id DESC
            LIMIT 10", new { Id = id }).ToList();

        // Tecnici assegnati alle fasi (non dal timesheet).
        // REPARTO = primario della persona (#74): le fasi in produzione hanno spesso
        // department_id NULL, e usare quello della fase lasciava il badge vuoto.
        // «ORE LAV.»: stesse ore del totale della commessa, senza Extra Lavoro (#39).
        data.ActiveTechnicians = c.Query<ActiveTechSummary>($@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(ed.code, '') AS DepartmentCode,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             {ProjectEconomics.ExtraWorkJoin}
                             WHERE te.employee_id = e.id
                             AND te.project_phase_id IN (SELECT pp2.id FROM project_phases pp2 WHERE pp2.project_id = @Id)
                             AND {ProjectEconomics.ExtraWorkCounts}), 0) AS TotalHours,
                   COUNT(DISTINCT pa.project_phase_id) AS PhaseCount
            FROM phase_assignments pa
            JOIN employees e ON e.id = pa.employee_id
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) empd ON empd.employee_id = e.id AND empd.rn = 1
            LEFT JOIN departments ed ON ed.id = empd.department_id
            WHERE pp.project_id = @Id
            GROUP BY e.id, e.first_name, e.last_name, ed.code
            ORDER BY e.last_name", new { Id = id }).ToList();

        // ── Ore settimanali (ultime 12 settimane) ────────────────
        // Anche qui senza Extra Lavoro (#39): le barre devono sommare alle «Ore totali».
        data.WeeklyHours = c.Query<WeeklyHoursSummary>($@"
            SELECT YEAR(te.work_date) AS Year,
                   WEEK(te.work_date, 1) AS Week,
                   SUM(te.hours) AS Hours,
                   CONCAT('S', WEEK(te.work_date, 1)) AS WeekLabel
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            {ProjectEconomics.ExtraWorkJoin}
            WHERE pp.project_id = @Id
              AND {ProjectEconomics.ExtraWorkCounts}
              AND te.work_date >= DATE_SUB(CURDATE(), INTERVAL 12 WEEK)
            GROUP BY YEAR(te.work_date), WEEK(te.work_date, 1),
                     CONCAT('S', WEEK(te.work_date, 1))
            ORDER BY Year, Week", new { Id = id }).ToList();

        // ── Gantt fasi ───────────────────────────────────────────
        // Snapshot-aware: LEFT JOIN + fallback pp.name per fasi locali (phase_template_id NULL)
        // Ore per fase senza Extra Lavoro (#39), come già fa PhasesController sulla stessa cifra.
        data.PhaseGantt = c.Query<PhaseGanttItem>($@"
            SELECT pp.id AS PhaseId,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct,
                   pp.budget_hours AS BudgetHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             {ProjectEconomics.ExtraWorkJoin}
                             WHERE te.project_phase_id = pp.id
                               AND {ProjectEconomics.ExtraWorkCounts}), 0) AS HoursWorked,
                   pp.start_date AS StartDate,
                   pp.end_date AS EndDate,
                   pp.sort_order AS SortOrder
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            ORDER BY pp.sort_order", new { Id = id }).ToList();

        // ── Scadenze prossime (fasi non completate con end_date) ─
        data.Deadlines = c.Query<UpcomingDeadline>(@"
            SELECT COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.end_date AS Deadline,
                   DATEDIFF(pp.end_date, CURDATE()) AS DaysRemaining,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
              AND pp.end_date IS NOT NULL
              AND pp.status NOT IN ('COMPLETED', 'CANCELLED')
            ORDER BY pp.end_date ASC
            LIMIT 10", new { Id = id }).ToList();

        return data;
    }
}
