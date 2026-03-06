using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/timesheet")]
[Authorize]
public class TimesheetController : ControllerBase
{
    private readonly DbService _db;
    public TimesheetController(DbService db) => _db = db;

    [HttpGet("week")]
    public IActionResult GetWeek([FromQuery] int employeeId, [FromQuery] string weekStart)
    {
        using var c = _db.Open();
        var start = DateTime.Parse(weekStart);
        var end = start.AddDays(6);

        var entries = c.Query<TimesheetEntryDto>(@"
            SELECT te.id, te.employee_id AS EmployeeId, te.project_phase_id AS ProjectPhaseId,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType, te.notes,
                   CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pt.name)) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, p.code",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    [HttpGet("projects-for-employee")]
    public IActionResult GetProjectsForEmployee([FromQuery] int employeeId)
    {
        using var c = _db.Open();
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool isPm = role == "ADMIN" || role == "PM";

        List<TimesheetProjectOption> projects;

        if (isPm)
        {
            projects = c.Query<TimesheetProjectOption>(@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                WHERE p.status IN ('ACTIVE','DRAFT')
                ORDER BY p.code").ToList();
        }
        else
        {
            projects = c.Query<TimesheetProjectOption>(@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                JOIN project_phases pp ON pp.project_id = p.id
                JOIN phase_assignments pa ON pa.project_phase_id = pp.id AND pa.employee_id = @EmpId
                WHERE p.status IN ('ACTIVE','DRAFT')
                ORDER BY p.code", new { EmpId = employeeId }).ToList();
        }

        return Ok(ApiResponse<List<TimesheetProjectOption>>.Ok(projects));
    }

    /// <summary>
    /// Fasi di una commessa assegnate al dipendente. PM/ADMIN vedono tutte.
    /// </summary>
    [HttpGet("phases-for-employee")]
    public IActionResult GetPhasesForEmployee([FromQuery] int employeeId, [FromQuery] int projectId)
    {
        using var c = _db.Open();
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool isPm = role == "ADMIN" || role == "PM";

        List<TimesheetPhaseOption> phases;

        if (isPm)
        {
            phases = c.Query<TimesheetPhaseOption>(@"
                SELECT pp.id AS PhaseId,
                       COALESCE(NULLIF(pp.custom_name,''), pt.name) AS Display
                FROM project_phases pp
                JOIN phase_templates pt ON pt.id = pp.phase_template_id
                WHERE pp.project_id = @ProjectId AND pp.status <> 'COMPLETED'
                ORDER BY pp.sort_order",
                new { ProjectId = projectId }).ToList();
        }
        else
        {
            phases = c.Query<TimesheetPhaseOption>(@"
                SELECT pp.id AS PhaseId,
                       COALESCE(NULLIF(pp.custom_name,''), pt.name) AS Display
                FROM project_phases pp
                JOIN phase_templates pt ON pt.id = pp.phase_template_id
                JOIN phase_assignments pa ON pa.project_phase_id = pp.id AND pa.employee_id = @EmpId
                WHERE pp.project_id = @ProjectId AND pp.status <> 'COMPLETED'
                ORDER BY pp.sort_order",
                new { EmpId = employeeId, ProjectId = projectId }).ToList();
        }

        return Ok(ApiResponse<List<TimesheetPhaseOption>>.Ok(phases));
    }

    [HttpPost]
    public IActionResult Save([FromBody] TimesheetSaveRequest req)
    {
        using var c = _db.Open();
        if (req.Id > 0)
        {
            c.Execute("UPDATE timesheet_entries SET project_phase_id=@ProjectPhaseId, work_date=@WorkDate, hours=@Hours, entry_type=@EntryType, notes=@Notes WHERE id=@Id", req);
        }
        else
        {
            req.Id = c.ExecuteScalar<int>("INSERT INTO timesheet_entries (employee_id,project_phase_id,work_date,hours,entry_type,notes) VALUES (@EmployeeId,@ProjectPhaseId,@WorkDate,@Hours,@EntryType,@Notes); SELECT LAST_INSERT_ID()", req);

            // Auto-avanzamento: NOT_STARTED → IN_PROGRESS al primo versamento ore
            c.Execute(@"UPDATE project_phases SET status = 'IN_PROGRESS' 
            WHERE id = @ProjectPhaseId AND status = 'NOT_STARTED'", req);
        }
        return Ok(ApiResponse<int>.Ok(req.Id));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM timesheet_entries WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("summary")]
    public IActionResult Summary([FromQuery] int employeeId, [FromQuery] string monthStart)
    {
        using var c = _db.Open();
        var start = DateTime.Parse(monthStart);
        var end = start.AddMonths(1).AddDays(-1);

        var rows = c.Query<TimesheetSummaryRow>(@"
            SELECT CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pt.name)) AS PhaseDisplay,
                   SUM(CASE WHEN te.entry_type='REGULAR' THEN te.hours ELSE 0 END) AS RegularHours,
                   SUM(CASE WHEN te.entry_type='OVERTIME' THEN te.hours ELSE 0 END) AS OvertimeHours,
                   SUM(CASE WHEN te.entry_type='TRAVEL' THEN te.hours ELSE 0 END) AS TravelHours,
                   SUM(te.hours) AS TotalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            GROUP BY pp.id, p.code, pp.custom_name, pt.name
            ORDER BY p.code",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetSummaryRow>>.Ok(rows));
    }

    /// <summary>
    /// Lista dipendenti per cui l'utente corrente può registrare ore.
    /// Restituisce: se stesso + dipendenti EXTERNAL dei propri reparti.
    /// PM/ADMIN: se stesso + tutti gli EXTERNAL.
    /// </summary>
    [HttpGet("registrable-employees")]
    public IActionResult GetRegistrableEmployees()
    {
        using var c = _db.Open();
        int empId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        string? role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        bool isPm = role == "ADMIN" || role == "PM";

        var list = new List<LookupItem>();

        // Aggiungi se stesso per primo
        var me = c.QueryFirstOrDefault<LookupItem>(
            "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE id=@Id",
            new { Id = empId });
        if (me != null) list.Add(me);

        if (isPm)
        {
            // PM/ADMIN: tutti gli EXTERNAL attivi
            var externals = c.Query<LookupItem>(@"
                SELECT id, CONCAT(first_name,' ',last_name,' (EXT)') AS Name 
                FROM employees 
                WHERE emp_type='EXTERNAL' AND status='ACTIVE' AND id <> @Id
                ORDER BY last_name", new { Id = empId }).ToList();
            list.AddRange(externals);
        }
        else if (role == "RESP_REPARTO")
        {
            // RESP: EXTERNAL dei propri reparti
            var externals = c.Query<LookupItem>(@"
                SELECT DISTINCT e.id, CONCAT(e.first_name,' ',e.last_name,' (EXT)') AS Name 
                FROM employees e
                JOIN employee_departments ed ON ed.employee_id = e.id
                WHERE e.emp_type='EXTERNAL' AND e.status='ACTIVE' AND e.id <> @Id
                  AND ed.department_id IN (SELECT department_id FROM employee_departments WHERE employee_id = @Id)
                ORDER BY e.last_name", new { Id = empId }).ToList();
            list.AddRange(externals);
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(list));
    }
}
