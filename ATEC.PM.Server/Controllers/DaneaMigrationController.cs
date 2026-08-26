using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Trasferimento catalogo Danea vecchio → Atec_PM (piano F2). Il vecchio archivio
/// è solo sorgente (nessuna scrittura); le scritture vanno sul nuovo.
/// </summary>
[ApiController]
[Route("api/danea-migration")]
[Authorize]
[RequireFeature("nav.danea_migration")]
public class DaneaMigrationController : ControllerBase
{
    private readonly DaneaMigrationService _svc;
    private readonly DaneaSyncService _sync;

    public DaneaMigrationController(DaneaMigrationService svc, DaneaSyncService sync)
    {
        _svc = svc;
        _sync = sync;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            return Ok(ApiResponse<DaneaMigrationStatus>.Ok(_svc.GetStatus()));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaMigrationStatus>.Fail(ex.Message));
        }
    }

    [HttpGet("filter-options")]
    public IActionResult GetFilterOptions()
    {
        try
        {
            return Ok(ApiResponse<DaneaFilterOptions>.Ok(_svc.GetFilterOptions()));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaFilterOptions>.Fail(ex.Message));
        }
    }

    [HttpGet("old-articles")]
    public IActionResult GetOldArticles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] bool onlyMissing = false,
        [FromQuery] string? codArticolo = null,
        [FromQuery] string? descrizione = null,
        [FromQuery] string? categoria = null,
        [FromQuery] string? sottocategoria = null,
        [FromQuery] string? fornitore = null,
        [FromQuery] string? produttore = null,
        [FromQuery] string? extra1 = null)
    {
        try
        {
            return Ok(ApiResponse<PagedResult<DaneaOldArticle>>.Ok(
                _svc.GetOldArticles(page, pageSize, search, onlyMissing,
                    codArticolo, descrizione, categoria, sottocategoria, fornitore, produttore, extra1)));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<DaneaOldArticle>>.Fail(ex.Message));
        }
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] DaneaTransferRequest req)
    {
        if (req.ArticleIds.Count == 0)
            return Ok(ApiResponse<DaneaTransferReport>.Fail("Nessun articolo selezionato."));
        if (req.ArticleIds.Count > 500)
            return Ok(ApiResponse<DaneaTransferReport>.Fail(
                "Massimo 500 articoli per lotto: dividere la selezione."));
        try
        {
            DaneaTransferReport report = _svc.Transfer(req.ArticleIds);

            // Il Catalogo articoli di ATEC PM e' uno SPECCHIO di Danea: finche' non gira il sync
            // l'articolo appena trasferito resta spento e la pagina non lo mostra, cosi' il
            // trasferimento sembra fallito anche quando e' andato benissimo (25/08/2026).
            // Si allineano SOLO le righe di questo lotto, PRIMA di rispondere: quando la finestra
            // di esito compare, il catalogo e' gia' a posto. Il giro completo resta al sync.
            // Anche gli "skipped" vanno allineati: erano gia' nell'archivio ma potevano essere
            // rimasti spenti nello specchio, ed e' proprio il caso che sembra un guasto.
            var daAllineare = report.Results
                .Where(r => r.Outcome != "error")
                .Select(r => r.IdArticolo)
                .ToList();
            if (daAllineare.Count > 0)
            {
                try
                {
                    report.CatalogAligned = await _sync.AllineaArticoli(daAllineare);
                }
                catch (Exception ex)
                {
                    // Gli articoli SONO passati: un intoppo qui non rende fallito il trasferimento.
                    report.CatalogWarning =
                        $"Catalogo articoli non aggiornato ({ex.Message}): usare «Sincronizza Danea».";
                }
            }
            return Ok(ApiResponse<DaneaTransferReport>.Ok(report));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaTransferReport>.Fail(ex.Message));
        }
    }
}
