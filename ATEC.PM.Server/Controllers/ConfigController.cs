using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/config")]
[Authorize(Roles = "ADMIN")]
public class ConfigController : ControllerBase
{
    private readonly DbService _db;
    public ConfigController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<AppConfigItem>(
            "SELECT config_key AS ConfigKey, config_value AS ConfigValue, description AS Description FROM app_config ORDER BY config_key").ToList();
        return Ok(ApiResponse<List<AppConfigItem>>.Ok(rows));
    }

    [HttpGet("{key}")]
    [AllowAnonymous]
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
        return Ok(ApiResponse<string>.Ok("Configurazione salvata"));
    }
}