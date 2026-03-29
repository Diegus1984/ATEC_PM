using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects/{projectId}/budget-vs-actual")]
[Authorize]
public class BudgetVsActualController : ControllerBase
{
    private readonly DbService _db;
    public BudgetVsActualController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult Get(int projectId)
    {
        using var c = _db.Open();

        // ── PREVENTIVO: sezioni + risorse ──────────────────────────
        var sections = c.Query<dynamic>(@"
            SELECT s.id, s.name, s.section_type, s.group_name, s.sort_order, s.template_id
            FROM project_cost_sections s
            WHERE s.project_id = @pid AND s.is_enabled = 1
            ORDER BY s.sort_order", new { pid = projectId }).ToList();

        var resources = c.Query<dynamic>(@"
            SELECT r.section_id AS SectionId,
                   r.resource_name AS ResourceName,
                   r.work_days AS WorkDays,
                   r.hours_per_day AS HoursPerDay,
                   (r.work_days * r.hours_per_day) AS TotalHours,
                   r.hourly_cost AS HourlyCost,
                   (r.work_days * r.hours_per_day * r.hourly_cost) AS TotalCost,
                   r.markup_value AS MarkupValue,
                   (r.work_days * r.hours_per_day * r.hourly_cost * r.markup_value) AS TotalSale
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.project_id = @pid AND s.is_enabled = 1
            ORDER BY r.sort_order", new { pid = projectId }).ToList();

        var resourcesBySection = resources.GroupBy(r => (int)r.SectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── CONSUNTIVO: usa view v_timesheet_with_section ────────────
        var actuals = c.Query<dynamic>(@"
            SELECT pcs.id AS CostSectionId,
                   vt.employee_id AS EmployeeId,
                   vt.employee_name AS EmployeeName,
                   vt.phase_name AS PhaseName,
                   vt.entry_type AS EntryType,
                   vt.work_date AS WorkDate,
                   vt.hours AS Hours,
                   vt.hourly_cost AS HourlyCost
            FROM v_timesheet_with_section vt
            JOIN project_cost_sections pcs
                 ON pcs.project_id = vt.project_id
                AND pcs.template_id = vt.cost_section_template_id
            WHERE vt.project_id = @pid
              AND vt.cost_section_template_id IS NOT NULL
              AND pcs.is_enabled = 1
            ORDER BY vt.employee_name, vt.work_date, vt.phase_name",
            new { pid = projectId }).ToList();

        // Ore orfane (fasi senza cost_section_template_id)
        var orphanActuals = c.Query<dynamic>(@"
            SELECT vt.employee_id AS EmployeeId,
                   vt.employee_name AS EmployeeName,
                   vt.phase_name AS PhaseName,
                   vt.entry_type AS EntryType,
                   vt.work_date AS WorkDate,
                   vt.hours AS Hours,
                   vt.hourly_cost AS HourlyCost
            FROM v_timesheet_with_section vt
            WHERE vt.project_id = @pid
              AND vt.cost_section_template_id IS NULL
            ORDER BY vt.employee_name, vt.work_date, vt.phase_name",
            new { pid = projectId }).ToList();

        var actualsBySection = actuals.GroupBy(a => (int)a.CostSectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── BUILD RESULT ───────────────────────────────────────────
        var colorMap = new Dictionary<string, int>
        {
            { "GESTIONE", 1 }, { "PRESCHIERAMENTO", 2 },
            { "INSTALLAZIONE", 3 }, { "OPZIONE", 4 }
        };

        var grouped = sections.GroupBy(s => (string)s.group_name)
            .OrderBy(g => colorMap.TryGetValue(g.Key, out int o) ? o : 99);

        var result = new BudgetVsActualData { ProjectId = projectId };

        foreach (var grp in grouped)
        {
            var groupDto = new BvaGroupDto
            {
                GroupName = grp.Key,
                SortOrder = colorMap.TryGetValue(grp.Key, out int o) ? o : 99
            };

            foreach (var sec in grp.OrderBy(s => (int)s.sort_order))
            {
                int secId = (int)sec.id;
                var sectionDto = new BvaSectionDto
                {
                    SectionName = (string)sec.name,
                    SectionType = (string)sec.section_type
                };

                // Preventivo
                if (resourcesBySection.TryGetValue(secId, out var resList))
                {
                    foreach (var r in resList)
                    {
                        sectionDto.BudgetResources.Add(new BvaBudgetResourceDto
                        {
                            ResourceName = (string)r.ResourceName,
                            WorkDays = (decimal)r.WorkDays,
                            HoursPerDay = (decimal)r.HoursPerDay,
                            TotalHours = (decimal)r.TotalHours,
                            HourlyCost = (decimal)r.HourlyCost,
                            TotalCost = (decimal)r.TotalCost,
                            MarkupValue = (decimal)r.MarkupValue,
                            TotalSale = (decimal)r.TotalSale
                        });
                    }
                    sectionDto.BudgetHours = sectionDto.BudgetResources.Sum(r => r.TotalHours);
                    sectionDto.BudgetCost = sectionDto.BudgetResources.Sum(r => r.TotalCost);
                    sectionDto.BudgetSale = sectionDto.BudgetResources.Sum(r => r.TotalSale);
                }

                // Consuntivo
                if (actualsBySection.TryGetValue(secId, out var actList))
                    BuildActualEmployees(sectionDto, actList);

                groupDto.Sections.Add(sectionDto);
            }

            groupDto.BudgetHours = groupDto.Sections.Sum(s => s.BudgetHours);
            groupDto.BudgetCost = groupDto.Sections.Sum(s => s.BudgetCost);
            groupDto.ActualHours = groupDto.Sections.Sum(s => s.ActualHours);
            groupDto.ActualCost = groupDto.Sections.Sum(s => s.ActualCost);
            result.Groups.Add(groupDto);
        }

        // Sezione orfana
        if (orphanActuals.Any())
        {
            var orphanGroup = new BvaGroupDto { GroupName = "NON ASSEGNATO", SortOrder = 999 };
            var orphanSection = new BvaSectionDto { SectionName = "Fasi senza sezione costo" };
            BuildActualEmployees(orphanSection, orphanActuals);
            orphanGroup.Sections.Add(orphanSection);
            orphanGroup.ActualHours = orphanSection.ActualHours;
            orphanGroup.ActualCost = orphanSection.ActualCost;
            result.Groups.Add(orphanGroup);
        }

        result.TotalBudgetHours = result.Groups.Sum(g => g.BudgetHours);
        result.TotalBudgetCost = result.Groups.Sum(g => g.BudgetCost);
        result.TotalActualHours = result.Groups.Sum(g => g.ActualHours);
        result.TotalActualCost = result.Groups.Sum(g => g.ActualCost);

        return Ok(ApiResponse<BudgetVsActualData>.Ok(result));
    }

    /// <summary>
    /// Raggruppa le entry per dipendente e popola ActualEmployees della sezione
    /// </summary>
    private static void BuildActualEmployees(BvaSectionDto sectionDto, IEnumerable<dynamic> entries)
    {
        var byEmployee = entries.GroupBy(a => (int)a.EmployeeId);
        foreach (var empGroup in byEmployee)
        {
            var empDto = new BvaActualEmployeeDto
            {
                EmployeeName = (string)empGroup.First().EmployeeName
            };
            foreach (var a in empGroup)
            {
                decimal hours = (decimal)a.Hours;
                decimal hCost = (decimal)a.HourlyCost;
                empDto.Details.Add(new BvaActualDetailDto
                {
                    WorkDate = (DateTime)a.WorkDate,
                    PhaseName = (string)a.PhaseName,
                    EntryType = (string)a.EntryType,
                    Hours = hours,
                    HourlyCost = hCost,
                    TotalCost = hours * hCost
                });
            }
            empDto.TotalHours = empDto.Details.Sum(d => d.Hours);
            empDto.TotalCost = empDto.Details.Sum(d => d.TotalCost);
            sectionDto.ActualEmployees.Add(empDto);
        }
        sectionDto.ActualHours = sectionDto.ActualEmployees.Sum(e => e.TotalHours);
        sectionDto.ActualCost = sectionDto.ActualEmployees.Sum(e => e.TotalCost);
    }
}