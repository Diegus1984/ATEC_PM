using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController : ControllerBase
{
    private readonly DbService _db;
    public CatalogController(DbService db) => _db = db;

    [HttpGet("filter-meta")]
    public IActionResult GetFilterMeta()
    {
        try
        {
            using var c = _db.Open();
            var suppliers = c.Query<string>(@"
                SELECT DISTINCT s.company_name FROM catalog_items i
                INNER JOIN suppliers s ON s.id = i.supplier_id
                WHERE i.is_active = 1 AND s.company_name IS NOT NULL AND s.company_name <> ''
                ORDER BY s.company_name").ToList();
            var manufacturers = c.Query<string>(@"
                SELECT DISTINCT manufacturer FROM catalog_items
                WHERE is_active = 1 AND manufacturer IS NOT NULL AND manufacturer <> ''
                ORDER BY manufacturer").ToList();
            var categories = c.Query<string>(@"
                SELECT DISTINCT category FROM catalog_items
                WHERE is_active = 1 AND category IS NOT NULL AND category <> ''
                ORDER BY category").ToList();
            return Ok(ApiResponse<object>.Ok(new { suppliers, manufacturers, categories }));
        }
        catch (Exception ex) { return Ok(ApiResponse<object>.Fail(ex.Message)); }
    }

    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? code = null,
        [FromQuery] string? description = null,
        [FromQuery] string? supplier = null,
        [FromQuery] string? manufacturer = null,
        [FromQuery] string? category = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
            var clauses = new List<string> { "i.is_active = 1" };
            var dp = new Dapper.DynamicParameters();

            void AddLike(string column, string? filter, string param)
            {
                string? pat = PagedQueryHelper.ToLikePattern(filter);
                if (pat == null) return;
                clauses.Add($"{column} LIKE @{param}");
                dp.Add(param, pat);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = $"%{search.Trim()}%";
                clauses.Add(@"(i.code LIKE @Search OR i.description LIKE @Search OR s.company_name LIKE @Search
                    OR i.manufacturer LIKE @Search OR i.category LIKE @Search)");
                dp.Add("Search", term);
            }

            AddLike("i.code", code, "Code");
            AddLike("i.description", description, "Description");
            AddLike("s.company_name", supplier, "Supplier");
            AddLike("i.manufacturer", manufacturer, "Manufacturer");
            AddLike("i.category", category, "Category");

            string where = "WHERE " + string.Join(" AND ", clauses);
            string from = @"FROM catalog_items i LEFT JOIN suppliers s ON s.id = i.supplier_id";

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<CatalogItemListItem>($@"
                SELECT i.id, i.code, i.description, i.category, i.unit, 
                       i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                       s.company_name AS SupplierName, i.manufacturer
                {from} {where}
                ORDER BY i.code
                LIMIT @Limit OFFSET @Offset", dp).ToList();

            int loaded = offset + rows.Count;
            return Ok(ApiResponse<PagedResult<CatalogItemListItem>>.Ok(new PagedResult<CatalogItemListItem>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<CatalogItemListItem>>.Fail(ex.Message));
        }
    }


    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        try
        {
            using var c = _db.Open();
            // Recuperiamo tutti i campi necessari per la maschera di modifica
            var item = c.QueryFirstOrDefault<CatalogItem>(@"
                SELECT 
                    id, code, description, category, subcategory, unit, 
                    unit_cost AS UnitCost, 
                    list_price AS ListPrice, 
                    supplier_id AS SupplierId, 
                    supplier_code AS SupplierCode, 
                    manufacturer, barcode, notes, is_active AS IsActive
                FROM catalog_items 
                WHERE id = @id", new { id });

            if (item == null)
                return Ok(ApiResponse<CatalogItem>.Fail("Articolo non trovato."));

            return Ok(ApiResponse<CatalogItem>.Ok(item));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<CatalogItem>.Fail(ex.Message));
        }
    }

    [HttpPost]
    public IActionResult Create(CatalogItem item)
    {
        try
        {
            using var c = _db.Open();
            string sql = @"INSERT INTO catalog_items 
                (code, description, category, subcategory, unit, unit_cost, list_price, 
                 supplier_id, supplier_code, manufacturer, barcode, notes, is_active)
                VALUES 
                (@Code, @Description, @Category, @Subcategory, @Unit, @UnitCost, @ListPrice, 
                 @SupplierId, @SupplierCode, @Manufacturer, @Barcode, @Notes, 1)";

            c.Execute(sql, item);
            return Ok(ApiResponse<string>.Ok("Articolo creato correttamente"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<string>.Fail(ex.Message));
        }
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, CatalogItem item)
    {
        try
        {
            using var c = _db.Open();
            string sql = @"UPDATE catalog_items SET 
                code = @Code, 
                description = @Description, 
                category = @Category, 
                subcategory = @Subcategory, 
                unit = @Unit, 
                unit_cost = @UnitCost, 
                list_price = @ListPrice, 
                supplier_id = @SupplierId, 
                supplier_code = @SupplierCode, 
                manufacturer = @Manufacturer, 
                barcode = @Barcode, 
                notes = @Notes 
                WHERE id = @id";

            // Assicuriamoci che l'id dell'oggetto sia quello della rotta URL
            item.Id = id;

            c.Execute(sql, item);
            return Ok(ApiResponse<string>.Ok("Articolo aggiornato correttamente"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<string>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _db.Open();

            // Blocca cancellazione se usato in una composizione
            int usedInComp = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM codex_compositions WHERE child_catalog_id=@Id",
                new { Id = id });
            if (usedInComp > 0)
                return Ok(ApiResponse<string>.Fail(
                    "Impossibile eliminare: questo articolo è utilizzato in una composizione"));

            // Invece di cancellare fisicamente, disattiviamo l'articolo
            c.Execute("UPDATE catalog_items SET is_active = 0 WHERE id = @id", new { id });
            return Ok(ApiResponse<string>.Ok("Articolo eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<string>.Fail(ex.Message));
        }
    }
}