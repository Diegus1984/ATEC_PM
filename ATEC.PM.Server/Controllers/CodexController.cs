using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/codex")]
[Authorize]
public class CodexController : ControllerBase
{
    private readonly DbService _db;
    private readonly CodexSyncService _sync;

    public CodexController(DbService db, CodexSyncService sync)
    {
        _db = db;
        _sync = sync;
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
}
