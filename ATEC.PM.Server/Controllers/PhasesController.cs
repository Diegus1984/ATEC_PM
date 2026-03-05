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
            SELECT pp.id, pp.phase_template_id AS PhaseTemplateId,
                   pp.custom_name AS CustomName,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS Name,
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
           pa.assign_role AS AssignRole, pa.planned_hours AS PlannedHours,
           COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te 
                     WHERE te.project_phase_id = @PhaseId AND te.employee_id = pa.employee_id), 0) AS HoursWorked
    FROM phase_assignments pa
    JOIN employees e ON e.id = pa.employee_id
    WHERE pa.project_phase_id = @PhaseId", new { PhaseId = phase.Id }).ToList();
        }

        return Ok(ApiResponse<List<PhaseListItem>>.Ok(phases));
    }

    // ── Crea singola fase ─────────────────────────────────────────────
    [HttpPost]
    public IActionResult Create([FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

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
            SELECT LAST_INSERT_ID()", new
        {
            req.ProjectId,
            req.PhaseTemplateId,
            req.CustomName,
            req.DepartmentId,
            req.BudgetHours,
            req.BudgetCost,
            req.Status,
            req.ProgressPct,
            req.SortOrder
        }, tx);

        SaveAssignments(c, tx, phaseId, req.Assignments);
        tx.Commit();
        return Ok(ApiResponse<int>.Ok(phaseId, "Fase creata"));
    }

    // ── Inserimento multiplo fasi da template ─────────────────────────
    [HttpPost("bulk")]
    public IActionResult BulkCreate([FromBody] BulkPhaseRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        foreach (int tplId in req.TemplateIds)
        {
            var tpl = c.QueryFirstOrDefault<dynamic>(
                "SELECT id, department_id, sort_order FROM phase_templates WHERE id=@Id",
                new { Id = tplId }, tx);
            if (tpl == null) continue;

            c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, department_id, sort_order)
                VALUES (@ProjId, @TplId, @DeptId, @Sort)",
                new { ProjId = req.ProjectId, TplId = (int)tpl.id, DeptId = (int?)tpl.department_id, Sort = (int)tpl.sort_order }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true, $"{req.TemplateIds.Count} fasi aggiunte"));
    }

    // ── Modifica fase completa ────────────────────────────────────────
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        c.Execute(@"
            UPDATE project_phases SET
                custom_name=@CustomName, department_id=@DepartmentId,
                budget_hours=@BudgetHours, budget_cost=@BudgetCost,
                status=@Status, progress_pct=@ProgressPct, sort_order=@SortOrder
            WHERE id=@Id", new
        {
            req.CustomName,
            req.DepartmentId,
            req.BudgetHours,
            req.BudgetCost,
            req.Status,
            req.ProgressPct,
            req.SortOrder,
            Id = id
        }, tx);

        c.Execute("DELETE FROM phase_assignments WHERE project_phase_id=@Id", new { Id = id }, tx);
        SaveAssignments(c, tx, id, req.Assignments);
        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiorna singolo campo inline ─────────────────────────────────
    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        using var c = _db.Open();
        string[] allowed = { "budget_hours", "budget_cost", "status", "progress_pct", "custom_name", "sort_order" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non modificabile."));

        c.Execute($"UPDATE project_phases SET {req.Field}=@Value WHERE id=@Id",
            new { Value = req.Value, Id = id });
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

    // ── Aggiungi singola assegnazione ─────────────────────────────────
    [HttpPost("{phaseId}/assignments")]
    public IActionResult AddAssignment(int phaseId, [FromBody] PhaseAssignmentDto req)
    {
        using var c = _db.Open();
        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_assignments (project_phase_id, employee_id, assign_role, planned_hours)
            VALUES (@PhaseId, @EmployeeId, @AssignRole, @PlannedHours);
            SELECT LAST_INSERT_ID()",
            new { PhaseId = phaseId, req.EmployeeId, req.AssignRole, req.PlannedHours });
        return Ok(ApiResponse<int>.Ok(newId));
    }

    // ── Rimuovi singola assegnazione ──────────────────────────────────
    [HttpDelete("assignments/{id}")]
    public IActionResult RemoveAssignment(int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM phase_assignments WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Helper assegnazioni ───────────────────────────────────────────
    private static void SaveAssignments(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
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

    [HttpPatch("assignments/{id}/hours")]
    public IActionResult UpdateAssignmentHours(int id, [FromBody] PlannedHoursUpdate req)
    {
        using var c = _db.Open();
        c.Execute("UPDATE phase_assignments SET planned_hours=@Hours WHERE id=@Id",
            new { Hours = req.PlannedHours, Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }


    [HttpPost("templates")]
    public IActionResult CreateTemplate([FromBody] PhaseTemplateSaveRequest req)
    {
        using var c = _db.Open();
        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_templates (name, category, department_id, sort_order, is_default)
            VALUES (@Name, @Category, @DepartmentId, @SortOrder, @IsDefault);
            SELECT LAST_INSERT_ID()",
            new { req.Name, req.Category, req.DepartmentId, req.SortOrder, IsDefault = req.IsDefault ? 1 : 0 });
        return Ok(ApiResponse<int>.Ok(newId, "Template creato"));
    }

    [HttpPatch("templates/{id}/field")]
    public IActionResult UpdateTemplateField(int id, [FromBody] FieldUpdateRequest req)
    {
        using var c = _db.Open();
        string[] allowed = { "name", "category", "department_id", "sort_order", "is_default" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non modificabile."));

        c.Execute($"UPDATE phase_templates SET {req.Field}=@Value WHERE id=@Id",
            new { Value = req.Value, Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(int id)
    {
        using var c = _db.Open();
        // Controlla se ci sono fasi che usano questo template
        int inUse = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_phases WHERE phase_template_id=@Id", new { Id = id });
        if (inUse > 0)
            return BadRequest(ApiResponse<string>.Fail($"Impossibile eliminare: {inUse} fasi usano questo template."));

        c.Execute("DELETE FROM phase_templates WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

}
