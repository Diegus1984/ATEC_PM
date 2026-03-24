using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/auth-levels")]
[Authorize]
public class AuthLevelController : ControllerBase
{
    private readonly DbService _db;
    public AuthLevelController(DbService db) => _db = db;

    /// <summary>Lista livelli autorizzazione</summary>
    [HttpGet]
    public IActionResult GetLevels()
    {
        using var c = _db.Open();
        var levels = c.Query<AuthLevelDto>(
            "SELECT id AS Id, level_value AS LevelValue, role_name AS RoleName, display_name AS DisplayName, sort_order AS SortOrder FROM auth_levels ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<AuthLevelDto>>.Ok(levels));
    }

    /// <summary>Lista tutte le feature con livello minimo</summary>
    [HttpGet("features")]
    public IActionResult GetFeatures()
    {
        using var c = _db.Open();
        var features = c.Query<AuthFeatureDto>(
            "SELECT id AS Id, feature_key AS FeatureKey, display_name AS DisplayName, category AS Category, min_level AS MinLevel, behavior AS Behavior FROM auth_features ORDER BY category, min_level, display_name").ToList();
        return Ok(ApiResponse<List<AuthFeatureDto>>.Ok(features));
    }

    /// <summary>Feature accessibili per l'utente corrente (chiamato al login)</summary>
    [HttpGet("features/my")]
    public IActionResult GetMyFeatures()
    {
        string role = User.FindFirst(ClaimTypes.Role)?.Value ?? "TECH";

        using var c = _db.Open();
        int userLevel = c.ExecuteScalar<int>(
            "SELECT COALESCE((SELECT level_value FROM auth_levels WHERE role_name=@Role), 0)",
            new { Role = role });

        var features = c.Query<AuthFeatureDto>(
            @"SELECT id AS Id, feature_key AS FeatureKey, display_name AS DisplayName,
                     category AS Category, min_level AS MinLevel, behavior AS Behavior
              FROM auth_features ORDER BY category, display_name").ToList();

        var levels = c.Query<AuthLevelDto>(
            "SELECT id AS Id, level_value AS LevelValue, role_name AS RoleName, display_name AS DisplayName, sort_order AS SortOrder FROM auth_levels ORDER BY sort_order").ToList();

        return Ok(ApiResponse<object>.Ok(new { UserLevel = userLevel, Features = features, Levels = levels }));
    }

    /// <summary>Aggiorna livello minimo o behavior di una feature</summary>
    [HttpPut("features/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult UpdateFeature(int id, [FromBody] UpdateAuthFeatureRequest req)
    {
        using var c = _db.Open();
        int affected = c.Execute(
            "UPDATE auth_features SET min_level=@MinLevel, behavior=@Behavior WHERE id=@Id",
            new { req.MinLevel, req.Behavior, Id = id });
        if (affected == 0) return NotFound(ApiResponse<string>.Fail("Feature non trovata"));
        return Ok(ApiResponse<string>.Ok("Feature aggiornata"));
    }

    /// <summary>Crea nuova feature</summary>
    [HttpPost("features")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult CreateFeature([FromBody] CreateAuthFeatureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FeatureKey) || string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest(ApiResponse<string>.Fail("FeatureKey e DisplayName sono obbligatori"));

        using var c = _db.Open();
        try
        {
            c.Execute(
                @"INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior)
                  VALUES (@FeatureKey, @DisplayName, @Category, @MinLevel, @Behavior)", req);
            return Ok(ApiResponse<string>.Ok("Feature creata"));
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062)
        {
            return Conflict(ApiResponse<string>.Fail($"Feature '{req.FeatureKey}' esiste già"));
        }
    }

    /// <summary>Elimina feature</summary>
    [HttpDelete("features/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DeleteFeature(int id)
    {
        using var c = _db.Open();
        int affected = c.Execute("DELETE FROM auth_features WHERE id=@Id", new { Id = id });
        if (affected == 0) return NotFound(ApiResponse<string>.Fail("Feature non trovata"));
        return Ok(ApiResponse<string>.Ok("Feature eliminata"));
    }
}
