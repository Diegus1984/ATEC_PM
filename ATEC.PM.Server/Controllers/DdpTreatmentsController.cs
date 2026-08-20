using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Trattamenti DDP: lettura libera (serve ai picker e alle griglie di ogni livello),
// scrittura riservata al livello della feature «nav.ddp_destinazioni».
[ApiController]
[Route("api/ddp-treatments")]
[Authorize]
public class DdpTreatmentsController : ControllerBase
{
    private readonly DbService _db;
    public DdpTreatmentsController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<DdpTreatmentItem>(@"
            SELECT id AS Id, name AS Name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_treatments
            ORDER BY name").ToList();
        return Ok(ApiResponse<List<DdpTreatmentItem>>.Ok(rows));
    }

    [HttpGet("active")]
    public IActionResult GetActive()
    {
        using var c = _db.Open();
        // is_active è TINYINT(1): confronta con 1 (TRUE a volte non filtra come atteso).
        var rows = c.Query<DdpTreatmentItem>(@"
            SELECT id AS Id, name AS Name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_treatments
            WHERE is_active = 1
            ORDER BY name").ToList();
        return Ok(ApiResponse<List<DdpTreatmentItem>>.Ok(rows));
    }

    [RequireFeature("nav.ddp_destinazioni")]
    [HttpPost]
    public IActionResult Create([FromBody] DdpTreatmentSaveRequest req)
    {
        string name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(ApiResponse<int>.Fail("Il nome del trattamento è obbligatorio."));

        using var c = _db.Open();

        // Anti-duplicato (case-insensitive, spazi esterni ignorati): se la voce esiste già
        // NON la duplico. Se era disattivata la riattivo così torna selezionabile.
        DdpTreatmentItem? existing = c.QueryFirstOrDefault<DdpTreatmentItem>(@"
            SELECT id, name, sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_treatments
            WHERE LOWER(TRIM(name)) = LOWER(@Name)
            LIMIT 1", new { Name = name });
        if (existing != null)
        {
            if (!existing.IsActive)
                c.Execute("UPDATE ddp_treatments SET is_active = TRUE WHERE id = @Id",
                    new { existing.Id });
            return Ok(ApiResponse<int>.Ok(existing.Id, "Trattamento già presente"));
        }

        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_treatments (name, sort_order, is_active)
            VALUES (@Name, 0, @IsActive);
            SELECT LAST_INSERT_ID()", new { Name = name, req.IsActive });
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [RequireFeature("nav.ddp_destinazioni")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] DdpTreatmentSaveRequest req)
    {
        using var c = _db.Open();

        // Nome precedente: serve a propagare la rinomina alle righe distinta (trattamento salvato per NOME).
        string? oldName = c.ExecuteScalar<string?>(
            "SELECT name FROM ddp_treatments WHERE id=@Id", new { Id = id });

        req.Id = id;
        c.Execute(@"UPDATE ddp_treatments SET name=@Name, is_active=@IsActive WHERE id=@Id",
            new { req.Name, req.IsActive, Id = id });

        // Se il nome è cambiato, riallinea le righe della distinta che usavano il vecchio nome.
        if (!string.IsNullOrEmpty(oldName) && oldName != req.Name)
        {
            c.Execute("UPDATE ddp_officina_items SET treatment=@New WHERE treatment=@Old",
                new { New = req.Name, Old = oldName });
        }

        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [RequireFeature("nav.ddp_destinazioni")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        // Verifica se è usata in ddp_officina_items
        int inUse = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM ddp_officina_items WHERE treatment = (SELECT name FROM ddp_treatments WHERE id=@Id)",
            new { Id = id });
        if (inUse > 0)
        {
            // Disattiva invece di eliminare
            c.Execute("UPDATE ddp_treatments SET is_active=FALSE WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<bool>.Ok(true, "Disattivata (in uso su DDP)"));
        }
        c.Execute("DELETE FROM ddp_treatments WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true, "Eliminata"));
    }
}
