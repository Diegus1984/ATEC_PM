using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Shared.Models;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/offers")]
[Authorize]
public class OffersController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    public OffersController(DbService db, NotificationService notif) { _db = db; _notif = notif; }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // ───────────────────── LIST ─────────────────────

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            using var c = _db.Open();
            var rows = c.Query<Offer>(@"
                SELECT o.id, o.offer_code AS OfferCode, o.revision, o.parent_offer_id AS ParentOfferId,
                       o.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       o.title, o.description, o.created_by AS CreatedById,
                       CONCAT(e.first_name,' ',e.last_name) AS CreatedByName,
                       o.status, o.converted_project_id AS ConvertedProjectId,
                       p.code AS ConvertedProjectCode,
                       o.created_at AS CreatedAt, o.updated_at AS UpdatedAt
                FROM offers o
                LEFT JOIN customers cu ON cu.id = o.customer_id
                LEFT JOIN employees e ON e.id = o.created_by
                LEFT JOIN projects p ON p.id = o.converted_project_id
                ORDER BY o.created_at DESC").ToList();
            return Ok(ApiResponse<List<Offer>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<Offer>>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ───────────────────── GET BY ID ─────────────────────

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var offer = c.QueryFirstOrDefault<Offer>(@"
            SELECT o.id, o.offer_code AS OfferCode, o.revision, o.parent_offer_id AS ParentOfferId,
                   o.customer_id AS CustomerId, cu.company_name AS CustomerName,
                   o.title, o.description, o.created_by AS CreatedById,
                   CONCAT(e.first_name,' ',e.last_name) AS CreatedByName,
                   o.status, o.converted_project_id AS ConvertedProjectId,
                   p.code AS ConvertedProjectCode,
                   o.created_at AS CreatedAt, o.updated_at AS UpdatedAt
            FROM offers o
            LEFT JOIN customers cu ON cu.id = o.customer_id
            LEFT JOIN employees e ON e.id = o.created_by
            LEFT JOIN projects p ON p.id = o.converted_project_id
            WHERE o.id=@Id", new { Id = id });
        if (offer == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<Offer>.Ok(offer));
    }

    // ───────────────────── CREATE ─────────────────────

    [HttpPost]
    public IActionResult Create([FromBody] OfferCreateDto req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            // Genera codice OF{anno}{progressivo 3 cifre}
            int year = DateTime.Now.Year;
            string prefix = $"OF{year}";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(offer_code, @len+1) AS UNSIGNED)),0) FROM offers WHERE offer_code LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string offerCode = $"{prefix}{(maxNum + 1):D3}";

            int offerId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO offers (offer_code, revision, customer_id, title, description, created_by, status)
                VALUES (@OfferCode, 1, @CustomerId, @Title, @Description, @CreatedBy, 'BOZZA');
                SELECT LAST_INSERT_ID()",
                new { OfferCode = offerCode, req.CustomerId, req.Title, req.Description, CreatedBy = GetCurrentEmployeeId() }, tx);

            // Init costing: copia template sezioni costo
            var templates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default=1 AND t.is_active=1
                ORDER BY t.sort_order", transaction: tx).ToList();

            foreach (var tmpl in templates)
            {
                int newSectionId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO offer_cost_sections (offer_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@offerId, @id, @name, @section_type, @group_name, @sort_order, 1);
                    SELECT LAST_INSERT_ID()",
                    new { offerId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, tmpl.sort_order }, tx);

                c.Execute(@"
                    INSERT INTO offer_cost_section_departments (offer_cost_section_id, department_id)
                    SELECT @newSectionId, department_id
                    FROM cost_section_template_departments
                    WHERE section_template_id = @templateId",
                    new { newSectionId, templateId = (int)tmpl.id }, tx);
            }

            // Init categorie materiali
            var categories = c.Query<dynamic>(@"
                SELECT id, name, default_markup, default_commission_markup, sort_order
                FROM material_categories WHERE is_active=1 ORDER BY sort_order", transaction: tx).ToList();

            foreach (var cat in categories)
            {
                c.Execute(@"
                    INSERT INTO offer_material_sections (offer_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                    VALUES (@offerId, @id, @name, @default_markup, @default_commission_markup, @sort_order, 1)",
                    new { offerId, cat.id, cat.name, cat.default_markup, cat.default_commission_markup, cat.sort_order }, tx);
            }

            // Init pricing default
            c.Execute("INSERT INTO offer_pricing (offer_id) VALUES (@offerId)", new { offerId }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(offerId, "Offerta creata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ───────────────────── UPDATE ─────────────────────

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] OfferUpdateDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE offers SET title=@Title, description=@Description, status=@Status WHERE id=@Id",
            new { req.Title, req.Description, req.Status, Id = id });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    // ───────────────────── DELETE ─────────────────────

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        var status = c.ExecuteScalar<string>("SELECT status FROM offers WHERE id=@Id", new { Id = id });
        if (status == "CONVERTITA")
            return BadRequest(ApiResponse<string>.Fail("Non puoi eliminare un'offerta già convertita"));
        c.Execute("DELETE FROM offers WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<string>.Ok("", "Eliminato"));
    }

    // ───────────────────── CREATE REVISION ─────────────────────

    [HttpPost("{id}/revision")]
    public IActionResult CreateRevision(int id)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var original = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM offers WHERE id=@Id", new { Id = id }, tx);
            if (original == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

            // Il parent è la prima revisione (parent_offer_id NULL → usa id, altrimenti parent_offer_id)
            int parentId = (int?)original.parent_offer_id ?? (int)original.id;

            // Segna la revisione corrente come SUPERATA
            c.Execute("UPDATE offers SET status='SUPERATA' WHERE id=@Id", new { Id = id }, tx);

            // Calcola prossimo numero revisione
            int nextRev = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(revision),0)+1 FROM offers WHERE id=@pid OR parent_offer_id=@pid",
                new { pid = parentId }, tx);

            // Crea nuova offerta
            int newOfferId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO offers (offer_code, revision, parent_offer_id, customer_id, title, description, created_by, status)
                VALUES (@code, @rev, @parentId, @custId, @title, @desc, @createdBy, 'BOZZA');
                SELECT LAST_INSERT_ID()",
                new
                {
                    code = (string)original.offer_code,
                    rev = nextRev,
                    parentId,
                    custId = (int)original.customer_id,
                    title = (string)original.title,
                    desc = (string)(original.description ?? ""),
                    createdBy = GetCurrentEmployeeId()
                }, tx);

            // Copia sezioni costo
            var sections = c.Query<dynamic>(
                "SELECT * FROM offer_cost_sections WHERE offer_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            foreach (var sec in sections)
            {
                int newSecId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO offer_cost_sections (offer_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@offerId, @tmplId, @name, @stype, @gname, @sort, @enabled);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        offerId = newOfferId,
                        tmplId = (int?)sec.template_id,
                        name = (string)sec.name,
                        stype = (string)sec.section_type,
                        gname = (string)sec.group_name,
                        sort = (int)sec.sort_order,
                        enabled = (bool)sec.is_enabled
                    }, tx);

                // Copia departments
                c.Execute(@"
                    INSERT INTO offer_cost_section_departments (offer_cost_section_id, department_id)
                    SELECT @newSecId, department_id FROM offer_cost_section_departments WHERE offer_cost_section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);

                // Copia risorse
                c.Execute(@"
                    INSERT INTO offer_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order)
                    SELECT @newSecId, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order
                    FROM offer_cost_resources WHERE section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);
            }

            // Copia sezioni materiali + items
            var matSections = c.Query<dynamic>(
                "SELECT * FROM offer_material_sections WHERE offer_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            foreach (var ms in matSections)
            {
                int newMsId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO offer_material_sections (offer_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                    VALUES (@offerId, @catId, @name, @markup, @commMarkup, @sort, @enabled);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        offerId = newOfferId,
                        catId = (int?)ms.category_id,
                        name = (string)ms.name,
                        markup = (decimal)ms.markup_value,
                        commMarkup = (decimal)ms.commission_markup,
                        sort = (int)ms.sort_order,
                        enabled = (bool)ms.is_enabled
                    }, tx);

                c.Execute(@"
                    INSERT INTO offer_material_items (section_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
                    SELECT @newMsId, description, quantity, unit_cost, markup_value, item_type, sort_order
                    FROM offer_material_items WHERE section_id=@oldMsId",
                    new { newMsId, oldMsId = (int)ms.id }, tx);
            }

            // Copia pricing
            c.Execute(@"
                INSERT INTO offer_pricing (offer_id, contingency_pct,
                    negotiation_margin_pct, travel_markup, allowance_markup)
                SELECT @newOfferId, contingency_pct,
                    negotiation_margin_pct, travel_markup, allowance_markup
                FROM offer_pricing WHERE offer_id=@oldOfferId",
                new { newOfferId, oldOfferId = id }, tx);

            // Copia distribuzione prezzi
            c.Execute(@"
                INSERT INTO offer_pricing_distribution (offer_id, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct)
                SELECT @newOfferId, section_type, section_id, section_name, sale_amount, contingency_pct, margin_pct
                FROM offer_pricing_distribution WHERE offer_id=@oldOfferId",
                new { newOfferId, oldOfferId = id }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newOfferId, $"Revisione {nextRev} creata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ───────────────────── CONVERT TO PROJECT ─────────────────────

    [HttpPost("{id}/convert")]
    public IActionResult ConvertToProject(int id, [FromBody] OfferConvertDto req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var offer = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM offers WHERE id=@Id", new { Id = id }, tx);
            if (offer == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
            if ((string)offer.status == "CONVERTITA")
                return BadRequest(ApiResponse<string>.Fail("Offerta già convertita"));

            // 1. Genera codice commessa AT{anno}{progressivo}
            int year = DateTime.Now.Year;
            string prefix = $"AT{year}";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(code, @len+1) AS UNSIGNED)),0) FROM projects WHERE code LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string projectCode = $"{prefix}{(maxNum + 1):D3}";

            // 2. Crea progetto
            int projectId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO projects (code, title, customer_id, pm_id, description, status, priority)
                VALUES (@Code, @Title, @CustId, @PmId, @Desc, 'DRAFT', 'MEDIUM');
                SELECT LAST_INSERT_ID()",
                new
                {
                    Code = projectCode,
                    Title = (string)offer.title,
                    CustId = (int)offer.customer_id,
                    PmId = req.PmId,
                    Desc = (string)(offer.description ?? "")
                }, tx);

            // 3. Crea fasi di default
            var phaseTemplates = c.Query("SELECT id, department_id, sort_order FROM phase_templates WHERE is_default=1 ORDER BY sort_order", transaction: tx);
            foreach (var t in phaseTemplates)
            {
                c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, department_id, sort_order)
                    VALUES (@ProjId, @TplId, @DeptId, @Sort)",
                    new { ProjId = projectId, TplId = (int)t.id, DeptId = (int?)t.department_id, Sort = (int)t.sort_order }, tx);
            }

            // 4. Copia sezioni costo offer → project (con is_from_offer)
            var offerSections = c.Query<dynamic>(
                "SELECT * FROM offer_cost_sections WHERE offer_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            foreach (var sec in offerSections)
            {
                int newSecId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@projectId, @tmplId, @name, @stype, @gname, @sort, @enabled);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        projectId,
                        tmplId = (int?)sec.template_id,
                        name = (string)sec.name,
                        stype = (string)sec.section_type,
                        gname = (string)sec.group_name,
                        sort = (int)sec.sort_order,
                        enabled = (bool)sec.is_enabled
                    }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_section_departments (project_cost_section_id, department_id)
                    SELECT @newSecId, department_id FROM offer_cost_section_departments WHERE offer_cost_section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order)
                    SELECT @newSecId, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order
                    FROM offer_cost_resources WHERE section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);
            }

            // 5. Copia materiali offer → project
            var offerMatSections = c.Query<dynamic>(
                "SELECT * FROM offer_material_sections WHERE offer_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            foreach (var ms in offerMatSections)
            {
                int newMsId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_material_sections (project_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                    VALUES (@projectId, @catId, @name, @markup, @commMarkup, @sort, @enabled);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        projectId,
                        catId = (int?)ms.category_id,
                        name = (string)ms.name,
                        markup = (decimal)ms.markup_value,
                        commMarkup = (decimal)ms.commission_markup,
                        sort = (int)ms.sort_order,
                        enabled = (bool)ms.is_enabled
                    }, tx);

                c.Execute(@"
                    INSERT INTO project_material_items (section_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
                    SELECT @newMsId, description, quantity, unit_cost, markup_value, item_type, sort_order
                    FROM offer_material_items WHERE section_id=@oldMsId",
                    new { newMsId, oldMsId = (int)ms.id }, tx);
            }

            // 6. Copia pricing
            c.Execute(@"
                INSERT INTO project_pricing (project_id, contingency_pct,
                    negotiation_margin_pct, travel_markup, allowance_markup)
                SELECT @projectId, contingency_pct,
                    negotiation_margin_pct, travel_markup, allowance_markup
                FROM offer_pricing WHERE offer_id=@offerId",
                new { projectId, offerId = id }, tx);

            // 7. Aggiorna offerta → CONVERTITA con link al progetto
            c.Execute("UPDATE offers SET status='CONVERTITA', converted_project_id=@projectId WHERE id=@Id",
                new { projectId, Id = id }, tx);

            // 8. Salva server_path
            string fullPath = "";
            try
            {
                string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
                fullPath = Path.Combine(basePath, year.ToString(), projectCode);
                c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = projectId }, tx);
            }
            catch { /* non critico */ }

            tx.Commit();

            // 9. Crea cartelle da template (dopo commit, non transazionale)
            try
            {
                CopyTemplateToProject(projectCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Offers] Warning: errore creazione cartelle {projectCode}: {ex.Message}");
            }

            // Notifica al PM assegnato
            try
            {
                string offerCode = (string)offer.offer_code;
                _notif.Create(
                    "OFFER_CONVERTED", "INFO",
                    $"Nuova commessa {projectCode} da offerta {offerCode}",
                    $"L'offerta {offerCode} — {(string)offer.title} — è stata convertita in commessa {projectCode} e assegnata a te.",
                    "PROJECT", projectId, projectId, GetCurrentEmployeeId(),
                    new[] { req.PmId });
            }
            catch { /* notifica non critica */ }

            return Ok(ApiResponse<int>.Ok(projectId, $"Commessa {projectCode} creata da offerta"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    private void CopyTemplateToProject(string projectCode)
    {
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        string templatePath = _db.GetConfig("TemplatePath", @"C:\ATEC_Commesse\MASTER_TEMPLATE");
        string year = DateTime.Now.Year.ToString();
        string targetPath = Path.Combine(basePath, year, projectCode);

        if (!Directory.Exists(templatePath))
        {
            Directory.CreateDirectory(targetPath);
            return;
        }

        Directory.CreateDirectory(targetPath);

        foreach (string dir in Directory.GetDirectories(templatePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(templatePath, dir);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (string file in Directory.GetFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(templatePath, file);
            string destFile = Path.Combine(targetPath, relativePath);
            System.IO.File.Copy(file, destFile, overwrite: false);
        }
    }
}

// DTO per conversione
public class OfferConvertDto
{
    public int PmId { get; set; }
}
