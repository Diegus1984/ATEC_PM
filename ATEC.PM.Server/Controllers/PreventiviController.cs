using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/preventivi")]
[Authorize]
public class PreventiviController : ControllerBase
{
    private readonly QuoteDbService _qdb;
    private readonly DbService _db;
    private readonly NotificationService _notif;
    public PreventiviController(QuoteDbService qdb, DbService db, NotificationService notif)
    { _qdb = qdb; _db = db; _notif = notif; }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // ───────────────────── LIST (per TreeView) ─────────────────────

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            using var c = _qdb.Open();
            var rows = c.Query<QuoteDto>(@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType,
                       q.revision AS Revision, q.parent_quote_id AS ParentQuoteId,
                       q.subtotal AS Subtotal, q.total AS Total, q.total_with_vat AS TotalWithVat,
                       q.cost_total AS CostTotal, q.profit AS Profit,
                       q.delivery_days AS DeliveryDays, q.validity_days AS ValidityDays,
                       q.assigned_to AS AssignedTo,
                       CONCAT(ea.first_name,' ',ea.last_name) AS AssignedToName,
                       q.created_by AS CreatedBy,
                       CONCAT(ec.first_name,' ',ec.last_name) AS CreatedByName,
                       q.created_at AS CreatedAt, q.updated_at AS UpdatedAt,
                       q.project_id AS ProjectId
                FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                LEFT JOIN employees ea ON ea.id = q.assigned_to
                LEFT JOIN employees ec ON ec.id = q.created_by
                ORDER BY q.created_at DESC").ToList();
            return Ok(ApiResponse<List<QuoteDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ───────────────────── CREATE (con init costing per IMPIANTO) ─────────────────────

    [HttpPost]
    public IActionResult Create([FromBody] QuoteSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            // Genera codice PRV-{anno}-{progressivo 4 cifre}
            int year = DateTime.Now.Year;
            string prefix = $"PRV-{year}-";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(quote_number, @len+1) AS UNSIGNED)),0) FROM quotes WHERE quote_number LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string quoteNumber = $"{prefix}{(maxNum + 1):D4}";

            string quoteType = dto.QuoteType ?? "SERVICE";

