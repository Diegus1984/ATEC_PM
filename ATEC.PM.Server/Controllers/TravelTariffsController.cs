using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/tariff-options")]
[Authorize]
public class TravelTariffsController : ControllerBase
{
    private readonly DbService _db;
    public TravelTariffsController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll([FromQuery] string? type = null)
    {
        using var c = _db.Open();

        List<TariffOptionDto> items;
        if (!string.IsNullOrEmpty(type))
        {
            items = c.Query<TariffOptionDto>(
                "SELECT id AS Id, tariff_type AS TariffType, value AS Value FROM tariff_options WHERE tariff_type=@Type ORDER BY value",
                new { Type = type }).ToList();
        }
        else
        {
            items = c.Query<TariffOptionDto>(
                "SELECT id AS Id, tariff_type AS TariffType, value AS Value FROM tariff_options ORDER BY tariff_type, value").ToList();
        }

        return Ok(ApiResponse<List<TariffOptionDto>>.Ok(items));
    }

    [HttpPost]
    public IActionResult Add([FromBody] TariffOptionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TariffType) || req.Value <= 0)
            return BadRequest(ApiResponse<string>.Fail("Tipo e valore obbligatori"));

        using var c = _db.Open();

        // Controlla duplicato
        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM tariff_options WHERE tariff_type=@Type AND value=@Value",
            new { Type = req.TariffType, Value = req.Value });

        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail("Valore già presente"));

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO tariff_options (tariff_type, value) VALUES (@Type, @Value);
            SELECT LAST_INSERT_ID()",
            new { Type = req.TariffType, Value = req.Value });

        return Ok(ApiResponse<int>.Ok(id, "Valore aggiunto"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();

        // Recupera tipo e valore della tariffa da eliminare
        var option = c.QueryFirstOrDefault<TariffOptionDto>(
            "SELECT id AS Id, tariff_type AS TariffType, value AS Value FROM tariff_options WHERE id=@id",
            new { id });

        if (option == null)
            return NotFound(ApiResponse<string>.Fail("Tariffa non trovata"));

        // Mappa tariff_type → colonna in project_cost_resources / quote_cost_resources
        string? column = option.TariffType switch
        {
            "COST_PER_KM" => "cost_per_km",
            "DAILY_FOOD" => "daily_food",
            "DAILY_HOTEL" => "daily_hotel",
            "DAILY_ALLOWANCE" => "daily_allowance",
            _ => null
        };

        if (column == null)
            return BadRequest(ApiResponse<string>.Fail("Tipo tariffa non valido"));

        // Controlla utilizzo in commesse (project_cost_resources)
        var usedInProjects = c.Query<string>($@"
            SELECT DISTINCT p.code
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            JOIN projects p ON p.id = s.project_id
            WHERE r.`{column}` = @Value",
            new { Value = option.Value }).ToList();

        // Controlla utilizzo in preventivi (quote_cost_resources)
        List<string> usedInQuotes = new();
        try
        {
            usedInQuotes = c.Query<string>($@"
                SELECT DISTINCT q.quote_number
                FROM quote_cost_resources r
                JOIN quote_cost_sections s ON s.id = r.section_id
                JOIN quotes q ON q.id = s.quote_id
                WHERE r.`{column}` = @Value",
                new { Value = option.Value }).ToList();
        }
        catch { /* tabella quote potrebbe non esistere */ }

        if (usedInProjects.Count > 0 || usedInQuotes.Count > 0)
        {
            List<string> references = new();
            if (usedInProjects.Count > 0)
                references.Add($"Commesse: {string.Join(", ", usedInProjects)}");
            if (usedInQuotes.Count > 0)
                references.Add($"Preventivi: {string.Join(", ", usedInQuotes)}");

            return BadRequest(ApiResponse<string>.Fail(
                $"Impossibile eliminare. Valore in uso in: {string.Join(" | ", references)}"));
        }

        c.Execute("DELETE FROM tariff_options WHERE id=@id", new { id });
        return Ok(ApiResponse<string>.Ok("", "Valore eliminato"));
    }
}
