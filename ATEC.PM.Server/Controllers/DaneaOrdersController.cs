using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Rendering (sola lettura) degli ordini fornitore Danea generati da ATEC PM:
/// il popup «ordine come su Danea» aperto dal Rif. Danea della DDP commerciale
/// e dal badge ordine delle RDO. Nessuna scrittura sull'archivio.
/// </summary>
[ApiController]
[Route("api/danea-orders")]
[Authorize]
// Controller di SOLA consultazione (solo GET): oltre all'Inbox Acquisti, il popup
// si apre anche dal Rif. Danea della DDP Commerciale — chi la legge (es. Tecnico,
// che l'Inbox non ce l'ha) deve poter consultare l'ordine. Le chiavi sono in OR.
[RequireFeature("nav.acquisti_inbox", "project.ddp_commerciale")]
public class DaneaOrdersController : ControllerBase
{
    private readonly DaneaOrderService _danea;
    public DaneaOrdersController(DaneaOrderService danea) => _danea = danea;

    // Ricerca per NUMERO d'ordine (il Rif. Danea scritto a mano, es. «123/26»):
    // prima l'archivio attuale, poi — siamo in migrazione — il vecchio, in sola
    // lettura. La risposta dice sempre da quale archivio arriva il documento
    // (campo Archivio = "VECCHIO" quando non è quello attuale).
    [HttpGet("by-ref")]
    public IActionResult GetByRef([FromQuery] string? rif)
    {
        if (!DaneaOrderService.TryParseRif(rif, out int num, out int? anno))
            return Ok(ApiResponse<DaneaOrderView>.Fail(
                $"Rif. Danea «{rif}» non riconosciuto: serve il numero d'ordine, es. «123» o «123/26»."));

        string etichetta = anno.HasValue ? $"{num}/{anno.Value % 100:00}" : num.ToString();
        try
        {
            var ordine = _danea.GetOrderByNumero(num, anno, vecchioArchivio: false);
            if (ordine != null)
            {
                // Numerazioni ripartite da capo con la migrazione: lo stesso numero
                // può esistere in ENTRAMBI gli archivi, e il popup deve avvisare.
                // Best-effort: il vecchio irraggiungibile non blocca la consultazione.
                try { ordine.AmbiguoConVecchio = _danea.EsisteOrdine(num, anno, vecchioArchivio: true); }
                catch { /* vecchio archivio non raggiungibile: niente avviso */ }
                return Ok(ApiResponse<DaneaOrderView>.Ok(ordine));
            }
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaOrderView>.Fail($"Lettura ordine non riuscita: {ex.Message}"));
        }

        // Non è nell'archivio attuale: si prova il vecchio (solo lettura).
        try
        {
            var vecchio = _danea.GetOrderByNumero(num, anno, vecchioArchivio: true);
            if (vecchio == null)
                return Ok(ApiResponse<DaneaOrderView>.Fail(
                    $"Ordine n. {etichetta} non trovato né nell'archivio Danea attuale (Atec_PM) " +
                    "né nel vecchio (Srl-2020-2021). Controlla il numero scritto nel Rif. Danea."));
            vecchio.Archivio = "VECCHIO";
            return Ok(ApiResponse<DaneaOrderView>.Ok(vecchio));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DaneaOrderView>.Fail(
                $"Ordine n. {etichetta} non trovato nell'archivio attuale, e il vecchio archivio " +
                $"non è raggiungibile in questo momento ({ex.Message}). Riprova più tardi."));
        }
    }

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
