using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Rendering (sola lettura) degli ordini fornitore Danea generati da ATEC PM:
/// il popup «ordine come su Danea» aperto dal Rif. Danea della DDP commerciale
/// e dal badge ordine delle RDO. Nessuna scrittura sull'archivio.
/// </summary>
[ApiController]
[Route("api/danea-orders")]
[Authorize]
public class DaneaOrdersController : ControllerBase
{
    private readonly DaneaOrderService _danea;
    public DaneaOrdersController(DaneaOrderService danea) => _danea = danea;

    [HttpGet("{idDoc:int}")]
    public IActionResult Get(int idDoc)
    {
        if (idDoc <= 0)
            return Ok(ApiResponse<DaneaOrderView>.Fail("Ordine non valido."));
        try
        {
            var order = _danea.GetOrder(idDoc);
            if (order == null)
                return Ok(ApiResponse<DaneaOrderView>.Fail(
                    "Ordine non trovato in Atec_PM (potrebbe essere stato eliminato da Danea)."));
            return Ok(ApiResponse<DaneaOrderView>.Ok(order));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaOrderView>.Fail($"Lettura ordine non riuscita: {ex.Message}"));
        }
    }
}
