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
            ORDER BY name").ToList();
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
            ORDER BY name").ToList();
        return Ok(ApiResponse<List<DdpDestinationItem>>.Ok(rows));
    }

    [HttpPost]
    public IActionResult Create([FromBody] DdpDestinationSaveRequest req)
    {
        string name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(ApiResponse<int>.Fail("Il nome della destinazione è obbligatorio."));

        using var c = _db.Open();

        // Anti-duplicato (case-insensitive, spazi esterni ignorati): se la voce esiste già
        // NON la duplico. Se era disattivata la riattivo così torna selezionabile.
        DdpDestinationItem? existing = c.QueryFirstOrDefault<DdpDestinationItem>(@"
            SELECT id, name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_destinations
            WHERE LOWER(TRIM(name)) = LOWER(@Name)
            LIMIT 1", new { Name = name });
        if (existing != null)
        {
            if (!existing.IsActive)
                c.Execute("UPDATE ddp_destinations SET is_active = TRUE WHERE id = @Id",
                    new { existing.Id });
            return Ok(ApiResponse<int>.Ok(existing.Id, "Destinazione già presente"));
        }

        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_destinations (name, sort_order, is_active)
            VALUES (@Name, 0, @IsActive);
            SELECT LAST_INSERT_ID()", new { Name = name, req.IsActive });
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] DdpDestinationSaveRequest req)
    {
        using var c = _db.Open();

        // Nome precedente: serve a propagare la rinomina alle righe distinta (destinazione salvata per NOME).
        string? oldName = c.ExecuteScalar<string?>(
            "SELECT name FROM ddp_destinations WHERE id=@Id", new { Id = id });

        req.Id = id;
        c.Execute(@"UPDATE ddp_destinations SET name=@Name, is_active=@IsActive WHERE id=@Id",
            new { req.Name, req.IsActive, Id = id });

        // Se il nome è cambiato, riallinea le righe della distinta che usavano il vecchio nome.
        if (!string.IsNullOrEmpty(oldName) && oldName != req.Name)
        {
            c.Execute("UPDATE bom_items SET destination=@New WHERE destination=@Old",
                new { New = req.Name, Old = oldName });
            c.Execute("UPDATE ddp_officina_items SET destination=@New WHERE destination=@Old",
                new { New = req.Name, Old = oldName });
        }

        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        // Verifica se è usata in bom_items
        int inUse = c.ExecuteScalar<int>(@"
            SELECT
                (SELECT COUNT(*) FROM bom_items WHERE destination = (SELECT name FROM ddp_destinations WHERE id=@Id))
              + (SELECT COUNT(*) FROM ddp_officina_items WHERE destination = (SELECT name FROM ddp_destinations WHERE id=@Id))",
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
