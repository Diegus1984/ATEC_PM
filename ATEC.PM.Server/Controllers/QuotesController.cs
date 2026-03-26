using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using Serilog;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Shared.Models;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/quotes")]
[Authorize]
public class QuotesController : ControllerBase
{
    private readonly QuoteDbService _qdb;
    private readonly QuotePdfService _pdf;
    public QuotesController(QuoteDbService qdb, QuotePdfService pdf) { _qdb = qdb; _pdf = pdf; }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // ═══════════════════════════════════════════════════════
    // LIST
    // ═══════════════════════════════════════════════════════

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? status = null, [FromQuery] int? customerId = null)
    {
        try
        {
            using var c = _qdb.Open();
            var conditions = new List<string>();
            if (!string.IsNullOrEmpty(status)) conditions.Add("q.status = @Status");
            if (customerId.HasValue) conditions.Add("q.customer_id = @CustomerId");
            string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            var rows = c.Query<QuoteDto>($@"
                SELECT q.id AS Id, q.quote_number AS QuoteNumber, q.title AS Title,
                       q.customer_id AS CustomerId, cu.company_name AS CustomerName,
                       q.status AS Status, COALESCE(q.quote_type,'SERVICE') AS QuoteType, q.revision AS Revision,
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
                FROM quotes q
                LEFT JOIN customers cu ON cu.id = q.customer_id
                LEFT JOIN quote_groups g ON g.id = q.group_id
                LEFT JOIN employees ea ON ea.id = q.assigned_to
                LEFT JOIN employees ec ON ec.id = q.created_by
                {where}
                ORDER BY q.created_at DESC",
                new { Status = status, CustomerId = customerId }).ToList();

            return Ok(ApiResponse<List<QuoteDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // GET BY ID (con items)
    // ═══════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════
    // CREATE — con auto-populate dal template (group_id)
    // ═══════════════════════════════════════════════════════

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

            int quoteId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quotes (quote_number, title, customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, price_list_id, group_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices,
                    notes_internal, notes_quote, assigned_to, created_by, status)
                VALUES (@QuoteNumber, @Title, @CustomerId, @ContactName1, @ContactName2, @ContactName3,
                    @DeliveryDays, @ValidityDays, @PaymentType, @Language, @PriceListId, @GroupId,
                    @DiscountPct, @DiscountAbs, @ShowItemPrices, @ShowSummary, @ShowSummaryPrices,
                    @NotesInternal, @NotesQuote, @AssignedTo, @CreatedBy, 'draft');
                SELECT LAST_INSERT_ID()",
                new {
                    QuoteNumber = quoteNumber, dto.PriceListId, dto.Title, dto.CustomerId,
                    dto.ContactName1, dto.ContactName2, dto.ContactName3,
                    dto.DeliveryDays, dto.ValidityDays, dto.PaymentType, dto.Language,
                    dto.GroupId, dto.DiscountPct, dto.DiscountAbs,
                    dto.ShowItemPrices, dto.ShowSummary, dto.ShowSummaryPrices,
                    dto.NotesInternal, dto.NotesQuote, dto.AssignedTo,
                    CreatedBy = GetCurrentEmployeeId()
                }, tx);

            // Auto-populate: inserisci TUTTI i prodotti auto_include dello stesso listino
            {
                // Se c'è un listino, prendi gli auto_include di quel listino.
                // Altrimenti prendi TUTTI gli auto_include globali.
                string autoSql = @"
                    SELECT p.id AS ProductId, p.item_type, p.code, p.name, p.description_rtf,
                           v.id AS VariantId, v.code AS VarCode, v.name AS VarName,
                           v.cost_price, v.markup_value
                    FROM quote_products p
                    JOIN quote_categories cat ON cat.id = p.category_id
                    JOIN quote_groups g ON g.id = cat.group_id
                    LEFT JOIN quote_product_variants v ON v.product_id = p.id
                    WHERE p.auto_include = 1 AND p.is_active = 1";
                if (dto.PriceListId.HasValue && dto.PriceListId.Value > 0)
                    autoSql += " AND g.price_list_id = @PriceListId";
                autoSql += " ORDER BY g.sort_order, cat.sort_order, p.sort_order, v.sort_order";

                var autoItems = c.Query<dynamic>(autoSql,
                    new { dto.PriceListId }, tx).ToList();

                // Raggruppa per ProductId: inserisci parent + varianti come AddProductWithAllVariants
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
                        // Inserisci parent (header)
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

                        // Inserisci varianti
                        foreach (var v in grp.Where(x => x.VariantId != null))
                        {
                            decimal qty = 1m;
                            decimal cost = v.cost_price ?? 0m;
                            decimal markup = v.markup_value ?? 1.3m;
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
                        // Prodotto senza varianti (es. content)
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

            // Log stato
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, '', 'draft', @By, 'Preventivo creato')",
                new { Id = quoteId, By = GetCurrentEmployeeId() }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(quoteId, $"Preventivo {quoteNumber} creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // UPDATE header
    // ═══════════════════════════════════════════════════════

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
            RecalcTotals(c2, id, null);

            return Ok(ApiResponse<string>.Ok("Preventivo aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // DELETE (solo draft)
    // ═══════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════
    // ITEMS — Aggiungi/Rimuovi/Aggiorna voci
    // ═══════════════════════════════════════════════════════

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

            RecalcTotals(c, id, null);
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
            Log.Information("[AddProduct] Prodotto {Name} — description_rtf.Length={Len}",
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
            Log.Information("[AddProduct] Parent inserito id={ParentId}, desc salvata={Len}", parentId, descriptionRtf.Length);

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

            RecalcTotals(c, id, null);
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

            RecalcTotals(c, quoteId, null);
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
            RecalcTotals(c, quoteId, null);
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

            RecalcTotals(c, quoteId, null);
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

    // ═══════════════════════════════════════════════════════
    // STATUS — Cambio stato con validazione
    // ═══════════════════════════════════════════════════════

    [HttpPut("{id}/status")]
    public IActionResult ChangeStatus(int id, [FromBody] QuoteStatusChangeDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            string currentStatus = c.ExecuteScalar<string>("SELECT status FROM quotes WHERE id=@Id", new { Id = id }) ?? "";

            // Validazione: lo stato deve essere valido
            var validStatuses = new[] { "draft", "sent", "negotiation", "accepted", "rejected", "expired", "converted", "superseded" };
            if (!validStatuses.Contains(dto.NewStatus))
                return Ok(ApiResponse<string>.Fail($"Stato '{dto.NewStatus}' non valido"));
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

            c.Execute($"UPDATE quotes SET status=@Status{extraSql} WHERE id=@Id",
                new { Status = dto.NewStatus, Id = id });

            // Log
            c.Execute(@"INSERT INTO quote_status_log (quote_id, old_status, new_status, changed_by, notes)
                        VALUES (@Id, @Old, @New, @By, @Notes)",
                new { Id = id, Old = currentStatus, New = dto.NewStatus, By = GetCurrentEmployeeId(), dto.Notes });

            return Ok(ApiResponse<string>.Ok($"Stato aggiornato a {dto.NewStatus}"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // DUPLICATE
    // ═══════════════════════════════════════════════════════

    [HttpPost("{id}/duplicate")]
    public IActionResult Duplicate(int id)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            var src = c.QueryFirstOrDefault<QuoteDto>(@"
                SELECT * FROM quotes WHERE id=@Id", new { Id = id }, tx);
            if (src == null) return Ok(ApiResponse<int>.Fail("Non trovato"));

            // Genera nuovo numero
            int year = DateTime.Now.Year;
            string prefix = $"PRV-{year}-";
            int maxNum = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTRING(quote_number, @len+1) AS UNSIGNED)),0) FROM quotes WHERE quote_number LIKE @pref",
                new { len = prefix.Length, pref = prefix + "%" }, tx);
            string newNumber = $"{prefix}{(maxNum + 1):D4}";

            int newId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quotes (quote_number, title, customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, group_id, price_list_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices, hide_quantities,
                    notes_internal, notes_quote, assigned_to, created_by, status, revision)
                SELECT @NewNum, CONCAT(title, ' (copia)'), customer_id, contact_name1, contact_name2, contact_name3,
                    delivery_days, validity_days, payment_type, language, group_id, price_list_id,
                    discount_pct, discount_abs, show_item_prices, show_summary, show_summary_prices, hide_quantities,
                    notes_internal, notes_quote, assigned_to, @By, status, 0
                FROM quotes WHERE id=@Id;
                SELECT LAST_INSERT_ID()",
                new { NewNum = newNumber, By = GetCurrentEmployeeId(), Id = id }, tx);

            // Clona items
            // Clona items mantenendo gerarchia padre/figlio
            var oldItems = c.Query<(int Id, int? ParentItemId)>(
                "SELECT id, parent_item_id FROM quote_items WHERE quote_id=@Id ORDER BY sort_order",
                new { Id = id }, tx).ToList();

            // Prima inserisci i padri (parent_item_id IS NULL)
            var idMap = new Dictionary<int, int>(); // oldId → newId
            foreach (var item in oldItems.Where(x => x.ParentItemId == null))
            {
                int newItemId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                    SELECT @NewId, product_id, variant_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, COALESCE(is_auto_include,0)
                    FROM quote_items WHERE id=@OldId;
                    SELECT LAST_INSERT_ID()",
                    new { NewId = newId, OldId = item.Id }, tx);
                idMap[item.Id] = newItemId;
            }

            // Poi inserisci i figli con parent_item_id mappato
            foreach (var item in oldItems.Where(x => x.ParentItemId != null))
            {
                int? newParentId = idMap.GetValueOrDefault(item.ParentItemId!.Value);
                c.Execute(@"
                    INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                    SELECT @NewId, product_id, variant_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, @NewParentId, COALESCE(is_auto_include,0)
                    FROM quote_items WHERE id=@OldId",
                    new { NewId = newId, OldId = item.Id, NewParentId = newParentId }, tx);
            }

            RecalcTotals(c, newId, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, $"Preventivo duplicato come {newNumber}"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // ITEM ACTIONS (clone, field patch)
    // ═══════════════════════════════════════════════════════

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
            RecalcTotals(c, quoteId, null);
            return Ok(ApiResponse<int>.Ok(newParentId, "Prodotto duplicato"));
        }
        catch (Exception ex) { return StatusCode(500, $"Errore clone: {ex.Message}"); }
    }

    [HttpPatch("{id}/field")]
    public IActionResult UpdateQuoteField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "title", "discount_pct", "discount_abs", "notes_internal", "notes_quote",
            "show_item_prices", "show_summary", "show_summary_prices", "hide_quantities",
            "contact_name1", "contact_name2", "contact_name3", "delivery_days", "validity_days", "payment_type" };
        if (!allowed.Contains(req.Field))
            return BadRequest($"Campo '{req.Field}' non consentito");

        using var c = _qdb.Open();
        c.Execute($"UPDATE quotes SET `{req.Field}`=@Value WHERE id=@id", new { Value = req.Value, id });
        RecalcTotals(c, id, null);
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
        c.Execute($"UPDATE quote_items SET `{req.Field}`=@Value WHERE id=@itemId AND quote_id=@quoteId",
            new { Value = req.Value, itemId, quoteId });
        RecalcTotals(c, quoteId, null);
        return Ok(ApiResponse<string>.Ok("", "Campo aggiornato"));
    }

    // ═══════════════════════════════════════════════════════
    // PDF
    // ═══════════════════════════════════════════════════════

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
                var costingData = LoadCostingData(c, id);
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

    // ═══════════════════════════════════════════════════════
    // STATS
    // ═══════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════
    // RECALC TOTALS
    // ═══════════════════════════════════════════════════════

    private void RecalcTotals(MySqlConnector.MySqlConnection c, int quoteId, System.Data.IDbTransaction? tx)
    {
        var totals = c.QueryFirstOrDefault<dynamic>(@"
            SELECT COALESCE(SUM(line_total),0) AS subtotal,
                   COALESCE(SUM(line_total * vat_pct / 100),0) AS vat_total,
                   COALESCE(SUM(CASE WHEN item_type='product' THEN quantity * cost_price ELSE 0 END),0) AS cost_total
            FROM quote_items WHERE quote_id=@Id AND COALESCE(is_active,1)=1 AND quantity>0",
            new { Id = quoteId }, (System.Data.IDbTransaction?)tx);

        decimal subtotal = (decimal)(totals?.subtotal ?? 0m);
        decimal vatTotal = (decimal)(totals?.vat_total ?? 0m);
        decimal costTotal = (decimal)(totals?.cost_total ?? 0m);

        // Applica sconto globale
        var discountInfo = c.QueryFirstOrDefault<dynamic>(
            "SELECT discount_pct, discount_abs FROM quotes WHERE id=@Id",
            new { Id = quoteId }, (System.Data.IDbTransaction?)tx);

        decimal discPct = (decimal)(discountInfo?.discount_pct ?? 0m);
        decimal discAbs = (decimal)(discountInfo?.discount_abs ?? 0m);
        decimal discountAmount = subtotal * discPct / 100m + discAbs;
        decimal total = subtotal - discountAmount;
        decimal totalWithVat = total + vatTotal;
        decimal profit = total - costTotal;

        c.Execute(@"UPDATE quotes SET subtotal=@Sub, vat_total=@Vat, total=@Tot,
                    total_with_vat=@TotVat, cost_total=@Cost, profit=@Profit WHERE id=@Id",
            new { Sub = subtotal, Vat = vatTotal, Tot = total, TotVat = totalWithVat,
                  Cost = costTotal, Profit = profit, Id = quoteId },
            (System.Data.IDbTransaction?)tx);
    }

    /// <summary>Carica dati costing per preventivo IMPIANTO (usato per PDF)</summary>
    private static ProjectCostingData LoadCostingData(MySqlConnector.MySqlConnection c, int quoteId)
    {
        var sections = c.Query<ProjectCostSectionDto>(@"
            SELECT id, quote_id AS ProjectId, template_id AS TemplateId, name, section_type AS SectionType,
                   group_name AS GroupName, sort_order AS SortOrder, is_enabled AS IsEnabled,
                   contingency_pct AS ContingencyPct, margin_pct AS MarginPct,
                   contingency_pinned AS ContingencyPinned, margin_pinned AS MarginPinned,
                   COALESCE(is_shadowed,0) AS IsShadowed
            FROM quote_cost_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();

        var allResources = c.Query<ProjectCostResourceDto>(@"
            SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId,
                   r.resource_name AS ResourceName, r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay,
                   r.hourly_cost AS HourlyCost, r.markup_value AS MarkupValue,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance,
                   r.sort_order AS SortOrder
            FROM quote_cost_resources r
            JOIN quote_cost_sections s ON s.id = r.section_id
            WHERE s.quote_id=@quoteId ORDER BY r.sort_order", new { quoteId }).ToList();
        foreach (var sec in sections)
            sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();

        var matSections = c.Query<ProjectMaterialSectionDto>(@"
            SELECT id, quote_id AS ProjectId, name, markup_value AS MarkupValue,
                   commission_markup AS CommissionMarkup, sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM quote_material_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();

        var allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.parent_item_id AS ParentItemId,
                   i.product_id AS ProductId, i.variant_id AS VariantId,
                   COALESCE(i.code,'') AS Code, i.description AS Description,
                   i.description_rtf AS DescriptionRtf,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost,
                   i.markup_value AS MarkupValue, i.item_type AS ItemType, i.sort_order AS SortOrder,
                   i.contingency_pct AS ContingencyPct, i.margin_pct AS MarginPct,
                   i.contingency_pinned AS ContingencyPinned, i.margin_pinned AS MarginPinned,
                   COALESCE(i.is_shadowed,0) AS IsShadowed, COALESCE(i.is_active,1) AS IsActive
            FROM quote_material_items i
            JOIN quote_material_sections s ON s.id = i.section_id
            WHERE s.quote_id=@quoteId ORDER BY i.sort_order", new { quoteId }).ToList();
        foreach (var ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();

        var pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, quote_id AS ProjectId, contingency_pct AS ContingencyPct,
                   negotiation_margin_pct AS NegotiationMarginPct,
                   travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup
            FROM quote_pricing WHERE quote_id=@quoteId", new { quoteId })
            ?? new ProjectPricingDto { ProjectId = quoteId };

        return new ProjectCostingData
        {
            ProjectId = quoteId, IsInitialized = true,
            CostSections = sections, MaterialSections = matSections, Pricing = pricing
        };
    }
}
