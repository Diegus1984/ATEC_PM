using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Data;

namespace ATEC.PM.Server.Controllers;

// Anagrafica attività: catalogo globale delle voci-attività standard di progetto.
// È l'elenco da cui si precaricano le milestone alla creazione di una commessa (copia PER VALORE:
// le milestone non referenziano il catalogo, quindi modifiche/cancellazioni qui NON toccano le
// commesse già create). Le voci hanno id stabile; l'ordine è gestito da sort_order.
[ApiController]
[Route("api/activity-catalog")]
[Authorize]
public class ActivityCatalogController : ControllerBase
{
    private readonly DbService _db;
    public ActivityCatalogController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<ActivityCatalogItem>(@"
            SELECT id, label, sort_order AS SortOrder, is_active AS IsActive
            FROM activity_catalog
            ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<ActivityCatalogItem>>.Ok(rows));
    }

    [HttpGet("active")]
    public IActionResult GetActive()
    {
        using var c = _db.Open();
        var rows = c.Query<ActivityCatalogItem>(@"
            SELECT id, label, sort_order AS SortOrder, is_active AS IsActive
            FROM activity_catalog
            WHERE is_active = TRUE
            ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<ActivityCatalogItem>>.Ok(rows));
    }

    [HttpPost]
    public IActionResult Create([FromBody] ActivityCatalogSaveRequest req)
    {
        string label = (req.Label ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(label))
            return Ok(ApiResponse<int>.Fail("Il testo della voce è obbligatorio."));

        using var c = _db.Open();

        // Anti-duplicato case-insensitive: se la voce esiste già non la duplico; se era disattivata la riattivo.
        ActivityCatalogItem? existing = c.QueryFirstOrDefault<ActivityCatalogItem>(@"
            SELECT id, label, sort_order AS SortOrder, is_active AS IsActive
            FROM activity_catalog
            WHERE LOWER(TRIM(label)) = LOWER(@Label)
            LIMIT 1", new { Label = label });
        if (existing != null)
        {
            if (!existing.IsActive)
                c.Execute("UPDATE activity_catalog SET is_active = TRUE WHERE id = @Id", new { existing.Id });
            return Ok(ApiResponse<int>.Ok(existing.Id, "Voce già presente in anagrafica"));
        }

        int sort = req.SortOrder > 0
            ? req.SortOrder
            : c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), 0) + 1 FROM activity_catalog");
        int id = c.ExecuteScalar<int>(@"
            INSERT INTO activity_catalog (label, sort_order, is_active)
            VALUES (@Label, @Sort, @IsActive);
            SELECT LAST_INSERT_ID()",
            new { Label = label, Sort = sort, req.IsActive });
        return Ok(ApiResponse<int>.Ok(id, "Voce creata"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] ActivityCatalogSaveRequest req)
    {
        string label = (req.Label ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(label))
            return Ok(ApiResponse<int>.Fail("Il testo della voce è obbligatorio."));

        using var c = _db.Open();

        // Anti-duplicato in rinomina (a differenza del prototipo, che non lo controllava).
        int dup = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM activity_catalog
            WHERE LOWER(TRIM(label)) = LOWER(@Label) AND id <> @Id", new { Label = label, Id = id });
        if (dup > 0)
            return Ok(ApiResponse<int>.Fail("Esiste già un'altra voce con questo testo."));

        int rows = c.Execute(@"
            UPDATE activity_catalog
               SET label = @Label, sort_order = @SortOrder, is_active = @IsActive
             WHERE id = @Id",
            new { Label = label, req.SortOrder, req.IsActive, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Voce non trovata"));
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        // Le milestone copiano la descrizione per valore (nessuna FK al catalogo): l'eliminazione è sicura.
        c.Execute("DELETE FROM activity_catalog WHERE id = @Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true, "Eliminata"));
    }

    [HttpPost("reorder")]
    public IActionResult Reorder([FromBody] ActivityCatalogReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0)
            return Ok(ApiResponse<bool>.Ok(true));

        using var c = _db.Open();
        int order = 1;
        foreach (int id in req.Ids)
            c.Execute("UPDATE activity_catalog SET sort_order = @Sort WHERE id = @Id",
                new { Sort = order++, Id = id });
        return Ok(ApiResponse<bool>.Ok(true, "Ordine aggiornato"));
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        using var c = _db.Open();
        // Ripristino distruttivo all'elenco standard (come nel prototipo): sostituisce l'intero catalogo.
        // Le commesse già create NON vengono modificate (le milestone sono copie per valore).
        c.Execute("DELETE FROM activity_catalog");
        int order = 1;
        foreach (string label in ActivityCatalogSeed.Labels)
            c.Execute("INSERT INTO activity_catalog (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = label, Sort = order++ });
        return Ok(ApiResponse<bool>.Ok(true, "Anagrafica attività ripristinata allo standard"));
    }
}
