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

        // ── CONSUNTIVO: ore timesheet aggregate per sezione costo ──
        // Catena: timesheet_entries → project_phases → phase_templates.cost_section_template_id
        //         cost_section_template_id → cost_section_templates.id = project_cost_sections.template_id
        var actuals = c.Query<dynamic>(@"
            SELECT pcs.id AS CostSectionId,
                   CONCAT(emp.first_name, ' ', emp.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName,
                   te.entry_type AS EntryType,
                   SUM(te.hours) AS Hours,
                   COALESCE(d.hourly_cost, 0) AS HourlyCost
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN employees emp ON emp.id = te.employee_id
            LEFT JOIN employee_departments ed ON ed.employee_id = emp.id
            LEFT JOIN departments d ON d.id = ed.department_id
            JOIN project_cost_sections pcs 
                 ON pcs.project_id = pp.project_id 
                AND pcs.template_id = pt.cost_section_template_id
            WHERE pp.project_id = @pid
              AND pt.cost_section_template_id IS NOT NULL
              AND pcs.is_enabled = 1
            GROUP BY pcs.id, emp.id, emp.first_name, emp.last_name, 
                     pp.id, pp.custom_name, pt.name, te.entry_type, d.hourly_cost
            ORDER BY emp.last_name, pt.name, te.entry_type",
            new { pid = projectId }).ToList();

        // Ore orfane (fasi senza cost_section_template_id)
        var orphanActuals = c.Query<dynamic>(@"
            SELECT CONCAT(emp.first_name, ' ', emp.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName,
                   te.entry_type AS EntryType,
                   SUM(te.hours) AS Hours,
                   COALESCE(d.hourly_cost, 0) AS HourlyCost
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN employees emp ON emp.id = te.employee_id
            LEFT JOIN employee_departments ed ON ed.employee_id = emp.id
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE pp.project_id = @pid
              AND pt.cost_section_template_id IS NULL
            GROUP BY emp.id, emp.first_name, emp.last_name, 
                     pp.id, pp.custom_name, pt.name, te.entry_type, d.hourly_cost
            ORDER BY emp.last_name, pt.name",
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
                {
                    foreach (var a in actList)
                    {
                        decimal hours = (decimal)a.Hours;
                        decimal hCost = (decimal)a.HourlyCost;
                        sectionDto.ActualEntries.Add(new BvaActualEntryDto
                        {
                            EmployeeName = (string)a.EmployeeName,
                            PhaseName = (string)a.PhaseName,
                            EntryType = (string)a.EntryType,
                            Hours = hours,
                            HourlyCost = hCost,
                            TotalCost = hours * hCost
                        });
                    }
                    sectionDto.ActualHours = sectionDto.ActualEntries.Sum(e => e.Hours);
                    sectionDto.ActualCost = sectionDto.ActualEntries.Sum(e => e.TotalCost);
                }

                groupDto.Sections.Add(sectionDto);
            }

            groupDto.BudgetHours = groupDto.Sections.Sum(s => s.BudgetHours);
            groupDto.BudgetCost = groupDto.Sections.Sum(s => s.BudgetCost);
            groupDto.ActualHours = groupDto.Sections.Sum(s => s.ActualHours);
            groupDto.ActualCost = groupDto.Sections.Sum(s => s.ActualCost);
            result.Groups.Add(groupDto);
        }

        // Sezione orfana (fasi senza sezione costo)
        if (orphanActuals.Any())
        {
            var orphanGroup = new BvaGroupDto { GroupName = "NON ASSEGNATO", SortOrder = 999 };
            var orphanSection = new BvaSectionDto { SectionName = "Fasi senza sezione costo" };
            foreach (var a in orphanActuals)
            {
                decimal hours = (decimal)a.Hours;
                decimal hCost = (decimal)a.HourlyCost;
                orphanSection.ActualEntries.Add(new BvaActualEntryDto
                {
                    EmployeeName = (string)a.EmployeeName,
                    PhaseName = (string)a.PhaseName,
                    EntryType = (string)a.EntryType,
                    Hours = hours,
                    HourlyCost = hCost,
                    TotalCost = hours * hCost
                });
            }
            orphanSection.ActualHours = orphanSection.ActualEntries.Sum(e => e.Hours);
            orphanSection.ActualCost = orphanSection.ActualEntries.Sum(e => e.TotalCost);
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
}
