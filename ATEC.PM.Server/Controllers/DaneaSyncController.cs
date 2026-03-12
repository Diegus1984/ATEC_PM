using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/danea-sync")]
[Authorize]
public class DaneaSyncController : ControllerBase
{
    private readonly DaneaSyncService _sync;
    public DaneaSyncController(DaneaSyncService sync) => _sync = sync;

    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            isSyncing = DaneaSyncService.IsSyncing,
            lastSync = DaneaSyncService.LastSync?.ToString("dd/MM/yyyy HH:mm:ss"),
            lastError = DaneaSyncService.LastError,
            progress = DaneaSyncService.ProgressMessage,
            suppliers = DaneaSyncService.SuppliersCount,
            customers = DaneaSyncService.CustomersCount,
            articles = DaneaSyncService.ArticlesCount
        });
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunSync()
    {
        if (DaneaSyncService.IsSyncing)
            return Conflict(new { message = "Sincronizzazione già in corso" });

        _ = Task.Run(() => _sync.RunSync());
        return Ok(new { message = "Sincronizzazione avviata" });
    }
}
