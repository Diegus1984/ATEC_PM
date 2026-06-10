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
            "SELECT id, CONCAT(first_name,' ',last_name) AS FullName, email, emp_type AS EmpType, status, username FROM employees WHERE status<>'TERMINATED' ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<EmployeeListItem>>.Ok(rows));
    }

    /// <summary>
    /// Solo dipendenti reali: esclude ADMIN, cessati e wildcard reparto ([PM] Generico, …).
    /// </summary>
    [HttpGet("real")]
    public IActionResult GetRealEmployees()
    {
        using var c = _db.Open();
        List<LookupItem> rows = c.Query<LookupItem>(EmployeeLookupQueries.RealEmployeesSql).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    /// <summary>
    /// Tecnici che appartengono a un reparto (employee_departments).
    /// Se departmentId è null/0 (fase trasversale) → restituisce tutti.
    /// </summary>
    [HttpGet("by-department")]
    public IActionResult GetByDepartment([FromQuery] int? departmentId)
    {
        using var c = _db.Open();
        List<LookupItem> rows;

        if (departmentId == null || departmentId == 0)
        {
            // Fase trasversale: tutti i dipendenti attivi (escluso ADMIN)
            rows = c.Query<LookupItem>(
                "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status<>'TERMINATED' AND user_role<>'ADMIN' ORDER BY last_name").ToList();
        }
        else
        {
            // Solo tecnici che appartengono al reparto
            rows = c.Query<LookupItem>(@"
                SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS Name
                FROM employees e
                JOIN employee_departments ed ON ed.employee_id = e.id
                WHERE e.status <> 'TERMINATED' AND e.user_role <> 'ADMIN' AND ed.department_id = @DeptId
                ORDER BY e.last_name",
                new { DeptId = departmentId }).ToList();
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    /// <summary>
    /// Tecnici dai reparti interessati alla sezione costo di una fase.
    /// Percorso: phase_template → cost_section_template → cost_section_template_departments → employee_departments → employees
    /// </summary>
    [HttpGet("by-phase/{phaseId}")]
    public IActionResult GetByPhase(int phaseId)
    {
        using var c = _db.Open();
        // Eredita reparti dalla SEZIONE DI COSTO DELLA COMMESSA (project_cost_sections),
        // non dal template globale. Così se i reparti della sezione sono stati modificati
        // localmente nella commessa, la fase rispetta quei reparti.
        // Snapshot-aware: phase locale → pp.cost_section_template_id ; legacy → pt.cost_section_template_id
        List<LookupItem> rows = c.Query<LookupItem>(@"
            SELECT DISTINCT e.id, CONCAT(e.first_name,' ',e.last_name) AS Name
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN project_cost_sections pcs
                 ON pcs.project_id = pp.project_id
                AND pcs.template_id = COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)
            JOIN project_cost_section_departments pcsd
                 ON pcsd.project_cost_section_id = pcs.id
            JOIN employee_departments ed ON ed.department_id = pcsd.department_id
            JOIN employees e ON e.id = ed.employee_id
            WHERE pp.id = @PhaseId
              AND e.status <> 'TERMINATED'
              AND e.user_role <> 'ADMIN'
            ORDER BY e.last_name",
            new { PhaseId = phaseId }).ToList();

        // Fallback: se la fase non ha sezione costo configurata, restituisce tutti (escluso admin)
        if (rows.Count == 0)
        {
            rows = c.Query<LookupItem>(
                "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status<>'TERMINATED' AND user_role<>'ADMIN' ORDER BY last_name").ToList();
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("pm-list")]
    public IActionResult GetPmList()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>(@"
            SELECT id, CONCAT(first_name,' ',last_name) AS Name
            FROM employees
            WHERE status<>'TERMINATED' AND user_role IN ('PM','ADMIN') AND username<>'admin'
            ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var emp = c.QueryFirstOrDefault<EmployeeSaveRequest>(
            "SELECT id, first_name AS FirstName, last_name AS LastName, email, emp_type AS EmpType, supplier_id AS SupplierId, status FROM employees WHERE id=@Id",
            new { Id = id });
        if (emp == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<EmployeeSaveRequest>.Ok(emp));
    }

    // Anagrafica dipendenti in scrittura: solo ADMIN (cfr. PermissionEngine.CanEditEmployee).
    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public IActionResult Create([FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        var newId = c.ExecuteScalar<int>(
            "INSERT INTO employees (first_name,last_name,email,emp_type,supplier_id,status) VALUES (@FirstName,@LastName,@Email,@EmpType,@SupplierId,@Status); SELECT LAST_INSERT_ID()",
            req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult Update(int id, [FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(
            "UPDATE employees SET first_name=@FirstName,last_name=@LastName,email=@Email,emp_type=@EmpType,supplier_id=@SupplierId,status=@Status WHERE id=@Id",
            req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE employees SET status='TERMINATED' WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
