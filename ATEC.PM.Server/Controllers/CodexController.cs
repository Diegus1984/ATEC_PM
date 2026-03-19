using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using System.Security.Claims;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/codex")]
[Authorize]
public class CodexController : ControllerBase
{
    private readonly DbService _db;
    private readonly CodexSyncService _sync;
    private readonly CodexGeneratorService _generator;

    public CodexController(DbService db, CodexSyncService sync, CodexGeneratorService generator)
    {
        _db = db;
        _sync = sync;
        _generator = generator;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<CodexListItem>(@"
            SELECT id, codice AS Codice, code_forn AS CodeForn, fornitore AS Fornitore,
                   prezzo_forn AS PrezzoForn, iva AS Iva, produttore AS Produttore,
                   data AS Data, descr AS Descr, note AS Note, categoria AS Categoria,
                   barcode AS Barcode, tipologia AS Tipologia,
                   extra1 AS Extra1, extra2 AS Extra2, extra3 AS Extra3,
                   code_prod AS CodeProd, spec AS Spec, oper AS Oper, um AS Um,
                   ubicazione AS Ubicazione, codexforn AS Codexforn
            FROM codex_items ORDER BY codice").ToList();
        return Ok(ApiResponse<List<CodexListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var item = c.QueryFirstOrDefault<CodexListItem>(@"
            SELECT id, codice AS Codice, code_forn AS CodeForn, fornitore AS Fornitore,
                   prezzo_forn AS PrezzoForn, iva AS Iva, produttore AS Produttore,
                   data AS Data, descr AS Descr, note AS Note, categoria AS Categoria,
                   barcode AS Barcode, tipologia AS Tipologia,
                   extra1 AS Extra1, extra2 AS Extra2, extra3 AS Extra3,
                   code_prod AS CodeProd, spec AS Spec, oper AS Oper, um AS Um,
                   ubicazione AS Ubicazione, codexforn AS Codexforn
            FROM codex_items WHERE id=@Id", new { Id = id });
        if (item == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<CodexListItem>.Ok(item));
    }

    // ── SYNC ────────────────────────────────────────────────

    [HttpPost("sync")]
    public IActionResult StartSync()
    {
        if (CodexSyncService.IsSyncing)
            return Ok(ApiResponse<string>.Fail("Sincronizzazione già in corso"));

        _ = Task.Run(() => _sync.RunSync());
        return Ok(ApiResponse<string>.Ok("Sincronizzazione avviata"));
    }

    [HttpGet("sync-status")]
    public IActionResult GetSyncStatus()
    {
        var status = new CodexSyncStatus
        {
            IsSyncing = CodexSyncService.IsSyncing,
            LastSync = CodexSyncService.LastSync,
            TotalRows = CodexSyncService.TotalRows,
            LastError = CodexSyncService.LastError
        };
        return Ok(ApiResponse<CodexSyncStatus>.Ok(status));
    }

    // ── GENERAZIONE CON PRENOTAZIONE ────────────────────────

    [HttpGet("prefixes")]
    public IActionResult GetPrefixes()
    {
        var prefixes = _generator.GetAvailablePrefixes()
            .Select(p => new CodexPrefix { Codice = p.Code, Descrizione = p.Description })
            .ToList();
        return Ok(ApiResponse<List<CodexPrefix>>.Ok(prefixes));
    }

    [HttpPost("reserve")]
    public IActionResult ReserveCode([FromBody] CodexReserveRequest req)
    {
        try
        {
            string userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var (code, reservationId) = _generator.ReserveNextCode(req.Prefisso, userName);
            var result = new CodexReservationResult
            {
                Codice = CodexGeneratorService.FormatCodeForDisplay(code),
                ReservationId = reservationId
            };
            return Ok(ApiResponse<CodexReservationResult>.Ok(result));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    [HttpPost("release/{reservationId}")]
    public IActionResult ReleaseReservation(int reservationId)
    {
        try
        {
            _generator.ReleaseReservation(reservationId);
            return Ok(ApiResponse<string>.Ok("Prenotazione rilasciata"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    [HttpPost("confirm")]
    public IActionResult ConfirmReservation([FromBody] CodexConfirmRequest req)
    {
        try
        {
            var (code, itemId) = _generator.ConfirmReservation(req.ReservationId, req.Descrizione);
            var result = new CodexGeneratedCode { Codice = CodexGeneratorService.FormatCodeForDisplay(code), Id = itemId };
            return Ok(ApiResponse<CodexGeneratedCode>.Ok(result, "Codice generato"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    // ── MODIFICA / ELIMINA ────────────────────────────────────

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] CodexUpdateRequest req)
    {
        using var c = _db.Open();
        int rows = c.Execute("UPDATE codex_items SET descr=@Descrizione WHERE id=@Id",
            new { req.Descrizione, Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();

        // Blocca cancellazione se usato in una composizione
        int usedInComp = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM codex_compositions WHERE child_codex_id=@Id",
            new { Id = id });
        if (usedInComp > 0)
            return BadRequest(ApiResponse<string>.Fail(
                "Impossibile eliminare: questo articolo è utilizzato in una composizione"));

        int rows = c.Execute("DELETE FROM codex_items WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Eliminato"));
    }

    // ── COMPOSIZIONE ────────────────────────────────────────

    private const string CompositionSelectSql = @"
        SELECT cc.id AS Id, cc.parent_codex_id AS ParentCodexId,
               cc.child_codex_id AS ChildCodexId,
               cc.child_catalog_id AS ChildCatalogId,
               COALESCE(ci.codice, cat.code) AS ChildCodice,
               COALESCE(ci.descr, cat.description) AS ChildDescr,
               cc.sort_order AS SortOrder,
               CASE WHEN cc.child_catalog_id IS NOT NULL THEN 'catalog' ELSE 'codex' END AS Source
        FROM codex_compositions cc
        LEFT JOIN codex_items ci ON ci.id = cc.child_codex_id
        LEFT JOIN catalog_items cat ON cat.id = cc.child_catalog_id";

    [HttpGet("compositions/{parentId}")]
    public IActionResult GetCompositionChildren(int parentId)
    {
        using var c = _db.Open();
        var rows = c.Query<CompositionChildDto>(
            CompositionSelectSql + @"
            WHERE cc.parent_codex_id = @ParentId
            ORDER BY cc.sort_order, COALESCE(ci.codice, cat.code)",
            new { ParentId = parentId }).ToList();
        return Ok(ApiResponse<List<CompositionChildDto>>.Ok(rows));
    }

    [HttpGet("compositions/tree/{codexId}")]
    public IActionResult GetCompositionTree(int codexId)
    {
        using var c = _db.Open();

        var root = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice, descr AS Descr FROM codex_items WHERE id=@Id",
            new { Id = codexId });
        if (root == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

        var allComps = c.Query<CompositionChildDto>(
            CompositionSelectSql + " ORDER BY cc.sort_order, COALESCE(ci.codice, cat.code)").ToList();

        var lookup = allComps.GroupBy(x => x.ParentCodexId)
                             .ToDictionary(g => g.Key, g => g.ToList());

        var tree = new CompositionTreeNode
        {
            CodexId = root.Id,
            Codice = root.Codice,
            Descr = root.Descr
        };
        BuildTree(tree, lookup);
        return Ok(ApiResponse<CompositionTreeNode>.Ok(tree));
    }

    private void BuildTree(CompositionTreeNode node, Dictionary<int, List<CompositionChildDto>> lookup)
    {
        if (!lookup.ContainsKey(node.CodexId)) return;
        foreach (var child in lookup[node.CodexId])
        {
            var childNode = new CompositionTreeNode
            {
                CodexId = child.ChildCodexId ?? 0,
                CatalogId = child.ChildCatalogId,
                Codice = child.ChildCodice,
                Descr = child.ChildDescr,
                CompositionId = child.Id,
                Source = child.Source
            };
            // Solo nodi codex possono avere sotto-figli
            if (child.ChildCodexId.HasValue)
                BuildTree(childNode, lookup);
            node.Children.Add(childNode);
        }
    }

    [HttpPost("compositions")]
    public IActionResult AddComposition([FromBody] AddCompositionRequest req)
    {
        using var c = _db.Open();

        if (req.ChildCodexId == null && req.ChildCatalogId == null)
            return BadRequest(ApiResponse<string>.Fail("Specificare ChildCodexId o ChildCatalogId"));

        var parent = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
            new { Id = req.ParentCodexId });
        if (parent == null)
            return BadRequest(ApiResponse<string>.Fail("Articolo parent non trovato"));

        string parentPrefix = parent.Codice.Substring(0, 1);

        // Validazione gerarchia solo per figli codex
        if (req.ChildCodexId.HasValue)
        {
            var child = c.QueryFirstOrDefault<CodexListItem>(
                "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
                new { Id = req.ChildCodexId.Value });
            if (child == null)
                return BadRequest(ApiResponse<string>.Fail("Articolo figlio non trovato"));

            string childPrefix = child.Codice.Substring(0, 1);
            string? error = ValidateHierarchy(parentPrefix, childPrefix);
            if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        }
        else if (req.ChildCatalogId.HasValue)
        {
            // Verifica che l'articolo catalogo esista
            int exists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM catalog_items WHERE id=@Id",
                new { Id = req.ChildCatalogId.Value });
            if (exists == 0)
                return BadRequest(ApiResponse<string>.Fail("Articolo catalogo non trovato"));
        }

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0) FROM codex_compositions WHERE parent_codex_id=@P",
            new { P = req.ParentCodexId });

        int qty = Math.Max(1, req.Quantity);
        int lastId = 0;
        for (int i = 0; i < qty; i++)
        {
            lastId = c.ExecuteScalar<int>(@"
                INSERT INTO codex_compositions (parent_codex_id, child_codex_id, child_catalog_id, sort_order)
                VALUES (@ParentCodexId, @ChildCodexId, @ChildCatalogId, @Sort);
                SELECT LAST_INSERT_ID()",
                new { req.ParentCodexId, req.ChildCodexId, req.ChildCatalogId, Sort = maxSort + 1 + i });
        }

        string msg = qty > 1 ? $"{qty} elementi aggiunti" : "Composizione aggiunta";
        return Ok(ApiResponse<int>.Ok(lastId, msg));
    }

    [HttpPut("compositions/{id}")]
    public IActionResult UpdateComposition(int id, [FromBody] UpdateCompositionRequest req)
    {
        using var c = _db.Open();
        int rows = c.Execute(
            "UPDATE codex_compositions SET sort_order=@SortOrder WHERE id=@Id",
            new { req.SortOrder, Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("compositions/{id}")]
    public IActionResult DeleteComposition(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM codex_compositions WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Rimosso"));
    }

    private static string? ValidateHierarchy(string parentPrefix, string childPrefix)
    {
        return parentPrefix switch
        {
            "5" => childPrefix is "1" or "2" or "3" or "4"
                ? null : "Gruppo meccanico (5xx) può contenere solo articoli 1xx-4xx",
            "6" => childPrefix is "5"
                ? null : "Assieme meccanico (6xx) può contenere solo gruppi 5xx",
            "7" => childPrefix is "6"
                ? null : "Layout meccanico (7xx) può contenere solo assiemi 6xx",
            _ => "Solo articoli 5xx, 6xx, 7xx possono avere composizioni"
        };
    }

    // ── RIFERIMENTI 101 → 201/401 ──────────────────────────────

    [HttpGet("references/{sourceId}")]
    public IActionResult GetReferences(int sourceId)
    {
        using var c = _db.Open();
        var refs = c.Query<CodexItemReference>(@"
            SELECT r.id AS Id, r.source_codex_id AS SourceCodexId,
                   r.ref_codex_id AS RefCodexId, r.ref_type AS RefType,
                   ci.codice AS RefCodice, ci.descr AS RefDescr
            FROM codex_item_references r
            JOIN codex_items ci ON ci.id = r.ref_codex_id
            WHERE r.source_codex_id = @Id
            ORDER BY r.ref_type", new { Id = sourceId }).ToList();
        return Ok(ApiResponse<List<CodexItemReference>>.Ok(refs));
    }

    [HttpPost("references")]
    public IActionResult AddReference([FromBody] AddCodexReferenceRequest req)
    {
        using var c = _db.Open();

        // Verifica che il source sia un 101
        var source = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id", new { Id = req.SourceCodexId });
        if (source == null) return BadRequest(ApiResponse<string>.Fail("Articolo sorgente non trovato"));
        if (!source.Codice.StartsWith("1"))
            return BadRequest(ApiResponse<string>.Fail("Solo i codici 1xx possono avere riferimenti 201/401"));

        // Verifica che il ref sia del tipo giusto
        var refItem = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id", new { Id = req.RefCodexId });
        if (refItem == null) return BadRequest(ApiResponse<string>.Fail("Articolo riferimento non trovato"));

        string refPrefix = refItem.Codice.Substring(0, 1);
        if (req.RefType == "201" && refPrefix != "2")
            return BadRequest(ApiResponse<string>.Fail("Riferimento 201 deve essere un codice 2xx"));
        if (req.RefType == "401" && refPrefix != "4")
            return BadRequest(ApiResponse<string>.Fail("Riferimento 401 deve essere un codice 4xx"));

        try
        {
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO codex_item_references (source_codex_id, ref_codex_id, ref_type)
                VALUES (@SourceCodexId, @RefCodexId, @RefType)
                ON DUPLICATE KEY UPDATE ref_codex_id = @RefCodexId;
                SELECT LAST_INSERT_ID()", req);
            return Ok(ApiResponse<int>.Ok(id, "Riferimento salvato"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpDelete("references/{id}")]
    public IActionResult DeleteReference(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM codex_item_references WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Riferimento rimosso"));
    }
}