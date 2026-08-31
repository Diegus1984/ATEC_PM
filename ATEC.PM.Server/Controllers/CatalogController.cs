using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Catalogo articoli: lettura libera (serve ai picker e alle griglie di ogni livello),
// scrittura riservata al livello della feature «nav.catalogo».
[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController : ControllerBase
{
    private readonly DbService _db;
    public CatalogController(DbService db) => _db = db;

    /// <summary>Colonne ordinabili (chiave client → colonna SQL).</summary>
    private static readonly Dictionary<string, string> CatalogSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "i.code",
        ["description"] = "i.description",
        ["supplierName"] = "s.company_name",
        ["manufacturer"] = "i.manufacturer",
        ["category"] = "i.category",
        ["subcategory"] = "i.subcategory",
        ["unit"] = "i.unit",
        ["unitCost"] = "i.unit_cost",
        ["listPrice"] = "i.list_price",
        ["atecCode"] = "i.atec_code",
    };

    [HttpGet("filter-meta")]
    [RequireFeature("nav.catalogo")]
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
            var subcategories = c.Query<string>(@"
                SELECT DISTINCT subcategory FROM catalog_items
                WHERE is_active = 1 AND subcategory IS NOT NULL AND subcategory <> ''
                ORDER BY subcategory").ToList();
            return Ok(ApiResponse<object>.Ok(new { suppliers, manufacturers, categories, subcategories }));
        }
        catch (Exception ex) { return Ok(ApiResponse<object>.Fail(ex.Message)); }
    }

    /// <summary>
    /// L'elenco articoli. Le cinque chiavi in OR sono le cinque pagine che lo aprono: la
    /// pagina Catalogo, il picker dentro la DDP di commessa, quello dell'Inbox Acquisti, la
    /// mappatura Codex-Danea e la Composizione Codex. Su GET basta il livello READ.
    ///
    /// <para><b>La chiave serve anche ad ACCENDERE il filtro prezzi</b>: `PrezziSensibiliFilter`
    /// ricava i micro dalle chiavi dell'endpoint, quindi finché qui non c'era nessun
    /// [RequireFeature] i costi marcati [DatoSensibile] uscivano interi lo stesso. Marcarli
    /// senza mettere la chiave sarebbe stato un cerotto su una porta aperta.</para>
    /// </summary>
    [HttpGet]
    // project.ddp_officina in OR: il picker unico delle DDP sfoglia il Catalogo
    // (famiglie 201/211/221) anche dal lato officina.
    [RequireFeature("nav.catalogo", "nav.codex", "nav.codex_composizione",
                    "nav.acquisti_inbox", "project.ddp_commerciale", "project.ddp_officina")]
    public IActionResult GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? code = null,
        [FromQuery] string? description = null,
        [FromQuery] string? supplier = null,
        [FromQuery] string? supplierCode = null,
        [FromQuery] string? manufacturer = null,
        [FromQuery] string? category = null,
        [FromQuery] string? subcategory = null,
        [FromQuery] string? atecCode = null,
        [FromQuery] string? atecState = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
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

            // Onora le regole jolly (abc, abc*, *abc, *abc*) anche sulla ricerca globale.
            string? searchPat = PagedQueryHelper.ToLikePattern(search);
            if (searchPat != null)
            {
                // #142: anche i.supplier_code — è il codice con cui il fornitore chiama
                // l'articolo (B0D12Z9NQP, 17RF…), spesso l'unica chiave che chi cerca ha in mano.
                clauses.Add(@"(i.code LIKE @Search OR i.description LIKE @Search OR s.company_name LIKE @Search
                    OR i.manufacturer LIKE @Search OR i.category LIKE @Search OR i.supplier_code LIKE @Search)");
                dp.Add("Search", searchPat);
            }

            AddLike("i.code", code, "Code");
            AddLike("i.description", description, "Description");
            AddLike("s.company_name", supplier, "Supplier");
            AddLike("i.supplier_code", supplierCode, "SupplierCode");
            AddLike("i.manufacturer", manufacturer, "Manufacturer");
            AddLike("i.category", category, "Category");
            AddLike("i.subcategory", subcategory, "Subcategory");
            AddLike("i.atec_code", atecCode?.Replace(".", ""), "AtecCode");

            // Stato mapping codice ATEC:
            // missing = senza codice (bonifica), done = associati, orphans = Extra1 senza match Codex.
            if (string.Equals(atecState, "missing", StringComparison.OrdinalIgnoreCase))
                clauses.Add("(i.atec_code IS NULL OR i.atec_code = '')");
            else if (string.Equals(atecState, "done", StringComparison.OrdinalIgnoreCase))
                clauses.Add("(i.atec_code IS NOT NULL AND i.atec_code <> '')");
            else if (string.Equals(atecState, "orphans", StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add("(i.atec_code IS NOT NULL AND i.atec_code <> '')");
                clauses.Add(@"NOT EXISTS (
                    SELECT 1 FROM codex_items cx
                    WHERE cx.codice_nuovo IS NOT NULL AND cx.codice_nuovo <> ''
                      AND cx.codice_nuovo = i.atec_code)");
            }

            string where = "WHERE " + string.Join(" AND ", clauses);
            string from = @"FROM catalog_items i LEFT JOIN suppliers s ON s.id = i.supplier_id";

            string orderBy = PagedQueryHelper.OrderBy(sortBy, sortDir, CatalogSortColumns, "ORDER BY i.code");

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<CatalogItemListItem>($@"
                SELECT i.id, i.code, i.description, i.category,
                       COALESCE(i.subcategory,'') AS Subcategory, i.unit,
                       i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                       i.supplier_id AS SupplierId,
                       s.company_name AS SupplierName,
                       COALESCE(i.supplier_code,'') AS SupplierCode, i.manufacturer,
                       COALESCE(i.atec_code,'') AS AtecCode, i.codex_item_id AS CodexItemId,
                       i.easyfatt_id AS EasyfattId
                {from} {where}
                {orderBy}
                LIMIT @Limit OFFSET @Offset", dp).ToList();
            foreach (CatalogItemListItem row in rows)
                if (row.AtecCode.Length > 0)
                    row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);

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
    [RequireFeature("nav.catalogo")]
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

    [RequireFeature("nav.catalogo")]
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

    [RequireFeature("nav.catalogo")]
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

    [RequireFeature("nav.catalogo")]
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