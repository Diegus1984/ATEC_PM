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
            SELECT s.id, s.name, s.section_type, s.group_name, s.sort_order, s.template_id,
                   COALESCE(csg.bg_color, '#6B7280') AS group_color,
                   COALESCE(csg.sort_order, 99) AS group_sort
            FROM project_cost_sections s
            LEFT JOIN cost_section_templates cst ON cst.id = s.template_id
            LEFT JOIN cost_section_groups csg ON csg.id = cst.group_id
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
                   (r.work_days * r.hours_per_day * r.hourly_cost * r.markup_value) AS TotalSale,
                   r.num_trips AS NumTrips,
                   r.km_per_trip AS KmPerTrip,
                   r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood,
                   r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays,
                   r.daily_allowance AS DailyAllowance
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.project_id = @pid AND s.is_enabled = 1
            ORDER BY r.sort_order", new { pid = projectId }).ToList();

        var resourcesBySection = resources.GroupBy(r => (int)r.SectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── ASSEGNATE: ore da phase_assignments aggregate per cost_section ──
        var assigned = c.Query<dynamic>(@"
            SELECT pcs.id AS CostSectionId,
                   SUM(pa.planned_hours) AS Hours
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN project_cost_sections pcs ON pcs.template_id = pt.cost_section_template_id
                                          AND pcs.project_id = pp.project_id
            WHERE pp.project_id = @pid AND pcs.is_enabled = 1
            GROUP BY pcs.id", new { pid = projectId }).ToList();

        var assignedBySection = assigned.ToDictionary(a => (int)a.CostSectionId);

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
        var grouped = sections.GroupBy(s => (string)s.group_name)
            .OrderBy(g => g.Min(s => (int)s.group_sort));

        var result = new BudgetVsActualData { ProjectId = projectId };

        foreach (var grp in grouped)
        {
            var groupDto = new BvaGroupDto
            {
                GroupName = grp.Key,
                Color = (string)grp.First().group_color,
                SortOrder = (int)grp.First().group_sort
            };

            foreach (var sec in grp.OrderBy(s => (int)s.sort_order))
            {
                int secId = (int)sec.id;
                var sectionDto = new BvaSectionDto
                {
                    SectionName = (string)sec.name,
                    SectionType = (string)sec.section_type,
                    TemplateId = (int?)sec.template_id
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
                            TotalSale = (decimal)r.TotalSale,
                            NumTrips = (int)r.NumTrips,
                            KmPerTrip = (decimal)r.KmPerTrip,
                            CostPerKm = (decimal)r.CostPerKm,
                            DailyFood = (decimal)r.DailyFood,
                            DailyHotel = (decimal)r.DailyHotel,
                            AllowanceDays = (int)r.AllowanceDays,
                            DailyAllowance = (decimal)r.DailyAllowance
                        });
                    }
                    sectionDto.BudgetHours = sectionDto.BudgetResources.Sum(r => r.TotalHours);
                    sectionDto.BudgetCost = sectionDto.BudgetResources.Sum(r => r.TotalCost);
                    sectionDto.BudgetSale = sectionDto.BudgetResources.Sum(r => r.TotalSale);
                    sectionDto.BudgetTravelCost = sectionDto.BudgetResources.Sum(r => r.TravelCost);
                    sectionDto.BudgetAccommodationCost = sectionDto.BudgetResources.Sum(r => r.AccommodationCost);
                    sectionDto.BudgetAllowanceCost = sectionDto.BudgetResources.Sum(r => r.AllowanceCost);
                }

                // Assegnate
                if (assignedBySection.TryGetValue(secId, out var asg))
                {
                    sectionDto.AssignedHours = (decimal)asg.Hours;
                    // Costo assegnato = ore assegnate × costo orario medio della sezione
                    decimal avgCost = sectionDto.BudgetHours > 0
                        ? sectionDto.BudgetCost / sectionDto.BudgetHours : 0;
                    sectionDto.AssignedCost = sectionDto.AssignedHours * avgCost;
                }

                // Consuntivo
                if (actualsBySection.TryGetValue(secId, out var actList))
                    BuildActualEmployees(sectionDto, actList);

                groupDto.Sections.Add(sectionDto);
            }

            groupDto.BudgetHours = groupDto.Sections.Sum(s => s.BudgetHours);
            groupDto.BudgetCost = groupDto.Sections.Sum(s => s.BudgetCost);
            groupDto.AssignedHours = groupDto.Sections.Sum(s => s.AssignedHours);
            groupDto.AssignedCost = groupDto.Sections.Sum(s => s.AssignedCost);
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
        result.TotalAssignedHours = result.Groups.Sum(g => g.AssignedHours);
        result.TotalAssignedCost = result.Groups.Sum(g => g.AssignedCost);
        result.TotalActualHours = result.Groups.Sum(g => g.ActualHours);
        result.TotalActualCost = result.Groups.Sum(g => g.ActualCost);

        // ── MATERIALI ─────────────────────────────────────────────
        var matSections = c.Query<dynamic>(@"
            SELECT id, name, markup_value, commission_markup
            FROM project_material_sections
            WHERE project_id = @pid AND is_enabled = 1
            ORDER BY sort_order", new { pid = projectId }).ToList();

        var matItemsRaw = c.Query<dynamic>(@"
            SELECT pmi.section_id AS SectionId, pmi.id AS Id, pmi.parent_item_id AS ParentItemId,
                   pmi.description AS Description, pmi.quantity AS Quantity,
                   pmi.unit_cost AS UnitCost, pmi.markup_value AS MarkupValue,
                   pmi.item_type AS ItemType
            FROM project_material_items pmi
            JOIN project_material_sections pms ON pms.id = pmi.section_id
            WHERE pms.project_id = @pid AND pms.is_enabled = 1
              AND (pmi.quantity > 0 OR pmi.unit_cost > 0)
            ORDER BY pmi.sort_order", new { pid = projectId }).ToList();

        var matItemsBySection = matItemsRaw.GroupBy(i => (int)i.SectionId)
            .ToDictionary(g => g.Key, g => g.Select(i => new BvaMaterialItemDto
            {
                Id = (int)i.Id,
                ParentItemId = (int?)i.ParentItemId,
                Description = (string)i.Description,
                Quantity = (decimal)i.Quantity,
                UnitCost = (decimal)i.UnitCost,
                MarkupValue = (decimal)i.MarkupValue,
                ItemType = (string)i.ItemType
            }).ToList());

        foreach (var ms in matSections)
        {
            int msId = (int)ms.id;
            var secDto = new BvaMaterialSectionDto
            {
                SectionName = (string)ms.name,
                MarkupValue = (decimal)ms.markup_value,
                CommissionMarkup = (decimal)ms.commission_markup
            };
            if (matItemsBySection.TryGetValue(msId, out var items))
            {
                secDto.Items = items;
                secDto.TotalNetCost = items.Sum(i => i.NetCost);
                secDto.TotalSaleCost = items.Sum(i => i.SaleCost);
            }
            result.MaterialSections.Add(secDto);
        }

        result.TotalMaterialNetCost = result.MaterialSections.Sum(s => s.TotalNetCost);
        result.TotalMaterialSaleCost = result.MaterialSections.Sum(s => s.TotalSaleCost);

        // ── SCHEDA PREZZI ─────────────────────────────────────────
        var pricing = c.QueryFirstOrDefault<dynamic>(
            "SELECT contingency_pct, negotiation_margin_pct FROM project_pricing WHERE project_id=@pid",
            new { pid = projectId });

        if (pricing != null)
        {
            // Costo vendita totale risorse (sale = cost * markup)
            decimal resourceSale = result.Groups.Sum(g => g.Sections.Sum(s => s.BudgetSale));
            // Costo trasferte totale
            decimal travelTotal = result.Groups.Sum(g => g.Sections.Sum(s => s.BudgetTotalTravelCost));
            decimal netCost = resourceSale + result.TotalMaterialSaleCost + travelTotal;

            decimal contPct = (decimal)pricing.contingency_pct;
            decimal contAmount = netCost * contPct;
            decimal offerPrice = netCost + contAmount;

            decimal negPct = (decimal)pricing.negotiation_margin_pct;
            decimal negAmount = offerPrice * negPct;
            decimal finalPrice = offerPrice + negAmount;

            result.Pricing = new BvaPricingDto
            {
                NetCost = netCost,
                ContingencyPct = contPct,
                ContingencyAmount = contAmount,
                OfferPrice = offerPrice,
                NegotiationPct = negPct,
                NegotiationAmount = negAmount,
                FinalPrice = finalPrice
            };
        }

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