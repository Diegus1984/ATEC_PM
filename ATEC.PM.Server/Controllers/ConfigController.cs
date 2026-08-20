using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/config")]
[Authorize]
[RequireFeature("action.app_config")]
public class ConfigController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;

    public ConfigController(DbService db, FeatureAccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<AppConfigItem>(
            "SELECT config_key AS ConfigKey, config_value AS ConfigValue, description AS Description FROM app_config ORDER BY config_key").ToList();
        return Ok(ApiResponse<List<AppConfigItem>>.Ok(rows));
    }

    /// <summary>Lettura singola chiave — stessa chiave della classe (action.app_config).</summary>
    [HttpGet("{key}")]
    public IActionResult GetByKey(string key)
    {
        using var c = _db.Open();
        var val = c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key=@Key", new { Key = key });
        if (val == null) return NotFound(ApiResponse<string>.Fail("Chiave non trovata"));
        return Ok(ApiResponse<string>.Ok(val));
    }

    [HttpPut]
    public IActionResult Save([FromBody] List<AppConfigItem> items)
    {
        using var c = _db.Open();
        foreach (AppConfigItem item in items)
        {
            c.Execute(@"INSERT INTO app_config (config_key, config_value, description)
                        VALUES (@ConfigKey, @ConfigValue, @Description)
                        ON DUPLICATE KEY UPDATE config_value=@ConfigValue, description=@Description", item);
        }

        // 🪤 Difetto trovato il 15/08/2026 (blocco E4). Qui dentro può passare anche
        // `PermissionsEngine`, l'interruttore del motore dei permessi, che
        // FeatureAccessService legge UNA volta e poi tiene per tutta la vita del processo:
        // senza questa riga, girarlo da questa pagina non aveva alcun effetto fino al
        // riavvio del servizio — e nessuno lo diceva. Ricaricare tutto costa una manciata
        // di query e succede quando un ADMIN salva la configurazione, cioè quasi mai.
        _access.Reload();

        return Ok(ApiResponse<string>.Ok("Configurazione salvata"));
    }
}