            int quoteId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quotes (quote_number, title, customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, price_list_id, group_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices,
                    notes_internal, notes_quote, assigned_to, created_by, status, quote_type)
                VALUES (@QuoteNumber, @Title, @CustomerId, @ContactName1, @ContactName2, @ContactName3,
                    @DeliveryDays, @ValidityDays, @PaymentType, @Language, @PriceListId, @GroupId,
                    @DiscountPct, @DiscountAbs, @ShowItemPrices, @ShowSummary, @ShowSummaryPrices,
                    @NotesInternal, @NotesQuote, @AssignedTo, @CreatedBy, 'draft', @QuoteType);
                SELECT LAST_INSERT_ID()",
                new
                {
                    QuoteNumber = quoteNumber, dto.PriceListId, dto.Title, dto.CustomerId,
                    dto.ContactName1, dto.ContactName2, dto.ContactName3,
                    dto.DeliveryDays, dto.ValidityDays, dto.PaymentType, dto.Language,
                    dto.GroupId, dto.DiscountPct, dto.DiscountAbs,
                    dto.ShowItemPrices, dto.ShowSummary, dto.ShowSummaryPrices,
                    dto.NotesInternal, dto.NotesQuote, dto.AssignedTo,
                    CreatedBy = GetCurrentEmployeeId(),
                    QuoteType = quoteType
                }, tx);

            // Se IMPIANTO: init costing (copia template sezioni costo)
            if (quoteType == "IMPIANTO")
                InitQuoteCosting(c, tx, quoteId);

            // Auto-populate items dal template gruppo (se specificato)
            if (dto.GroupId.HasValue && dto.GroupId.Value > 0)
                AutoPopulateItems(c, tx, quoteId, dto.PriceListId);

            // Log stato
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, 'Preventivo creato')",
                new { Id = quoteId, By = GetCurrentEmployeeId() }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(quoteId, $"Preventivo {quoteNumber} creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ───────────────────── CONVERT TO PROJECT (solo IMPIANTO) ─────────────────────

    [HttpPost("{id}/convert")]
    public IActionResult ConvertToProject(int id, [FromBody] PreventivoConvertDto req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var quote = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM quotes WHERE id=@Id", new { Id = id }, tx);
            if (quote == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

            string qtype = (string)(quote.quote_type ?? "SERVICE");
            if (qtype != "IMPIANTO")
                return BadRequest(ApiResponse<string>.Fail("Solo preventivi IMPIANTO possono essere convertiti"));

            string status = (string)quote.status;
            if (status == "converted")
                return BadRequest(ApiResponse<string>.Fail("Preventivo già convertito"));

            // 1. Genera codice commessa
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
                    Title = (string)quote.title,
                    CustId = (int)quote.customer_id,
                    PmId = req.PmId,
                    Desc = (string)(quote.notes_internal ?? "")
                }, tx);

            // 3. Crea fasi di default
            var phaseTemplates = c.Query("SELECT id, department_id, sort_order FROM phase_templates WHERE is_default=1 ORDER BY sort_order", transaction: tx);
            foreach (var t in phaseTemplates)
            {
                c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, department_id, sort_order)
                    VALUES (@ProjId, @TplId, @DeptId, @Sort)",
                    new { ProjId = projectId, TplId = (int)t.id, DeptId = (int?)t.department_id, Sort = (int)t.sort_order }, tx);
            }

            // 4. Copia sezioni costo quote → project
            var quoteSections = c.Query<dynamic>(
                "SELECT * FROM quote_cost_sections WHERE quote_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            foreach (var sec in quoteSections)
            {
                int newSecId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled, contingency_pct, margin_pct)
                    VALUES (@projectId, @tmplId, @name, @stype, @gname, @sort, @enabled, @contPct, @margPct);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        projectId,
                        tmplId = (int?)sec.template_id,
                        name = (string)sec.name,
                        stype = (string)sec.section_type,
                        gname = (string)sec.group_name,
                        sort = (int)sec.sort_order,
                        enabled = (bool)sec.is_enabled,
                        contPct = (decimal)sec.contingency_pct,
                        margPct = (decimal)sec.margin_pct
                    }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_section_departments (project_cost_section_id, department_id)
                    SELECT @newSecId, department_id FROM quote_cost_section_departments WHERE quote_cost_section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order)
                    SELECT @newSecId, employee_id, resource_name, work_days, hours_per_day,
                        hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                        allowance_days, daily_allowance, sort_order
                    FROM quote_cost_resources WHERE section_id=@oldSecId",
                    new { newSecId, oldSecId = (int)sec.id }, tx);
            }

            // 4b. Aggiungi sezioni template is_default_project=1 non presenti nel preventivo
            var copiedTemplateIds = quoteSections
                .Where(s => s.template_id != null)
                .Select(s => (int)s.template_id)
                .ToHashSet();

            int maxSort = quoteSections.Any() ? quoteSections.Max(s => (int)s.sort_order) : 0;

            var missingTemplates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default_project=1 AND t.is_active=1
                  AND t.id NOT IN @copiedIds
                ORDER BY t.sort_order",
                new { copiedIds = copiedTemplateIds.Count > 0 ? copiedTemplateIds.ToArray() : new[] { -1 } }, tx).ToList();

            foreach (var tmpl in missingTemplates)
            {
                maxSort++;
                int newSecId2 = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @section_type, @group_name, @sort_order, 1);
                    SELECT LAST_INSERT_ID();",
                    new { projectId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, sort_order = maxSort }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_section_departments (project_cost_section_id, department_id)
                    SELECT @newSecId, department_id
                    FROM cost_section_template_departments
                    WHERE section_template_id = @templateId",
                    new { newSecId = newSecId2, templateId = (int)tmpl.id }, tx);
            }

            // 5. Copia materiali quote → project (preservando parent_item_id)
            var quoteMatSections = c.Query<dynamic>(
                "SELECT * FROM quote_material_sections WHERE quote_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            // Mappa quote_item.id → project_item.id per ricostruire parent_item_id
            var itemIdMap = new Dictionary<int, int>();

            foreach (var ms in quoteMatSections)
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

                // Inserisce item uno per uno per tracciare il mapping degli ID
                var quoteItems = c.Query<dynamic>(
                    "SELECT id, description, quantity, unit_cost, markup_value, item_type, sort_order FROM quote_material_items WHERE section_id=@sid ORDER BY sort_order, id",
                    new { sid = (int)ms.id }, tx).ToList();

                foreach (var item in quoteItems)
                {
                    int newItemId = (int)c.ExecuteScalar<long>(@"
                        INSERT INTO project_material_items (section_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
                        VALUES (@sid, @desc, @qty, @cost, @markup, @type, @sort);
                        SELECT LAST_INSERT_ID()",
                        new
                        {
                            sid = newMsId,
                            desc = (string)item.description,
                            qty = (decimal)item.quantity,
                            cost = (decimal)item.unit_cost,
                            markup = (decimal)item.markup_value,
                            type = (string)item.item_type,
                            sort = (int)item.sort_order
                        }, tx);
                    itemIdMap[(int)item.id] = newItemId;
                }
            }

            // Aggiorna parent_item_id usando il mapping quote→project
            var itemsWithParent = c.Query<(int QuoteItemId, int QuoteParentId)>(@"
                SELECT qmi.id, qmi.parent_item_id
                FROM quote_material_items qmi
                JOIN quote_material_sections qms ON qms.id = qmi.section_id
                WHERE qms.quote_id = @qid AND qmi.parent_item_id IS NOT NULL",
                new { qid = id }, tx).ToList();

            foreach (var (quoteItemId, quoteParentId) in itemsWithParent)
            {
                if (itemIdMap.TryGetValue(quoteItemId, out int projItemId) &&
                    itemIdMap.TryGetValue(quoteParentId, out int projParentId))
                {
                    c.Execute("UPDATE project_material_items SET parent_item_id=@parentId WHERE id=@itemId",
                        new { parentId = projParentId, itemId = projItemId }, tx);
                }
            }

            // 6. Copia pricing
            c.Execute(@"
                INSERT INTO project_pricing (project_id, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup)
                SELECT @projectId, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup
                FROM quote_pricing WHERE quote_id=@quoteId",
                new { projectId, quoteId = id }, tx);

            // 7. Aggiorna preventivo → converted
            c.Execute("UPDATE quotes SET status='converted', converted_at=NOW(), project_id=@projectId WHERE id=@Id",
                new { projectId, Id = id }, tx);

            // 8. Server path
            string fullPath = "";
            try
            {
                string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
                fullPath = Path.Combine(basePath, year.ToString(), projectCode);
                c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = projectId }, tx);
            }
            catch { }

            tx.Commit();

            // Crea cartelle
            try { CopyTemplateToProject(projectCode); } catch { }

            // Notifica PM
            try
            {
                string qn = (string)quote.quote_number;
                _notif.Create("QUOTE_CONVERTED", "INFO",
                    $"Nuova commessa {projectCode} da preventivo {qn}",
                    $"Il preventivo {qn} — {(string)quote.title} — è stato convertito in commessa {projectCode}.",
                    "PROJECT", projectId, projectId, GetCurrentEmployeeId(),
                    new[] { req.PmId });
            }
            catch { }

            return Ok(ApiResponse<int>.Ok(projectId, $"Commessa {projectCode} creata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ───────────────────── REVISIONE ─────────────────────

    [HttpPost("{id}/revision")]
    public IActionResult CreateRevision(int id)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            var original = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM quotes WHERE id=@Id", new { Id = id }, tx);
            if (original == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

            // Trova il master (root della catena revisioni)
            int masterId = (int?)original.parent_quote_id ?? id;

            // Calcola prossimo numero revisione per questo master
            int maxRev = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(revision),0) FROM quotes WHERE parent_quote_id=@MasterId OR id=@MasterId",
                new { MasterId = masterId }, tx);
            int newRev = maxRev + 1;

            // Numero: stessa base + Rev X
            string baseNumber = ((string)original.quote_number).Split(" Rev")[0];
            string newNumber = $"{baseNumber} Rev {newRev}";

            // Crea la nuova quote copiando i dati
            int newId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quotes (quote_number, title, customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, price_list_id, group_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices,
                    hide_quantities, notes_internal, notes_quote, assigned_to, created_by,
                    status, quote_type, revision, parent_quote_id)
                VALUES (@Number, @Title, @CustId, @C1, @C2, @C3,
                    @DelDays, @ValDays, @PayType, @Lang, @PlId, @GrpId,
                    @DiscPct, @DiscAbs, @SIP, @SS, @SSP,
                    @HQ, @NI, @NQ, @AssTo, @CreBy,
                    'draft', @QType, @Rev, @ParentId);
                SELECT LAST_INSERT_ID()",
                new
                {
                    Number = newNumber,
                    Title = (string)original.title,
                    CustId = (int)original.customer_id,
                    C1 = (string?)original.contact_name1,
                    C2 = (string?)original.contact_name2,
                    C3 = (string?)original.contact_name3,
                    DelDays = (int)original.delivery_days,
                    ValDays = (int)original.validity_days,
                    PayType = (string?)original.payment_type,
                    Lang = (string?)original.language,
                    PlId = (int?)original.price_list_id,
                    GrpId = (int?)original.group_id,
                    DiscPct = (decimal)original.discount_pct,
                    DiscAbs = (decimal)original.discount_abs,
                    SIP = (bool)original.show_item_prices,
                    SS = (bool)original.show_summary,
                    SSP = (bool)original.show_summary_prices,
                    HQ = (bool)(original.hide_quantities ?? false),
                    NI = (string?)original.notes_internal,
                    NQ = (string?)original.notes_quote,
                    AssTo = (int?)original.assigned_to,
                    CreBy = GetCurrentEmployeeId(),
                    QType = (string)(original.quote_type ?? "SERVICE"),
                    Rev = newRev,
                    ParentId = masterId
                }, tx);

            // Copia items (con gerarchia parent_item_id)
            CopyQuoteItems(c, tx, id, newId);

            // Se IMPIANTO: copia costing completo
            string quoteType = (string)(original.quote_type ?? "SERVICE");
            if (quoteType == "IMPIANTO")
                CopyQuoteCosting(c, tx, id, newId);

            // Vecchia revisione → SUPERATA
            c.Execute("UPDATE quotes SET status='superseded' WHERE id=@Id", new { Id = id }, tx);

            // Log
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, @OldSt, 'superseded', @By, @Notes)",
                new { Id = id, OldSt = (string)original.status, By = GetCurrentEmployeeId(),
                      Notes = $"Superato da {newNumber}" }, tx);
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, @Notes)",
                new { Id = newId, By = GetCurrentEmployeeId(),
                      Notes = $"Revisione {newRev} da {(string)original.quote_number}" }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, $"Revisione {newNumber} creata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ───────────────────── DUPLICAZIONE ─────────────────────

    [HttpPost("{id}/duplicate")]
    public IActionResult Duplicate(int id)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            var original = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM quotes WHERE id=@Id", new { Id = id }, tx);
            if (original == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

            // Genera nuovo codice progressivo
            int year = DateTime.Now.Year;
            string prefix = $"PRV-{year}-";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(quote_number, @len+1, 4) AS UNSIGNED)),0) FROM quotes WHERE quote_number LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string newNumber = $"{prefix}{(maxNum + 1):D4}";

            // Crea copia indipendente (no parent_quote_id, revision=0)
            int newId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quotes (quote_number, title, customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, price_list_id, group_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices,
                    hide_quantities, notes_internal, notes_quote, assigned_to, created_by,
                    status, quote_type, revision, parent_quote_id)
                VALUES (@Number, @Title, @CustId, @C1, @C2, @C3,
                    @DelDays, @ValDays, @PayType, @Lang, @PlId, @GrpId,
                    @DiscPct, @DiscAbs, @SIP, @SS, @SSP,
                    @HQ, @NI, @NQ, @AssTo, @CreBy,
                    'draft', @QType, 0, NULL);
                SELECT LAST_INSERT_ID()",
                new
                {
                    Number = newNumber,
                    Title = $"{(string)original.title} (copia)",
                    CustId = (int)original.customer_id,
                    C1 = (string?)original.contact_name1,
                    C2 = (string?)original.contact_name2,
                    C3 = (string?)original.contact_name3,
                    DelDays = (int)original.delivery_days,
                    ValDays = (int)original.validity_days,
                    PayType = (string?)original.payment_type,
                    Lang = (string?)original.language,
                    PlId = (int?)original.price_list_id,
                    GrpId = (int?)original.group_id,
                    DiscPct = (decimal)original.discount_pct,
                    DiscAbs = (decimal)original.discount_abs,
                    SIP = (bool)original.show_item_prices,
                    SS = (bool)original.show_summary,
                    SSP = (bool)original.show_summary_prices,
                    HQ = (bool)(original.hide_quantities ?? false),
                    NI = (string?)original.notes_internal,
                    NQ = (string?)original.notes_quote,
                    AssTo = (int?)original.assigned_to,
                    CreBy = GetCurrentEmployeeId(),
                    QType = (string)(original.quote_type ?? "SERVICE")
                }, tx);

            CopyQuoteItems(c, tx, id, newId);

            string quoteType = (string)(original.quote_type ?? "SERVICE");
            if (quoteType == "IMPIANTO")
                CopyQuoteCosting(c, tx, id, newId);

            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, @Notes)",
                new { Id = newId, By = GetCurrentEmployeeId(),
                      Notes = $"Duplicato da {(string)original.quote_number}" }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, $"Preventivo {newNumber} creato (duplicato)"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ───────────────────── COPY HELPERS ─────────────────────

    private static void CopyQuoteItems(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx, int fromId, int toId)
    {
        var items = c.Query<dynamic>(
            "SELECT * FROM quote_items WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        // Map old parent_item_id → new id
        var idMap = new Dictionary<int, int>();

        foreach (var item in items)
        {
            int? parentId = (int?)item.parent_item_id;
            int? mappedParent = parentId.HasValue && idMap.ContainsKey(parentId.Value)
                ? idMap[parentId.Value] : null;

            int newItemId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, product_id, variant_id, item_type, code, name,
                    description_rtf, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                    line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                VALUES (@QId, @PId, @VId, @IType, @Code, @Name,
                    @Desc, @Unit, @Qty, @Cost, @Sell, @Disc, @Vat,
                    @LT, @LP, @Sort, @Active, @Conf, @Parent, @Auto);
                SELECT LAST_INSERT_ID()",
                new
                {
                    QId = toId,
                    PId = (int?)item.product_id,
                    VId = (int?)item.variant_id,
                    IType = (string)item.item_type,
                    Code = (string?)item.code,
                    Name = (string?)item.name,
                    Desc = (string?)item.description_rtf,
                    Unit = (string?)item.unit,
                    Qty = (decimal)item.quantity,
                    Cost = (decimal)item.cost_price,
                    Sell = (decimal)item.sell_price,
                    Disc = (decimal)item.discount_pct,
                    Vat = (decimal)item.vat_pct,
                    LT = (decimal)item.line_total,
                    LP = (decimal)item.line_profit,
                    Sort = (int)item.sort_order,
                    Active = (bool)(item.is_active ?? true),
                    Conf = (bool)(item.is_confirmed ?? false),
                    Parent = mappedParent,
                    Auto = (bool)(item.is_auto_include ?? false)
                }, tx);

            idMap[(int)item.id] = newItemId;
        }

        RecalcTotals(c, toId, tx);
    }

    private static void CopyQuoteCosting(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx, int fromId, int toId)
    {
        // Copia sezioni costo
        var sections = c.Query<dynamic>(
            "SELECT * FROM quote_cost_sections WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        foreach (var sec in sections)
        {
            int newSecId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_cost_sections (quote_id, template_id, name, section_type, group_name,
                    sort_order, is_enabled, contingency_pct, margin_pct, contingency_pinned, margin_pinned, is_shadowed)
                VALUES (@qid, @tid, @name, @stype, @gname, @sort, @enabled, @cpct, @mpct, @cpin, @mpin, @shad);
                SELECT LAST_INSERT_ID()",
                new
                {
                    qid = toId,
                    tid = (int?)sec.template_id,
                    name = (string)sec.name,
                    stype = (string)sec.section_type,
                    gname = (string)sec.group_name,
                    sort = (int)sec.sort_order,
                    enabled = (bool)sec.is_enabled,
                    cpct = (decimal)sec.contingency_pct,
                    mpct = (decimal)sec.margin_pct,
                    cpin = (bool)(sec.contingency_pinned ?? false),
                    mpin = (bool)(sec.margin_pinned ?? false),
                    shad = (bool)(sec.is_shadowed ?? false)
                }, tx);

            // Reparti sezione
            c.Execute(@"INSERT INTO quote_cost_section_departments (quote_cost_section_id, department_id)
                        SELECT @newId, department_id FROM quote_cost_section_departments WHERE quote_cost_section_id=@oldId",
                new { newId = newSecId, oldId = (int)sec.id }, tx);

            // Risorse
            c.Execute(@"INSERT INTO quote_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day,
                            hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                            allowance_days, daily_allowance, sort_order)
                        SELECT @newId, employee_id, resource_name, work_days, hours_per_day,
                            hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                            allowance_days, daily_allowance, sort_order
                        FROM quote_cost_resources WHERE section_id=@oldId",
                new { newId = newSecId, oldId = (int)sec.id }, tx);
        }

        // Copia sezioni materiali
        var matSections = c.Query<dynamic>(
            "SELECT * FROM quote_material_sections WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        foreach (var ms in matSections)
        {
            int newMsId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_material_sections (quote_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                VALUES (@qid, @catId, @name, @mk, @cmk, @sort, @enabled);
                SELECT LAST_INSERT_ID()",
                new
                {
                    qid = toId,
                    catId = (int?)ms.category_id,
                    name = (string)ms.name,
                    mk = (decimal)ms.markup_value,
                    cmk = (decimal)ms.commission_markup,
                    sort = (int)ms.sort_order,
                    enabled = (bool)ms.is_enabled
                }, tx);

            // Copia items con gerarchia parent_item_id
            var items = c.Query<dynamic>(
                "SELECT * FROM quote_material_items WHERE section_id=@Id ORDER BY sort_order",
                new { Id = (int)ms.id }, tx).ToList();

            var matIdMap = new Dictionary<int, int>();
            foreach (var item in items)
            {
                int? parentId = (int?)item.parent_item_id;
                int? mappedParent = parentId.HasValue && matIdMap.ContainsKey(parentId.Value)
                    ? matIdMap[parentId.Value] : null;

                int newItemId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id,
                        code, description, description_rtf, quantity, unit_cost, markup_value,
                        item_type, sort_order, contingency_pct, margin_pct, contingency_pinned, margin_pinned, is_shadowed, is_active)
                    VALUES (@sid, @parent, @pid, @vid, @code, @desc, @descRtf, @qty, @ucost, @mk,
                        @itype, @sort, @cpct, @mpct, @cpin, @mpin, @shad, @active);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        sid = newMsId,
                        parent = mappedParent,
                        pid = (int?)item.product_id,
                        vid = (int?)item.variant_id,
                        code = (string?)item.code,
                        desc = (string?)item.description,
                        descRtf = (string?)item.description_rtf,
                        qty = (decimal)item.quantity,
                        ucost = (decimal)item.unit_cost,
                        mk = (decimal)item.markup_value,
                        itype = (string)item.item_type,
                        sort = (int)item.sort_order,
                        cpct = (decimal)item.contingency_pct,
                        mpct = (decimal)item.margin_pct,
                        cpin = (bool)(item.contingency_pinned ?? false),
                        mpin = (bool)(item.margin_pinned ?? false),
                        shad = (bool)(item.is_shadowed ?? false),
                        active = (bool)(item.is_active ?? true)
                    }, tx);

                matIdMap[(int)item.id] = newItemId;
            }
        }

        // Copia pricing
        c.Execute(@"INSERT INTO quote_pricing (quote_id, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup)
                    SELECT @newId, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup
                    FROM quote_pricing WHERE quote_id=@oldId",
            new { newId = toId, oldId = fromId }, tx);
    }

    // ───────────────────── HELPERS ─────────────────────

    private static void InitQuoteCosting(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx, int quoteId)
    {
        // Copia template sezioni costo
        var templates = c.Query<dynamic>(@"
            SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
            FROM cost_section_templates t
            JOIN cost_section_groups g ON g.id = t.group_id
            WHERE t.is_default_quote=1 AND t.is_active=1
            ORDER BY t.sort_order", transaction: tx).ToList();

        foreach (var tmpl in templates)
        {
            int newSectionId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_cost_sections (quote_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                VALUES (@quoteId, @id, @name, @section_type, @group_name, @sort_order, 1);
                SELECT LAST_INSERT_ID()",
                new { quoteId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, tmpl.sort_order }, tx);

            c.Execute(@"
                INSERT INTO quote_cost_section_departments (quote_cost_section_id, department_id)
                SELECT @newSectionId, department_id
                FROM cost_section_template_departments
                WHERE section_template_id = @templateId",
                new { newSectionId, templateId = (int)tmpl.id }, tx);
        }

        // Init sezione materiali unica (lista piatta)
        c.Execute(@"
            INSERT INTO quote_material_sections (quote_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
            VALUES (@quoteId, NULL, 'Materiali', 1.300, 1.100, 0, 1)",
            new { quoteId }, tx);

        // Init pricing default
        c.Execute("INSERT INTO quote_pricing (quote_id) VALUES (@quoteId)", new { quoteId }, tx);
    }

    private static void AutoPopulateItems(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx, int quoteId, int? priceListId)
    {
        string autoSql = @"
            SELECT p.id AS ProductId, p.item_type, p.code, p.name, p.description_rtf,
                   v.id AS VariantId, v.code AS VarCode, v.name AS VarName,
                   v.cost_price, v.markup_value
            FROM quote_products p
            JOIN quote_categories cat ON cat.id = p.category_id
            JOIN quote_groups g ON g.id = cat.group_id
            LEFT JOIN quote_product_variants v ON v.product_id = p.id
            WHERE p.auto_include = 1 AND p.is_active = 1";
        if (priceListId.HasValue && priceListId.Value > 0)
            autoSql += " AND g.price_list_id = @PriceListId";
        autoSql += " ORDER BY g.sort_order, cat.sort_order, p.sort_order, v.sort_order";

        var autoItems = c.Query<dynamic>(autoSql, new { PriceListId = priceListId }, tx).ToList();

        int sortOrder = 0;
        var grouped = autoItems.GroupBy(x => (int)x.ProductId);

        foreach (var grp in grouped)
        {
            var first = grp.First();
            string productName = (string)first.name;
            string productCode = (string)(first.code ?? "");
            string productType = (string)first.item_type;
            string desc = (string?)(first.description_rtf) ?? "";
            bool hasVariants = grp.Any(x => x.VariantId != null);

            if (hasVariants)
            {
                int parentId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_items (quote_id, product_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                    VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, '', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1);
                    SELECT LAST_INSERT_ID()",
                    new { QId = quoteId, PId = grp.Key, Type = productType,
                          Code = productCode, Name = productName, Desc = desc,
                          Sort = sortOrder++ }, tx);

                foreach (var v in grp.Where(x => x.VariantId != null))
                {
                    decimal cost = v.cost_price ?? 0m;
                    decimal markup = v.markup_value ?? 1m;
                    decimal qty = 1m;
                    decimal sell = cost * markup;
                    decimal disc = 0m;
                    decimal vat = 22m;
                    decimal lt = qty * sell * (1 - disc / 100m);
                    decimal lp = lt - (qty * cost);

                    c.Execute(@"
                        INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                            code, name, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                            line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                        VALUES (@QId, @PId, @VId, 'product', @Code, @Name, @Unit, @Qty,
                            @Cost, @Sell, @Disc, @Vat, @LT, @LP, @Sort, 0, 0, @ParentId, 1)",
                        new { QId = quoteId, PId = grp.Key, VId = (int?)v.VariantId,
                              Code = (string)(v.VarCode ?? ""), Name = (string)(v.VarName ?? productName),
                              Unit = "nr.", Qty = qty,
                              Cost = cost, Sell = sell, Disc = disc, Vat = vat,
                              LT = lt, LP = lp, Sort = sortOrder++, ParentId = parentId }, tx);
                }
            }
            else
            {
                c.Execute(@"
                    INSERT INTO quote_items (quote_id, product_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                    VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, 'nr.', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1)",
                    new { QId = quoteId, PId = grp.Key, Type = productType,
                          Code = productCode, Name = productName, Desc = desc,
                          Sort = sortOrder++ }, tx);
            }
        }

        // Ricalcola totali
        RecalcTotals(c, quoteId, tx);
    }

    private static void RecalcTotals(MySqlConnector.MySqlConnection c, int quoteId, System.Data.IDbTransaction? tx)
    {
        var totals = c.QueryFirstOrDefault<(decimal Subtotal, decimal CostTotal, decimal VatTotal)>(@"
            SELECT COALESCE(SUM(line_total),0) AS Subtotal,
                   COALESCE(SUM(quantity * cost_price),0) AS CostTotal,
                   COALESCE(SUM(line_total * vat_pct / 100),0) AS VatTotal
            FROM quote_items WHERE quote_id=@Id AND COALESCE(parent_item_id,0)=0 AND COALESCE(is_active,1)=1",
            new { Id = quoteId }, tx);

        var disc = c.QueryFirstOrDefault<(decimal DiscPct, decimal DiscAbs)>(
            "SELECT COALESCE(discount_pct,0), COALESCE(discount_abs,0) FROM quotes WHERE id=@Id",
            new { Id = quoteId }, tx);

        decimal subtotal = totals.Subtotal;
        decimal afterDiscount = subtotal * (1 - disc.DiscPct / 100m) - disc.DiscAbs;
        decimal total = afterDiscount;
        decimal vatTotal = totals.VatTotal * (1 - disc.DiscPct / 100m);
        decimal totalWithVat = total + vatTotal;
        decimal profit = total - totals.CostTotal;

        c.Execute(@"UPDATE quotes SET subtotal=@Sub, cost_total=@Cost, vat_total=@Vat,
                    total=@Total, total_with_vat=@TotalVat, profit=@Profit WHERE id=@Id",
            new { Sub = subtotal, Cost = totals.CostTotal, Vat = vatTotal,
                  Total = total, TotalVat = totalWithVat, Profit = profit, Id = quoteId }, tx);
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
            Directory.CreateDirectory(Path.Combine(targetPath, Path.GetRelativePath(templatePath, dir)));
        foreach (string file in Directory.GetFiles(templatePath, "*.*", SearchOption.AllDirectories))
            System.IO.File.Copy(file, Path.Combine(targetPath, Path.GetRelativePath(templatePath, file)), overwrite: false);
    }
}

public class PreventivoConvertDto
{
    public int PmId { get; set; }
}
