using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/phases")]
[Authorize]
public class PhasesController : ControllerBase
{
    private readonly DbService _db;
    public PhasesController(DbService db) => _db = db;

    // ── Lista template disponibili (per picker) ───────────────────────
    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        using var c = _db.Open();
        List<PhaseTemplateDto> rows = c.Query<PhaseTemplateDto>(@"
            SELECT pt.id, pt.name, pt.category, pt.department_id AS DepartmentId,
                   COALESCE(d.code,'') AS DepartmentCode,
                   COALESCE(d.name,'') AS DepartmentName,
                   pt.sort_order AS SortOrder, pt.is_default AS IsDefault
            FROM phase_templates pt
            LEFT JOIN departments d ON d.id = pt.department_id
            ORDER BY pt.sort_order").ToList();
        return Ok(ApiResponse<List<PhaseTemplateDto>>.Ok(rows));
    }

    // ── Fasi di una commessa ──────────────────────────────────────────
    [HttpGet("project/{projectId}")]
    public IActionResult GetByProject(int projectId)
    {
        using var c = _db.Open();

        List<PhaseListItem> phases = c.Query<PhaseListItem>(@"
            SELECT pp.id, COALESCE(NULLIF(pp.custom_name,''), pt.name) AS Name,
                   pt.category, pp.department_id AS DepartmentId,
                   COALESCE(d.code,'') AS DepartmentCode,
                   COALESCE(d.name,'') AS DepartmentName,
                   pp.budget_hours AS BudgetHours, pp.budget_cost AS BudgetCost,
                   pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id), 0) AS HoursWorked
            FROM project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @ProjectId
            ORDER BY pp.sort_order", new { ProjectId = projectId }).ToList();

        // Carica assegnazioni per ogni fase
        foreach (PhaseListItem phase in phases)
        {
            phase.Assignments = c.Query<PhaseAssignmentDto>(@"
                SELECT pa.id, pa.employee_id AS EmployeeId,
                       CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                       pa.assign_role AS AssignRole, pa.planned_hours AS PlannedHours
                FROM phase_assignments pa
                JOIN employees e ON e.id = pa.employee_id
                WHERE pa.project_phase_id = @PhaseId", new { PhaseId = phase.Id }).ToList();
        }

        return Ok(ApiResponse<List<PhaseListItem>>.Ok(phases));
    }

    // ── Crea fase ─────────────────────────────────────────────────────
    [HttpPost]
    public IActionResult Create([FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        // Prendi department_id dal template se non specificato
        if (req.DepartmentId == null)
            req.DepartmentId = c.ExecuteScalar<int?>(
                "SELECT department_id FROM phase_templates WHERE id=@Id",
                new { Id = req.PhaseTemplateId }, tx);

        int phaseId = c.ExecuteScalar<int>(@"
            INSERT INTO project_phases
                (project_id, phase_template_id, custom_name, department_id,
                 budget_hours, budget_cost, status, progress_pct, sort_order)
            VALUES
                (@ProjectId, @PhaseTemplateId, @CustomName, @DepartmentId,
                 @BudgetHours, @BudgetCost, @Status, @ProgressPct, @SortOrder);
            SELECT LAST_INSERT_ID()", req, tx);

        SaveAssignments(c, tx, phaseId, req.Assignments);

        tx.Commit();
        return Ok(ApiResponse<int>.Ok(phaseId, "Fase creata"));
    }

    // ── Aggiorna fase ─────────────────────────────────────────────────
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        req.Id = id;
        c.Execute(@"
            UPDATE project_phases SET
                custom_name=@CustomName, department_id=@DepartmentId,
                budget_hours=@BudgetHours, budget_cost=@BudgetCost,
                status=@Status, progress_pct=@ProgressPct, sort_order=@SortOrder
            WHERE id=@Id", req, tx);

        c.Execute("DELETE FROM phase_assignments WHERE project_phase_id=@Id", new { Id = id }, tx);
        SaveAssignments(c, tx, id, req.Assignments);

        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Elimina fase ──────────────────────────────────────────────────
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        int hasTimesheet = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM timesheet_entries WHERE project_phase_id=@Id", new { Id = id });
        if (hasTimesheet > 0)
            return BadRequest(ApiResponse<string>.Fail("Impossibile eliminare: esistono ore registrate su questa fase."));

        c.Execute("DELETE FROM phase_assignments WHERE project_phase_id=@Id", new { Id = id });
        c.Execute("DELETE FROM project_phases WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiorna solo avanzamento % ───────────────────────────────────
    [HttpPatch("{id}/progress")]
    public IActionResult UpdateProgress(int id, [FromBody] int progressPct)
    {
        using var c = _db.Open();
        c.Execute("UPDATE project_phases SET progress_pct=@P WHERE id=@Id",
            new { P = progressPct, Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Helper assegnazioni ───────────────────────────────────────────
    private static void SaveAssignments(
        MySqlConnector.MySqlConnection c,
        MySqlConnector.MySqlTransaction tx,
        int phaseId,
        List<PhaseAssignmentDto> assignments)
    {
        foreach (PhaseAssignmentDto a in assignments)
        {
            c.Execute(@"
                INSERT INTO phase_assignments (project_phase_id, employee_id, assign_role, planned_hours)
                VALUES (@PhaseId, @EmployeeId, @AssignRole, @PlannedHours)",
                new { PhaseId = phaseId, a.EmployeeId, a.AssignRole, a.PlannedHours }, tx);
        }
    }
}
