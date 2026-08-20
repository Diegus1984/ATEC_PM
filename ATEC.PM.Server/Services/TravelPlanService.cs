using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Lettura della trasferta di una commessa e sua sincronizzazione con il Bilancio (blocco 6).
///
/// **Solo la metà «Spese Trasferta» va al Bilancio** (decisione D6-C): la metà «Risorse Atec» a
/// consuntivo in ATEC PM è già automatica e strutturale, viene dal timesheet reale, ed è più
/// affidabile dei nominativi digitati qui — rigenerarla dagli step, come fa il prototipo, sarebbe
/// una regressione.
/// </summary>
public static class TravelPlanService
{
    /// <summary>Riga di trasferta che si porta dietro la commessa (query cross-commessa).</summary>
    private sealed class SummaryRow : TravelRowDto
    {
        public int ProjectId { get; set; }
    }

    private sealed class StepCount
    {
        public int ProjectId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Coppia ore/costo per commessa: consuntivo cantiere e previsto fasi cantiere.</summary>
    private sealed class HoursCost
    {
        public int ProjectId { get; set; }
        public decimal Hours { get; set; }
        public decimal Cost { get; set; }
    }

    /// <summary>Marcatore di provenienza delle righe di costo generate da uno step.</summary>
    public const string SourcePrefix = "trasferta:step:";

    public static string SourceOf(int stepId) => SourcePrefix + stepId;

