using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects/{projectId}/costing")]
[Authorize]
public class ProjectCostingController : ControllerBase
{
    private readonly DbService _db;
    public ProjectCostingController(DbService db) => _db = db;

    [HttpPost("init")]
    public IActionResult Initialize(int projectId)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        try
        {
            int exists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_markup_values WHERE project_id=@projectId",
                new { projectId }, tx);
            if (exists > 0)
                return BadRequest(ApiResponse<string>.Fail("Configurazione già inizializzata"));

            // 1. Copia K ricarico
            c.Execute(@"
                INSERT INTO project_markup_values (project_id, original_code, description, coefficient_type, markup_value, hourly_cost, sort_order)
                SELECT @projectId, code, description, coefficient_type, markup_value, hourly_cost, sort_order
                FROM markup_coefficients WHERE is_active=1 ORDER BY sort_order",
                new { projectId }, tx);

            // 2. Copia sezioni costo + reparti associati
            var templates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default=1 AND t.is_active=1
                ORDER BY t.sort_order", transaction: tx).ToList();

            foreach (var tmpl in templates)
            {
                int newSectionId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @section_type, @group_name, @sort_order, 1);
                    SELECT LAST_INSERT_ID();",
                    new { projectId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, tmpl.sort_order }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_section_departments (project_cost_section_id, department_id)
                    SELECT @newSectionId, department_id
                    FROM cost_section_template_departments
                    WHERE section_template_id = @templateId",
                    new { newSectionId, templateId = (int)tmpl.id }, tx);
            }

            // 3. Copia categorie materiali
            var categories = c.Query<dynamic>(@"
                SELECT mc.id, mc.name, mc.markup_code, COALESCE(mk.markup_value, 1.000) AS markup_value, mc.sort_order
                FROM material_categories mc
                LEFT JOIN markup_coefficients mk ON mk.code = mc.markup_code
                WHERE mc.is_active=1 ORDER BY mc.sort_order", transaction: tx).ToList();

            foreach (var cat in categories)
            {
                c.Execute(@"
                    INSERT INTO project_material_sections (project_id, category_id, name, markup_code, markup_value, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @markup_code, @markup_value, @sort_order, 1)",
                    new { projectId, cat.id, cat.name, cat.markup_code, cat.markup_value, cat.sort_order }, tx);
            }

            // 4. Scheda prezzi default
            c.Execute("INSERT INTO project_pricing (project_id) VALUES (@projectId)", new { projectId }, tx);

            tx.Commit();
            return Ok(ApiResponse<string>.Ok("", "Configurazione inizializzata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpGet]
    public IActionResult GetAll(int projectId)
    {
        using var c = _db.Open();

        int mkCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_markup_values WHERE project_id=@projectId", new { projectId });

        if (mkCount == 0)
            return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData { ProjectId = projectId, IsInitialized = false }));

        var markups = c.Query<ProjectMarkupValueDto>(@"
            SELECT id, project_id AS ProjectId, original_code AS OriginalCode, description,
                   coefficient_type AS CoefficientType, markup_value AS MarkupValue,
                   hourly_cost AS HourlyCost, sort_order AS SortOrder
            FROM project_markup_values WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

        var sections = c.Query<ProjectCostSectionDto>(@"
            SELECT id, project_id AS ProjectId, template_id AS TemplateId, name,
                   section_type AS SectionType, group_name AS GroupName,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM project_cost_sections WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

        // Reparti per sezione
        var sectionDepts = c.Query<(int SectionId, int DepartmentId)>(@"
            SELECT project_cost_section_id AS SectionId, department_id AS DepartmentId
            FROM project_cost_section_departments psd
            JOIN project_cost_sections ps ON ps.id = psd.project_cost_section_id
            WHERE ps.project_id=@projectId", new { projectId }).ToList();

        foreach (var sec in sections)
            sec.DepartmentIds = sectionDepts.Where(d => d.SectionId == sec.Id).Select(d => d.DepartmentId).ToList();

        var allResources = c.Query<ProjectCostResourceDto>(@"
            SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId,
                   r.resource_name AS ResourceName,
                   r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay, r.hourly_cost AS HourlyCost,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance,
                   r.sort_order AS SortOrder
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.project_id=@projectId ORDER BY r.sort_order",
            new { projectId }).ToList();

        foreach (var sec in sections)
            sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();

        var matSections = c.Query<ProjectMaterialSectionDto>(@"
            SELECT id, project_id AS ProjectId, category_id AS CategoryId, name,
                   markup_code AS MarkupCode, markup_value AS MarkupValue,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM project_material_sections WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

        var allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.description AS Description,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost, i.sort_order AS SortOrder
            FROM project_material_items i
            JOIN project_material_sections s ON s.id = i.section_id
            WHERE s.project_id=@projectId ORDER BY i.sort_order",
            new { projectId }).ToList();

        foreach (var ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();

        var pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, project_id AS ProjectId, structure_costs_pct AS StructureCostsPct,
                   contingency_pct AS ContingencyPct, risk_warranty_pct AS RiskWarrantyPct,
                   negotiation_margin_pct AS NegotiationMarginPct
            FROM project_pricing WHERE project_id=@projectId",
            new { projectId }) ?? new ProjectPricingDto { ProjectId = projectId };

        return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData
        {
            ProjectId = projectId,
            IsInitialized = true,
            Markups = markups,
            CostSections = sections,
            MaterialSections = matSections,
            Pricing = pricing
        }));
    }

    // ══════════════════════════════════════════════════════════════
    // DIPENDENTI PER SEZIONE (filtrati per reparti associati)
    // ══════════════════════════════════════════════════════════════

    [HttpGet("sections/{sectionId}/employees")]
    public IActionResult GetEmployeesForSection(int projectId, int sectionId)
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeCostLookup>(@"
            SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS FullName,
                   MAX(d.code) AS DepartmentCode, MAX(d.hourly_cost) AS HourlyCost
            FROM employees e
            JOIN employee_departments ed ON ed.employee_id = e.id
            JOIN departments d ON d.id = ed.department_id
            JOIN project_cost_section_departments psd ON psd.department_id = d.id
            WHERE psd.project_cost_section_id = @sectionId
              AND e.status <> 'TERMINATED'
            GROUP BY e.id, e.first_name, e.last_name
            ORDER BY e.last_name",
            new { sectionId }).ToList();
        return Ok(ApiResponse<List<EmployeeCostLookup>>.Ok(rows));
    }

    // ══════════════════════════════════════════════════════════════
    // MARKUP
    // ══════════════════════════════════════════════════════════════

    [HttpPatch("markup/{id}")]
    public IActionResult UpdateMarkup(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "markup_value", "hourly_cost" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE project_markup_values SET {req.Field}=@Value WHERE id=@id AND project_id=@projectId",
            new { Value = req.Value, id, projectId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    // ══════════════════════════════════════════════════════════════
    // SEZIONI COSTO
    // ══════════════════════════════════════════════════════════════

    [HttpPost("sections")]
    public IActionResult AddSection(int projectId, [FromBody] ProjectCostSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
            VALUES (@ProjectId, @TemplateId, @Name, @SectionType, @GroupName, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { ProjectId = projectId, req.TemplateId, req.Name, req.SectionType, req.GroupName, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione aggiunta"));
    }

    [HttpPatch("sections/{id}/field")]
    public IActionResult UpdateSectionField(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "is_enabled", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE project_cost_sections SET {req.Field}=@Value WHERE id=@id AND project_id=@projectId",
            new { Value = req.Value, id, projectId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("sections/{id}")]
    public IActionResult DeleteSection(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_cost_sections WHERE id=@id AND project_id=@projectId", new { id, projectId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    // ══════════════════════════════════════════════════════════════
    // RISORSE
    // ══════════════════════════════════════════════════════════════

    [HttpPost("resources")]
    public IActionResult AddResource(int projectId, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day, hourly_cost,
                num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance, sort_order)
            VALUES (@SectionId, @EmployeeId, @ResourceName, @WorkDays, @HoursPerDay, @HourlyCost,
                @NumTrips, @KmPerTrip, @CostPerKm, @DailyFood, @DailyHotel, @AllowanceDays, @DailyAllowance, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Risorsa aggiunta"));
    }

    [HttpPut("resources/{id}")]
    public IActionResult UpdateResource(int projectId, int id, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE project_cost_resources SET employee_id=@EmployeeId, resource_name=@ResourceName,
                work_days=@WorkDays, hours_per_day=@HoursPerDay, hourly_cost=@HourlyCost,
                num_trips=@NumTrips, km_per_trip=@KmPerTrip, cost_per_km=@CostPerKm,
                daily_food=@DailyFood, daily_hotel=@DailyHotel,
                allowance_days=@AllowanceDays, daily_allowance=@DailyAllowance, sort_order=@SortOrder
            WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Risorsa aggiornata"));
    }

    [HttpDelete("resources/{id}")]
    public IActionResult DeleteResource(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_cost_resources WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Risorsa eliminata"));
    }

    // ══════════════════════════════════════════════════════════════
    // MATERIALI
    // ══════════════════════════════════════════════════════════════

    [HttpPost("material-sections")]
    public IActionResult AddMaterialSection(int projectId, [FromBody] ProjectMaterialSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_material_sections (project_id, category_id, name, markup_code, markup_value, sort_order, is_enabled)
            VALUES (@ProjectId, @CategoryId, @Name, @MarkupCode, @MarkupValue, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { ProjectId = projectId, req.CategoryId, req.Name, req.MarkupCode, req.MarkupValue, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione materiale aggiunta"));
    }

    [HttpPatch("material-sections/{id}/field")]
    public IActionResult UpdateMaterialSectionField(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "markup_value", "is_enabled", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE project_material_sections SET {req.Field}=@Value WHERE id=@id AND project_id=@projectId",
            new { Value = req.Value, id, projectId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("material-sections/{id}")]
    public IActionResult DeleteMaterialSection(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_material_sections WHERE id=@id AND project_id=@projectId", new { id, projectId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    [HttpPost("material-items")]
    public IActionResult AddMaterialItem(int projectId, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_material_items (section_id, description, quantity, unit_cost, sort_order)
            VALUES (@SectionId, @Description, @Quantity, @UnitCost, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Materiale aggiunto"));
    }

    [HttpPut("material-items/{id}")]
    public IActionResult UpdateMaterialItem(int projectId, int id, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE project_material_items SET description=@Description, quantity=@Quantity,
                unit_cost=@UnitCost, sort_order=@SortOrder
            WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Materiale aggiornato"));
    }

    [HttpDelete("material-items/{id}")]
    public IActionResult DeleteMaterialItem(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_material_items WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Materiale eliminato"));
    }

    // ══════════════════════════════════════════════════════════════
    // PRICING
    // ══════════════════════════════════════════════════════════════

    [HttpPut("pricing")]
    public IActionResult UpdatePricing(int projectId, [FromBody] ProjectPricingDto req)
    {
        using var c = _db.Open();
        c.Execute(@"
            UPDATE project_pricing SET structure_costs_pct=@StructureCostsPct,
                contingency_pct=@ContingencyPct, risk_warranty_pct=@RiskWarrantyPct,
                negotiation_margin_pct=@NegotiationMarginPct
            WHERE project_id=@projectId",
            new { req.StructureCostsPct, req.ContingencyPct, req.RiskWarrantyPct, req.NegotiationMarginPct, projectId });
        return Ok(ApiResponse<string>.Ok("", "Prezzi aggiornati"));
    }
}
