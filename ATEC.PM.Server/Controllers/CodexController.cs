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
        int rows = c.Execute("DELETE FROM codex_items WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Eliminato"));
    }

    // ── COMPOSIZIONE ────────────────────────────────────────

    [HttpGet("compositions/{parentId}")]
    public IActionResult GetCompositionChildren(int parentId)
    {
        using var c = _db.Open();
        var rows = c.Query<CompositionChildDto>(@"
            SELECT cc.id AS Id, cc.parent_codex_id AS ParentCodexId,
                   cc.child_codex_id AS ChildCodexId,
                   ci.codice AS ChildCodice, ci.descr AS ChildDescr,
                   cc.quantity AS Quantity, cc.sort_order AS SortOrder
            FROM codex_compositions cc
            JOIN codex_items ci ON ci.id = cc.child_codex_id
            WHERE cc.parent_codex_id = @ParentId
            ORDER BY cc.sort_order, ci.codice",
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

        var allComps = c.Query<CompositionChildDto>(@"
            SELECT cc.id AS Id, cc.parent_codex_id AS ParentCodexId,
                   cc.child_codex_id AS ChildCodexId,
                   ci.codice AS ChildCodice, ci.descr AS ChildDescr,
                   cc.quantity AS Quantity, cc.sort_order AS SortOrder
            FROM codex_compositions cc
            JOIN codex_items ci ON ci.id = cc.child_codex_id
            ORDER BY cc.sort_order, ci.codice").ToList();

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
                CodexId = child.ChildCodexId,
                Codice = child.ChildCodice,
                Descr = child.ChildDescr,
                Quantity = child.Quantity
            };
            BuildTree(childNode, lookup);
            node.Children.Add(childNode);
        }
    }

    [HttpPost("compositions")]
    public IActionResult AddComposition([FromBody] AddCompositionRequest req)
    {
        using var c = _db.Open();

        var parent = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
            new { Id = req.ParentCodexId });
        var child = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
            new { Id = req.ChildCodexId });

        if (parent == null || child == null)
            return BadRequest(ApiResponse<string>.Fail("Articolo non trovato"));

        string parentPrefix = parent.Codice.Substring(0, 1);
        string childPrefix = child.Codice.Substring(0, 1);

        string? error = ValidateHierarchy(parentPrefix, childPrefix);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));

        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM codex_compositions WHERE parent_codex_id=@P AND child_codex_id=@C",
            new { P = req.ParentCodexId, C = req.ChildCodexId });
        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail("Relazione già esistente"));

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0) FROM codex_compositions WHERE parent_codex_id=@P",
            new { P = req.ParentCodexId });

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO codex_compositions (parent_codex_id, child_codex_id, quantity, sort_order)
            VALUES (@ParentCodexId, @ChildCodexId, @Quantity, @Sort);
            SELECT LAST_INSERT_ID()",
            new { req.ParentCodexId, req.ChildCodexId, req.Quantity, Sort = maxSort + 1 });

        return Ok(ApiResponse<int>.Ok(id, "Composizione aggiunta"));
    }

    [HttpPut("compositions/{id}")]
    public IActionResult UpdateComposition(int id, [FromBody] UpdateCompositionRequest req)
    {
        using var c = _db.Open();
        int rows = c.Execute(
            "UPDATE codex_compositions SET quantity=@Quantity, sort_order=@SortOrder WHERE id=@Id",
            new { req.Quantity, req.SortOrder, Id = id });
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
}