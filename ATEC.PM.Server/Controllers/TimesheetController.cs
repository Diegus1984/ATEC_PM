using System.Data;
using System.Security.Claims;
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
    private readonly FeatureAccessService _access;
    public TimesheetController(DbService db, FeatureAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ── Controllo accessi timesheet (anti-IDOR) ─────────────────────────
    // L'employeeId arriva da query/body: senza questo check chiunque potrebbe
    // leggere/scrivere le ore di chiunque. Regola speculare a PermissionEngine:
    //   self  OR  livello >= PM (vede tutti)  OR  RESP_REPARTO sul proprio reparto.

    private int CallerId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    private string CallerRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    private bool CanAccessTimesheet(IDbConnection c, int targetEmployeeId)
    {
        int callerId = CallerId();
        if (targetEmployeeId > 0 && targetEmployeeId == callerId) return true;

        string role = CallerRole();
        // PM / ADMIN / DEVELOPER (livello >= PM) vedono tutti.
        if (_access.GetLevelForRole(role) >= _access.GetLevelForRole("PM")) return true;

        // Responsabile di reparto: solo dipendenti che condividono un suo reparto.
        if (role == "RESP_REPARTO" && targetEmployeeId > 0)
        {
            int shared = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM employee_departments ed
                WHERE ed.employee_id = @Target
                  AND ed.department_id IN (SELECT department_id FROM employee_departments WHERE employee_id = @Caller)",
                new { Target = targetEmployeeId, Caller = callerId });
            return shared > 0;
        }
        return false;
    }

    private IActionResult Forbidden() =>
        StatusCode(403, ApiResponse<string>.Fail("Accesso negato: non puoi accedere a questo timesheet."));

    [HttpGet("week")]
    public IActionResult GetWeek([FromQuery] int employeeId, [FromQuery] string weekStart)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var start = DateTime.Parse(weekStart);
        var end = start.AddDays(6);

        var entries = c.Query<TimesheetEntryDto>(@"
            SELECT te.id, te.employee_id AS EmployeeId, te.project_phase_id AS ProjectPhaseId,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType, te.notes,
                   CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, p.code",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    /// <summary>Voci timesheet in un intervallo di date (per la vista calendario mese/settimana).</summary>
    [HttpGet("range")]
    public IActionResult GetRange([FromQuery] int employeeId, [FromQuery] string start, [FromQuery] string end)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var startDate = DateTime.Parse(start);
        var endDate = DateTime.Parse(end);

        var entries = c.Query<TimesheetEntryDto>(@"
            SELECT te.id, te.employee_id AS EmployeeId, te.project_phase_id AS ProjectPhaseId,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType, te.notes,
                   CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, p.code",
            new { EmpId = employeeId, Start = startDate, End = endDate }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    /// <summary>Somma ore già registrate per dipendente/giorno (esclude la riga in modifica).</summary>
    [HttpGet("day-total")]
    public IActionResult DayTotal([FromQuery] int employeeId, [FromQuery] string date, [FromQuery] int excludeId = 0)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        DateTime workDate = DateTime.Parse(date).Date;
        decimal sum = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(hours), 0) FROM timesheet_entries
            WHERE employee_id = @Emp AND work_date = @Date AND (@ExcludeId = 0 OR id <> @ExcludeId)",
            new { Emp = employeeId, Date = workDate, ExcludeId = excludeId });
        return Ok(ApiResponse<decimal>.Ok(sum));
    }

    [HttpGet("projects-for-employee")]
    public IActionResult GetProjectsForEmployee([FromQuery] int employeeId)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool isPm = role == "ADMIN" || role == "PM";

        List<TimesheetProjectOption> projects;

        if (isPm)
        {
            projects = c.Query<TimesheetProjectOption>(@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                WHERE p.status = 'ACTIVE'
                ORDER BY p.code").ToList();
        }
        else
        {
            projects = c.Query<TimesheetProjectOption>(@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                JOIN project_phases pp ON pp.project_id = p.id
                JOIN phase_assignments pa ON pa.project_phase_id = pp.id AND pa.employee_id = @EmpId
                WHERE p.status = 'ACTIVE'
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
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool isPm = role == "ADMIN" || role == "PM";

        List<TimesheetPhaseOption> phases;

        // Snapshot-aware: legge pp.name come fallback per fasi locali (phase_template_id NULL)
        if (isPm)
        {
            phases = c.Query<TimesheetPhaseOption>(@"
                SELECT pp.id AS PhaseId,
                       COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS Display
                FROM project_phases pp
                LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
                WHERE pp.project_id = @ProjectId AND pp.status <> 'COMPLETED'
                ORDER BY pp.sort_order",
                new { ProjectId = projectId }).ToList();
        }
        else
        {
            phases = c.Query<TimesheetPhaseOption>(@"
                SELECT pp.id AS PhaseId,
                       COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS Display
                FROM project_phases pp
                LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
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

        // Anti-IDOR: su update verifica il proprietario reale della riga, su insert l'employeeId richiesto.
        int targetEmp = req.Id > 0
            ? c.ExecuteScalar<int>("SELECT employee_id FROM timesheet_entries WHERE id=@Id", new { req.Id })
            : req.EmployeeId;
        if (!CanAccessTimesheet(c, targetEmp)) return Forbidden();

        if (req.WorkDate.Date > DateTime.Today)
            return BadRequest(ApiResponse<int>.Fail("Non è possibile registrare o spostare ore in date future."));
        if (req.Hours <= 0 || req.Hours > 24)
            return BadRequest(ApiResponse<int>.Fail("Ogni registrazione deve avere ore maggiori di zero e non oltre 24."));

        // Regola fondamentale: somma giornaliera (altre voci + questa) <= 24h. In modifica la riga corrente è esclusa.
        int excludeId = req.Id > 0 ? req.Id : 0;
        decimal existingDayHours = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(hours), 0) FROM timesheet_entries
            WHERE employee_id = @Emp AND work_date = @Date AND (@ExcludeId = 0 OR id <> @ExcludeId)",
            new { Emp = targetEmp, Date = req.WorkDate.Date, ExcludeId = excludeId });
        if (existingDayHours + req.Hours > 24)
        {
            decimal available = Math.Max(0, 24 - existingDayHours);
            return BadRequest(ApiResponse<int>.Fail(
                $"Limite giornaliero 24h: altre registrazioni {existingDayHours:N1}h, massimo consentito per questa voce {available:N1}h."));
        }

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
        // Anti-IDOR: si può cancellare solo una riga di cui si ha accesso al proprietario.
        int targetEmp = c.ExecuteScalar<int>("SELECT employee_id FROM timesheet_entries WHERE id=@Id", new { Id = id });
        if (!CanAccessTimesheet(c, targetEmp)) return Forbidden();
        c.Execute("DELETE FROM timesheet_entries WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("summary")]
    public IActionResult Summary([FromQuery] int employeeId, [FromQuery] string monthStart)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var start = DateTime.Parse(monthStart);
        var end = start.AddMonths(1).AddDays(-1);

        var rows = c.Query<TimesheetSummaryRow>(@"
            SELECT CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay,
                   SUM(CASE WHEN te.entry_type='REGULAR' THEN te.hours ELSE 0 END) AS RegularHours,
                   SUM(CASE WHEN te.entry_type='OVERTIME' THEN te.hours ELSE 0 END) AS OvertimeHours,
                   SUM(CASE WHEN te.entry_type='TRAVEL' THEN te.hours ELSE 0 END) AS TravelHours,
                   SUM(te.hours) AS TotalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            GROUP BY pp.id, p.code, pp.custom_name, pp.name, pt.name
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
