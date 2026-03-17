using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/offers/{offerId}/costing")]
[Authorize]
public class OfferCostingController : ControllerBase
{
    private readonly DbService _db;
    public OfferCostingController(DbService db) => _db = db;

    [HttpPut("sections/{sectionId}/departments")]
    public IActionResult SetSectionDepartments(int offerId, int sectionId, [FromBody] SectionDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM offer_cost_section_departments WHERE offer_cost_section_id=@sectionId",
            new { sectionId }, tx);

        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO offer_cost_section_departments (offer_cost_section_id, department_id) VALUES (@sectionId, @deptId)",
                new { sectionId, deptId }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [HttpGet("available-templates")]
    public IActionResult GetAvailableTemplates(int offerId)
    {
        using var c = _db.Open();

        var allGroups = c.Query<CostSectionGroupDto>(
            "SELECT id, name, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups WHERE is_active=1 ORDER BY sort_order").ToList();

        var allTemplates = c.Query<CostSectionTemplateDto>(@"
            SELECT t.id, t.name, t.section_type AS SectionType, t.group_id AS GroupId,
                   g.name AS GroupName, t.is_default AS IsDefault, t.sort_order AS SortOrder
            FROM cost_section_templates t
            JOIN cost_section_groups g ON g.id = t.group_id
            WHERE t.is_active=1
            ORDER BY t.sort_order").ToList();

        var usedTemplateIds = c.Query<int?>(
            "SELECT template_id FROM offer_cost_sections WHERE offer_id=@offerId AND template_id IS NOT NULL",
            new { offerId }).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        var usedGroupNames = c.Query<string>(
            "SELECT DISTINCT group_name FROM offer_cost_sections WHERE offer_id=@offerId",
            new { offerId }).ToHashSet();

        var availableTemplates = allTemplates.Where(t => !usedTemplateIds.Contains(t.Id)).ToList();

        var availableGroups = allGroups.Where(g =>
            !usedGroupNames.Contains(g.Name) ||
            availableTemplates.Any(t => t.GroupId == g.Id)
        ).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Groups = availableGroups,
            Templates = availableTemplates
        }));
    }

    [HttpGet]
    public IActionResult GetAll(int offerId)
    {
        using var c = _db.Open();

        int secCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM offer_cost_sections WHERE offer_id=@offerId", new { offerId });

        if (secCount == 0)
            return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData { ProjectId = offerId, IsInitialized = false }));

        var sections = c.Query<ProjectCostSectionDto>(@"
            SELECT id, offer_id AS ProjectId, template_id AS TemplateId, name,
                   section_type AS SectionType, group_name AS GroupName,
                   sort_order AS SortOrder, is_enabled AS IsEnabled,
                   contingency_pct AS ContingencyPct, margin_pct AS MarginPct
            FROM offer_cost_sections WHERE offer_id=@offerId ORDER BY sort_order",
            new { offerId }).ToList();

        var sectionDepts = c.Query<(int SectionId, int DepartmentId)>(@"
            SELECT offer_cost_section_id AS SectionId, department_id AS DepartmentId
            FROM offer_cost_section_departments osd
            JOIN offer_cost_sections os ON os.id = osd.offer_cost_section_id
            WHERE os.offer_id=@offerId", new { offerId }).ToList();

        foreach (var sec in sections)
            sec.DepartmentIds = sectionDepts.Where(d => d.SectionId == sec.Id).Select(d => d.DepartmentId).ToList();

        var allResources = c.Query<ProjectCostResourceDto>(@"
            SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId,
                   r.resource_name AS ResourceName,
                   r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay, r.hourly_cost AS HourlyCost,
                   r.markup_value AS MarkupValue,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance,
                   r.sort_order AS SortOrder
            FROM offer_cost_resources r
            JOIN offer_cost_sections s ON s.id = r.section_id
            WHERE s.offer_id=@offerId ORDER BY r.sort_order",
            new { offerId }).ToList();

        foreach (var sec in sections)
            sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();

        var matSections = c.Query<ProjectMaterialSectionDto>(@"
            SELECT id, offer_id AS ProjectId, category_id AS CategoryId, name,
                   markup_value AS MarkupValue, commission_markup AS CommissionMarkup,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM offer_material_sections WHERE offer_id=@offerId ORDER BY sort_order",
            new { offerId }).ToList();

        var allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.description AS Description,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost,
                   i.markup_value AS MarkupValue, i.item_type AS ItemType,
                   i.sort_order AS SortOrder
            FROM offer_material_items i
            JOIN offer_material_sections s ON s.id = i.section_id
            WHERE s.offer_id=@offerId ORDER BY i.sort_order",
            new { offerId }).ToList();

        foreach (var ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();

        var pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, offer_id AS ProjectId,
                   contingency_pct AS ContingencyPct,
                   negotiation_margin_pct AS NegotiationMarginPct,
                   travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup
            FROM offer_pricing WHERE offer_id=@offerId",
            new { offerId }) ?? new ProjectPricingDto { ProjectId = offerId };

        return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData
        {
            ProjectId = offerId,
            IsInitialized = true,
            CostSections = sections,
            MaterialSections = matSections,
            Pricing = pricing
        }));
    }

    // ── DIPENDENTI PER SEZIONE ──

    [HttpGet("sections/{sectionId}/employees")]
    public IActionResult GetEmployeesForSection(int offerId, int sectionId)
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeCostLookup>(@"
            SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS FullName,
                   MAX(d.code) AS DepartmentCode, MAX(d.hourly_cost) AS HourlyCost,
                   MAX(d.default_markup) AS DefaultMarkup
            FROM employees e
            JOIN employee_departments ed ON ed.employee_id = e.id
            JOIN departments d ON d.id = ed.department_id
            JOIN offer_cost_section_departments osd ON osd.department_id = d.id
            WHERE osd.offer_cost_section_id = @sectionId
              AND e.status <> 'TERMINATED'
            GROUP BY e.id, e.first_name, e.last_name
            ORDER BY e.last_name",
            new { sectionId }).ToList();
        return Ok(ApiResponse<List<EmployeeCostLookup>>.Ok(rows));
    }

    // ── SEZIONI COSTO ──

    [HttpPost("sections")]
    public IActionResult AddSection(int offerId, [FromBody] ProjectCostSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO offer_cost_sections (offer_id, template_id, name, section_type, group_name, sort_order, is_enabled)
            VALUES (@OfferId, @TemplateId, @Name, @SectionType, @GroupName, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { OfferId = offerId, req.TemplateId, req.Name, req.SectionType, req.GroupName, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione aggiunta"));
    }

    [HttpPatch("sections/{id}/field")]
    public IActionResult UpdateSectionField(int offerId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "is_enabled", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE offer_cost_sections SET {req.Field}=@Value WHERE id=@id AND offer_id=@offerId",
            new { Value = req.Value, id, offerId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("sections/{id}")]
    public IActionResult DeleteSection(int offerId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM offer_cost_sections WHERE id=@id AND offer_id=@offerId", new { id, offerId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    // ── RISORSE ──

    [HttpPost("resources")]
    public IActionResult AddResource(int offerId, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO offer_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day, hourly_cost, markup_value,
                num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance, sort_order)
            VALUES (@SectionId, @EmployeeId, @ResourceName, @WorkDays, @HoursPerDay, @HourlyCost, @MarkupValue,
                @NumTrips, @KmPerTrip, @CostPerKm, @DailyFood, @DailyHotel, @AllowanceDays, @DailyAllowance, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Risorsa aggiunta"));
    }

    [HttpPut("resources/{id}")]
    public IActionResult UpdateResource(int offerId, int id, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE offer_cost_resources SET employee_id=@EmployeeId, resource_name=@ResourceName,
                work_days=@WorkDays, hours_per_day=@HoursPerDay, hourly_cost=@HourlyCost, markup_value=@MarkupValue,
                num_trips=@NumTrips, km_per_trip=@KmPerTrip, cost_per_km=@CostPerKm,
                daily_food=@DailyFood, daily_hotel=@DailyHotel,
                allowance_days=@AllowanceDays, daily_allowance=@DailyAllowance, sort_order=@SortOrder
            WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Risorsa aggiornata"));
    }

    [HttpDelete("resources/{id}")]
    public IActionResult DeleteResource(int offerId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM offer_cost_resources WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Risorsa eliminata"));
    }

    // ── MATERIALI ──

    [HttpPost("material-sections")]
    public IActionResult AddMaterialSection(int offerId, [FromBody] ProjectMaterialSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO offer_material_sections (offer_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
            VALUES (@OfferId, @CategoryId, @Name, @MarkupValue, @CommissionMarkup, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { OfferId = offerId, req.CategoryId, req.Name, req.MarkupValue, req.CommissionMarkup, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione materiale aggiunta"));
    }

    [HttpPatch("material-sections/{id}/field")]
    public IActionResult UpdateMaterialSectionField(int offerId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "markup_value", "commission_markup", "is_enabled", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE offer_material_sections SET {req.Field}=@Value WHERE id=@id AND offer_id=@offerId",
            new { Value = req.Value, id, offerId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("material-sections/{id}")]
    public IActionResult DeleteMaterialSection(int offerId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM offer_material_sections WHERE id=@id AND offer_id=@offerId", new { id, offerId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    [HttpPost("material-items")]
    public IActionResult AddMaterialItem(int offerId, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO offer_material_items (section_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
            VALUES (@SectionId, @Description, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Materiale aggiunto"));
    }

    [HttpPut("material-items/{id}")]
    public IActionResult UpdateMaterialItem(int offerId, int id, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE offer_material_items SET description=@Description, quantity=@Quantity,
                unit_cost=@UnitCost, markup_value=@MarkupValue, item_type=@ItemType, sort_order=@SortOrder
            WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Materiale aggiornato"));
    }

    [HttpDelete("material-items/{id}")]
    public IActionResult DeleteMaterialItem(int offerId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM offer_material_items WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Materiale eliminato"));
    }

    // ── PRICING ──

    [HttpPut("pricing")]
    public IActionResult UpdatePricing(int offerId, [FromBody] ProjectPricingDto req)
    {
        using var c = _db.Open();
        c.Execute(@"
            UPDATE offer_pricing SET
                contingency_pct=@ContingencyPct,
                negotiation_margin_pct=@NegotiationMarginPct,
                travel_markup=@TravelMarkup, allowance_markup=@AllowanceMarkup
            WHERE offer_id=@offerId",
            new
            {
                req.ContingencyPct,
                req.NegotiationMarginPct,
                req.TravelMarkup,
                req.AllowanceMarkup,
                offerId
            });
        return Ok(ApiResponse<string>.Ok("", "Prezzi aggiornati"));
    }

    // ── DISTRIBUZIONE PREZZI ──

    /// <summary>GET /api/offers/{offerId}/costing/pricing-distribution</summary>
    [HttpGet("pricing-distribution")]
    public IActionResult GetPricingDistribution(int offerId)
    {
        using var c = _db.Open();
        var rows = c.Query<PricingDistributionRow>(@"
        SELECT id, offer_id AS OfferId, section_type AS SectionType,
               section_id AS SectionId, section_name AS SectionName,
               sale_amount AS SaleAmount, contingency_pct AS ContingencyPct,
               margin_pct AS MarginPct
        FROM offer_pricing_distribution WHERE offer_id=@offerId
        ORDER BY section_type, id",
            new { offerId }).ToList();
        return Ok(ApiResponse<List<PricingDistributionRow>>.Ok(rows));
    }

    /// <summary>POST /api/offers/{offerId}/costing/pricing-distribution/generate</summary>
    [HttpPost("pricing-distribution/generate")]
    public IActionResult GeneratePricingDistribution(int offerId)
    {
        using var c = _db.Open();

        // Cancella distribuzione esistente
        c.Execute("DELETE FROM offer_pricing_distribution WHERE offer_id=@offerId", new { offerId });

        // Prendi totale vendita per ogni sezione costo
        var costSections = c.Query<dynamic>(@"
        SELECT s.id, s.name,
               COALESCE(SUM(r.work_days * r.hours_per_day * r.hourly_cost * r.markup_value), 0) AS sale
        FROM offer_cost_sections s
        LEFT JOIN offer_cost_resources r ON r.section_id = s.id
        WHERE s.offer_id = @offerId AND s.is_enabled = 1
        GROUP BY s.id, s.name
        ORDER BY s.sort_order", new { offerId }).ToList();

        // Prendi totale vendita per ogni sezione materiale
        var matSections = c.Query<dynamic>(@"
        SELECT s.id, s.name,
               COALESCE(SUM(i.quantity * i.unit_cost * i.markup_value), 0) AS sale
        FROM offer_material_sections s
        LEFT JOIN offer_material_items i ON i.section_id = s.id
        WHERE s.offer_id = @offerId AND s.is_enabled = 1
        GROUP BY s.id, s.name
        ORDER BY s.sort_order", new { offerId }).ToList();

        decimal totalSale = costSections.Sum(s => (decimal)s.sale) + matSections.Sum(s => (decimal)s.sale);
        if (totalSale == 0) return Ok(ApiResponse<string>.Ok("", "Nessun importo"));

        foreach (var s in costSections)
        {
            decimal weight = (decimal)s.sale / totalSale;
            c.Execute(@"INSERT INTO offer_pricing_distribution 
            (offer_id, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct)
            VALUES (@offerId, 'COST', @secId, @name, @sale, @weight, @weight)",
                new { offerId, secId = (int)s.id, name = (string)s.name, sale = (decimal)s.sale, weight });
        }

        foreach (var s in matSections)
        {
            decimal weight = (decimal)s.sale / totalSale;
            c.Execute(@"INSERT INTO offer_pricing_distribution 
            (offer_id, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct)
            VALUES (@offerId, 'MATERIAL', @secId, @name, @sale, @weight, @weight)",
                new { offerId, secId = (int)s.id, name = (string)s.name, sale = (decimal)s.sale, weight });
        }

        return Ok(ApiResponse<string>.Ok("", "Distribuzione generata"));
    }

    /// <summary>PUT /api/offers/{offerId}/costing/pricing-distribution/{id}</summary>
    [HttpPut("pricing-distribution/{id}")]
    public IActionResult UpdatePricingDistribution(int offerId, int id, [FromBody] PricingDistributionRow req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE offer_pricing_distribution 
        SET contingency_pct=@ContingencyPct, margin_pct=@MarginPct
        WHERE id=@Id AND offer_id=@offerId",
            new { req.ContingencyPct, req.MarginPct, Id = id, offerId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    /// <summary>PUT /api/offers/{offerId}/costing/pricing-distribution/rebalance</summary>
    [HttpPut("pricing-distribution/rebalance")]
    public IActionResult RebalancePricingDistribution(int offerId, [FromBody] RebalanceRequest req)
    {
        using var c = _db.Open();

        // Prendi tutte le righe
        var rows = c.Query<PricingDistributionRow>(@"
        SELECT id, contingency_pct AS ContingencyPct, margin_pct AS MarginPct, sale_amount AS SaleAmount
        FROM offer_pricing_distribution WHERE offer_id=@offerId",
            new { offerId }).ToList();

        // La riga modificata ha il valore fisso
        var fixedRow = rows.FirstOrDefault(r => r.Id == req.FixedRowId);
        if (fixedRow == null) return BadRequest(ApiResponse<string>.Fail("Riga non trovata"));

        if (req.Field == "contingency")
        {
            decimal fixedPct = req.NewValue;
            decimal remaining = 1m - fixedPct;
            decimal othersTotal = rows.Where(r => r.Id != req.FixedRowId).Sum(r => r.ContingencyPct);

            c.Execute("UPDATE offer_pricing_distribution SET contingency_pct=@pct WHERE id=@id",
                new { pct = fixedPct, id = req.FixedRowId });

            foreach (var r in rows.Where(r => r.Id != req.FixedRowId))
            {
                decimal newPct = othersTotal > 0 ? r.ContingencyPct / othersTotal * remaining : remaining / (rows.Count - 1);
                c.Execute("UPDATE offer_pricing_distribution SET contingency_pct=@pct WHERE id=@id",
                    new { pct = newPct, id = r.Id });
            }
        }
        else // margin
        {
            decimal fixedPct = req.NewValue;
            decimal remaining = 1m - fixedPct;
            decimal othersTotal = rows.Where(r => r.Id != req.FixedRowId).Sum(r => r.MarginPct);

            c.Execute("UPDATE offer_pricing_distribution SET margin_pct=@pct WHERE id=@id",
                new { pct = fixedPct, id = req.FixedRowId });

            foreach (var r in rows.Where(r => r.Id != req.FixedRowId))
            {
                decimal newPct = othersTotal > 0 ? r.MarginPct / othersTotal * remaining : remaining / (rows.Count - 1);
                c.Execute("UPDATE offer_pricing_distribution SET margin_pct=@pct WHERE id=@id",
                    new { pct = newPct, id = r.Id });
            }
        }

        return Ok(ApiResponse<string>.Ok("", "Ribilanciato"));
    }
    [HttpPut("sections/{id}/distribution")]
    public IActionResult UpdateSectionDistribution(int offerId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE offer_cost_sections 
        SET contingency_pct=@ContPct, margin_pct=@MargPct, 
            contingency_pinned=@ContPin, margin_pinned=@MargPin 
        WHERE id=@Id AND offer_id=@offerId",
            new
            {
                ContPct = req.ContingencyPct,
                MargPct = req.MarginPct,
                ContPin = req.ContingencyPinned,
                MargPin = req.MarginPinned,
                Id = id,
                offerId
            });
        return Ok(ApiResponse<string>.Ok("", "Distribuzione aggiornata"));
    }
}
