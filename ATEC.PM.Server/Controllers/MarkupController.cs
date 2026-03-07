using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/markup")]
[Authorize]
public class MarkupController : ControllerBase
{
    private readonly DbService _db;
    public MarkupController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<MarkupCoefficientDto>(
            @"SELECT id, code, description, coefficient_type AS CoefficientType,
                     markup_value AS MarkupValue, hourly_cost AS HourlyCost,
                     sort_order AS SortOrder, is_active AS IsActive
              FROM markup_coefficients ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<MarkupCoefficientDto>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var row = c.QueryFirstOrDefault<MarkupCoefficientDto>(
            @"SELECT id, code, description, coefficient_type AS CoefficientType,
                     markup_value AS MarkupValue, hourly_cost AS HourlyCost,
                     sort_order AS SortOrder, is_active AS IsActive
              FROM markup_coefficients WHERE id=@id", new { id });
        if (row == null) return NotFound(ApiResponse<string>.Fail("Coefficiente non trovato"));
        return Ok(ApiResponse<MarkupCoefficientDto>.Ok(row));
    }

    [HttpPost]
    public IActionResult Create([FromBody] MarkupCoefficientSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<string>.Fail("Codice obbligatorio"));

        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM markup_coefficients WHERE code=@Code", new { req.Code });
        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail($"Codice '{req.Code}' già esistente"));

        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO markup_coefficients (code, description, coefficient_type, markup_value, hourly_cost, sort_order, is_active)
              VALUES (@Code, @Description, @CoefficientType, @MarkupValue, @HourlyCost, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req);

        return Ok(ApiResponse<int>.Ok(id, "Coefficiente creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] MarkupCoefficientSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(ApiResponse<string>.Fail("Codice obbligatorio"));

        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM markup_coefficients WHERE code=@Code AND id<>@id", new { req.Code, id });
        if (exists > 0)
            return BadRequest(ApiResponse<string>.Fail($"Codice '{req.Code}' già esistente"));

        int rows = c.Execute(
            @"UPDATE markup_coefficients SET code=@Code, description=@Description,
              coefficient_type=@CoefficientType, markup_value=@MarkupValue,
              hourly_cost=@HourlyCost, sort_order=@SortOrder, is_active=@IsActive
              WHERE id=@id",
            new { req.Code, req.Description, req.CoefficientType, req.MarkupValue, req.HourlyCost, req.SortOrder, req.IsActive, id });

        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Coefficiente non trovato"));
        return Ok(ApiResponse<string>.Ok("", "Coefficiente aggiornato"));
    }

    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "code", "description", "coefficient_type", "markup_value", "hourly_cost", "sort_order", "is_active" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE markup_coefficients SET {req.Field}=@Value WHERE id=@id", new { Value = req.Value, id });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM markup_coefficients WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Coefficiente non trovato"));
        return Ok(ApiResponse<string>.Ok("", "Coefficiente eliminato"));
    }
}
