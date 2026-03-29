using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/preventivi/{quoteId}/costing")]
[Authorize]
public class PreventiviCostingController : ControllerBase
{
    private readonly DbService _db;
    private readonly QuoteDbService _qdb;
    public PreventiviCostingController(DbService db, QuoteDbService qdb) { _db = db; _qdb = qdb; }

    [HttpPut("sections/{sectionId}/departments")]
    public IActionResult SetSectionDepartments(int quoteId, int sectionId, [FromBody] SectionDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM quote_cost_section_departments WHERE quote_cost_section_id=@sectionId", new { sectionId }, tx);
        foreach (int deptId in req.DepartmentIds)
            c.Execute("INSERT INTO quote_cost_section_departments (quote_cost_section_id, department_id) VALUES (@sectionId, @deptId)", new { sectionId, deptId }, tx);
        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [HttpGet("available-templates")]
    public IActionResult GetAvailableTemplates(int quoteId)
    {
        using var c = _db.Open();
        var allGroups = c.Query<CostSectionGroupDto>("SELECT id, name, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups WHERE is_active=1 ORDER BY sort_order").ToList();
        var allTemplates = c.Query<CostSectionTemplateDto>(@"SELECT t.id, t.name, t.section_type AS SectionType, t.group_id AS GroupId, g.name AS GroupName, t.is_default AS IsDefault, t.sort_order AS SortOrder FROM cost_section_templates t JOIN cost_section_groups g ON g.id = t.group_id WHERE t.is_active=1 ORDER BY t.sort_order").ToList();
        var usedTemplateIds = c.Query<int?>("SELECT template_id FROM quote_cost_sections WHERE quote_id=@quoteId AND template_id IS NOT NULL", new { quoteId }).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var usedGroupNames = c.Query<string>("SELECT DISTINCT group_name FROM quote_cost_sections WHERE quote_id=@quoteId", new { quoteId }).ToHashSet();
        var availableTemplates = allTemplates.Where(t => !usedTemplateIds.Contains(t.Id)).ToList();
        var availableGroups = allGroups.Where(g => !usedGroupNames.Contains(g.Name) || availableTemplates.Any(t => t.GroupId == g.Id)).ToList();
        return Ok(ApiResponse<object>.Ok(new { Groups = availableGroups, Templates = availableTemplates }));
    }

    [HttpGet]
    public IActionResult GetAll(int quoteId)
    {
        using var c = _db.Open();
        int secCount = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quote_cost_sections WHERE quote_id=@quoteId", new { quoteId });
        if (secCount == 0)
            return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData { ProjectId = quoteId, IsInitialized = false }));
        var sections = c.Query<ProjectCostSectionDto>(@"SELECT id, quote_id AS ProjectId, template_id AS TemplateId, name, section_type AS SectionType, group_name AS GroupName, sort_order AS SortOrder, is_enabled AS IsEnabled, contingency_pct AS ContingencyPct, margin_pct AS MarginPct, contingency_pinned AS ContingencyPinned, margin_pinned AS MarginPinned, COALESCE(is_shadowed,0) AS IsShadowed FROM quote_cost_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();
        var sectionDepts = c.Query<(int SectionId, int DepartmentId)>(@"SELECT quote_cost_section_id AS SectionId, department_id AS DepartmentId FROM quote_cost_section_departments osd JOIN quote_cost_sections os ON os.id = osd.quote_cost_section_id WHERE os.quote_id=@quoteId", new { quoteId }).ToList();
        foreach (var sec in sections) sec.DepartmentIds = sectionDepts.Where(d => d.SectionId == sec.Id).Select(d => d.DepartmentId).ToList();
        var allResources = c.Query<ProjectCostResourceDto>(@"SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId, r.resource_name AS ResourceName, r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay, r.hourly_cost AS HourlyCost, r.markup_value AS MarkupValue, r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm, r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel, r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance, r.sort_order AS SortOrder FROM quote_cost_resources r JOIN quote_cost_sections s ON s.id = r.section_id WHERE s.quote_id=@quoteId ORDER BY r.sort_order", new { quoteId }).ToList();
        foreach (var sec in sections) sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();
        var matSections = c.Query<ProjectMaterialSectionDto>(@"SELECT id, quote_id AS ProjectId, category_id AS CategoryId, name, markup_value AS MarkupValue, commission_markup AS CommissionMarkup, sort_order AS SortOrder, is_enabled AS IsEnabled FROM quote_material_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();
        var allItems = c.Query<ProjectMaterialItemDto>(@"SELECT i.id, i.section_id AS SectionId, i.parent_item_id AS ParentItemId, i.product_id AS ProductId, i.variant_id AS VariantId, COALESCE(i.code,'') AS Code, i.description AS Description, i.description_rtf AS DescriptionRtf, i.quantity AS Quantity, i.unit_cost AS UnitCost, i.markup_value AS MarkupValue, i.item_type AS ItemType, i.sort_order AS SortOrder, i.contingency_pct AS ContingencyPct, i.margin_pct AS MarginPct, i.contingency_pinned AS ContingencyPinned, i.margin_pinned AS MarginPinned, COALESCE(i.is_shadowed,0) AS IsShadowed, COALESCE(i.is_active,1) AS IsActive FROM quote_material_items i JOIN quote_material_sections s ON s.id = i.section_id WHERE s.quote_id=@quoteId ORDER BY i.sort_order", new { quoteId }).ToList();
        foreach (var ms in matSections) ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();
        var pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"SELECT id, quote_id AS ProjectId, contingency_pct AS ContingencyPct, negotiation_margin_pct AS NegotiationMarginPct, travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup FROM quote_pricing WHERE quote_id=@quoteId", new { quoteId }) ?? new ProjectPricingDto { ProjectId = quoteId };
        return Ok(ApiResponse<ProjectCostingData>.Ok(new ProjectCostingData { ProjectId = quoteId, IsInitialized = true, CostSections = sections, MaterialSections = matSections, Pricing = pricing }));
    }

