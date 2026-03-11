using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/ddp-destinations")]
[Authorize]
public class DdpDestinationsController : ControllerBase
{
    private readonly DbService _db;
    public DdpDestinationsController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<DdpDestinationItem>(@"
            SELECT id, name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_destinations
            ORDER BY sort_order, name").ToList();
        return Ok(ApiResponse<List<DdpDestinationItem>>.Ok(rows));
    }

    [HttpGet("active")]
    public IActionResult GetActive()
    {
        using var c = _db.Open();
        var rows = c.Query<DdpDestinationItem>(@"
            SELECT id, name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_destinations
            WHERE is_active = TRUE
            ORDER BY sort_order, name").ToList();
        return Ok(ApiResponse<List<DdpDestinationItem>>.Ok(rows));
    }

    [HttpPost]
    public IActionResult Create([FromBody] DdpDestinationSaveRequest req)
    {
        using var c = _db.Open();
        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_destinations (name, sort_order, is_active)
            VALUES (@Name, @SortOrder, @IsActive);
            SELECT LAST_INSERT_ID()", req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] DdpDestinationSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"UPDATE ddp_destinations SET name=@Name, sort_order=@SortOrder, is_active=@IsActive WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        // Verifica se è usata in bom_items
        int inUse = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM bom_items WHERE destination = (SELECT name FROM ddp_destinations WHERE id=@Id)",
            new { Id = id });
        if (inUse > 0)
        {
            // Disattiva invece di eliminare
            c.Execute("UPDATE ddp_destinations SET is_active=FALSE WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<bool>.Ok(true, "Disattivata (in uso su DDP)"));
        }
        c.Execute("DELETE FROM ddp_destinations WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true, "Eliminata"));
    }
}
