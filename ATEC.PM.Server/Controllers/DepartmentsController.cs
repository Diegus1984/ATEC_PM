using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public DepartmentsController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>
    /// Avvisa chi ha aperto la Configurazione sezioni: il pannello dei reparti vive in quella pagina.
    /// Stesso evento di <c>CostSectionsController</c>.
    /// </summary>
    private void NotifyCostSectionsChanged(string action) =>
        _ = _hub.Clients.Group(ProjectHub.CostSectionsGroup)
            .SendAsync("CostSectionsChanged", new { action });


    /// <summary>
    /// I reparti <b>come li configura la Configurazione sezioni</b>: costo orario e ricarico
    /// compresi, quindi dietro <c>nav.config_sezioni</c> come la pagina che li scrive.
    /// Chi deve solo spuntarli usa <see cref="GetLookup"/>.
    /// </summary>
    [HttpGet]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<DepartmentDto>(
            @"SELECT id, code, name, hourly_cost AS HourlyCost, default_markup AS DefaultMarkup,
              sort_order AS SortOrder, is_active AS IsActive
              FROM departments ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<DepartmentDto>>.Ok(rows));
    }

    [HttpGet("{id}")]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var row = c.QueryFirstOrDefault<DepartmentDto>(
            @"SELECT id, code, name, hourly_cost AS HourlyCost, default_markup AS DefaultMarkup,
              sort_order AS SortOrder, is_active AS IsActive
              FROM departments WHERE id=@id", new { id });
        if (row == null) return NotFound(ApiResponse<string>.Fail("Reparto non trovato"));
        return Ok(ApiResponse<DepartmentDto>.Ok(row));
    }

    /// <summary>
    /// I reparti per una <b>lista di spunta</b>: sigla, nome, attivo, ordine. Niente costo
    /// orario, niente ricarico.
    ///
    /// <para>Aperta a tutti gli autenticati per un motivo dichiarato: i reparti si spuntano
    /// dalla scheda di un dipendente (Utenti) e dal preventivo, e quelle pagine dei numeri non
    /// fanno niente. Chi i numeri li deve vedere apre la Configurazione sezioni, che ha la sua
    /// chiave.</para>
    /// </summary>
    [HttpGet("lookup")]
    public IActionResult GetLookup()
    {
        using var c = _db.Open();
        var rows = c.Query<DepartmentLookupDto>(
            @"SELECT id, code, name, sort_order AS SortOrder, is_active AS IsActive
              FROM departments ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<DepartmentLookupDto>>.Ok(rows));
    }

    [HttpPost]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult Create([FromBody] DepartmentSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<string>.Fail("Codice obbligatorio"));
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM departments WHERE code=@Code", new { req.Code });
        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail($"Codice '{req.Code}' già esistente"));

        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO departments (code, name, hourly_cost, default_markup, sort_order, is_active)
              VALUES (@Code, @Name, @HourlyCost, @DefaultMarkup, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req);

        NotifyCostSectionsChanged("department-created");
        return Ok(ApiResponse<int>.Ok(id, "Reparto creato"));
    }

    [HttpPut("{id}")]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult Update(int id, [FromBody] DepartmentSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<string>.Fail("Codice obbligatorio"));
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM departments WHERE code=@Code AND id<>@id", new { req.Code, id });
        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail($"Codice '{req.Code}' già esistente"));

        int rows = c.Execute(
            @"UPDATE departments SET code=@Code, name=@Name, hourly_cost=@HourlyCost,
              default_markup=@DefaultMarkup, sort_order=@SortOrder, is_active=@IsActive WHERE id=@id",
            new { req.Code, req.Name, req.HourlyCost, req.DefaultMarkup, req.SortOrder, req.IsActive, id });

        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Reparto non trovato"));
        NotifyCostSectionsChanged("department-updated");
        return Ok(ApiResponse<string>.Ok("", "Reparto aggiornato"));
    }

    /// <summary>
    /// Modifica in linea di una cella (il doppio clic nel dock reparti). Fra i campi ammessi
    /// ci sono <c>hourly_cost</c> e <c>default_markup</c>: è una scrittura come le altre e sta
    /// dietro la stessa chiave. Senza, era la porta di servizio della porta di servizio.
    /// </summary>
    [HttpPatch("{id}/field")]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "code", "name", "hourly_cost", "default_markup", "sort_order", "is_active" };
        string? error = _db.UpdateField("departments", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("{id}")]
    [RequireFeature("nav.config_sezioni")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();

        int used = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employee_departments WHERE department_id=@id", new { id });
        if (used > 0)
            return BadRequest(ApiResponse<string>.Fail(
                $"Impossibile eliminare: {used} dipendenti assegnati a questo reparto. Disattivalo invece."));

        int rows = c.Execute("DELETE FROM departments WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Reparto non trovato"));
        NotifyCostSectionsChanged("department-deleted");
        return Ok(ApiResponse<string>.Ok("", "Reparto eliminato"));
    }
}
