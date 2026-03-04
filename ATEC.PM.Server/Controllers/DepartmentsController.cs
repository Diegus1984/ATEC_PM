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
            "SELECT id, code, name, sort_order AS SortOrder, is_active AS IsActive FROM departments WHERE is_active=1 ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<DepartmentDto>>.Ok(rows));
    }
}
