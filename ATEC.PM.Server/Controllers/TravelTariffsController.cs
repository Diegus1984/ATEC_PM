using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Tariffe trasferta: lettura libera (serve ai picker e alle griglie di ogni livello),
// scrittura riservata al livello della feature «nav.config_sezioni».
[ApiController]
[Route("api/tariff-options")]
[Authorize]
public class TravelTariffsController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public TravelTariffsController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>
    /// Avvisa chi ha aperto la Configurazione sezioni: l'anagrafica tariffe vive in quella pagina.
    /// Stesso evento di <c>CostSectionsController</c>.
    /// </summary>
    private void NotifyCostSectionsChanged(string action) =>
        _hub.Clients.Group(ProjectHub.CostSectionsGroup)
            .SendAsync("CostSectionsChanged", new { action }).SenzaAttesa("CostSectionsChanged");

    /// <summary>Nome della tariffa (#87), ripulito e troncato al limite della colonna.</summary>
    private static string Nome(string? label)
    {
        string v = (label ?? "").Trim();
        return v.Length <= 100 ? v : v.Substring(0, 100);
    }


    [HttpGet]
    public IActionResult GetAll([FromQuery] string? type = null)
    {
        using var c = _db.Open();

        List<TariffOptionDto> items;
        if (!string.IsNullOrEmpty(type))
        {
            items = c.Query<TariffOptionDto>(
                "SELECT id AS Id, tariff_type AS TariffType, label AS Label, value AS Value FROM tariff_options WHERE tariff_type=@Type ORDER BY value",
                new { Type = type }).ToList();
        }
        else
        {
            items = c.Query<TariffOptionDto>(
                "SELECT id AS Id, tariff_type AS TariffType, label AS Label, value AS Value FROM tariff_options ORDER BY tariff_type, value").ToList();
        }

        return Ok(ApiResponse<List<TariffOptionDto>>.Ok(items));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpPost]
    public IActionResult Add([FromBody] TariffOptionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TariffType) || req.Value <= 0)
            return BadRequest(ApiResponse<string>.Fail("Tipo e valore obbligatori"));
        if (!TariffTypes.IsKnown(req.TariffType))
            return BadRequest(ApiResponse<string>.Fail("Tipo tariffa non valido"));

        using var c = _db.Open();

        // Controlla duplicato
        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM tariff_options WHERE tariff_type=@Type AND value=@Value",
            new { Type = req.TariffType, Value = req.Value });

        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail("Valore già presente"));

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO tariff_options (tariff_type, label, value) VALUES (@Type, @Label, @Value);
            SELECT LAST_INSERT_ID()",
            new { Type = req.TariffType, Label = Nome(req.Label), Value = req.Value });

        NotifyCostSectionsChanged("tariff-created");
        return Ok(ApiResponse<int>.Ok(id, "Valore aggiunto"));
    }

    /// <summary>
    /// Modifica l'importo di una tariffa esistente (il tipo non si cambia: sarebbe un'altra voce).
    /// Non riscrive la storia: il valore viene COPIATO nella riga quando lo si sceglie
    /// (<c>project_cost_resources.daily_food</c>, <c>project_calc_rows.unit_cost</c>, …), quindi
    /// cambiarlo qui sposta solo quello che verrà proposto d'ora in avanti.
    /// </summary>
    [RequireFeature("nav.config_sezioni")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] TariffOptionSaveRequest req)
    {
        if (req.Value <= 0)
            return BadRequest(ApiResponse<string>.Fail("Il valore deve essere maggiore di zero"));

        using var c = _db.Open();

        string? type = c.ExecuteScalar<string?>(
            "SELECT tariff_type FROM tariff_options WHERE id=@id", new { id });
        if (type == null)
            return NotFound(ApiResponse<string>.Fail("Tariffa non trovata"));

        int duplicate = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM tariff_options WHERE tariff_type=@Type AND value=@Value AND id<>@id",
            new { Type = type, Value = req.Value, id });
        if (duplicate > 0)
            return BadRequest(ApiResponse<string>.Fail("Valore già presente"));

        c.Execute("UPDATE tariff_options SET label=@Label, value=@Value WHERE id=@id",
            new { Label = Nome(req.Label), Value = req.Value, id });

        NotifyCostSectionsChanged("tariff-updated");
        return Ok(ApiResponse<string>.Ok("", "Valore aggiornato"));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();

        // Recupera tipo e valore della tariffa da eliminare
        var option = c.QueryFirstOrDefault<TariffOptionDto>(
            "SELECT id AS Id, tariff_type AS TariffType, label AS Label, value AS Value FROM tariff_options WHERE id=@id",
            new { id });

        if (option == null)
            return NotFound(ApiResponse<string>.Fail("Tariffa non trovata"));

        // Mappa tariff_type → colonna in project_cost_resources / quote_cost_resources.
        // HOURLY_RATE non ha una colonna dedicata: vive nelle righe delle finestre di calcolo
        // (project_calc_rows.unit_cost), quindi ha un controllo di utilizzo tutto suo.
        string? column = option.TariffType switch
        {
            TariffTypes.CostPerKm => "cost_per_km",
            TariffTypes.DailyFood => "daily_food",
            TariffTypes.DailyHotel => "daily_hotel",
            TariffTypes.DailyAllowance => "daily_allowance",
            _ => null
        };

        if (column == null && option.TariffType != TariffTypes.HourlyRate)
            return BadRequest(ApiResponse<string>.Fail("Tipo tariffa non valido"));

        List<string> usedInProjects;
        if (column == null)
        {
            usedInProjects = c.Query<string>(@"
                SELECT DISTINCT p.code
                FROM project_calc_rows r
                JOIN project_calc_sheets s ON s.id = r.sheet_id
                JOIN projects p ON p.id = s.project_id
                WHERE r.unit_cost = @Value",
                new { Value = option.Value }).ToList();
        }
        else
        {
            // Controlla utilizzo in commesse (project_cost_resources)
            usedInProjects = c.Query<string>($@"
                SELECT DISTINCT p.code
                FROM project_cost_resources r
                JOIN project_cost_sections s ON s.id = r.section_id
                JOIN projects p ON p.id = s.project_id
                WHERE r.`{column}` = @Value",
                new { Value = option.Value }).ToList();
        }

        // Controlla utilizzo in preventivi (quote_cost_resources)
        List<string> usedInQuotes = new();
        if (column != null)
        {
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
        }

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
        NotifyCostSectionsChanged("tariff-deleted");
        return Ok(ApiResponse<string>.Ok("", "Valore eliminato"));
    }
}
