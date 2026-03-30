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
                "SELECT COUNT(*) FROM project_cost_sections WHERE project_id=@projectId",
                new { projectId }, tx);
            if (exists > 0)
                return BadRequest(ApiResponse<string>.Fail("Configurazione già inizializzata"));

            var templates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default_project=1 AND t.is_active=1
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
                SELECT id, name, default_markup, default_commission_markup, sort_order
                FROM material_categories
                WHERE is_active=1 ORDER BY sort_order", transaction: tx).ToList();

            foreach (var cat in categories)
            {
                c.Execute(@"
                    INSERT INTO project_material_sections (project_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @default_markup, @default_commission_markup, @sort_order, 1)",
                    new { projectId, cat.id, cat.name, cat.default_markup, cat.default_commission_markup, cat.sort_order }, tx);
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

    [HttpPut("sections/{sectionId}/departments")]
    public IActionResult SetSectionDepartments(int projectId, int sectionId, [FromBody] SectionDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM project_cost_section_departments WHERE project_cost_section_id=@sectionId",
            new { sectionId }, tx);

        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO project_cost_section_departments (project_cost_section_id, department_id) VALUES (@sectionId, @deptId)",
                new { sectionId, deptId }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [HttpGet("available-templates")]
    public IActionResult GetAvailableTemplates(int projectId)
    {
        using var c = _db.Open();

        // Gruppi template attivi
        var allGroups = c.Query<CostSectionGroupDto>(
            "SELECT id, name, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups WHERE is_active=1 ORDER BY sort_order").ToList();

        // Sezioni template attive (tutte, non solo is_default)
        var allTemplates = c.Query<CostSectionTemplateDto>(@"
            SELECT t.id, t.name, t.section_type AS SectionType, t.group_id AS GroupId,
                   g.name AS GroupName, t.is_default_project AS IsDefault, t.is_default_quote AS IsDefaultQuote, t.sort_order AS SortOrder
            FROM cost_section_templates t
            JOIN cost_section_groups g ON g.id = t.group_id
            WHERE t.is_active=1
            ORDER BY t.sort_order").ToList();

        // Sezioni già presenti nella commessa (per template_id)
        var usedTemplateIds = c.Query<int?>(
            "SELECT template_id FROM project_cost_sections WHERE project_id=@projectId AND template_id IS NOT NULL",
            new { projectId }).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        // Gruppi già presenti nella commessa
        var usedGroupNames = c.Query<string>(
            "SELECT DISTINCT group_name FROM project_cost_sections WHERE project_id=@projectId",
            new { projectId }).ToHashSet();

        // Filtra: template non ancora usati
        var availableTemplates = allTemplates.Where(t => !usedTemplateIds.Contains(t.Id)).ToList();

        // Gruppi che hanno almeno un template disponibile O non sono ancora nella commessa
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
    public IActionResult GetAll(int projectId)
    {
        using var c = _db.Open();

        int secCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_cost_sections WHERE project_id=@projectId", new { projectId });

        if (secCount == 0)
            return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData { ProjectId = projectId, IsInitialized = false }));

        var sections = c.Query<ProjectCostSectionDto>(@"
            SELECT id, project_id AS ProjectId, template_id AS TemplateId, name,
                   section_type AS SectionType, group_name AS GroupName,
                   sort_order AS SortOrder, is_enabled AS IsEnabled,
                   contingency_pct AS ContingencyPct, margin_pct AS MarginPct,
                   contingency_pinned AS ContingencyPinned, margin_pinned AS MarginPinned,
                   COALESCE(is_shadowed,0) AS IsShadowed
            FROM project_cost_sections WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

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
                   r.markup_value AS MarkupValue,
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
                   markup_value AS MarkupValue, commission_markup AS CommissionMarkup,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM project_material_sections WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

        var allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.description AS Description,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost,
                   i.markup_value AS MarkupValue, i.item_type AS ItemType,
                   i.sort_order AS SortOrder,
                   i.contingency_pct AS ContingencyPct, i.margin_pct AS MarginPct,
                   i.contingency_pinned AS ContingencyPinned, i.margin_pinned AS MarginPinned,
                   COALESCE(i.is_shadowed,0) AS IsShadowed
            FROM project_material_items i
            JOIN project_material_sections s ON s.id = i.section_id
            WHERE s.project_id=@projectId ORDER BY i.sort_order",
            new { projectId }).ToList();

        foreach (var ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();

        var pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, project_id AS ProjectId,
                   contingency_pct AS ContingencyPct,
                   negotiation_margin_pct AS NegotiationMarginPct,
                   travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup
            FROM project_pricing WHERE project_id=@projectId",
            new { projectId }) ?? new ProjectPricingDto { ProjectId = projectId };

        return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData
        {
            ProjectId = projectId,
            IsInitialized = true,
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
                   MAX(d.code) AS DepartmentCode, MAX(d.hourly_cost) AS HourlyCost,
                   MAX(d.default_markup) AS DefaultMarkup
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
        string? error = _db.UpdateField("project_cost_sections", id, req.Field, req.Value, allowed,
            "project_id=@projectId", new { projectId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
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
            INSERT INTO project_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day, hourly_cost, markup_value,
                num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance, sort_order)
            VALUES (@SectionId, @EmployeeId, @ResourceName, @WorkDays, @HoursPerDay, @HourlyCost, @MarkupValue,
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
                work_days=@WorkDays, hours_per_day=@HoursPerDay, hourly_cost=@HourlyCost, markup_value=@MarkupValue,
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
            INSERT INTO project_material_sections (project_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
            VALUES (@ProjectId, @CategoryId, @Name, @MarkupValue, @CommissionMarkup, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { ProjectId = projectId, req.CategoryId, req.Name, req.MarkupValue, req.CommissionMarkup, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione materiale aggiunta"));
    }

    [HttpPatch("material-sections/{id}/field")]
    public IActionResult UpdateMaterialSectionField(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "markup_value", "commission_markup", "is_enabled", "sort_order" };
        string? error = _db.UpdateField("project_material_sections", id, req.Field, req.Value, allowed,
            "project_id=@projectId", new { projectId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
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
            INSERT INTO project_material_items (section_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
            VALUES (@SectionId, @Description, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder);
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
                unit_cost=@UnitCost, markup_value=@MarkupValue, item_type=@ItemType, sort_order=@SortOrder
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

    /// <summary>Autocomplete descrizioni materiali già usate in tutti i progetti.</summary>
    [HttpGet("material-items/suggestions")]
    public IActionResult GetMaterialSuggestions(int projectId, [FromQuery] string q = "")
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(ApiResponse<List<string>>.Ok(new()));

        using var c = _db.Open();
        List<string> suggestions = c.Query<string>(@"
            SELECT DISTINCT description
            FROM project_material_items
            WHERE description LIKE CONCAT('%', @Query, '%')
              AND description != ''
            ORDER BY description
            LIMIT 15",
            new { Query = q.Trim() }).ToList();

        return Ok(ApiResponse<List<string>>.Ok(suggestions));
    }

    // ══════════════════════════════════════════════════════════════
    // PRICING
    // ══════════════════════════════════════════════════════════════

    [HttpPut("pricing")]
    public IActionResult UpdatePricing(int projectId, [FromBody] ProjectPricingDto req)
    {
        using var c = _db.Open();
        c.Execute(@"
            UPDATE project_pricing SET
                contingency_pct=@ContingencyPct,
                negotiation_margin_pct=@NegotiationMarginPct,
                travel_markup=@TravelMarkup, allowance_markup=@AllowanceMarkup
            WHERE project_id=@projectId",
            new
            {
                req.ContingencyPct,
                req.NegotiationMarginPct,
                req.TravelMarkup,
                req.AllowanceMarkup,
                projectId
            });
        return Ok(ApiResponse<string>.Ok("", "Prezzi aggiornati"));
    }

    [HttpPut("sections/{id}/distribution")]
    public IActionResult UpdateSectionDistribution(int projectId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE project_cost_sections
            SET contingency_pct=@ContPct, margin_pct=@MargPct,
                contingency_pinned=@ContPin, margin_pinned=@MargPin,
                is_shadowed=@Shadowed
            WHERE id=@Id AND project_id=@projectId",
            new
            {
                ContPct = req.ContingencyPct,
                MargPct = req.MarginPct,
                ContPin = req.ContingencyPinned,
                MargPin = req.MarginPinned,
                Shadowed = req.IsShadowed,
                Id = id,
                projectId
            });
        return Ok(ApiResponse<string>.Ok("", "Distribuzione aggiornata"));
    }

    [HttpPut("material-items/{id}/distribution")]
    public IActionResult UpdateMaterialItemDistribution(int projectId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE project_material_items
            SET contingency_pct=@ContPct, margin_pct=@MargPct,
                contingency_pinned=@ContPin, margin_pinned=@MargPin,
                is_shadowed=@Shadowed
            WHERE id=@Id",
            new
            {
                ContPct = req.ContingencyPct,
                MargPct = req.MarginPct,
                ContPin = req.ContingencyPinned,
                MargPin = req.MarginPinned,
                Shadowed = req.IsShadowed,
                Id = id
            });
        return Ok(ApiResponse<string>.Ok("", "Distribuzione materiale aggiornata"));
    }
}
