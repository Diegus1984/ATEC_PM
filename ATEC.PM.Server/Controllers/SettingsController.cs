using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Configurazione applicativa modificabile solo da ADMIN. Oggi: SMTP per il digest email del
/// piano risorse. I valori vivono in res_settings (chiavi email.*), con fallback su
/// appsettings.json alla prima configurazione. La password non viene mai restituita in chiaro.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(Roles = "ADMIN")]
public class SettingsController : ControllerBase
{
    private readonly EmailService _email;

    public SettingsController(EmailService email) => _email = email;

    [HttpGet("email")]
    public IActionResult GetEmail()
    {
        try
        {
            EmailSettingsDto cfg = _email.ResolveConfig();
            cfg.Password = null; // mai restituita al client
            return Ok(ApiResponse<EmailSettingsDto>.Ok(cfg));
        }
        catch (Exception ex) { return Ok(ApiResponse<EmailSettingsDto>.Fail($"Errore: {ex.Message}")); }
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
