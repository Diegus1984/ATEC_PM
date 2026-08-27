using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Modulo HR presenze (PIANO-HR-PRESENZE.md, Fase 1) dietro <c>nav.hr_timbrature</c>.
///
/// <para><b>Chi vede cosa.</b> Con la sola lettura si vede il PROPRIO cartellino: le
/// presenze dei colleghi non sono un dato di reparto (§8 del piano — un dipendente non
/// vede le assenze degli altri). Il cartellino altrui, l'import, la mappatura e le
/// rettifiche richiedono la concessione in scrittura della stessa chiave.</para>
/// </summary>
[ApiController]
[Route("api/hr")]
[Authorize]
[RequireFeature("nav.hr_timbrature")]
public class HrController : ControllerBase
{
    private readonly HrPresenzeService _presenze;
    private readonly EcosClient _ecos;
    private readonly FeatureAccessService _access;

    public HrController(HrPresenzeService presenze, EcosClient ecos, FeatureAccessService access)
    {
        _presenze = presenze;
        _ecos = ecos;
        _access = access;
    }

    private int MeId
    {
        get
        {
            _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id);
            return id;
        }
    }

    private string Ruolo => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    /// <summary>La concessione in scrittura su nav.hr_timbrature: apre cartellini altrui,
    /// import, mappatura e rettifiche. Il controllo va fatto a mano sulle GET, che
    /// [RequireFeature] considera sempre di sola lettura.</summary>
    private bool PuoGestire => _access.CanWriteUser(MeId, Ruolo, "nav.hr_timbrature");

    // ── CARTELLINO ────────────────────────────────────────────────────────────

    [HttpGet("cartellino")]
    public IActionResult Cartellino([FromQuery] int anno, [FromQuery] int mese, [FromQuery] int? employeeId)
    {
        if (anno < 2020 || anno > 2100 || mese < 1 || mese > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        int destinatario = employeeId ?? MeId;
        if (destinatario != MeId && !PuoGestire)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Puoi vedere solo il tuo cartellino."));
        }

        return Ok(ApiResponse<HrCartellinoMeseDto>.Ok(_presenze.CartellinoMese(destinatario, anno, mese)));
    }

    /// <summary>Stato dell'import: conteggi, ultimo esito e configurazione Ecos. Dietro la
    /// scrittura perché <c>UltimoEsito</c> riporta il messaggio d'errore grezzo di Ecos —
    /// dettaglio d'infrastruttura che al dipendente non serve e non deve arrivare.</summary>
    [HttpGet("stato")]
    public IActionResult Stato()
    {
        if (!PuoGestire)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Lo stato dell'import richiede la scrittura su Timbrature."));
        return Ok(ApiResponse<HrStatoDto>.Ok(_presenze.Stato()));
    }

    // ── IMPORT DA ECOS ────────────────────────────────────────────────────────

    /// <summary>Import a mano. <c>?completo=true</c> ignora il cursore e ripassa tutto lo storico.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Importa([FromQuery] bool completo = false)
    {
        HrImportEsitoDto esito = await _presenze.ImportaAsync(completo, HttpContext.RequestAborted);
        return Ok(esito.Successo
            ? ApiResponse<HrImportEsitoDto>.Ok(esito, esito.Messaggio)
            : ApiResponse<HrImportEsitoDto>.Fail(esito.Messaggio));
    }

    // ── MAPPATURA DIPENDENTI ↔ ECOS ───────────────────────────────────────────

    [HttpGet("mappatura")]
    public IActionResult Mappatura()
    {
        if (!PuoGestire)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La mappatura Ecos richiede la scrittura su Timbrature."));
        return Ok(ApiResponse<List<HrMappaturaRigaDto>>.Ok(_presenze.Mappatura()));
    }

    /// <summary>I badge letti VIVI da Ecos, per suggerire i codici nella mappatura.
    /// Senza credenziali risponde vuoto con <c>configurato=false</c>, non un errore.</summary>
    [HttpGet("mappatura/badges")]
    public async Task<IActionResult> Badges()
    {
        if (!PuoGestire)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La mappatura Ecos richiede la scrittura su Timbrature."));

        if (!_ecos.Configurato)
            return Ok(ApiResponse<HrBadgesDto>.Ok(new HrBadgesDto { Configurato = false }));

        try
        {
            string token = await _ecos.TokenAsync(HttpContext.RequestAborted);
            List<EcosBadge> badges = await _ecos.BadgesAsync(token, HttpContext.RequestAborted);
            return Ok(ApiResponse<HrBadgesDto>.Ok(new HrBadgesDto
            {
                Configurato = true,
                Badges = badges
                    .OrderByDescending(b => b.InForza)
                    .ThenBy(b => b.Nome, StringComparer.OrdinalIgnoreCase)
                    .Select(b => new HrBadgeDto { EmplCode = b.EmplCode, Nome = b.Nome, InForza = b.InForza })
                    .ToList(),
            }));
        }
        catch (EcosApiException ex)
        {
            return Ok(ApiResponse<HrBadgesDto>.Fail(ex.Message));
        }
    }

    [HttpPut("mappatura/{employeeId:int}")]
    public IActionResult AggiornaMappatura(int employeeId, [FromBody] HrMappaturaRequest req)
    {
        string? errore = _presenze.AggiornaMappatura(employeeId, req.EcosEmplCode);
        return Ok(errore == null
            ? ApiResponse<bool>.Ok(true, "Mappatura aggiornata")
            : ApiResponse<bool>.Fail(errore));
    }

    // ── RETTIFICHE ────────────────────────────────────────────────────────────

    [HttpPost("rettifica")]
    public IActionResult Rettifica([FromBody] HrRettificaRequest req)
    {
        string? errore = _presenze.Rettifica(req, MeId);
        return Ok(errore == null
            ? ApiResponse<bool>.Ok(true, "Rettifica registrata")
            : ApiResponse<bool>.Fail(errore));
    }

    [HttpDelete("rettifica/{id:long}")]
    public IActionResult EliminaRettifica(long id)
    {
        string? errore = _presenze.EliminaRettifica(id, MeId);
        return Ok(errore == null
            ? ApiResponse<bool>.Ok(true, "Rettifica eliminata")
            : ApiResponse<bool>.Fail(errore));
    }
}
