using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Shared.Models;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/quotes")]
[Authorize]
// Preventivi: prezzi e margini, stessa chiave della voce di menu.
[RequireFeature("nav.preventivi")]
public class QuotesController : ControllerBase
{
    private readonly QuoteDbService _qdb;
    private readonly QuotePdfService _pdf;
    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly ProjectTemplateCopyService _templateCopy;
    private readonly ILogger<QuotesController> _logger;

    public QuotesController(
        QuoteDbService qdb,
        QuotePdfService pdf,
        DbService db,
        NotificationService notif,
        ProjectTemplateCopyService templateCopy,
        ILogger<QuotesController> logger)
    {
        _qdb = qdb;
        _pdf = pdf;
        _db = db;
        _notif = notif;
        _templateCopy = templateCopy;
        _logger = logger;
    }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // LIST
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? customerId = null,
        [FromQuery] string? quoteType = null,
        [FromQuery] string? quoteNumber = null,
        [FromQuery] string? customerName = null,
        [FromQuery] string? title = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
            using var c = _qdb.Open();
            var conditions = new List<string>();
            var dp = new Dapper.DynamicParameters();

            if (!string.IsNullOrEmpty(status)) { conditions.Add("q.status = @Status"); dp.Add("Status", status); }
            else
                // Lista principale: le revisioni superseded compaiono solo come sotto-righe in UI
                conditions.Add("q.status <> 'superseded'");
            if (customerId.HasValue) { conditions.Add("q.customer_id = @CustomerId"); dp.Add("CustomerId", customerId); }
            if (!string.IsNullOrEmpty(quoteType)) { conditions.Add("COALESCE(q.quote_type,'SERVICE') = @QuoteType"); dp.Add("QuoteType", quoteType); }

            void AddLike(string column, string? filter, string param)
            {
                string? pat = PagedQueryHelper.ToLikePattern(filter);
                if (pat == null) return;
                conditions.Add($"{column} LIKE @{param}");
                dp.Add(param, pat);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = $"%{search.Trim()}%";
                conditions.Add(@"(q.quote_number LIKE @Search OR q.title LIKE @Search OR cu.company_name LIKE @Search
                    OR CONCAT(ec.first_name,' ',ec.last_name) LIKE @Search)");
                dp.Add("Search", term);
            }

            AddLike("q.quote_number", quoteNumber, "QuoteNumber");
            AddLike("cu.company_name", customerName, "CustomerName");
            AddLike("q.title", title, "Title");

