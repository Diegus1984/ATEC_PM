using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly DbService _db;
    public EmployeesController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeListItem>(
            "SELECT id, badge_number AS BadgeNumber, CONCAT(first_name,' ',last_name) AS FullName, email, phone, emp_type AS EmpType, status, hourly_cost AS HourlyCost, weekly_hours AS WeeklyHours, username FROM employees WHERE status<>'TERMINATED' ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<EmployeeListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var emp = c.QueryFirstOrDefault<EmployeeSaveRequest>(
            "SELECT id, badge_number AS BadgeNumber, first_name AS FirstName, last_name AS LastName, email, phone, emp_type AS EmpType, supplier_id AS SupplierId, hourly_cost AS HourlyCost, weekly_hours AS WeeklyHours, hire_date AS HireDate, end_date AS EndDate, status, notes FROM employees WHERE id=@Id",
            new { Id = id });
        if (emp == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<EmployeeSaveRequest>.Ok(emp));
    }

    [HttpPost]
    public IActionResult Create([FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        var newId = c.ExecuteScalar<int>(
            "INSERT INTO employees (badge_number,first_name,last_name,email,phone,emp_type,supplier_id,hourly_cost,weekly_hours,hire_date,end_date,status,notes) VALUES (@BadgeNumber,@FirstName,@LastName,@Email,@Phone,@EmpType,@SupplierId,@HourlyCost,@WeeklyHours,@HireDate,@EndDate,@Status,@Notes); SELECT LAST_INSERT_ID()",
            req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(
            "UPDATE employees SET badge_number=@BadgeNumber,first_name=@FirstName,last_name=@LastName,email=@Email,phone=@Phone,emp_type=@EmpType,supplier_id=@SupplierId,hourly_cost=@HourlyCost,weekly_hours=@WeeklyHours,hire_date=@HireDate,end_date=@EndDate,status=@Status,notes=@Notes WHERE id=@Id",
            req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE employees SET status='TERMINATED',end_date=CURDATE() WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