    [HttpGet("sections/{sectionId}/employees")]
    public IActionResult GetEmployeesForSection(int quoteId, int sectionId)
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeCostLookup>(@"SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS FullName, MAX(d.code) AS DepartmentCode, MAX(d.hourly_cost) AS HourlyCost, MAX(d.default_markup) AS DefaultMarkup FROM employees e JOIN employee_departments ed ON ed.employee_id = e.id JOIN departments d ON d.id = ed.department_id JOIN quote_cost_section_departments osd ON osd.department_id = d.id WHERE osd.quote_cost_section_id = @sectionId AND e.status <> 'TERMINATED' GROUP BY e.id, e.first_name, e.last_name ORDER BY e.last_name", new { sectionId }).ToList();
        return Ok(ApiResponse<List<EmployeeCostLookup>>.Ok(rows));
    }

    [HttpPost("sections")]
    public IActionResult AddSection(int quoteId, [FromBody] ProjectCostSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_cost_sections (quote_id, template_id, name, section_type, group_name, sort_order, is_enabled) VALUES (@QuoteId, @TemplateId, @Name, @SectionType, @GroupName, @SortOrder, @IsEnabled); SELECT LAST_INSERT_ID();", new { QuoteId = quoteId, req.TemplateId, req.Name, req.SectionType, req.GroupName, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione aggiunta"));
    }

    [HttpPatch("sections/{id}/field")]
    public IActionResult UpdateSectionField(int quoteId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "is_enabled", "sort_order" };
        string? error = _db.UpdateField("quote_cost_sections", id, req.Field, req.Value, allowed,
            "quote_id=@quoteId", new { quoteId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("sections/{id}")]
    public IActionResult DeleteSection(int quoteId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM quote_cost_sections WHERE id=@id AND quote_id=@quoteId", new { id, quoteId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    [HttpPost("resources")]
    public IActionResult AddResource(int quoteId, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day, hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance, sort_order) VALUES (@SectionId, @EmployeeId, @ResourceName, @WorkDays, @HoursPerDay, @HourlyCost, @MarkupValue, @NumTrips, @KmPerTrip, @CostPerKm, @DailyFood, @DailyHotel, @AllowanceDays, @DailyAllowance, @SortOrder); SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Risorsa aggiunta"));
    }

    [HttpPut("resources/{id}")]
    public IActionResult UpdateResource(int quoteId, int id, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"UPDATE quote_cost_resources SET employee_id=@EmployeeId, resource_name=@ResourceName, work_days=@WorkDays, hours_per_day=@HoursPerDay, hourly_cost=@HourlyCost, markup_value=@MarkupValue, num_trips=@NumTrips, km_per_trip=@KmPerTrip, cost_per_km=@CostPerKm, daily_food=@DailyFood, daily_hotel=@DailyHotel, allowance_days=@AllowanceDays, daily_allowance=@DailyAllowance, sort_order=@SortOrder WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Risorsa aggiornata"));
    }

    [HttpDelete("resources/{id}")]
    public IActionResult DeleteResource(int quoteId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM quote_cost_resources WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Risorsa eliminata"));
    }

    [HttpPost("material-sections")]
    public IActionResult AddMaterialSection(int quoteId, [FromBody] ProjectMaterialSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_material_sections (quote_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled) VALUES (@QuoteId, @CategoryId, @Name, @MarkupValue, @CommissionMarkup, @SortOrder, @IsEnabled); SELECT LAST_INSERT_ID();", new { QuoteId = quoteId, req.CategoryId, req.Name, req.MarkupValue, req.CommissionMarkup, req.SortOrder, req.IsEnabled });
        return Ok(ApiResponse<int>.Ok(id, "Sezione materiale aggiunta"));
    }

    [HttpPatch("material-sections/{id}/field")]
    public IActionResult UpdateMaterialSectionField(int quoteId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "markup_value", "commission_markup", "is_enabled", "sort_order" };
        string? error = _db.UpdateField("quote_material_sections", id, req.Field, req.Value, allowed,
            "quote_id=@quoteId", new { quoteId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("material-sections/{id}")]
    public IActionResult DeleteMaterialSection(int quoteId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM quote_material_sections WHERE id=@id AND quote_id=@quoteId", new { id, quoteId });
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    [HttpPost("material-items")]
    public IActionResult AddMaterialItem(int quoteId, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id, code, description, description_rtf, quantity, unit_cost, markup_value, item_type, sort_order, is_active) VALUES (@SectionId, @ParentItemId, @ProductId, @VariantId, @Code, @Description, @DescriptionRtf, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder, @IsActive); SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Materiale aggiunto"));
    }

    [HttpPost("material-items/{parentId}/variant")]
    public IActionResult AddMaterialVariant(int quoteId, int parentId, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        var parent = c.QueryFirstOrDefault<dynamic>("SELECT section_id FROM quote_material_items WHERE id=@parentId", new { parentId });
        if (parent == null) return NotFound(ApiResponse<string>.Fail("Prodotto padre non trovato"));
        req.SectionId = (int)parent.section_id;
        req.ParentItemId = parentId;
        // Varianti locali sempre in coda
        int maxSort = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order),0) FROM quote_material_items WHERE parent_item_id=@parentId", new { parentId });
        req.SortOrder = maxSort + 1;
        int id = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id, code, description, description_rtf, quantity, unit_cost, markup_value, item_type, sort_order, is_active) VALUES (@SectionId, @ParentItemId, @ProductId, @VariantId, @Code, @Description, @DescriptionRtf, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder, @IsActive); SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Variante aggiunta"));
    }

    [HttpPut("material-items/{id}")]
    public IActionResult UpdateMaterialItem(int quoteId, int id, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"UPDATE quote_material_items SET description=@Description, code=@Code, description_rtf=@DescriptionRtf, quantity=@Quantity, unit_cost=@UnitCost, markup_value=@MarkupValue, item_type=@ItemType, sort_order=@SortOrder, is_active=@IsActive WHERE id=@Id", req);
        return Ok(ApiResponse<string>.Ok("", "Materiale aggiornato"));
    }

    [HttpDelete("material-items/{id}")]
    public IActionResult DeleteMaterialItem(int quoteId, int id)
    {
        using var c = _db.Open();
        // Elimina anche figli (varianti)
        c.Execute("DELETE FROM quote_material_items WHERE parent_item_id=@id", new { id });
        c.Execute("DELETE FROM quote_material_items WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Materiale eliminato"));
    }

    /// <summary>Toggle is_active su una variante materiale</summary>
    [HttpPatch("material-items/{id}/toggle-active")]
    public IActionResult ToggleMaterialItemActive(int quoteId, int id, [FromBody] ToggleActiveRequest req)
    {
        using var c = _db.Open();
        c.Execute("UPDATE quote_material_items SET is_active=@IsActive WHERE id=@id", new { req.IsActive, id });
        return Ok(ApiResponse<string>.Ok("", "Stato aggiornato"));
    }

    /// <summary>Clona un prodotto materiale (parent + varianti)</summary>
    [HttpPost("material-items/{id}/clone")]
    public IActionResult CloneMaterialItem(int quoteId, int id)
    {
        using var c = _db.Open();
        var item = c.QueryFirstOrDefault<ProjectMaterialItemDto>(@"SELECT id, section_id AS SectionId, parent_item_id AS ParentItemId, product_id AS ProductId, variant_id AS VariantId, COALESCE(code,'') AS Code, description AS Description, description_rtf AS DescriptionRtf, quantity AS Quantity, unit_cost AS UnitCost, markup_value AS MarkupValue, item_type AS ItemType, sort_order AS SortOrder, COALESCE(is_active,1) AS IsActive FROM quote_material_items WHERE id=@id", new { id });
        if (item == null) return NotFound();

        using var tx = c.BeginTransaction();
        // Clona parent
        int newParentId = (int)c.ExecuteScalar<long>(@"INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id, code, description, description_rtf, quantity, unit_cost, markup_value, item_type, sort_order, is_active) VALUES (@SectionId, @ParentItemId, @ProductId, @VariantId, @Code, @Description, @DescriptionRtf, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder, @IsActive); SELECT LAST_INSERT_ID();", item, tx);

        // Clona varianti figlie
        var children = c.Query<ProjectMaterialItemDto>(@"SELECT id, section_id AS SectionId, product_id AS ProductId, variant_id AS VariantId, COALESCE(code,'') AS Code, description AS Description, description_rtf AS DescriptionRtf, quantity AS Quantity, unit_cost AS UnitCost, markup_value AS MarkupValue, item_type AS ItemType, sort_order AS SortOrder, COALESCE(is_active,1) AS IsActive FROM quote_material_items WHERE parent_item_id=@id", new { id }, tx).ToList();
        foreach (var child in children)
        {
            child.ParentItemId = newParentId;
            c.Execute(@"INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id, code, description, description_rtf, quantity, unit_cost, markup_value, item_type, sort_order, is_active) VALUES (@SectionId, @ParentItemId, @ProductId, @VariantId, @Code, @Description, @DescriptionRtf, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder, @IsActive)", child, tx);
        }
        tx.Commit();
        return Ok(ApiResponse<int>.Ok(newParentId, "Prodotto clonato"));
    }

    /// <summary>Aggiorna locale da catalogo (refresh varianti + prezzi)</summary>
    [HttpPost("material-items/{id}/refresh-from-catalog")]
    public IActionResult RefreshFromCatalog(int quoteId, int id)
    {
        using var c = _db.Open();
        using var cmsConn = _qdb.Open();

        var item = c.QueryFirstOrDefault<dynamic>("SELECT product_id, section_id FROM quote_material_items WHERE id=@id AND parent_item_id IS NULL", new { id });
        if (item == null) return NotFound(ApiResponse<string>.Fail("Prodotto non trovato"));
        int? productId = (int?)item.product_id;
        if (productId == null) return BadRequest(ApiResponse<string>.Fail("Nessun prodotto catalogo collegato"));

        // Leggi dati catalogo
        var product = cmsConn.QueryFirstOrDefault<dynamic>("SELECT name, description_rtf FROM quote_products WHERE id=@productId", new { productId });
        if (product == null) return NotFound(ApiResponse<string>.Fail("Prodotto non trovato nel catalogo"));
        var catVariants = cmsConn.Query<dynamic>("SELECT id, code, name, cost_price, markup_value FROM quote_product_variants WHERE product_id=@productId ORDER BY id", new { productId }).ToList();

        using var tx = c.BeginTransaction();
        // Aggiorna parent
        c.Execute("UPDATE quote_material_items SET description=@Name, description_rtf=@Rtf WHERE id=@id",
            new { Name = (string)product.name, Rtf = (string?)product.description_rtf, id }, tx);

        // Elimina vecchie varianti
        c.Execute("DELETE FROM quote_material_items WHERE parent_item_id=@id", new { id }, tx);

        // Reinserisci dal catalogo
        int sectionId = (int)item.section_id;
        foreach (var v in catVariants)
        {
            decimal markup = v.markup_value != null ? (decimal)v.markup_value : 1.300m;
            c.Execute(@"INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id, code, description, quantity, unit_cost, markup_value, item_type, sort_order, is_active) VALUES (@sid, @pid, @prodId, @vid, @code, @name, 0, @cost, @markup, 'MATERIAL', 0, 1)",
                new { sid = sectionId, pid = id, prodId = productId, vid = (int)v.id, code = (string?)v.code ?? "", name = (string)v.name, cost = (decimal)v.cost_price, markup }, tx);
        }
        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Aggiornato dal catalogo"));
    }

    /// <summary>Push locale → catalogo (aggiorna prezzi varianti nel catalogo)</summary>
    [HttpPost("material-items/{id}/push-to-catalog")]
    public IActionResult PushToCatalog(int quoteId, int id)
    {
        using var c = _db.Open();
        using var cmsConn = _qdb.Open();

        var item = c.QueryFirstOrDefault<dynamic>("SELECT product_id FROM quote_material_items WHERE id=@id AND parent_item_id IS NULL", new { id });
        if (item == null) return NotFound();
        int? productId = (int?)item.product_id;
        if (productId == null) return BadRequest(ApiResponse<string>.Fail("Nessun prodotto catalogo collegato"));

        // Leggi varianti locali con variant_id
        var localVariants = c.Query<dynamic>("SELECT variant_id, description, unit_cost, markup_value, code FROM quote_material_items WHERE parent_item_id=@id AND variant_id IS NOT NULL", new { id }).ToList();

        using var tx = cmsConn.BeginTransaction();
        // Aggiorna parent description_rtf
        var parentLocal = c.QueryFirstOrDefault<dynamic>("SELECT description, description_rtf FROM quote_material_items WHERE id=@id", new { id });
        if (parentLocal != null)
            cmsConn.Execute("UPDATE quote_products SET name=@Name, description_rtf=@Rtf WHERE id=@productId",
                new { Name = (string)parentLocal.description, Rtf = (string?)parentLocal.description_rtf, productId }, tx);

        // Aggiorna varianti
        foreach (var v in localVariants)
        {
            cmsConn.Execute("UPDATE quote_product_variants SET name=@name, cost_price=@cost, markup_value=@markup, code=@code WHERE id=@vid",
                new { name = (string)v.description, cost = (decimal)v.unit_cost, markup = (decimal)v.markup_value, code = (string?)v.code, vid = (int)v.variant_id }, tx);
        }
        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Catalogo aggiornato"));
    }

    [HttpPut("pricing")]
    public IActionResult UpdatePricing(int quoteId, [FromBody] ProjectPricingDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE quote_pricing SET contingency_pct=@ContingencyPct, negotiation_margin_pct=@NegotiationMarginPct, travel_markup=@TravelMarkup, allowance_markup=@AllowanceMarkup WHERE quote_id=@quoteId", new { req.ContingencyPct, req.NegotiationMarginPct, req.TravelMarkup, req.AllowanceMarkup, quoteId });
        return Ok(ApiResponse<string>.Ok("", "Prezzi aggiornati"));
    }

    [HttpGet("pricing-distribution")]
    public IActionResult GetPricingDistribution(int quoteId)
    {
        using var c = _db.Open();
        var rows = c.Query<PricingDistributionRow>(@"SELECT id, quote_id AS OfferId, section_type AS SectionType, section_id AS SectionId, section_name AS SectionName, sale_amount AS SaleAmount, contingency_pct AS ContingencyPct, margin_pct AS MarginPct FROM quote_pricing_distribution WHERE quote_id=@quoteId ORDER BY section_type, id", new { quoteId }).ToList();
        return Ok(ApiResponse<List<PricingDistributionRow>>.Ok(rows));
    }

    [HttpPost("pricing-distribution/generate")]
    public IActionResult GeneratePricingDistribution(int quoteId)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM quote_pricing_distribution WHERE quote_id=@quoteId", new { quoteId });
        var costSections = c.Query<dynamic>(@"SELECT s.id, s.name, COALESCE(SUM(r.work_days * r.hours_per_day * r.hourly_cost * r.markup_value), 0) AS sale FROM quote_cost_sections s LEFT JOIN quote_cost_resources r ON r.section_id = s.id WHERE s.quote_id = @quoteId AND s.is_enabled = 1 GROUP BY s.id, s.name ORDER BY s.sort_order", new { quoteId }).ToList();
        var matSections = c.Query<dynamic>(@"SELECT s.id, s.name, COALESCE(SUM(i.quantity * i.unit_cost * i.markup_value), 0) AS sale FROM quote_material_sections s LEFT JOIN quote_material_items i ON i.section_id = s.id WHERE s.quote_id = @quoteId AND s.is_enabled = 1 GROUP BY s.id, s.name ORDER BY s.sort_order", new { quoteId }).ToList();
        decimal totalSale = costSections.Sum(s => (decimal)s.sale) + matSections.Sum(s => (decimal)s.sale);
        if (totalSale == 0) return Ok(ApiResponse<string>.Ok("", "Nessun importo"));
        foreach (var s in costSections)
        {
            decimal weight = (decimal)s.sale / totalSale;
            c.Execute(@"INSERT INTO quote_pricing_distribution (quote_id, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct) VALUES (@quoteId, 'COST', @secId, @name, @sale, @weight, @weight)", new { quoteId, secId = (int)s.id, name = (string)s.name, sale = (decimal)s.sale, weight });
        }
        foreach (var s in matSections)
        {
            decimal weight = (decimal)s.sale / totalSale;
            c.Execute(@"INSERT INTO quote_pricing_distribution (quote_id, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct) VALUES (@quoteId, 'MATERIAL', @secId, @name, @sale, @weight, @weight)", new { quoteId, secId = (int)s.id, name = (string)s.name, sale = (decimal)s.sale, weight });
        }
        return Ok(ApiResponse<string>.Ok("", "Distribuzione generata"));
    }

    [HttpPut("pricing-distribution/{id}")]
    public IActionResult UpdatePricingDistribution(int quoteId, int id, [FromBody] PricingDistributionRow req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE quote_pricing_distribution SET contingency_pct=@ContingencyPct, margin_pct=@MarginPct WHERE id=@Id AND quote_id=@quoteId", new { req.ContingencyPct, req.MarginPct, Id = id, quoteId });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpPut("pricing-distribution/rebalance")]
    public IActionResult RebalancePricingDistribution(int quoteId, [FromBody] RebalanceRequest req)
    {
        using var c = _db.Open();
        var rows = c.Query<PricingDistributionRow>(@"SELECT id, contingency_pct AS ContingencyPct, margin_pct AS MarginPct, sale_amount AS SaleAmount FROM quote_pricing_distribution WHERE quote_id=@quoteId", new { quoteId }).ToList();
        var fixedRow = rows.FirstOrDefault(r => r.Id == req.FixedRowId);
        if (fixedRow == null) return BadRequest(ApiResponse<string>.Fail("Riga non trovata"));
        if (req.Field == "contingency")
        {
            decimal fixedPct = req.NewValue;
            decimal remaining = 1m - fixedPct;
            decimal othersTotal = rows.Where(r => r.Id != req.FixedRowId).Sum(r => r.ContingencyPct);
            c.Execute("UPDATE quote_pricing_distribution SET contingency_pct=@pct WHERE id=@id", new { pct = fixedPct, id = req.FixedRowId });
            foreach (var r in rows.Where(r => r.Id != req.FixedRowId))
            {
                decimal newPct = othersTotal > 0 ? r.ContingencyPct / othersTotal * remaining : remaining / (rows.Count - 1);
                c.Execute("UPDATE quote_pricing_distribution SET contingency_pct=@pct WHERE id=@id", new { pct = newPct, id = r.Id });
            }
        }
        else
        {
            decimal fixedPct = req.NewValue;
            decimal remaining = 1m - fixedPct;
            decimal othersTotal = rows.Where(r => r.Id != req.FixedRowId).Sum(r => r.MarginPct);
            c.Execute("UPDATE quote_pricing_distribution SET margin_pct=@pct WHERE id=@id", new { pct = fixedPct, id = req.FixedRowId });
            foreach (var r in rows.Where(r => r.Id != req.FixedRowId))
            {
                decimal newPct = othersTotal > 0 ? r.MarginPct / othersTotal * remaining : remaining / (rows.Count - 1);
                c.Execute("UPDATE quote_pricing_distribution SET margin_pct=@pct WHERE id=@id", new { pct = newPct, id = r.Id });
            }
        }
        return Ok(ApiResponse<string>.Ok("", "Ribilanciato"));
    }

    [HttpPut("sections/{id}/distribution")]
    public IActionResult UpdateSectionDistribution(int quoteId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE quote_cost_sections SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed WHERE id=@Id AND quote_id=@quoteId", new { ContPct = req.ContingencyPct, MargPct = req.MarginPct, ContPin = req.ContingencyPinned, MargPin = req.MarginPinned, Shadowed = req.IsShadowed, Id = id, quoteId });
        return Ok(ApiResponse<string>.Ok("", "Distribuzione aggiornata"));
    }

    [HttpPut("material-items/{id}/distribution")]
    public IActionResult UpdateMaterialItemDistribution(int quoteId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE quote_material_items SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed WHERE id=@Id", new { ContPct = req.ContingencyPct, MargPct = req.MarginPct, ContPin = req.ContingencyPinned, MargPin = req.MarginPinned, Shadowed = req.IsShadowed, Id = id });
        return Ok(ApiResponse<string>.Ok("", "Distribuzione materiale aggiornata"));
    }

    /// <summary>Salva tutte le distribuzioni in un unico batch (1 request invece di N)</summary>
    [HttpPut("distributions/batch")]
    public IActionResult SaveAllDistributionsBatch(int quoteId, [FromBody] BatchDistributionRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            foreach (var s in req.Sections ?? new())
                c.Execute(@"UPDATE quote_cost_sections SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed WHERE id=@Id AND quote_id=@qid",
                    new { ContPct = s.ContingencyPct, MargPct = s.MarginPct, ContPin = s.ContingencyPinned, MargPin = s.MarginPinned, Shadowed = s.IsShadowed, s.Id, qid = quoteId }, tx);
            foreach (var m in req.MaterialItems ?? new())
                c.Execute(@"UPDATE quote_material_items SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed WHERE id=@Id",
                    new { ContPct = m.ContingencyPct, MargPct = m.MarginPct, ContPin = m.ContingencyPinned, MargPin = m.MarginPinned, Shadowed = m.IsShadowed, m.Id }, tx);
            tx.Commit();
            return Ok(ApiResponse<string>.Ok("", $"Salvate {(req.Sections?.Count ?? 0) + (req.MaterialItems?.Count ?? 0)} distribuzioni"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail(ex.Message));
        }
    }
}
