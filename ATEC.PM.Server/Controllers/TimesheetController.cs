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
                   CONCAT(p.code,' - ',pp.custom_name) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, p.code",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    [HttpGet("phases-for-employee")]
    public IActionResult GetPhasesForEmployee([FromQuery] int employeeId)
    {
        using var c = _db.Open();
        var phases = c.Query<TimesheetPhaseOption>(@"
            SELECT pp.id AS PhaseId, CONCAT(p.code,' - ',pp.custom_name) AS Display
            FROM project_phases pp
            JOIN projects p ON p.id = pp.project_id
            WHERE p.status IN ('ACTIVE','DRAFT') AND pp.status <> 'COMPLETED'
            ORDER BY p.code, pp.sort_order").ToList();
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
            SELECT CONCAT(p.code,' - ',pp.custom_name) AS PhaseDisplay,
                   SUM(CASE WHEN te.entry_type='REGULAR' THEN te.hours ELSE 0 END) AS RegularHours,
                   SUM(CASE WHEN te.entry_type='OVERTIME' THEN te.hours ELSE 0 END) AS OvertimeHours,
                   SUM(CASE WHEN te.entry_type='TRAVEL' THEN te.hours ELSE 0 END) AS TravelHours,
                   SUM(te.hours) AS TotalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            GROUP BY pp.id, p.code, pp.custom_name
            ORDER BY p.code",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetSummaryRow>>.Ok(rows));
    }
}
