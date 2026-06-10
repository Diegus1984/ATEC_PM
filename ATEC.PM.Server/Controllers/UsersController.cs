using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "ADMIN")]
public class UsersController : ControllerBase
{
    private readonly DbService _db;
    public UsersController(DbService db) => _db = db;

    // ── Lista utenti ─────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetAll([FromQuery] bool includeTerminated = false)
    {
        // I cessati sono nascosti di default; col flag tornano visibili per poterli riattivare.
        string terminatedFilter = includeTerminated ? "" : "AND status <> 'TERMINATED'";

        using var c = _db.Open();

        var employees = c.Query<UserListItem>(
            $@"SELECT id,
                     CONCAT(first_name,' ',last_name) AS FullName,
                     email, user_role AS UserRole, status,
                     username,
                     (username IS NOT NULL AND username <> '') AS HasCredentials
              FROM employees
              WHERE first_name NOT LIKE '[%'   -- esclude le wildcard reparto ([XXX] Generico, usate solo dalla MoM)
                {terminatedFilter}
              ORDER BY last_name").ToList();

        // Carica reparti e competenze per ogni dipendente
        foreach (UserListItem emp in employees)
        {
            emp.DepartmentCodes = c.Query<string>(
                @"SELECT d.code FROM employee_departments ed
                  JOIN departments d ON d.id = ed.department_id
                  WHERE ed.employee_id = @Id", new { Id = emp.Id }).ToList();

            emp.CompetenceCodes = c.Query<string>(
                @"SELECT d.code FROM employee_competences ec
                  JOIN departments d ON d.id = ec.department_id
                  WHERE ec.employee_id = @Id", new { Id = emp.Id }).ToList();
        }

        return Ok(ApiResponse<List<UserListItem>>.Ok(employees));
    }

    // ── Dettaglio utente (reparti + competenze) ───────────────────────
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();

        UserDetailDto? user = c.QueryFirstOrDefault<UserDetailDto>(
            "SELECT id, CONCAT(first_name,' ',last_name) AS FullName, user_role AS UserRole, username FROM employees WHERE id=@Id",
            new { Id = id });

        if (user == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

        user.Departments = c.Query<EmployeeDepartmentDto>(
            @"SELECT ed.id, ed.department_id AS DepartmentId, d.code AS DepartmentCode,
                     d.name AS DepartmentName, ed.is_responsible AS IsResponsible, ed.is_primary AS IsPrimary
              FROM employee_departments ed
              JOIN departments d ON d.id = ed.department_id
              WHERE ed.employee_id = @Id
              ORDER BY d.sort_order", new { Id = id }).ToList();

        user.Competences = c.Query<EmployeeCompetenceDto>(
            @"SELECT ec.id, ec.department_id AS DepartmentId, d.code AS DepartmentCode,
                     d.name AS DepartmentName, ec.notes
              FROM employee_competences ec
              JOIN departments d ON d.id = ec.department_id
              WHERE ec.employee_id = @Id
              ORDER BY d.sort_order", new { Id = id }).ToList();

        return Ok(ApiResponse<UserDetailDto>.Ok(user));
    }

    // ── Cambia ruolo ─────────────────────────────────────────────────
    [HttpPut("role")]
    public IActionResult SetRole([FromBody] SaveUserRoleRequest req)
    {
        using var c = _db.Open();
        c.Execute("UPDATE employees SET user_role=@UserRole WHERE id=@EmployeeId", req);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Reset credenziali a quelle iniziali (username e password = n.cognome) ──
    [HttpPost("reset-password")]
    public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
    {
        using var c = _db.Open();
        EmployeeNameRow? row = c.QueryFirstOrDefault<EmployeeNameRow>(
            "SELECT first_name AS FirstName, last_name AS LastName FROM employees WHERE id=@EmployeeId",
            new { req.EmployeeId });

        if (row == null)
            return NotFound(ApiResponse<string>.Fail("Utente non trovato"));

        string initialLogin = InitialPasswordHelper.Build(row.FirstName, row.LastName);
        if (string.IsNullOrEmpty(initialLogin))
            return BadRequest(ApiResponse<string>.Fail("Impossibile calcolare la password iniziale"));

        string hash = BCrypt.Net.BCrypt.HashPassword(initialLogin);
        c.Execute(
            "UPDATE employees SET username=@Username, password_hash=@Hash WHERE id=@EmployeeId",
            new { Username = initialLogin, Hash = hash, req.EmployeeId });

        return Ok(ApiResponse<string>.Ok(initialLogin, $"Password reimpostata a {initialLogin}"));
    }

    private class EmployeeNameRow
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }

    // ── Cambia stato (riattivazione cessati / disattivazione) ─────────
    [HttpPut("status")]
    public IActionResult SetStatus([FromBody] SaveUserStatusRequest req)
    {
        string status = req.IsActive ? "ACTIVE" : "TERMINATED";

        using var c = _db.Open();
        int rows = c.Execute(
            "UPDATE employees SET status=@Status WHERE id=@EmployeeId",
            new { Status = status, req.EmployeeId });
        if (rows == 0)
            return NotFound(ApiResponse<string>.Fail("Utente non trovato"));

        return Ok(ApiResponse<bool>.Ok(true, req.IsActive ? "Utente riattivato" : "Utente cessato"));
    }

    // ── Salva reparti dipendente (replace completo) ───────────────────
    [HttpPut("departments")]
    public IActionResult SaveDepartments([FromBody] SaveEmployeeDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM employee_departments WHERE employee_id=@EmployeeId", new { req.EmployeeId }, tx);

        foreach (EmployeeDepartmentDto d in req.Departments)
        {
            c.Execute(
                @"INSERT INTO employee_departments (employee_id, department_id, is_responsible, is_primary)
                  VALUES (@EmployeeId, @DepartmentId, @IsResponsible, @IsPrimary)",
                new { req.EmployeeId, d.DepartmentId, d.IsResponsible, d.IsPrimary }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Salva competenze dipendente (replace completo) ────────────────
    [HttpPut("competences")]
    public IActionResult SaveCompetences([FromBody] SaveEmployeeCompetencesRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM employee_competences WHERE employee_id=@EmployeeId", new { req.EmployeeId }, tx);

        foreach (EmployeeCompetenceDto comp in req.Competences)
        {
            c.Execute(
                @"INSERT INTO employee_competences (employee_id, department_id, notes)
                  VALUES (@EmployeeId, @DepartmentId, @Notes)",
                new { req.EmployeeId, comp.DepartmentId, comp.Notes }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
