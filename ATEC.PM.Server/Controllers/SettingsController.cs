using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Configurazione applicativa riservata a chi ha la funzione «Digest Email». Oggi: SMTP per il
/// digest email del piano risorse. I valori vivono in res_settings (chiavi email.*), con fallback
/// su appsettings.json alla prima configurazione. La password non viaggia con la configurazione:
/// la restituisce solo <c>GET email/password</c>, l'occhiolino del modulo, e resta nel log chi l'ha vista.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
[RequireFeature("nav.digest_email")]
public class SettingsController : ControllerBase
{
    private readonly EmailService _email;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(EmailService email, ILogger<SettingsController> logger)
    {
        _email = email;
        _logger = logger;
    }

    [HttpGet("email")]
    public IActionResult GetEmail()
    {
        try
        {
            EmailSettingsDto cfg = _email.ResolveConfig();
            cfg.Password = null; // non viaggia con la configurazione: la dà solo GET email/password
            return Ok(ApiResponse<EmailSettingsDto>.Ok(cfg));
        }
        catch (Exception ex) { return Ok(ApiResponse<EmailSettingsDto>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// La password SMTP salvata, in chiaro, per l'occhiolino della Configurazione email (Diego,
    /// 09/09/2026 sera). Stessa funzione richiesta del salvataggio, e nel log resta chi l'ha vista.
    /// </summary>
    [HttpGet("email/password")]
    public IActionResult GetEmailPassword()
    {
        try
        {
            (bool ok, string? password, string message) = _email.PasswordInChiaro();
            if (!ok) return Ok(ApiResponse<string>.Fail(message));
            _logger.LogWarning("[EmailService] Password SMTP mostrata in chiaro a {Utente}", User.Identity?.Name ?? "?");
            return Ok(ApiResponse<string>.Ok(password!));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("email")]
    public IActionResult SaveEmail([FromBody] EmailSettingsDto dto)
    {
        try
        {
            _email.SaveConfig(dto);
            return Ok(ApiResponse<string>.Ok("", "Configurazione email salvata."));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("email/test")]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest req)
    {
        try
        {
            (bool ok, string message) = await _email.SendTestAsync(req.ToEmail);
            return Ok(ok ? ApiResponse<string>.Ok("", message) : ApiResponse<string>.Fail(message));
        }
        catch (Exception ex) { return Ok(ApiResponse<string>.Fail($"Errore: {ex.Message}")); }
    }
}
