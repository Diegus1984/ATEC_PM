using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/material-categories")]
[Authorize]
public class MaterialCategoriesController : ControllerBase
{
    private readonly DbService _db;
    public MaterialCategoriesController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<MaterialCategoryDto>(
            @"SELECT id, name, markup_code AS MarkupCode,
                     default_markup AS DefaultMarkup,
                     default_commission_markup AS DefaultCommissionMarkup,
                     sort_order AS SortOrder, is_active AS IsActive
              FROM material_categories
              ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<MaterialCategoryDto>>.Ok(rows));
    }

    [HttpPost]
    public IActionResult Create([FromBody] MaterialCategorySaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO material_categories (name, markup_code, default_markup, default_commission_markup, sort_order, is_active)
              VALUES (@Name, '', @DefaultMarkup, @DefaultCommissionMarkup, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Categoria creata"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] MaterialCategorySaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        int rows = c.Execute(
            @"UPDATE material_categories SET name=@Name,
              default_markup=@DefaultMarkup, default_commission_markup=@DefaultCommissionMarkup,
              sort_order=@SortOrder, is_active=@IsActive WHERE id=@id",
            new { req.Name, req.DefaultMarkup, req.DefaultCommissionMarkup, req.SortOrder, req.IsActive, id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Categoria non trovata"));
        return Ok(ApiResponse<string>.Ok("", "Categoria aggiornata"));
    }

    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "default_markup", "default_commission_markup", "sort_order", "is_active" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE material_categories SET {req.Field}=@Value WHERE id=@id", new { Value = req.Value, id });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM material_categories WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Categoria non trovata"));
        return Ok(ApiResponse<string>.Ok("", "Categoria eliminata"));
    }
}
