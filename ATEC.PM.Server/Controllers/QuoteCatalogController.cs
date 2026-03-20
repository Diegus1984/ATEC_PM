using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using Serilog;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Shared.Models;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/quote-catalog")]
[Authorize]
public class QuoteCatalogController : ControllerBase
{
    private readonly QuoteDbService _qdb;
    private readonly IConfiguration _config;
    public QuoteCatalogController(QuoteDbService qdb, IConfiguration config)
    {
        _qdb = qdb;
        _config = config;
    }

    // ═══════════════════════════════════════════════════════
    // PRICE LISTS — Listini
    // ═══════════════════════════════════════════════════════

    [HttpGet("price-lists")]
    public IActionResult GetPriceLists()
    {
        try
        {
            using var c = _qdb.Open();
            var rows = c.Query<QuotePriceListDto>(@"
                SELECT pl.id AS Id, pl.name AS Name, pl.currency AS Currency,
                       pl.locale AS Locale, pl.is_active AS IsActive, pl.sort_order AS SortOrder,
                       (SELECT COUNT(*) FROM quote_groups WHERE price_list_id = pl.id) AS GroupCount
                FROM quote_price_lists pl
                ORDER BY pl.sort_order, pl.name").ToList();
            return Ok(ApiResponse<List<QuotePriceListDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuotePriceListDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("price-lists")]
    public IActionResult CreatePriceList([FromBody] QuotePriceListSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            int id = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_price_lists (name, currency, locale, is_active, sort_order)
                VALUES (@Name, @Currency, @Locale, @IsActive, @SortOrder);
                SELECT LAST_INSERT_ID()", dto);
            return Ok(ApiResponse<int>.Ok(id, "Listino creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("price-lists/{id}")]
    public IActionResult UpdatePriceList(int id, [FromBody] QuotePriceListSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute(@"UPDATE quote_price_lists SET name=@Name, currency=@Currency,
                        locale=@Locale, is_active=@IsActive, sort_order=@SortOrder WHERE id=@Id",
                new { dto.Name, dto.Currency, dto.Locale, dto.IsActive, dto.SortOrder, Id = id });
            return Ok(ApiResponse<string>.Ok("Listino aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("price-lists/{id}")]
    public IActionResult DeletePriceList(int id)
    {
        try
        {
            using var c = _qdb.Open();
            // Aggiorna gruppi orfani
            c.Execute("UPDATE quote_groups SET price_list_id=NULL WHERE price_list_id=@Id", new { Id = id });
            c.Execute("DELETE FROM quote_price_lists WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("Listino eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // TREE — Albero completo Gruppi → Categorie (per TreeView)
    // ═══════════════════════════════════════════════════════

    [HttpGet("tree")]
    public IActionResult GetTree([FromQuery] int? priceListId = null)
    {
        try
        {
            using var c = _qdb.Open();

            string whereGroup = priceListId.HasValue ? "WHERE g.price_list_id = @PriceListId" : "";
            var groups = c.Query<QuoteGroupDto>($@"
                SELECT g.id AS Id, g.price_list_id AS PriceListId,
                       COALESCE(pl.name,'') AS PriceListName,
                       g.name AS Name, g.description AS Description,
                       g.sort_order AS SortOrder, g.is_active AS IsActive,
                       (SELECT COUNT(*) FROM quote_categories WHERE group_id = g.id) AS CategoryCount,
                       (SELECT COUNT(*) FROM quote_products p
                        JOIN quote_categories cat ON cat.id = p.category_id
                        WHERE cat.group_id = g.id) AS ProductCount
                FROM quote_groups g
                LEFT JOIN quote_price_lists pl ON pl.id = g.price_list_id
                {whereGroup}
                ORDER BY g.sort_order, g.name", new { PriceListId = priceListId }).ToList();

            var categories = c.Query<QuoteCategoryDto>(@"
                SELECT c.id AS Id, c.group_id AS GroupId, g.name AS GroupName,
                       c.name AS Name, c.description AS Description,
                       c.sort_order AS SortOrder, c.is_active AS IsActive,
                       (SELECT COUNT(*) FROM quote_products WHERE category_id = c.id) AS ProductCount
                FROM quote_categories c
                JOIN quote_groups g ON g.id = c.group_id
                ORDER BY c.sort_order, c.name").ToList();

            foreach (var g in groups)
                g.Categories = categories.Where(cat => cat.GroupId == g.Id).ToList();

            var tree = new QuoteCatalogTreeDto
            {
                Groups = groups,
                TotalGroups = groups.Count,
                TotalCategories = categories.Count,
                TotalProducts = groups.Sum(g => g.ProductCount)
            };

            return Ok(ApiResponse<QuoteCatalogTreeDto>.Ok(tree));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<QuoteCatalogTreeDto>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ═══════════════════════════════════════════════════════
    // GROUPS — CRUD
    // ═══════════════════════════════════════════════════════

    [HttpGet("groups")]
    public IActionResult GetGroups([FromQuery] int? priceListId = null)
    {
        try
        {
            using var c = _qdb.Open();
            string where = priceListId.HasValue ? "WHERE g.price_list_id = @PriceListId" : "";
            var rows = c.Query<QuoteGroupDto>($@"
                SELECT g.id AS Id, g.price_list_id AS PriceListId,
                       COALESCE(pl.name,'') AS PriceListName,
                       g.name AS Name, g.description AS Description,
                       g.sort_order AS SortOrder, g.is_active AS IsActive,
                       (SELECT COUNT(*) FROM quote_categories WHERE group_id = g.id) AS CategoryCount,
                       (SELECT COUNT(*) FROM quote_products p
                        JOIN quote_categories cat ON cat.id = p.category_id
                        WHERE cat.group_id = g.id) AS ProductCount
                FROM quote_groups g
                LEFT JOIN quote_price_lists pl ON pl.id = g.price_list_id
                {where}
                ORDER BY g.sort_order, g.name", new { PriceListId = priceListId }).ToList();
            return Ok(ApiResponse<List<QuoteGroupDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteGroupDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("groups")]
    public IActionResult CreateGroup([FromBody] QuoteGroupSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            int id = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_groups (price_list_id, name, description, sort_order, is_active)
                VALUES (@PriceListId, @Name, @Description, @SortOrder, @IsActive);
                SELECT LAST_INSERT_ID()", dto);
            return Ok(ApiResponse<int>.Ok(id, "Gruppo creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("groups/{id}")]
    public IActionResult UpdateGroup(int id, [FromBody] QuoteGroupSaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute(@"UPDATE quote_groups SET price_list_id=@PriceListId, name=@Name, description=@Description,
                        sort_order=@SortOrder, is_active=@IsActive WHERE id=@Id",
                new { dto.PriceListId, dto.Name, dto.Description, dto.SortOrder, dto.IsActive, Id = id });
            return Ok(ApiResponse<string>.Ok("Gruppo aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("groups/{id}")]
    public IActionResult DeleteGroup(int id)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute("DELETE FROM quote_groups WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("Gruppo eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // CATEGORIES — CRUD
    // ═══════════════════════════════════════════════════════

    [HttpGet("categories")]
    public IActionResult GetCategories([FromQuery] int? groupId = null)
    {
        try
        {
            using var c = _qdb.Open();
            string where = groupId.HasValue ? "WHERE c.group_id = @GroupId" : "";
            var rows = c.Query<QuoteCategoryDto>($@"
                SELECT c.id AS Id, c.group_id AS GroupId, g.name AS GroupName,
                       c.name AS Name, c.description AS Description,
                       c.sort_order AS SortOrder, c.is_active AS IsActive,
                       (SELECT COUNT(*) FROM quote_products WHERE category_id = c.id) AS ProductCount
                FROM quote_categories c
                JOIN quote_groups g ON g.id = c.group_id
                {where}
                ORDER BY c.sort_order, c.name", new { GroupId = groupId }).ToList();
            return Ok(ApiResponse<List<QuoteCategoryDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteCategoryDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("categories")]
    public IActionResult CreateCategory([FromBody] QuoteCategorySaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            int id = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_categories (group_id, name, description, sort_order, is_active)
                VALUES (@GroupId, @Name, @Description, @SortOrder, @IsActive);
                SELECT LAST_INSERT_ID()", dto);
            return Ok(ApiResponse<int>.Ok(id, "Categoria creata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("categories/{id}")]
    public IActionResult UpdateCategory(int id, [FromBody] QuoteCategorySaveDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute(@"UPDATE quote_categories SET group_id=@GroupId, name=@Name,
                        description=@Description, sort_order=@SortOrder, is_active=@IsActive
                        WHERE id=@Id",
                new { dto.GroupId, dto.Name, dto.Description, dto.SortOrder, dto.IsActive, Id = id });
            return Ok(ApiResponse<string>.Ok("Categoria aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("categories/{id}")]
    public IActionResult DeleteCategory(int id)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute("DELETE FROM quote_categories WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("Categoria eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // PRODUCTS — CRUD con varianti
    // ═══════════════════════════════════════════════════════

    [HttpGet("products")]
    public IActionResult GetProducts([FromQuery] int? categoryId = null, [FromQuery] int? groupId = null)
    {
        try
        {
            using var c = _qdb.Open();
            var conditions = new List<string>();
            if (categoryId.HasValue) conditions.Add("p.category_id = @CategoryId");
            if (groupId.HasValue) conditions.Add("cat.group_id = @GroupId");
            string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            var products = c.Query<QuoteProductDto>($@"
                SELECT p.id AS Id, p.category_id AS CategoryId, cat.name AS CategoryName,
                       g.name AS GroupName, p.item_type AS ItemType, p.code AS Code,
                       p.name AS Name, p.description_rtf AS DescriptionRtf,
                       p.image_path AS ImagePath, p.attachment_path AS AttachmentPath,
                       p.auto_include AS AutoInclude, p.sort_order AS SortOrder,
                       p.is_active AS IsActive
                FROM quote_products p
                JOIN quote_categories cat ON cat.id = p.category_id
                JOIN quote_groups g ON g.id = cat.group_id
                {where}
                ORDER BY p.sort_order, p.name",
                new { CategoryId = categoryId, GroupId = groupId }).ToList();

            if (products.Count > 0)
            {
                var productIds = products.Select(p => p.Id).ToList();
                var variants = c.Query<QuoteProductVariantDto>(@"
                    SELECT id AS Id, product_id AS ProductId, code AS Code, name AS Name,
                           cost_price AS CostPrice, sell_price AS SellPrice,
                           discount_pct AS DiscountPct, vat_pct AS VatPct,
                           unit AS Unit, default_qty AS DefaultQty, sort_order AS SortOrder
                    FROM quote_product_variants
                    WHERE product_id IN @Ids
                    ORDER BY sort_order, name", new { Ids = productIds }).ToList();

                foreach (var p in products)
                    p.Variants = variants.Where(v => v.ProductId == p.Id).ToList();
            }

            return Ok(ApiResponse<List<QuoteProductDto>>.Ok(products));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<QuoteProductDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("products/{id}")]
    public IActionResult GetProduct(int id)
    {
        try
        {
            using var c = _qdb.Open();
            var product = c.QueryFirstOrDefault<QuoteProductDto>(@"
                SELECT p.id AS Id, p.category_id AS CategoryId, cat.name AS CategoryName,
                       g.name AS GroupName, p.item_type AS ItemType, p.code AS Code,
                       p.name AS Name, p.description_rtf AS DescriptionRtf,
                       p.image_path AS ImagePath, p.attachment_path AS AttachmentPath,
                       p.auto_include AS AutoInclude, p.sort_order AS SortOrder,
                       p.is_active AS IsActive
                FROM quote_products p
                JOIN quote_categories cat ON cat.id = p.category_id
                JOIN quote_groups g ON g.id = cat.group_id
                WHERE p.id = @Id", new { Id = id });

            if (product == null)
                return Ok(ApiResponse<QuoteProductDto>.Fail("Prodotto non trovato"));

            product.Variants = c.Query<QuoteProductVariantDto>(@"
                SELECT id AS Id, product_id AS ProductId, code AS Code, name AS Name,
                       cost_price AS CostPrice, sell_price AS SellPrice,
                       discount_pct AS DiscountPct, vat_pct AS VatPct,
                       unit AS Unit, default_qty AS DefaultQty, sort_order AS SortOrder
                FROM quote_product_variants WHERE product_id = @Id
                ORDER BY sort_order, name", new { Id = id }).ToList();

            return Ok(ApiResponse<QuoteProductDto>.Ok(product));
        }
        catch (Exception ex) { return Ok(ApiResponse<QuoteProductDto>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("products")]
    public IActionResult CreateProduct([FromBody] QuoteProductSaveDto? dto)
    {
        Log.Information("[QuoteProduct] POST /products chiamato");

        if (dto == null)
        {
            Log.Warning("[QuoteProduct] DTO null — body non deserializzato");
            return Ok(ApiResponse<int>.Fail("Dati non ricevuti (body vuoto o non valido)"));
        }

        Log.Information("[QuoteProduct] DTO ricevuto: Name={Name}, Code={Code}, CategoryId={CatId}, ItemType={Type}, AttachmentPath={Att}, DescriptionRtf.Length={DescLen}, Variants={VarCount}",
            dto.Name, dto.Code, dto.CategoryId, dto.ItemType, dto.AttachmentPath,
            dto.DescriptionRtf?.Length ?? 0, dto.Variants?.Count ?? 0);

        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            int productId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_products (category_id, item_type, code, name, description_rtf,
                    image_path, attachment_path, auto_include, sort_order, is_active)
                VALUES (@CategoryId, @ItemType, @Code, @Name, @DescriptionRtf,
                    @ImagePath, @AttachmentPath, @AutoInclude, @SortOrder, @IsActive);
                SELECT LAST_INSERT_ID()", dto, tx);

            Log.Information("[QuoteProduct] Prodotto inserito con Id={ProductId}", productId);

            foreach (var v in dto.Variants)
            {
                c.Execute(@"INSERT INTO quote_product_variants
                    (product_id, code, name, cost_price, sell_price, discount_pct, vat_pct, unit, default_qty, sort_order)
                    VALUES (@ProductId, @Code, @Name, @CostPrice, @SellPrice, @DiscountPct, @VatPct, @Unit, @DefaultQty, @SortOrder)",
                    new { ProductId = productId, v.Code, v.Name, v.CostPrice, v.SellPrice,
                          v.DiscountPct, v.VatPct, v.Unit, v.DefaultQty, v.SortOrder }, tx);
            }

            tx.Commit();
            Log.Information("[QuoteProduct] Commit OK — prodotto {Id} creato con {VarCount} varianti", productId, dto.Variants.Count);
            return Ok(ApiResponse<int>.Ok(productId, "Prodotto creato"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuoteProduct] Errore creazione prodotto");
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("products/{id}")]
    public IActionResult UpdateProduct(int id, [FromBody] QuoteProductSaveDto? dto)
    {
        Log.Information("[QuoteProduct] PUT /products/{Id} chiamato", id);

        if (dto == null)
        {
            Log.Warning("[QuoteProduct] DTO null — body non deserializzato per Id={Id}", id);
            return Ok(ApiResponse<string>.Fail("Dati non ricevuti (body vuoto o non valido)"));
        }

        Log.Information("[QuoteProduct] DTO ricevuto: Name={Name}, Code={Code}, CategoryId={CatId}, ItemType={Type}, AttachmentPath={Att}, DescriptionRtf.Length={DescLen}, Variants={VarCount}",
            dto.Name, dto.Code, dto.CategoryId, dto.ItemType, dto.AttachmentPath,
            dto.DescriptionRtf?.Length ?? 0, dto.Variants?.Count ?? 0);

        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            Log.Information("[QuoteProduct] Esecuzione UPDATE quote_products per Id={Id}", id);
            c.Execute(@"UPDATE quote_products SET category_id=@CategoryId, item_type=@ItemType,
                        code=@Code, name=@Name, description_rtf=@DescriptionRtf,
                        image_path=@ImagePath, attachment_path=@AttachmentPath,
                        auto_include=@AutoInclude, sort_order=@SortOrder, is_active=@IsActive
                        WHERE id=@Id",
                new { dto.CategoryId, dto.ItemType, dto.Code, dto.Name, dto.DescriptionRtf,
                      dto.ImagePath, dto.AttachmentPath, dto.AutoInclude, dto.SortOrder, dto.IsActive, Id = id }, tx);
            Log.Information("[QuoteProduct] UPDATE OK");

            // Strategia varianti: elimina le non presenti, aggiorna le esistenti, inserisci le nuove
            var incomingIds = dto.Variants.Where(v => v.Id > 0).Select(v => v.Id).ToList();
            Log.Information("[QuoteProduct] Varianti: {Existing} esistenti, {Total} totali", incomingIds.Count, dto.Variants.Count);

            if (incomingIds.Count > 0)
                c.Execute("DELETE FROM quote_product_variants WHERE product_id=@Pid AND id NOT IN @Ids",
                    new { Pid = id, Ids = incomingIds }, tx);
            else
                c.Execute("DELETE FROM quote_product_variants WHERE product_id=@Pid",
                    new { Pid = id }, tx);

            foreach (var v in dto.Variants)
            {
                if (v.Id > 0)
                {
                    c.Execute(@"UPDATE quote_product_variants SET code=@Code, name=@Name,
                                cost_price=@CostPrice, sell_price=@SellPrice, discount_pct=@DiscountPct,
                                vat_pct=@VatPct, unit=@Unit, default_qty=@DefaultQty, sort_order=@SortOrder
                                WHERE id=@Id",
                        new { v.Code, v.Name, v.CostPrice, v.SellPrice, v.DiscountPct,
                              v.VatPct, v.Unit, v.DefaultQty, v.SortOrder, v.Id }, tx);
                }
                else
                {
                    c.Execute(@"INSERT INTO quote_product_variants
                        (product_id, code, name, cost_price, sell_price, discount_pct, vat_pct, unit, default_qty, sort_order)
                        VALUES (@Pid, @Code, @Name, @CostPrice, @SellPrice, @DiscountPct, @VatPct, @Unit, @DefaultQty, @SortOrder)",
                        new { Pid = id, v.Code, v.Name, v.CostPrice, v.SellPrice,
                              v.DiscountPct, v.VatPct, v.Unit, v.DefaultQty, v.SortOrder }, tx);
                }
            }

            tx.Commit();
            Log.Information("[QuoteProduct] Commit OK — prodotto {Id} aggiornato", id);
            return Ok(ApiResponse<string>.Ok("Prodotto aggiornato"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuoteProduct] Errore aggiornamento prodotto {Id}", id);
            return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>Pulizia: rimuove base64 da description_rtf e pulisce image_path/attachment_path vecchi</summary>
    [HttpPost("products/cleanup-images")]
    public IActionResult CleanupOldImages()
    {
        try
        {
            using var c = _qdb.Open();

            // Trova prodotti con base64 nel description_rtf
            var products = c.Query<(int Id, string DescriptionRtf)>(
                "SELECT id AS Id, description_rtf AS DescriptionRtf FROM quote_products WHERE description_rtf LIKE '%data:image%'").ToList();

            int cleaned = 0;
            foreach (var p in products)
            {
                // Rimuovi tag <img> con src base64
                string html = System.Text.RegularExpressions.Regex.Replace(
                    p.DescriptionRtf ?? "",
                    @"<img[^>]*src\s*=\s*[""']data:image[^""']*[""'][^>]*\/?>",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                c.Execute("UPDATE quote_products SET description_rtf=@Html WHERE id=@Id",
                    new { Html = html, Id = p.Id });
                cleaned++;
            }

            // Pulisci image_path e attachment_path con path locali
            int pathsCleaned = c.Execute(@"
                UPDATE quote_products SET image_path='', attachment_path=''
                WHERE image_path LIKE 'C:%' OR image_path LIKE 'D:%'
                   OR attachment_path LIKE 'C:%' OR attachment_path LIKE 'D:%'");

            // Stessa pulizia per quote_items
            var items = c.Query<(int Id, string DescriptionRtf)>(
                "SELECT id AS Id, description_rtf AS DescriptionRtf FROM quote_items WHERE description_rtf LIKE '%data:image%'").ToList();

            foreach (var item in items)
            {
                string html = System.Text.RegularExpressions.Regex.Replace(
                    item.DescriptionRtf ?? "",
                    @"<img[^>]*src\s*=\s*[""']data:image[^""']*[""'][^>]*\/?>",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                c.Execute("UPDATE quote_items SET description_rtf=@Html WHERE id=@Id",
                    new { Html = html, Id = item.Id });
            }

            Log.Information("[QuoteProduct] Cleanup: {Products} prodotti puliti, {Paths} path locali rimossi, {Items} quote_items puliti",
                cleaned, pathsCleaned, items.Count);

            return Ok(ApiResponse<string>.Ok($"Pulizia completata: {cleaned} prodotti, {items.Count} items, {pathsCleaned} path locali"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuoteProduct] Errore cleanup immagini");
            return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpDelete("products/{id}")]
    public IActionResult DeleteProduct(int id)
    {
        try
        {
            using var c = _qdb.Open();
            c.Execute("DELETE FROM quote_products WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("Prodotto eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    // ── Upload allegato prodotto ──

    [HttpPost("products/upload")]
    [RequestSizeLimit(50_000_000)] // 50 MB
    public IActionResult UploadProductAttachment(IFormFile file)
    {
        Log.Information("[QuoteProduct] POST /products/upload — FileName={Name}, Size={Size}",
            file?.FileName ?? "null", file?.Length ?? 0);
        try
        {
            if (file == null || file.Length == 0)
            {
                Log.Warning("[QuoteProduct] Upload fallito: file null o vuoto");
                return Ok(ApiResponse<string>.Fail("Nessun file ricevuto"));
            }

            string cmsPath = _config["Uploads:CmsPath"]
                ?? Path.Combine(AppContext.BaseDirectory, "uploads", "cms");
            string productsDir = Path.Combine(cmsPath, "products");
            Directory.CreateDirectory(productsDir);

            // Nome sicuro con timestamp per evitare collisioni
            string safeName = Path.GetFileName(file.FileName).Replace("..", "");
            string fileName = $"att_{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}";
            string fullPath = Path.Combine(productsDir, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                file.CopyTo(stream);

            // Ritorna il path relativo per accesso via URL
            string relativePath = $"/uploads/cms/products/{fileName}";
            Log.Information("[QuoteProduct] Upload OK — salvato in {Path}", fullPath);
            return Ok(ApiResponse<string>.Ok(relativePath, "File caricato"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuoteProduct] Errore upload file");
            return Ok(ApiResponse<string>.Fail($"Errore upload: {ex.Message}"));
        }
    }

    [HttpPost("products/{id}/duplicate")]
    public IActionResult DuplicateProduct(int id)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            var src = c.QueryFirstOrDefault<dynamic>(
                "SELECT * FROM quote_products WHERE id=@Id", new { Id = id }, tx);
            if (src == null)
                return Ok(ApiResponse<int>.Fail("Prodotto non trovato"));

            int newId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_products (category_id, item_type, code, name, description_rtf,
                    image_path, attachment_path, auto_include, sort_order, is_active)
                SELECT category_id, item_type, CONCAT(code, '-COPY'), CONCAT(name, ' (copia)'),
                    description_rtf, image_path, attachment_path, auto_include, sort_order, is_active
                FROM quote_products WHERE id=@Id;
                SELECT LAST_INSERT_ID()", new { Id = id }, tx);

            c.Execute(@"INSERT INTO quote_product_variants
                (product_id, code, name, cost_price, sell_price, discount_pct, vat_pct, unit, default_qty, sort_order)
                SELECT @NewId, code, name, cost_price, sell_price, discount_pct, vat_pct, unit, default_qty, sort_order
                FROM quote_product_variants WHERE product_id=@Id",
                new { NewId = newId, Id = id }, tx);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, "Prodotto duplicato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // IMPORT — Importazione catalogo da Excel
    // ═══════════════════════════════════════════════════════

    [HttpPost("import")]
    public IActionResult ImportCatalog([FromBody] QuoteCatalogImportDto dto)
    {
        try
        {
            using var c = _qdb.Open();
            using var tx = c.BeginTransaction();

            int totalGroups = 0, totalCats = 0, totalProds = 0, totalVars = 0;

            foreach (var listino in dto.PriceLists)
            {
                int plId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_price_lists (name, currency, locale, is_active)
                    VALUES (@Name, @Currency, @Locale, 1);
                    SELECT LAST_INSERT_ID()",
                    new { listino.Name, listino.Currency, listino.Locale }, tx);

                int gSort = 0;
                foreach (var group in listino.Groups)
                {
                    int gId = (int)c.ExecuteScalar<long>(@"
                        INSERT INTO quote_groups (price_list_id, name, sort_order, is_active)
                        VALUES (@PlId, @Name, @Sort, 1);
                        SELECT LAST_INSERT_ID()",
                        new { PlId = plId, group.Name, Sort = gSort++ }, tx);
                    totalGroups++;

                    int cSort = 0;
                    foreach (var cat in group.Categories)
                    {
                        int catId = (int)c.ExecuteScalar<long>(@"
                            INSERT INTO quote_categories (group_id, name, sort_order, is_active)
                            VALUES (@GId, @Name, @Sort, 1);
                            SELECT LAST_INSERT_ID()",
                            new { GId = gId, cat.Name, Sort = cSort++ }, tx);
                        totalCats++;

                        int pSort = 0;
                        foreach (var prod in cat.Products)
                        {
                            string itemType = prod.ItemType == "content" ? "content" : "product";
                            int pId = (int)c.ExecuteScalar<long>(@"
                                INSERT INTO quote_products (category_id, item_type, code, name, description_rtf, sort_order, is_active)
                                VALUES (@CatId, @Type, @Code, @Name, @Desc, @Sort, 1);
                                SELECT LAST_INSERT_ID()",
                                new { CatId = catId, Type = itemType, prod.Code, prod.Name, Desc = prod.Description, Sort = pSort++ }, tx);
                            totalProds++;

                            int vSort = 0;
                            foreach (var v in prod.Variants)
                            {
                                c.Execute(@"
                                    INSERT INTO quote_product_variants (product_id, code, name, cost_price, sell_price, vat_pct, sort_order)
                                    VALUES (@PId, @Code, @Name, @Cost, @Price, @Vat, @Sort)",
                                    new { PId = pId, v.Code, v.Name, Cost = v.CostPrice, Price = v.SellPrice, Vat = v.VatPct, Sort = vSort++ }, tx);
                                totalVars++;
                            }
                        }
                    }
                }
            }

            tx.Commit();
            string msg = $"Importati: {dto.PriceLists.Count} listini, {totalGroups} gruppi, {totalCats} categorie, {totalProds} prodotti, {totalVars} varianti";
            return Ok(ApiResponse<string>.Ok(msg));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore import: {ex.Message}")); }
    }
}