            string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            string from = @"FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                LEFT JOIN quote_groups g ON g.id = q.group_id
                LEFT JOIN employees ea ON ea.id = q.assigned_to
                LEFT JOIN employees ec ON ec.id = q.created_by";

            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<QuoteDto>($@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType,
                       q.revision AS Revision, q.parent_quote_id AS ParentQuoteId,
                       q.group_id AS GroupId, g.name AS GroupName,
                       q.subtotal AS Subtotal, q.total AS Total, q.total_with_vat AS TotalWithVat,
                       q.cost_total AS CostTotal, q.profit AS Profit,
                       q.discount_pct AS DiscountPct, q.discount_abs AS DiscountAbs,
                       q.delivery_days AS DeliveryDays, q.validity_days AS ValidityDays,
                       q.payment_type AS PaymentType,
                       q.assigned_to AS AssignedTo,
                       CONCAT(ea.first_name,' ',ea.last_name) AS AssignedToName,
                       q.created_by AS CreatedBy,
                       CONCAT(ec.first_name,' ',ec.last_name) AS CreatedByName,
                       q.created_at AS CreatedAt, q.updated_at AS UpdatedAt,
                       q.sent_at AS SentAt, q.accepted_at AS AcceptedAt,
                       q.converted_at AS ConvertedAt,
                       q.project_id AS ProjectId
                {from} {where}
                ORDER BY q.created_at DESC
                LIMIT @Limit OFFSET @Offset", dp).ToList();

            int loaded = offset + rows.Count;
            return Ok(ApiResponse<PagedResult<QuoteDto>>.Ok(new PagedResult<QuoteDto>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total
            }));
        }
        catch (Exception ex) { return Ok(ApiResponse<PagedResult<QuoteDto>>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Tutte le versioni di una o piÃ¹ famiglie di revisione (incluso originale e superseded).</summary>
    [HttpGet("chains")]
    public IActionResult GetRevisionChains([FromQuery] string? masterIds)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(masterIds))
                return Ok(ApiResponse<List<QuoteDto>>.Ok(new List<QuoteDto>()));

            List<int> ids = masterIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out int n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
                return Ok(ApiResponse<List<QuoteDto>>.Ok(new List<QuoteDto>()));

            using var c = _qdb.Open();
            string from = @"FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                LEFT JOIN quote_groups g ON g.id = q.group_id
                LEFT JOIN employees ea ON ea.id = q.assigned_to
                LEFT JOIN employees ec ON ec.id = q.created_by";

            List<QuoteDto> rows = c.Query<QuoteDto>($@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType,
                       q.revision AS Revision, q.parent_quote_id AS ParentQuoteId,
                       q.group_id AS GroupId, g.name AS GroupName,
                       q.subtotal AS Subtotal, q.total AS Total, q.total_with_vat AS TotalWithVat,
                       q.cost_total AS CostTotal, q.profit AS Profit,
                       q.discount_pct AS DiscountPct, q.discount_abs AS DiscountAbs,
                       q.delivery_days AS DeliveryDays, q.validity_days AS ValidityDays,
                       q.payment_type AS PaymentType,
                       q.assigned_to AS AssignedTo,
                       CONCAT(ea.first_name,' ',ea.last_name) AS AssignedToName,
                       q.created_by AS CreatedBy,
                       CONCAT(ec.first_name,' ',ec.last_name) AS CreatedByName,
                       q.created_at AS CreatedAt, q.updated_at AS UpdatedAt,
                       q.sent_at AS SentAt, q.accepted_at AS AcceptedAt,
                       q.converted_at AS ConvertedAt,
                       q.project_id AS ProjectId
                {from}
                WHERE q.id IN @Ids OR q.parent_quote_id IN @Ids
                ORDER BY COALESCE(q.parent_quote_id, q.id), q.revision, q.created_at",
                new { Ids = ids }).ToList();

            return Ok(ApiResponse<List<QuoteDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET BY ID (con items)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        try
        {
            using var c = _qdb.Open();
            var quote = c.QueryFirstOrDefault<QuoteDto>(@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.contact_name1 AS ContactName1, q.contact_name2 AS ContactName2,
                       q.contact_name3 AS ContactName3,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType, q.revision AS Revision,
                       q.group_id AS GroupId, g.name AS GroupName,
                       q.subtotal AS Subtotal, q.discount_pct AS DiscountPct,
                       q.discount_abs AS DiscountAbs, q.vat_total AS VatTotal,
                       q.total AS Total, q.total_with_vat AS TotalWithVat,
                       q.cost_total AS CostTotal, q.profit AS Profit,
                       q.delivery_days AS DeliveryDays, q.validity_days AS ValidityDays,
                       q.payment_type AS PaymentType, q.language AS Language,
                       q.show_item_prices AS ShowItemPrices,
                       q.show_summary AS ShowSummary,
                       q.show_summary_prices AS ShowSummaryPrices,
                       COALESCE(q.hide_quantities,0) AS HideQuantities,
                       q.notes_internal AS NotesInternal, q.notes_quote AS NotesQuote,
                       q.assigned_to AS AssignedTo,
                       CONCAT(ea.first_name,' ',ea.last_name) AS AssignedToName,
                       q.created_by AS CreatedBy,
                       CONCAT(ec.first_name,' ',ec.last_name) AS CreatedByName,
                       q.created_at AS CreatedAt, q.updated_at AS UpdatedAt,
                       q.sent_at AS SentAt, q.accepted_at AS AcceptedAt,
                       q.converted_at AS ConvertedAt,
                       q.project_id AS ProjectId, p.code AS ProjectCode
                FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                LEFT JOIN quote_groups g ON g.id = q.group_id
                LEFT JOIN employees ea ON ea.id = q.assigned_to
                LEFT JOIN employees ec ON ec.id = q.created_by
                LEFT JOIN projects p ON p.id = q.project_id
                WHERE q.id = @Id", new { Id = id });

            if (quote == null)
                return Ok(ApiResponse<QuoteDto>.Fail("Preventivo non trovato"));

            quote.Items = c.Query<QuoteItemDto>(@"
                SELECT id AS Id, quote_id AS QuoteId, product_id AS ProductId,
                       variant_id AS VariantId, item_type AS ItemType,
                       code AS Code, name AS Name, description_rtf AS DescriptionRtf,
                       unit AS Unit, quantity AS Quantity,
                       cost_price AS CostPrice, sell_price AS SellPrice,
                       discount_pct AS DiscountPct, vat_pct AS VatPct,
                       line_total AS LineTotal, line_profit AS LineProfit,
                       sort_order AS SortOrder,
                       COALESCE(is_active,1) AS IsActive, COALESCE(is_confirmed,0) AS IsConfirmed,
                       parent_item_id AS ParentItemId,
                       COALESCE(is_auto_include,0) AS IsAutoInclude
                FROM quote_items WHERE quote_id = @Id
                ORDER BY sort_order", new { Id = id }).ToList();

            return Ok(ApiResponse<QuoteDto>.Ok(quote));
        }
        catch (Exception ex) { return Ok(ApiResponse<QuoteDto>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // CREATE â€” con init costing IMPIANTO e auto-populate opzionale
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPost]
    public IActionResult Create([FromBody] QuoteSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

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

            if (quoteType == "IMPIANTO")
                QuoteService.InitQuoteCosting(c, tx, quoteId);

            if (dto.GroupId.HasValue && dto.GroupId.Value > 0)
            {
                QuoteService.AutoPopulateItems(c, tx, quoteId, dto.PriceListId);
                QuoteService.RecalcTotals(c, quoteId, tx);
            }

            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, 'Preventivo creato')",
                new { Id = quoteId, By = GetCurrentEmployeeId() }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(quoteId, $"Preventivo {quoteNumber} creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // UPDATE header
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] QuoteSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute(@"UPDATE quotes SET title=@Title, customer_id=@CustomerId,
                        contact_name1=@ContactName1, contact_name2=@ContactName2, contact_name3=@ContactName3,
                        delivery_days=@DeliveryDays, validity_days=@ValidityDays,
                        payment_type=@PaymentType, language=@Language, group_id=@GroupId,
                        discount_pct=@DiscountPct, discount_abs=@DiscountAbs,
                        show_item_prices=@ShowItemPrices, show_summary=@ShowSummary,
                        show_summary_prices=@ShowSummaryPrices, hide_quantities=@HideQuantities,
                        notes_internal=@NotesInternal, notes_quote=@NotesQuote,
                        assigned_to=@AssignedTo
                        WHERE id=@Id",
                new { dto.Title, dto.CustomerId, dto.ContactName1, dto.ContactName2, dto.ContactName3,
                      dto.DeliveryDays, dto.ValidityDays, dto.PaymentType, dto.Language, dto.GroupId,
                      dto.DiscountPct, dto.DiscountAbs, dto.ShowItemPrices, dto.ShowSummary,
                      dto.ShowSummaryPrices, dto.HideQuantities, dto.NotesInternal, dto.NotesQuote, dto.AssignedTo, Id = id });

            // Ricalcola totali
            using var c2 = _qdb.Open();
            QuoteService.RecalcTotals(c2, id, null);

            return Ok(ApiResponse<string>.Ok("Preventivo aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // DELETE (solo draft)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _qdb.Open();
            string status = c.ExecuteScalar<string>("SELECT status FROM quotes WHERE id=@Id", new { Id = id }) ?? "";
            if (status != "draft")
                return Ok(ApiResponse<string>.Fail("Solo i preventivi in bozza possono essere eliminati"));

            c.Execute("DELETE FROM quotes WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("Preventivo eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // ITEMS â€” Aggiungi/Rimuovi/Aggiorna voci
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPost("{id}/items")]
    public IActionResult AddItem(int id, [FromBody] QuoteItemSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            decimal lineTotal = dto.Quantity * dto.SellPrice * (1 - dto.DiscountPct / 100m);
            decimal lineCost = dto.Quantity * dto.CostPrice;
            decimal lineProfit = lineTotal - lineCost;

            int maxSort = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order),0) FROM quote_items WHERE quote_id=@Id", new { Id = id });

            int itemId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                    code, name, description_rtf, unit, quantity,
                    cost_price, sell_price, discount_pct, vat_pct,
                    line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id)
                VALUES (@QuoteId, @ProductId, @VariantId, @ItemType,
                    @Code, @Name, @DescriptionRtf, @Unit, @Quantity,
                    @CostPrice, @SellPrice, @DiscountPct, @VatPct,
                    @LineTotal, @LineProfit, @SortOrder, @IsActive, @IsConfirmed, @ParentItemId);
                SELECT LAST_INSERT_ID()",
                new { QuoteId = id, dto.ProductId, dto.VariantId, dto.ItemType,
                      dto.Code, dto.Name, dto.DescriptionRtf, dto.Unit, dto.Quantity,
                      dto.CostPrice, dto.SellPrice, dto.DiscountPct, dto.VatPct,
                      LineTotal = lineTotal, LineProfit = lineProfit, SortOrder = maxSort + 1,
                      dto.IsActive, dto.IsConfirmed, dto.ParentItemId });

            QuoteService.RecalcTotals(c, id, null);
            return Ok(ApiResponse<int>.Ok(itemId, "Voce aggiunta"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Ricarica i contenuti automatici (auto_include) dal catalogo per un preventivo esistente.</summary>
    [HttpPost("{id}/reload-auto-includes")]
    public IActionResult ReloadAutoIncludes(int id)
    {
        try
        {
            using var c = _qdb.Open();
            var quote = c.QueryFirstOrDefault<dynamic>("SELECT id, group_id FROM quotes WHERE id=@id", new { id });
            if (quote == null) return NotFound(ApiResponse<string>.Fail("Preventivo non trovato"));

            int? priceListId = null;
            if (quote.group_id != null)
                priceListId = c.ExecuteScalar<int?>("SELECT price_list_id FROM quote_groups WHERE id=@gid", new { gid = (int)quote.group_id });

            using var tx = c.BeginTransaction();

            // Remove existing auto-includes (children first, then parents)
            c.Execute("DELETE FROM quote_items WHERE quote_id=@id AND is_auto_include=1 AND parent_item_id IS NOT NULL", new { id }, tx);
            c.Execute("DELETE FROM quote_items WHERE quote_id=@id AND is_auto_include=1", new { id }, tx);

            // Get max sort_order
            int maxSort = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order),0) FROM quote_items WHERE quote_id=@id", new { id }, tx);

            // Re-insert auto_include products
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

            int sortOrder = maxSort + 1;
            int count = 0;
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
                        INSERT INTO quote_items (quote_id, product_id, item_type, code, name, description_rtf,
                            unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                            line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                        VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, '', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1);
                        SELECT LAST_INSERT_ID()",
                        new { QId = id, PId = grp.Key, Type = productType, Code = productCode, Name = productName, Desc = desc, Sort = sortOrder++ }, tx);

                    foreach (var v in grp.Where(x => x.VariantId != null))
                    {
                        decimal cost = v.cost_price ?? 0m;
                        decimal markup = v.markup_value ?? 1.3m;
                        decimal sell = cost * markup;
                        c.Execute(@"
                            INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                                code, name, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                                line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                            VALUES (@QId, @PId, @VId, 'product', @Code, @Name, 'nr.', 1, @Cost, @Sell, 0, 22,
                                @LT, @LP, @Sort, 0, 0, @ParentId, 1)",
                            new { QId = id, PId = grp.Key, VId = (int?)v.VariantId,
                                  Code = (string)(v.VarCode ?? ""), Name = (string)(v.VarName ?? productName),
                                  Cost = cost, Sell = sell, LT = sell, LP = sell - cost, Sort = sortOrder++, ParentId = parentId }, tx);
                        count++;
                    }
                }
                else
                {
                    c.Execute(@"
                        INSERT INTO quote_items (quote_id, product_id, item_type, code, name, description_rtf,
                            unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                            line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                        VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, 'nr.', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1)",
                        new { QId = id, PId = grp.Key, Type = productType, Code = productCode, Name = productName, Desc = desc, Sort = sortOrder++ }, tx);
                    count++;
                }
            }

            tx.Commit();
            return Ok(ApiResponse<string>.Ok("", $"{count} contenuti automatici caricati"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Aggiunge un prodotto con TUTTE le sue varianti dal catalogo.</summary>
    [HttpPost("{id}/items/product/{productId}")]
    public IActionResult AddProductWithAllVariants(int id, int productId)
    {
        try
        {
            using var c = _qdb.Open();
            // Leggi direttamente la description come stringa per evitare problemi di mapping
            var product = c.QueryFirstOrDefault<QuoteProductDto>(@"
                SELECT id AS Id, item_type AS ItemType, code AS Code, name AS Name
                FROM quote_products WHERE id=@Id", new { Id = productId });
            if (product == null) return Ok(ApiResponse<string>.Fail("Prodotto non trovato"));

            string descriptionRtf = c.ExecuteScalar<string>(
                "SELECT description_rtf FROM quote_products WHERE id=@Id", new { Id = productId }) ?? "";
            _logger.LogInformation("[AddProduct] Prodotto {Name} â€” description_rtf.Length={Len}",
                product.Name, descriptionRtf.Length);

            var variants = c.Query<QuoteProductVariantDto>(@"
                SELECT id AS Id, product_id AS ProductId, code AS Code, name AS Name,
                       cost_price AS CostPrice, markup_value AS MarkupValue, sort_order AS SortOrder
                FROM quote_product_variants WHERE product_id=@Id ORDER BY sort_order",
                new { Id = productId }).ToList();

            int maxSort = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(sort_order),0) FROM quote_items WHERE quote_id=@Id", new { Id = id });

            // Inserisci riga header prodotto (parent)
            int parentId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, product_id, item_type, code, name, description_rtf,
                    unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                    line_total, line_profit, sort_order, is_active, is_confirmed)
                VALUES (@QId, @PId, @Type, @Code, @Name, @Desc,
                    '', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0);
                SELECT LAST_INSERT_ID()",
                new { QId = id, PId = productId, Type = product.ItemType,
                      product.Code, product.Name, Desc = descriptionRtf, Sort = maxSort + 1 });
            _logger.LogInformation("[AddProduct] Parent inserito id={ParentId}, desc salvata={Len}", parentId, descriptionRtf.Length);

            // Inserisci tutte le varianti come figlie
            int vSort = 0;
            foreach (var v in variants)
            {
                decimal qty = 1m;
                decimal sell = v.SellPrice; // computed: CostPrice * MarkupValue
                decimal disc = 0m;
                decimal vat = 22m;
                decimal lineTotal = qty * sell * (1 - disc / 100m);
                decimal lineProfit = lineTotal - (qty * v.CostPrice);

                c.Execute(@"
                    INSERT INTO quote_items (quote_id, product_id, variant_id, item_type, code, name,
                        unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id)
                    VALUES (@QId, @PId, @VId, 'product', @Code, @Name,
                        @Unit, @Qty, @Cost, @Price, @Disc, @Vat,
                        @Total, @Profit, @Sort, 0, 0, @ParentId)",
                    new { QId = id, PId = productId, VId = v.Id, v.Code, v.Name,
                          Unit = "nr.", Qty = qty, Cost = v.CostPrice, Price = sell,
                          Disc = disc, Vat = vat,
                          Total = lineTotal, Profit = lineProfit, Sort = maxSort + 2 + vSort++,
                          ParentId = parentId });
            }

            QuoteService.RecalcTotals(c, id, null);
            return Ok(ApiResponse<int>.Ok(parentId, $"Prodotto aggiunto con {variants.Count} varianti"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("{quoteId}/items/{itemId}")]
    public IActionResult UpdateItem(int quoteId, int itemId, [FromBody] QuoteItemSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            decimal lineTotal = dto.Quantity * dto.SellPrice * (1 - dto.DiscountPct / 100m);
            decimal lineCost = dto.Quantity * dto.CostPrice;
            decimal lineProfit = lineTotal - lineCost;

            c.Execute(@"UPDATE quote_items SET product_id=@ProductId, variant_id=@VariantId,
                        item_type=@ItemType, code=@Code, name=@Name, description_rtf=@DescriptionRtf,
                        unit=@Unit, quantity=@Quantity, cost_price=@CostPrice, sell_price=@SellPrice,
                        discount_pct=@DiscountPct, vat_pct=@VatPct,
                        line_total=@LineTotal, line_profit=@LineProfit, sort_order=@SortOrder,
                        is_active=@IsActive, is_confirmed=@IsConfirmed, parent_item_id=@ParentItemId
                        WHERE id=@Id AND quote_id=@QuoteId",
                new { dto.ProductId, dto.VariantId, dto.ItemType, dto.Code, dto.Name,
                      dto.DescriptionRtf, dto.Unit, dto.Quantity, dto.CostPrice, dto.SellPrice,
                      dto.DiscountPct, dto.VatPct, LineTotal = lineTotal, LineProfit = lineProfit,
                      dto.SortOrder, dto.IsActive, dto.IsConfirmed, dto.ParentItemId,
                      Id = itemId, QuoteId = quoteId });

            QuoteService.RecalcTotals(c, quoteId, null);
            return Ok(ApiResponse<string>.Ok("Voce aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("{quoteId}/items/{itemId}")]
    public IActionResult DeleteItem(int quoteId, int itemId)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute("DELETE FROM quote_items WHERE id=@Id AND quote_id=@QuoteId",
                new { Id = itemId, QuoteId = quoteId });
            QuoteService.RecalcTotals(c, quoteId, null);
            return Ok(ApiResponse<string>.Ok("Voce rimossa"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Aggiunge una variante locale a un parent item nel preventivo.</summary>
    [HttpPost("{quoteId}/items/{parentItemId}/variant")]
    public IActionResult AddLocalVariant(int quoteId, int parentItemId, [FromBody] QuoteItemSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            // Verifica che il parent esista
            var parent = c.QueryFirstOrDefault<dynamic>(
                "SELECT id, product_id, is_auto_include FROM quote_items WHERE id=@Id AND quote_id=@QId",
                new { Id = parentItemId, QId = quoteId });
            if (parent == null)
                return Ok(ApiResponse<string>.Fail("Voce parent non trovata"));

            int maxSort = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(sort_order),0) FROM quote_items WHERE quote_id=@Id", new { Id = quoteId });

            decimal lineTotal = dto.Quantity * dto.SellPrice * (1 - dto.DiscountPct / 100m);
            decimal lineProfit = lineTotal - (dto.Quantity * dto.CostPrice);

            int itemId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                    code, name, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                    line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                VALUES (@QId, @PId, NULL, 'product', @Code, @Name, @Unit, @Qty,
                    @Cost, @Sell, @Disc, @Vat, @LT, @LP, @Sort, @Active, 0, @ParentId, @AutoInc);
                SELECT LAST_INSERT_ID()",
                new { QId = quoteId, PId = (int?)parent.product_id,
                      dto.Code, dto.Name, dto.Unit, Qty = dto.Quantity,
                      Cost = dto.CostPrice, Sell = dto.SellPrice, Disc = dto.DiscountPct,
                      Vat = dto.VatPct, LT = lineTotal, LP = lineProfit,
                      Sort = maxSort + 1, Active = dto.IsActive,
                      ParentId = parentItemId,
                      AutoInc = (bool)(parent.is_auto_include ?? false) ? 1 : 0 });

            QuoteService.RecalcTotals(c, quoteId, null);
            return Ok(ApiResponse<int>.Ok(itemId, "Variante locale aggiunta"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("{id}/items/reorder")]
    public IActionResult ReorderItems(int id, [FromBody] List<int> itemIds)
    {
        try
        {
            using var c = _qdb.Open();
            for (int i = 0; i < itemIds.Count; i++)
                c.Execute("UPDATE quote_items SET sort_order=@Order WHERE id=@Id AND quote_id=@QuoteId",
                    new { Order = i, Id = itemIds[i], QuoteId = id });
            return Ok(ApiResponse<string>.Ok("Ordine aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // STATUS â€” Cambio stato con validazione
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPut("{id}/status")]
    public IActionResult ChangeStatus(int id, [FromBody] QuoteStatusChangeDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            string currentStatus = c.ExecuteScalar<string>("SELECT status FROM quotes WHERE id=@Id", new { Id = id }) ?? "";

            // Validazione: lo stato deve essere valido
            var validStatuses = new[] { "draft", "sent", "negotiation", "accepted", "rejected", "expired", "superseded" };
            if (!validStatuses.Contains(dto.NewStatus))
                return Ok(ApiResponse<string>.Fail($"Stato '{dto.NewStatus}' non valido"));
            if (currentStatus == "converted")
                return Ok(ApiResponse<string>.Fail("Preventivo giÃ  convertito: stato non modificabile"));
            if (currentStatus == dto.NewStatus)
                return Ok(ApiResponse<string>.Ok("Stato invariato"));

            // Aggiorna stato + date
            string extraSql = dto.NewStatus switch
            {
                "sent" => ", sent_at = NOW()",
                "accepted" => ", accepted_at = NOW()",
                "converted" => ", converted_at = NOW()",
                _ => ""
            };

            // Stato e registro insieme (F1): un cambio senza riga di log non si ricostruisce più.
            using var tx = c.BeginTransaction();
            c.Execute($"UPDATE quotes SET status=@Status{extraSql} WHERE id=@Id",
                new { Status = dto.NewStatus, Id = id }, tx);

            // Log
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, @Old, @New, @By, @Notes)",
                new { Id = id, Old = currentStatus, New = dto.NewStatus, By = GetCurrentEmployeeId(), dto.Notes }, tx);
            tx.Commit();

            return Ok(ApiResponse<string>.Ok($"Stato aggiornato a {dto.NewStatus}"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // CONVERT / REVISION / DUPLICATE
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPost("{id}/convert")]
    public IActionResult ConvertToProject(int id, [FromBody] QuoteConvertDto req)
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
                return BadRequest(ApiResponse<string>.Fail("Preventivo giÃ  convertito"));

            // Stesso formato del dialog "Nuova commessa": C{aaaammgg}.{progressivo del giorno}.
            string projectCode = ProjectCodeGenerator.Next(c, tx);

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

            var copiedTemplateIds = quoteSections
                .Where(s => s.template_id != null)
                .Select(s => (int)s.template_id)
                .ToHashSet();
            HashSet<string> copiedNamesLower = quoteSections
                .Select(s => ((string)s.name ?? "").Trim().ToLowerInvariant())
                .Where(n => n.Length > 0)
                .ToHashSet();

            int maxSort = quoteSections.Any() ? quoteSections.Max(s => (int)s.sort_order) : 0;

            var missingTemplates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default_project=1 AND t.is_active=1
                  AND t.id NOT IN @copiedIds
                ORDER BY t.sort_order",
                new { copiedIds = copiedTemplateIds.Count > 0 ? copiedTemplateIds.ToArray() : new[] { -1 } }, tx)
                .Where(t => !copiedNamesLower.Contains(((string)t.name ?? "").Trim().ToLowerInvariant()))
                .ToList();

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

            List<int> sectionTemplateIds = c.Query<int>(
                "SELECT DISTINCT template_id FROM project_cost_sections WHERE project_id=@pid AND template_id IS NOT NULL",
                new { pid = projectId }, tx).ToList();

            // v73 — le fasi arrivano dai LEGAMI delle sezioni finite in commessa: la stessa fase
            // agganciata a due di quelle sezioni entra due volte, una per sezione, con la sezione
            // scritta sulla riga. Prima si leggeva `phase_templates.cost_section_template_id`,
            // che con una fase su più sezioni ne indica una sola.
            List<dynamic> phasesToCopy = new();
            if (sectionTemplateIds.Count > 0)
            {
                phasesToCopy = c.Query<dynamic>(@"
                    SELECT pt.id, pt.name, pt.department_id,
                           pts.cost_section_template_id, pts.sort_order
                    FROM phase_template_sections pts
                    JOIN phase_templates pt ON pt.id = pts.phase_template_id
                    JOIN cost_section_templates cst ON cst.id = pts.cost_section_template_id
                    LEFT JOIN cost_section_groups g ON g.id = cst.group_id
                    WHERE pts.cost_section_template_id IN @ids
                    ORDER BY g.sort_order, cst.sort_order, pts.sort_order",
                    new { ids = sectionTemplateIds }, tx).ToList();
            }

            // Fasi «trasversali»: predefinite e senza nessuna sezione. Restano com'erano — se
            // vadano tenute è una scelta di anagrafica (vedi PIANO-FASI-MULTISEZIONE.md §7).
            List<dynamic> crossPhases = c.Query<dynamic>(@"
                SELECT pt.id, pt.name, pt.department_id,
                       NULL AS cost_section_template_id, pt.sort_order
                FROM phase_templates pt
                WHERE pt.is_default = 1
                  AND NOT EXISTS (SELECT 1 FROM phase_template_sections s WHERE s.phase_template_id = pt.id)
                ORDER BY pt.sort_order", transaction: tx).ToList();
            phasesToCopy.AddRange(crossPhases);

            foreach (dynamic t in phasesToCopy)
            {
                c.Execute(@"INSERT INTO project_phases
                    (project_id, phase_template_id, name, cost_section_template_id,
                     department_id, sort_order, is_local)
                    VALUES (@ProjId, @TplId, @Name, @SecTplId, @DeptId, @Sort, 0)",
                    new
                    {
                        ProjId = projectId,
                        TplId = (int)t.id,
                        Name = (string)t.name,
                        SecTplId = (int?)t.cost_section_template_id,
                        DeptId = (int?)t.department_id,
                        Sort = (int)t.sort_order
                    }, tx);
            }

            var quoteMatSections = c.Query<dynamic>(
                "SELECT * FROM quote_material_sections WHERE quote_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

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

                var quoteItems = c.Query<dynamic>(
                    "SELECT id, description, quantity, unit_cost, markup_value, item_type, sort_order FROM quote_material_items WHERE section_id=@sid AND COALESCE(is_active,1)=1 ORDER BY sort_order, id",
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

            c.Execute(@"
                INSERT INTO project_pricing (project_id, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup)
                SELECT @projectId, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup
                FROM quote_pricing WHERE quote_id=@quoteId",
                new { projectId, quoteId = id }, tx);

            c.Execute("UPDATE quotes SET status='converted', converted_at=NOW(), project_id=@projectId WHERE id=@Id",
                new { projectId, Id = id }, tx);

            try
            {
                string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
                string fullPath = Path.Combine(basePath, DateTime.Now.Year.ToString(), projectCode);
                c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = projectId }, tx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QuotesController] Impossibile aggiornare server_path per il progetto {ProjectId}", projectId);
            }

            tx.Commit();

            try { _templateCopy.CopyToProject(projectCode); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QuotesController] Impossibile copiare il template della cartella per il progetto {ProjectCode}", projectCode);
            }

            try
            {
                string qn = (string)quote.quote_number;
                _notif.Create("QUOTE_CONVERTED", "INFO",
                    $"Nuova commessa {projectCode} da preventivo {qn}",
                    $"Il preventivo {qn} - {(string)quote.title} - e' stato convertito in commessa {projectCode}.",
                    "PROJECT", projectId, projectId, GetCurrentEmployeeId(),
                    new[] { req.PmId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QuotesController] Impossibile creare la notifica QUOTE_CONVERTED per il PM {PmId} sul progetto {ProjectId}", req.PmId, projectId);
            }

            return Ok(ApiResponse<int>.Ok(projectId, $"Commessa {projectCode} creata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

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

            int masterId = (int?)original.parent_quote_id ?? id;

            int maxRev = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(revision),0) FROM quotes WHERE parent_quote_id=@MasterId OR id=@MasterId",
                new { MasterId = masterId }, tx);
            int newRev = maxRev + 1;

            string baseNumber = ((string)original.quote_number).Split(" Rev")[0];
            string newNumber = $"{baseNumber} Rev {newRev}";

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

            QuoteService.CopyQuoteItems(c, tx, id, newId);

            string quoteType = (string)(original.quote_type ?? "SERVICE");
            if (quoteType == "IMPIANTO")
                QuoteService.CopyQuoteCosting(c, tx, id, newId);

            c.Execute("UPDATE quotes SET status='superseded' WHERE id=@Id", new { Id = id }, tx);

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

            int year = DateTime.Now.Year;
            string prefix = $"PRV-{year}-";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(quote_number, @len+1, 4) AS UNSIGNED)),0) FROM quotes WHERE quote_number LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string newNumber = $"{prefix}{(maxNum + 1):D4}";

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

            QuoteService.CopyQuoteItems(c, tx, id, newId);

            string quoteType = (string)(original.quote_type ?? "SERVICE");
            if (quoteType == "IMPIANTO")
            {
                QuoteService.CopyQuoteCosting(c, tx, id, newId);
                QuoteService.CopyTotalsFromSource(c, id, newId, tx);
            }
            else
            {
                QuoteService.RecalcTotals(c, newId, tx);
            }

            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, @Notes)",
                new { Id = newId, By = GetCurrentEmployeeId(),
                      Notes = $"Duplicato da {(string)original.quote_number}" }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, $"Preventivo {newNumber} creato (duplicato)"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // ITEM ACTIONS (clone, field patch)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpPost("{quoteId}/items/{itemId}/clone")]
    public IActionResult CloneItem(int quoteId, int itemId)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            // Copia parent
            var parent = c.QueryFirstOrDefault<QuoteItemDto>(@"
                SELECT id AS Id, item_type AS ItemType, code AS Code, name AS Name,
                       description_rtf AS DescriptionRtf, unit AS Unit, quantity AS Quantity,
                       cost_price AS CostPrice, sell_price AS SellPrice, discount_pct AS DiscountPct,
                       vat_pct AS VatPct, line_total AS LineTotal, sort_order AS SortOrder,
                       COALESCE(is_active,1) AS IsActive, COALESCE(is_auto_include,0) AS IsAutoInclude,
                       product_id AS ProductId, variant_id AS VariantId
                FROM quote_items WHERE id=@itemId AND quote_id=@quoteId", new { itemId, quoteId }, tx);
            if (parent == null) return NotFound();

            int maxSort = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order),0) FROM quote_items WHERE quote_id=@quoteId", new { quoteId }, tx);

            int newParentId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, item_type, code, name, description_rtf, unit, quantity,
                    cost_price, sell_price, discount_pct, vat_pct, line_total, sort_order, is_active, is_auto_include, product_id, variant_id)
                VALUES (@quoteId, @ItemType, @Code, @Name, @DescriptionRtf, @Unit, @Quantity,
                    @CostPrice, @SellPrice, @DiscountPct, @VatPct, @LineTotal, @sort, @IsActive, @IsAutoInclude, @ProductId, @VariantId);
                SELECT LAST_INSERT_ID();",
                new { quoteId, parent.ItemType, parent.Code, parent.Name, parent.DescriptionRtf, parent.Unit,
                      parent.Quantity, parent.CostPrice, parent.SellPrice, parent.DiscountPct, parent.VatPct,
                      parent.LineTotal, sort = maxSort + 1, parent.IsActive, parent.IsAutoInclude, parent.ProductId, parent.VariantId }, tx);

            // Copia figli
            var children = c.Query<QuoteItemDto>(@"
                SELECT item_type AS ItemType, code AS Code, name AS Name, description_rtf AS DescriptionRtf,
                       unit AS Unit, quantity AS Quantity, cost_price AS CostPrice, sell_price AS SellPrice,
                       discount_pct AS DiscountPct, vat_pct AS VatPct, line_total AS LineTotal,
                       sort_order AS SortOrder, COALESCE(is_active,1) AS IsActive,
                       product_id AS ProductId, variant_id AS VariantId
                FROM quote_items WHERE parent_item_id=@itemId AND quote_id=@quoteId
                ORDER BY sort_order", new { itemId, quoteId }, tx).ToList();

            foreach (var child in children)
            {
                c.Execute(@"
                    INSERT INTO quote_items (quote_id, item_type, code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct, line_total, sort_order, is_active,
                        parent_item_id, product_id, variant_id)
                    VALUES (@quoteId, @ItemType, @Code, @Name, @DescriptionRtf, @Unit, @Quantity,
                        @CostPrice, @SellPrice, @DiscountPct, @VatPct, @LineTotal, @SortOrder, @IsActive,
                        @parentId, @ProductId, @VariantId)",
                    new { quoteId, child.ItemType, child.Code, child.Name, child.DescriptionRtf, child.Unit,
                          child.Quantity, child.CostPrice, child.SellPrice, child.DiscountPct, child.VatPct,
                          child.LineTotal, child.SortOrder, child.IsActive, parentId = newParentId,
                          child.ProductId, child.VariantId }, tx);
            }

            tx.Commit();
            QuoteService.RecalcTotals(c, quoteId, null);
            return Ok(ApiResponse<int>.Ok(newParentId, "Prodotto duplicato"));
        }
        catch (Exception ex) { return StatusCode(500, $"Errore clone: {ex.Message}"); }
    }

    [HttpPatch("{id}/field")]
    public IActionResult UpdateQuoteField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "title", "discount_pct", "discount_abs", "notes_internal", "notes_quote",
            "show_item_prices", "show_summary", "show_summary_prices", "hide_quantities",
            "contact_name1", "contact_name2", "contact_name3", "delivery_days", "validity_days", "payment_type",
            "total", "profit" };
        if (!allowed.Contains(req.Field))
            return BadRequest($"Campo '{req.Field}' non consentito");

        using var c = _qdb.Open();
        string? dbValue = NormalizeDecimalValue(req.Field, req.Value);
        c.Execute($"UPDATE quotes SET `{req.Field}`=@Value WHERE id=@id", new { Value = dbValue, id });
        // Non ricalcolare totali se stiamo aggiornando total/profit direttamente (IMPIANTO costing)
        if (req.Field != "total" && req.Field != "profit")
            QuoteService.RecalcTotals(c, id, null);
        return Ok(ApiResponse<string>.Ok("", "Campo aggiornato"));
    }

    [HttpPatch("{quoteId}/items/{itemId}/field")]
    public IActionResult UpdateItemField(int quoteId, int itemId, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "code", "description_rtf", "unit", "quantity",
            "cost_price", "sell_price", "discount_pct", "vat_pct", "is_active", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest($"Campo '{req.Field}' non consentito");

        using var c = _qdb.Open();
        string? dbValue = NormalizeDecimalValue(req.Field, req.Value);
        c.Execute($"UPDATE quote_items SET `{req.Field}`=@Value WHERE id=@itemId AND quote_id=@quoteId",
            new { Value = dbValue, itemId, quoteId });
        QuoteService.RecalcTotals(c, quoteId, null);
        return Ok(ApiResponse<string>.Ok("", "Campo aggiornato"));
    }

    // Campi decimali nelle quote: converte virgolaâ†’punto per MySQL
    private static readonly HashSet<string> _decimalFields = new()
    {
        "total", "profit", "discount_pct", "discount_abs", "delivery_days", "validity_days",
        "quantity", "cost_price", "sell_price", "vat_pct", "sort_order"
    };

    private static string? NormalizeDecimalValue(string field, string? value)
    {
        if (value == null || !_decimalFields.Contains(field)) return value;
        return value.Replace(',', '.');
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PDF
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet("{id}/pdf")]
    public IActionResult GeneratePdf(int id)
    {
        try
        {
            using var c = _qdb.Open();
            var quote = c.QueryFirstOrDefault<QuoteDto>(@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.contact_name1 AS ContactName1, q.contact_name2 AS ContactName2,
                       q.contact_name3 AS ContactName3,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType, q.revision AS Revision,
                       q.subtotal AS Subtotal, q.discount_pct AS DiscountPct,
                       q.discount_abs AS DiscountAbs, q.vat_total AS VatTotal,
                       q.total AS Total, q.total_with_vat AS TotalWithVat,
                       q.cost_total AS CostTotal, q.profit AS Profit,
                       q.delivery_days AS DeliveryDays, q.validity_days AS ValidityDays,
                       q.payment_type AS PaymentType,
                       q.show_item_prices AS ShowItemPrices,
                       q.show_summary AS ShowSummary,
                       q.show_summary_prices AS ShowSummaryPrices,
                       COALESCE(q.hide_quantities,0) AS HideQuantities,
                       q.notes_quote AS NotesQuote,
                       q.created_at AS CreatedAt
                FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                WHERE q.id = @Id", new { Id = id });

            if (quote == null)
                return NotFound();

            quote.Items = c.Query<QuoteItemDto>(@"
                SELECT id AS Id, item_type AS ItemType, code AS Code, name AS Name,
                       description_rtf AS DescriptionRtf, unit AS Unit,
                       quantity AS Quantity, cost_price AS CostPrice,
                       sell_price AS SellPrice, discount_pct AS DiscountPct,
                       vat_pct AS VatPct, line_total AS LineTotal,
                       line_profit AS LineProfit, sort_order AS SortOrder,
                       COALESCE(is_active,1) AS IsActive, parent_item_id AS ParentItemId,
                       COALESCE(is_auto_include,0) AS IsAutoInclude
                FROM quote_items WHERE quote_id = @Id
                ORDER BY sort_order", new { Id = id }).ToList();

            byte[] pdf;
            if (quote.QuoteType == "IMPIANTO")
            {
                // Carica dati costing per IMPIANTO
                ProjectCostingData costingData = CostingDataService.LoadQuote(c, id);
                pdf = _pdf.GenerateImpianto(quote, costingData);
            }
            else
            {
                pdf = _pdf.Generate(quote);
            }
            string fileName = $"{quote.QuoteNumber.Replace("/", "-")}.pdf";
            return File(pdf, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Errore generazione PDF: {ex.Message}");
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // STATS
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            using var c = _qdb.Open();
            var stats = new QuoteStatsDto
            {
                TotalQuotes = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes"),
                QuotesDraft = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes WHERE status='draft'"),
                QuotesSent = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes WHERE status='sent'"),
                QuotesAccepted = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes WHERE status='accepted'"),
                QuotesRejected = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes WHERE status='rejected'"),
                QuotesConverted = c.ExecuteScalar<int>("SELECT COUNT(*) FROM quotes WHERE status='converted'"),
                TotalValue = c.ExecuteScalar<decimal>("SELECT COALESCE(SUM(total),0) FROM quotes WHERE status NOT IN ('draft','rejected','expired')"),
                TotalProfit = c.ExecuteScalar<decimal>("SELECT COALESCE(SUM(profit),0) FROM quotes WHERE status NOT IN ('draft','rejected','expired')"),
            };
            int closed = stats.QuotesAccepted + stats.QuotesRejected + stats.QuotesConverted;
            stats.ConversionRate = closed > 0 ? Math.Round((decimal)(stats.QuotesAccepted + stats.QuotesConverted) / closed * 100, 1) : 0;
            stats.AvgProfit = stats.TotalQuotes > 0 ? Math.Round(stats.TotalProfit / stats.TotalQuotes, 2) : 0;

            return Ok(ApiResponse<QuoteStatsDto>.Ok(stats));
        }
        catch (Exception ex) { return Ok(ApiResponse<QuoteStatsDto>.Fail($"Errore: {ex.Message}")); }
    }
}

