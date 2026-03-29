using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly DbService _db;
    public DepartmentsController(DbService db) => _db = db;

    [HttpGet]
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

    [HttpPost]
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

        return Ok(ApiResponse<int>.Ok(id, "Reparto creato"));
    }

    [HttpPut("{id}")]
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
        return Ok(ApiResponse<string>.Ok("", "Reparto aggiornato"));
    }

    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "code", "name", "hourly_cost", "default_markup", "sort_order", "is_active" };
        string? error = _db.UpdateField("departments", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("{id}")]
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
        return Ok(ApiResponse<string>.Ok("", "Reparto eliminato"));
    }
}
