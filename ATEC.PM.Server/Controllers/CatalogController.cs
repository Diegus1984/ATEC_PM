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
}