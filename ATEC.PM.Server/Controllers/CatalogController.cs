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

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            using var c = _db.Open();
            // Leggiamo dalla tabella catalog_items dove hai importato gli articoli
            var rows = c.Query<CatalogItemListItem>(@"
                SELECT i.id, i.code, i.description, i.category, i.unit, 
                       i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                       s.company_name AS SupplierName, i.manufacturer
                FROM catalog_items i
                LEFT JOIN suppliers s ON s.id = i.supplier_id
                WHERE i.is_active = 1
                ORDER BY i.code").ToList();

            return Ok(ApiResponse<List<CatalogItemListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            // Restituiamo un errore in formato JSON, così il client non crasha
            return Ok(ApiResponse<List<CatalogItemListItem>>.Fail(ex.Message));
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