    /// <summary>Trasferta completa: step, righe, riepilogo per nominativo e totale generale.</summary>
    public static TravelPlanDto Load(IDbConnection c, int projectId)
    {
        var plan = new TravelPlanDto { ProjectId = projectId };

        plan.Steps = c.Query<TravelStepDto>(@"
            SELECT id AS Id, project_phase_id AS ProjectPhaseId, description AS Description,
                   sort_order AS SortOrder, row_version AS RowVersion
            FROM travel_steps WHERE project_id = @Pid ORDER BY sort_order, id",
            new { Pid = projectId }).ToList();

        if (plan.Steps.Count == 0) return plan;

        List<TravelRowDto> rows = c.Query<TravelRowDto>(@"
            SELECT r.id AS Id, r.step_id AS StepId, r.employee_id AS EmployeeId,
                   r.person_name AS PersonName, r.start_date AS StartDate, r.end_date AS EndDate,
                   r.work_date AS WorkDate, r.source AS Source, r.travel_days AS TravelDays,
                   r.hours_missing AS HoursMissing,
                   r.exclude_sat AS ExcludeSat, r.exclude_sun AS ExcludeSun,
                   r.hours AS Hours, r.hourly_rate AS HourlyRate,
                   r.nights AS Nights, r.night_price AS NightPrice,
                   r.meal_cost AS MealCost, r.allowance_cost AS AllowanceCost,
                   r.car_cost AS CarCost, r.transport_cost AS TransportCost,
                   r.sort_order AS SortOrder, r.row_version AS RowVersion
            FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            WHERE s.project_id = @Pid
            ORDER BY r.sort_order, r.id",
            new { Pid = projectId }).ToList();

        var byStep = rows.GroupBy(r => r.StepId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (TravelStepDto step in plan.Steps)
            if (byStep.TryGetValue(step.Id, out List<TravelRowDto>? list))
                step.Rows = list;

        // Riepilogo: un rigo per nominativo distinto. Il prototipo lascia le celle vuote (è solo
        // un elenco); avendo i dati li valorizziamo, che è più utile e non costa nulla.
        foreach (string name in TravelMath.DistinctNames(plan.Steps))
        {
            plan.Summary.Add(new TravelSummaryRowDto
            {
                PersonName = name,
                Totals = TravelTotalsDto.Of(rows.Where(r =>
                    string.Equals((r.PersonName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))),
            });
        }

        plan.GrandTotals = TravelTotalsDto.Of(rows);
        return plan;
    }

    /// <summary>
    /// Riporta i costi trasferta nella voce «Spese Trasferta / indennità» del Riepilogo Costi:
    /// una riga di calcolo per step, marcata <c>trasferta:step:{id}</c>. Le righe scritte a mano
    /// nello stesso foglio non vengono toccate (è il senso di <c>linked_source</c>).
    ///
    /// Va chiamata dopo OGNI scrittura sulla trasferta, e fuori da una transazione.
    /// </summary>
    public static void SyncToBudget(IDbConnection c, int projectId, int? userId)
    {
        TravelPlanDto plan = Load(c, projectId);

        var generated = plan.Steps.Select((step, index) => new ProjectCalcRowDto
        {
            GroupKey = "",
            Description = string.IsNullOrWhiteSpace(step.Description)
                ? $"Step trasferta {index + 1}"
                : step.Description.Trim(),
            // Importo secco della riga: il costo trasferta dello step (alloggio, vitto,
            // indennità, auto, treno/aereo). Il personale NON entra: sta nelle Risorse Atec,
            // che a consuntivo arrivano dal timesheet.
            UnitCost = step.Totals.TravelCost,
            MarkupValue = 1.000m,
            LinkedSource = SourceOf(step.Id),
        }).ToList();

        ProjectCalcSheets.SyncLinkedRows(
            c, projectId, CalcKeys.TravelActual, SourcePrefix, generated, userId);
    }

    /// <summary>
    /// Costo trasferta a consuntivo da mostrare nel Bilancio: il totale del foglio se la
    /// trasferta è stata compilata, altrimenti il valore digitato a mano su
    /// <c>projects.actual_travel_cost</c>, che resta la fonte per tutte le commesse di prima.
    /// </summary>
    public static decimal ActualTravelCost(IDbConnection c, int projectId, decimal manualValue)
    {
        ProjectCalcSheetDto sheet = ProjectCalcSheets.Load(c, projectId, CalcKeys.TravelActual);
        return sheet.Total ?? manualValue;
    }

    /// <summary>
    /// La STESSA regola in SQL, per le query che leggono più commesse in un colpo solo
    /// (pagina /bilancio, dashboard commessa). L'alias della tabella projects dev'essere `p`.
    /// Tre lettori dello stesso numero: se divergono torna l'incoerenza che il blocco 4 ha sanato.
    /// </summary>
    public const string ActualTravelCostSql = @"
        COALESCE(
            (SELECT SUM(r.amount) FROM project_calc_rows r
             JOIN project_calc_sheets cs ON cs.id = r.sheet_id
             WHERE cs.project_id = p.id AND cs.calc_key = 'spese.actual' AND r.amount IS NOT NULL),
            p.actual_travel_cost, 0)";

    /// <summary>Card della pagina «Gestione Trasferta»: i KPI per ogni commessa aperta.</summary>
    public static List<TravelProjectSummaryDto> Summaries(IDbConnection c, bool includeClosed)
    {
        // Le bozze e le annullate restano fuori, come nella pagina /bilancio.
        string statusFilter = includeClosed
            ? "p.status NOT IN ('DRAFT','CANCELLED')"
            : "p.status NOT IN ('DRAFT','CANCELLED','COMPLETED')";

        var projects = c.Query<TravelProjectSummaryDto>($@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title, p.status AS Status,
                   COALESCE(cu.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN employees e ON e.id = p.pm_id
            WHERE {statusFilter}
            ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

        if (projects.Count == 0) return projects;

        // Una query sola per tutte le righe: le formule (giorni con esclusione weekend) stanno
        // in TravelMath e vanno applicate in memoria, non riscritte in SQL.
        List<SummaryRow> rows = c.Query<SummaryRow>(@"
            SELECT s.project_id AS ProjectId,
                   r.id AS Id, r.step_id AS StepId, r.person_name AS PersonName,
                   r.start_date AS StartDate, r.end_date AS EndDate,
                   -- Anche qui servono i giorni delle righe derivate (1/0 deciso dal motore),
                   -- altrimenti le card della Gestione Trasferta conterebbero 0 giorni.
                   r.travel_days AS TravelDays, r.source AS Source,
                   r.exclude_sat AS ExcludeSat, r.exclude_sun AS ExcludeSun,
                   r.hours AS Hours, r.hourly_rate AS HourlyRate,
                   r.nights AS Nights, r.night_price AS NightPrice,
                   r.meal_cost AS MealCost, r.allowance_cost AS AllowanceCost,
                   r.car_cost AS CarCost, r.transport_cost AS TransportCost
            FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id").ToList();

        Dictionary<int, int> stepCounts = c.Query<StepCount>(
            "SELECT project_id AS ProjectId, COUNT(*) AS Count FROM travel_steps GROUP BY project_id")
            .ToDictionary(x => x.ProjectId, x => x.Count);

        // «Costo Ore Trasferta» (#92) + «Ore cantiere» (#96): le ore del timesheet sulle fasi
        // di cantiere — le stesse che generano le righe derivate (TravelFromTimesheet). Come la
        // derivazione, IGNORA counts_in_project: la persona in cantiere c'è stata, contabilizzata
        // o no. KPI informativi: NON entrano nel SyncToBudget (le ore sono già in «Risorse Atec»).
        Dictionary<int, HoursCost> hoursCosts = c.Query<HoursCost>(@"
            SELECT v.project_id AS ProjectId,
                   SUM(v.hours) AS Hours,
                   SUM(v.hours * v.hourly_cost) AS Cost
            FROM v_timesheet_with_section v
            JOIN cost_section_templates cs ON cs.id = v.cost_section_template_id
            WHERE cs.section_type = 'DA_CLIENTE'
            GROUP BY v.project_id")
            .ToDictionary(x => x.ProjectId);

        // Il PREVISTO delle fasi cantiere (#96): le STESSE fonti del Bilancio, così i numeri
        // della card combaciano riga per riga con la commessa aperta.
        // Ore = phase_assignments.planned_hours (la query «Assegnate» di
        // BudgetVsActualController); costo = ore × tariffa media della sezione
        // (BudgetCost / BudgetHours delle risorse pianificate), che è esattamente
        // l'AssignedCost del Bilancio riscritto in SQL.
        // Il TIPO di sezione si legge dal template VIVO (cst), lo stesso del consuntivo
        // qui sopra e della derivazione: se un template cambiasse tipo dopo la nascita
        // della commessa, consuntivo e previsto della stessa card devono contare le
        // stesse fasi. Lo snapshot (pcs) resta per is_enabled e per la tariffa media.
        Dictionary<int, HoursCost> planned = c.Query<HoursCost>(@"
            SELECT pp.project_id AS ProjectId,
                   SUM(pa.planned_hours) AS Hours,
                   SUM(pa.planned_hours * COALESCE(rate.avg_rate, 0)) AS Cost
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            JOIN cost_section_templates cst ON cst.id = pp.cost_section_template_id
            JOIN project_cost_sections pcs
                 ON pcs.template_id = pp.cost_section_template_id
                AND pcs.project_id = pp.project_id
            LEFT JOIN (
                SELECT r.section_id,
                       CASE WHEN SUM(r.work_days * r.hours_per_day) > 0
                            THEN SUM(r.work_days * r.hours_per_day * r.hourly_cost)
                                 / SUM(r.work_days * r.hours_per_day)
                            ELSE 0 END AS avg_rate
                FROM project_cost_resources r
                GROUP BY r.section_id
            ) rate ON rate.section_id = pcs.id
            WHERE pcs.is_enabled = 1 AND cst.section_type = 'DA_CLIENTE'
            GROUP BY pp.project_id")
            .ToDictionary(x => x.ProjectId);

        // GIORNI di trasferta a preventivo (#98): le risorse pianificate delle sezioni
        // DA_CLIENTE, GG per il valore della giornata secondo la regola di Zanoni — fino a
        // 4 Ore/g la giornata vale 0,5, oltre vale 1. È TravelMath.GiorniDaOre riscritta in
        // SQL (stessa soglia anche nel backfill della migrazione v98): il consuntivo applica
        // la stessa regola alle ore scaricate, così i due numeri della card si parlano.
        Dictionary<int, decimal> plannedDays = c.Query<HoursCost>(@"
            SELECT s.project_id AS ProjectId,
                   SUM(r.work_days * CASE WHEN r.hours_per_day <= 4 THEN 0.5 ELSE 1 END) AS Cost
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            JOIN cost_section_templates cst ON cst.id = s.template_id
            WHERE s.is_enabled = 1 AND cst.section_type = 'DA_CLIENTE'
            GROUP BY s.project_id")
            .ToDictionary(x => x.ProjectId, x => x.Cost);

        // «Spese Trasferta» a preventivo (#96): la voce del Riepilogo Costi del Bilancio, con la
        // sua regola anti-doppio-conteggio (#33) applicata PER SEZIONE: se la sezione ha righe di
        // trasferta valgono SOLO quelle (notti×prezzo + vitto + auto + treno/aereo + indennità),
        // altrimenti i 7 campi legacy delle risorse pianificate. Tutto a costo secco (D34-K).
        Dictionary<int, decimal> plannedTravel = c.Query<HoursCost>(@"
            SELECT s.project_id AS ProjectId,
                   SUM(CASE WHEN tr.section_id IS NOT NULL
                            THEN tr.total
                            ELSE COALESCE(res.total, 0) END) AS Cost
            FROM project_cost_sections s
            LEFT JOIN (
                SELECT t.section_id,
                       SUM(COALESCE(t.nights,0) * COALESCE(t.night_price,0)
                         + COALESCE(t.meal_cost,0) + COALESCE(t.car_cost,0)
                         + COALESCE(t.transport_cost,0) + COALESCE(t.allowance_cost,0)) AS total
                FROM project_cost_travel_rows t
                GROUP BY t.section_id
            ) tr ON tr.section_id = s.id
            LEFT JOIN (
                SELECT r.section_id,
                       SUM(r.num_trips * r.km_per_trip * r.cost_per_km
                         + r.work_days * (r.daily_food + r.daily_hotel)
                         + r.allowance_days * r.daily_allowance) AS total
                FROM project_cost_resources r
                GROUP BY r.section_id
            ) res ON res.section_id = s.id
            WHERE s.is_enabled = 1
            GROUP BY s.project_id")
            .ToDictionary(x => x.ProjectId, x => x.Cost);

        Dictionary<int, List<TravelRowDto>> byProject = rows.GroupBy(x => x.ProjectId)
            .ToDictionary(g => g.Key, g => g.Cast<TravelRowDto>().ToList());

        // Scarico ore non ancora verificato (#102): due dizionari, uno per quello che
        // manca da guardare e uno per l'ultima verifica data. Perimetro «da cliente»:
        // sono le sole ore che generano righe di trasferta.
        var pendenti = ScaricoOre.PerCommessa(c, ScaricoOre.ScopeTrasferta);
        var verifiche = ScaricoOre.VerificheFatte(c, ScaricoOre.ScopeTrasferta);

        foreach (TravelProjectSummaryDto p in projects)
        {
            p.StepCount = stepCounts.TryGetValue(p.ProjectId, out int n) ? n : 0;
            if (hoursCosts.TryGetValue(p.ProjectId, out HoursCost? hc))
            {
                p.TravelHours = hc.Hours;
                p.TravelHoursCost = hc.Cost;
            }
            if (planned.TryGetValue(p.ProjectId, out HoursCost? pl))
            {
                p.PlannedHours = pl.Hours;
                p.PlannedHoursCost = pl.Cost;
            }
            p.PlannedTravelCost = plannedTravel.TryGetValue(p.ProjectId, out decimal ptc) ? ptc : 0m;
            p.PlannedDays = plannedDays.TryGetValue(p.ProjectId, out decimal pd) ? pd : 0m;
            if (pendenti.TryGetValue(p.ProjectId, out ScaricoOre.Pendenza? pend))
            {
                p.PendingPeople = pend.Persone;
                p.PendingHours = pend.Ore;
                p.PendingFrom = pend.DalGiorno;
                p.PendingTo = pend.AlGiorno;
            }
            if (verifiche.TryGetValue(p.ProjectId, out ScaricoOre.Verifica? ver))
            {
                p.VerifiedAt = ver.VerifiedAt;
                p.VerifiedByName = ver.VerifiedByName;
            }
            if (!byProject.TryGetValue(p.ProjectId, out List<TravelRowDto>? list)) continue;
            TravelTotalsDto t = TravelTotalsDto.Of(list);
            p.Days = t.Days;
            p.Hours = t.Hours;
            p.PersonnelCost = t.PersonnelCost;
            p.TravelCost = t.TravelCost;
        }

        return projects;
    }
}
