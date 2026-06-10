using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/phases")]
[Authorize]
public class PhasesController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly ILogger<PhasesController> _logger;

    public PhasesController(DbService db, NotificationService notif, ILogger<PhasesController> logger)
    {
        _db = db;
        _notif = notif;
        _logger = logger;
    }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // ── Lista template disponibili (per picker) ───────────────────────
    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        using var c = _db.Open();
        List<PhaseTemplateDto> rows = c.Query<PhaseTemplateDto>(@"
            SELECT pt.id, pt.name, pt.category,
                   pt.cost_section_template_id AS CostSectionTemplateId,
                   COALESCE(cst.name,'') AS CostSectionName,
                   pt.sort_order AS SortOrder, pt.is_default AS IsDefault
            FROM phase_templates pt
            LEFT JOIN cost_section_templates cst ON cst.id = pt.cost_section_template_id
            ORDER BY pt.sort_order").ToList();
        return Ok(ApiResponse<List<PhaseTemplateDto>>.Ok(rows));
    }

    // ── Fasi di una commessa ──────────────────────────────────────────
    [HttpGet("project/{projectId}")]
    public IActionResult GetByProject(int projectId)
    {
        using var c = _db.Open();

        // Snapshot-aware: pp.name/category/cost_section_template_id sono denormalizzati.
        // LEFT JOIN su phase_templates per non escludere fasi locali (phase_template_id NULL).
        List<PhaseListItem> phases = c.Query<PhaseListItem>(@"
            SELECT pp.id, pp.phase_template_id AS PhaseTemplateId,
                   pp.custom_name AS CustomName,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS Name,
                   COALESCE(pp.category, pt.category) AS Category,
                   COALESCE((SELECT SUM(pa.planned_hours) FROM phase_assignments pa WHERE pa.project_phase_id = pp.id), 0) AS BudgetHours, pp.budget_cost AS BudgetCost,
                   pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id), 0) AS HoursWorked,
                   COALESCE(cst.name, '') AS CostSectionName,
                   COALESCE(pp.cost_section_template_id, pt.cost_section_template_id) AS CostSectionTemplateId,
                   pp.is_local AS IsLocal
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN cost_section_templates cst ON cst.id = COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)
            WHERE pp.project_id = @ProjectId
            ORDER BY pp.sort_order", new { ProjectId = projectId }).ToList();

        // Carica tutte le assegnazioni in un'unica query
        var phaseIds = phases.Select(p => p.Id).ToList();
        var allAssignments = phaseIds.Count > 0 ? c.Query<PhaseAssignmentDto>(@"
            SELECT pa.id, pa.project_phase_id AS ProjectPhaseId, pa.employee_id AS EmployeeId,
                   CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   pa.assign_role AS AssignRole, pa.planned_hours AS PlannedHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             WHERE te.project_phase_id = pa.project_phase_id AND te.employee_id = pa.employee_id), 0) AS HoursWorked
            FROM phase_assignments pa
            JOIN employees e ON e.id = pa.employee_id
            WHERE pa.project_phase_id IN @PhaseIds", new { PhaseIds = phaseIds }).ToList()
            : new List<PhaseAssignmentDto>();
        var assignmentsByPhase = allAssignments.ToLookup(a => a.ProjectPhaseId);
        foreach (var phase in phases)
            phase.Assignments = assignmentsByPhase[phase.Id].ToList();

        return Ok(ApiResponse<List<PhaseListItem>>.Ok(phases));
    }

    // ── Crea fase LOCALE alla commessa (non tocca phase_templates) ───
    // Body: { projectId, costSectionTemplateId, name, departmentId? }
    [HttpPost("local")]
    public IActionResult CreateLocal([FromBody] LocalPhaseRequest req)
    {
        string name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<int>.Fail("Nome fase obbligatorio"));

        using var c = _db.Open();

        // Unicità nome (case-insensitive) all'interno della commessa.
        // Confronta contro pp.custom_name, pp.name (snapshot) e pt.name (legacy).
        int duplicates = c.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.project_id = @pid
              AND LOWER(COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name, '')) = LOWER(@n)",
            new { pid = req.ProjectId, n = name });
        if (duplicates > 0)
            return Ok(ApiResponse<int>.Fail($"Esiste già una fase chiamata \"{name}\" in questa commessa."));

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0)+1 FROM project_phases WHERE project_id=@pid",
            new { pid = req.ProjectId });

        int phaseId = c.ExecuteScalar<int>(@"
            INSERT INTO project_phases
                (project_id, phase_template_id, name, category, cost_section_template_id,
                 department_id, sort_order, status, is_local)
            VALUES
                (@ProjectId, NULL, @Name, 'LOCALE', @SecTplId,
                 @DeptId, @Sort, 'NOT_STARTED', 1);
            SELECT LAST_INSERT_ID()",
            new
            {
                req.ProjectId,
                Name = name,
                SecTplId = req.CostSectionTemplateId,
                DeptId = req.DepartmentId,
                Sort = maxSort
            });

        return Ok(ApiResponse<int>.Ok(phaseId, "Fase locale creata"));
    }

    // ── Promuovi una fase LOCALE a TEMPLATE globale ────────────────
    // POST /api/phases/{phaseId}/promote-to-template
    // - Crea phase_templates con name/category/cost_section_template_id della fase locale
    // - Aggiorna project_phases: phase_template_id = nuovo, is_local = 0
    // - Unicità nome nella stessa sezione (case-insensitive)
    [HttpPost("{phaseId}/promote-to-template")]
    public IActionResult PromoteToTemplate(int phaseId)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        // Tipizzata: TINYINT(1) può essere mappato da Dapper come bool o sbyte → uso bool esplicito.
        var phase = c.QueryFirstOrDefault<(int Id, int ProjectId, int? PhaseTemplateId, string? Name,
                                           string? Category, int? CostSectionTemplateId, bool IsLocal)>(@"
            SELECT id AS Id, project_id AS ProjectId, phase_template_id AS PhaseTemplateId,
                   name AS Name, category AS Category, cost_section_template_id AS CostSectionTemplateId,
                   is_local AS IsLocal
            FROM project_phases WHERE id = @Id", new { Id = phaseId }, tx);
        if (phase.Id == 0)
            return NotFound(ApiResponse<int>.Fail("Fase non trovata."));
        if (!phase.IsLocal && phase.PhaseTemplateId != null)
            return Ok(ApiResponse<int>.Fail("Questa fase è già un template globale."));

        string name = (phase.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<int>.Fail("Nome fase vuoto."));

        int? secId = phase.CostSectionTemplateId;

        // Sezione (e gruppo) devono esistere ancora nel master e essere attivi.
        if (secId.HasValue)
        {
            var secInfo = c.QueryFirstOrDefault<(int Id, string SectionName, bool SecActive,
                                                 int? GroupId, string? GroupName, bool? GrpActive)>(@"
                SELECT cst.id AS Id, cst.name AS SectionName, cst.is_active AS SecActive,
                       g.id AS GroupId, g.name AS GroupName, g.is_active AS GrpActive
                FROM cost_section_templates cst
                LEFT JOIN cost_section_groups g ON g.id = cst.group_id
                WHERE cst.id = @sid", new { sid = secId.Value }, tx);
            if (secInfo.Id == 0)
                return Ok(ApiResponse<int>.Fail("La sezione di costo originale non esiste più nel master. Impossibile promuovere."));
            if (!secInfo.SecActive)
                return Ok(ApiResponse<int>.Fail($"La sezione \"{secInfo.SectionName}\" è disattivata nel master."));
            if (secInfo.GroupId == null)
                return Ok(ApiResponse<int>.Fail($"La sezione \"{secInfo.SectionName}\" non ha un gruppo valido."));
            if (secInfo.GrpActive != true)
                return Ok(ApiResponse<int>.Fail($"Il gruppo \"{secInfo.GroupName}\" è disattivato."));
        }

        // Unicità nome nella stessa sezione (case-insensitive)
        int duplicates = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM phase_templates
            WHERE LOWER(name) = LOWER(@n)
              AND ((@sid IS NULL AND cost_section_template_id IS NULL)
                   OR cost_section_template_id = @sid)",
            new { n = name, sid = secId }, tx);
        if (duplicates > 0)
        {
            string scope = secId.HasValue ? "in questa sezione" : "tra le fasi trasversali";
            return Ok(ApiResponse<int>.Fail($"Esiste già un template chiamato \"{name}\" {scope}."));
        }

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0)+1 FROM phase_templates", transaction: tx);

        int newTemplateId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_templates (name, category, cost_section_template_id, sort_order, is_default)
            VALUES (@Name, @Cat, @Sid, @Sort, 0);
            SELECT LAST_INSERT_ID()",
            new { Name = name, Cat = phase.Category, Sid = secId, Sort = maxSort }, tx);

        c.Execute(@"UPDATE project_phases
                    SET phase_template_id = @Tid, is_local = 0
                    WHERE id = @Id",
            new { Tid = newTemplateId, Id = phaseId }, tx);

        tx.Commit();
        return Ok(ApiResponse<int>.Ok(newTemplateId, "Fase promossa a template globale"));
    }

    // ── Crea singola fase ─────────────────────────────────────────────
    [HttpPost]
    public IActionResult Create([FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        int phaseId = c.ExecuteScalar<int>(@"
            INSERT INTO project_phases
                (project_id, phase_template_id, custom_name,
                 budget_hours, budget_cost, status, progress_pct, sort_order)
            VALUES
                (@ProjectId, @PhaseTemplateId, @CustomName,
                 @BudgetHours, @BudgetCost, @Status, @ProgressPct, @SortOrder);
            SELECT LAST_INSERT_ID()", new
        {
            req.ProjectId,
            req.PhaseTemplateId,
            req.CustomName,
            req.BudgetHours,
            req.BudgetCost,
            req.Status,
            req.ProgressPct,
            req.SortOrder
        }, tx);

        SaveAssignments(c, tx, phaseId, req.ProjectId, req.Assignments);
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
                "SELECT id, sort_order FROM phase_templates WHERE id=@Id",
                new { Id = tplId }, tx);
            if (tpl == null) continue;

            c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, sort_order)
                VALUES (@ProjId, @TplId, @Sort)",
                new { ProjId = req.ProjectId, TplId = (int)tpl.id, Sort = (int)tpl.sort_order }, tx);
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
                custom_name=@CustomName,
                budget_hours=@BudgetHours, budget_cost=@BudgetCost,
                status=@Status, progress_pct=@ProgressPct, sort_order=@SortOrder
            WHERE id=@Id", new
        {
            req.CustomName,
            req.BudgetHours,
            req.BudgetCost,
            req.Status,
            req.ProgressPct,
            req.SortOrder,
            Id = id
        }, tx);

        // Recupera vecchie assegnazioni per confronto
        List<int> oldEmployeeIds = c.Query<int>(
            "SELECT employee_id FROM phase_assignments WHERE project_phase_id=@Id",
            new { Id = id }, tx).ToList();

        c.Execute("DELETE FROM phase_assignments WHERE project_phase_id=@Id", new { Id = id }, tx);

        int projectId = c.ExecuteScalar<int>(
            "SELECT project_id FROM project_phases WHERE id=@Id", new { Id = id }, tx);

        SaveAssignments(c, tx, id, projectId, req.Assignments);
        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiorna singolo campo inline ─────────────────────────────────
    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "budget_hours", "budget_cost", "status", "progress_pct", "custom_name", "sort_order" };
        string? error = _db.UpdateField("project_phases", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
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

        // Notifica al tecnico assegnato (solo se commessa ACTIVE)
        try
        {
            var info = c.QueryFirstOrDefault<dynamic>(@"
                SELECT pp.project_id, p.code AS project_code, p.status AS project_status,
                       COALESCE(NULLIF(pp.custom_name,''), pt.name) AS phase_name
                FROM project_phases pp
                JOIN projects p ON p.id = pp.project_id
                JOIN phase_templates pt ON pt.id = pp.phase_template_id
                WHERE pp.id = @PhaseId", new { PhaseId = phaseId });

            if (info != null && (string)info!.project_status == "ACTIVE")
            {
                int currentEmpId = GetCurrentEmployeeId();
                if (req.EmployeeId != currentEmpId)
                {
                    _notif.Create("PHASE_ASSIGNED", "INFO",
                        $"Nuova assegnazione - {(string)info!.project_code}",
                        $"Sei stato assegnato alla fase: {(string)info!.phase_name}",
                        "PHASE", phaseId, (int)info!.project_id, currentEmpId,
                        new[] { req.EmployeeId });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PhasesController] Errore durante la creazione della notifica di assegnazione fase {PhaseId} all'impiegato {EmployeeId}", phaseId, req.EmployeeId);
        }

        return Ok(ApiResponse<int>.Ok(newId));
    }

    // ── Rimuovi singola assegnazione ──────────────────────────────────
    [HttpDelete("assignments/{id}")]
    public IActionResult RemoveAssignment(int id)
    {
        using var c = _db.Open();

        int hasHours = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM timesheet_entries te
            JOIN phase_assignments pa ON pa.project_phase_id = te.project_phase_id AND pa.employee_id = te.employee_id
            WHERE pa.id = @Id", new { Id = id });
        if (hasHours > 0)
            return BadRequest(ApiResponse<string>.Fail("Impossibile rimuovere: il tecnico ha già ore versate su questa fase."));

        c.Execute("DELETE FROM phase_assignments WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Helper assegnazioni con notifiche ─────────────────────────────
    private void SaveAssignments(
        System.Data.IDbConnection c,
        System.Data.IDbTransaction tx,
        int phaseId,
        int projectId,
        List<PhaseAssignmentDto> assignments)
    {
        // Recupera info fase per le notifiche
        var info = c.QueryFirstOrDefault<dynamic>(@"
            SELECT p.code AS project_code, p.status AS project_status,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS phase_name
            FROM project_phases pp
            JOIN projects p ON p.id = pp.project_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.id = @PhaseId", new { PhaseId = phaseId }, tx);

        int currentEmpId = GetCurrentEmployeeId();
        bool isActive = info != null && (string)info!.project_status == "ACTIVE";

        foreach (PhaseAssignmentDto a in assignments)
        {
            c.Execute(@"
                INSERT INTO phase_assignments (project_phase_id, employee_id, assign_role, planned_hours)
                VALUES (@PhaseId, @EmployeeId, @AssignRole, @PlannedHours)",
                new { PhaseId = phaseId, a.EmployeeId, a.AssignRole, a.PlannedHours }, tx);

            // Notifica al tecnico assegnato (solo se commessa ACTIVE)
            if (isActive && a.EmployeeId != currentEmpId)
            {
                try
                {
                    _notif.Create("PHASE_ASSIGNED", "INFO",
                        $"Nuova assegnazione - {(string)info!.project_code}",
                        $"Sei stato assegnato alla fase: {(string)info.phase_name}",
                        "PHASE", phaseId, projectId, currentEmpId,
                        new[] { a.EmployeeId });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PhasesController] Errore durante il salvataggio della notifica di assegnazione fase {PhaseId} all'impiegato {EmployeeId}", phaseId, a.EmployeeId);
                }
            }
        }
    }

    [HttpPatch("assignments/{id}/hours")]
    public IActionResult UpdateAssignmentHours(int id, [FromBody] PlannedHoursUpdate req)
    {
        using var c = _db.Open();

        // Leggi dati prima dell'update per la notifica
        var info = c.QueryFirstOrDefault<dynamic>(@"
            SELECT pa.employee_id, pa.planned_hours AS OldHours,
                   p.id AS project_id, p.code AS project_code, p.status AS project_status,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS phase_name
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE pa.id = @Id", new { Id = id });

        c.Execute("UPDATE phase_assignments SET planned_hours=@Hours WHERE id=@Id",
            new { Hours = req.PlannedHours, Id = id });

        // Notifica al tecnico se commessa ACTIVE e le ore sono cambiate
        if (info != null && (string)info!.project_status == "ACTIVE")
        {
            int empId = (int)info!.employee_id;
            int currentEmpId = GetCurrentEmployeeId();
            decimal oldHours = (decimal)info.OldHours;

            if (empId != currentEmpId && oldHours != req.PlannedHours)
            {
                try
                {
                    string direction = req.PlannedHours > oldHours ? "aumentate" : "ridotte";
                    _notif.Create("PHASE_HOURS_CHANGED", "INFO",
                        $"Ore aggiornate - {(string)info.project_code}",
                        $"Le ore per la fase {(string)info.phase_name} sono state {direction}: {oldHours:F0}h -> {req.PlannedHours:F0}h",
                        "PHASE", id, (int)info.project_id, currentEmpId,
                        new[] { empId });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PhasesController] Errore durante la creazione della notifica di cambio ore per assegnazione {AssignmentId}", id);
                }
            }
        }

        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("templates")]
    public IActionResult CreateTemplate([FromBody] PhaseTemplateSaveRequest req)
    {
        string name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<int>.Fail("Nome fase obbligatorio"));

        using var c = _db.Open();

        // Unicità case-insensitive nella stessa sezione (cost_section_template_id).
        // NULL = fasi trasversali → unicità globale tra le NULL.
        int duplicates = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM phase_templates
            WHERE LOWER(name) = LOWER(@n)
              AND ((@sid IS NULL AND cost_section_template_id IS NULL)
                   OR cost_section_template_id = @sid)",
            new { n = name, sid = req.CostSectionTemplateId });
        if (duplicates > 0)
        {
            string scope = req.CostSectionTemplateId.HasValue ? "in questa sezione" : "tra le fasi trasversali";
            return Ok(ApiResponse<int>.Fail($"Esiste già una fase chiamata \"{name}\" {scope}."));
        }

        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_templates (name, category, cost_section_template_id, sort_order, is_default)
            VALUES (@Name, @Category, @CostSectionTemplateId, @SortOrder, @IsDefault);
            SELECT LAST_INSERT_ID()",
            new { Name = name, req.Category, req.CostSectionTemplateId, req.SortOrder, IsDefault = req.IsDefault ? 1 : 0 });
        return Ok(ApiResponse<int>.Ok(newId, "Template creato"));
    }

    [HttpPatch("templates/{id}/field")]
    public IActionResult UpdateTemplateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "category", "cost_section_template_id", "sort_order", "is_default" };
        string? error = _db.UpdateField("phase_templates", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(int id)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        // Le project_phases collegate vengono "degradate a locali":
        // hanno già lo snapshot (pp.name, pp.category, pp.cost_section_template_id)
        // grazie alla migrazione, quindi continuano a funzionare anche senza il template.
        int degraded = c.Execute(@"
            UPDATE project_phases
            SET phase_template_id = NULL, is_local = 1
            WHERE phase_template_id = @Id", new { Id = id }, tx);

        c.Execute("DELETE FROM phase_templates WHERE id=@Id", new { Id = id }, tx);
        tx.Commit();

        string msg = degraded > 0
            ? $"Template eliminato. {degraded} fasi nelle commesse esistenti sono state convertite in fasi locali."
            : "Template eliminato.";
        return Ok(ApiResponse<bool>.Ok(true, msg));
    }

    [HttpGet("{id}/project-id")]
    public IActionResult GetProjectId(int id)
    {
        using var c = _db.Open();
        int? projectId = c.ExecuteScalar<int?>(
            "SELECT project_id FROM project_phases WHERE id=@Id", new { Id = id });
        if (projectId == null) return NotFound(ApiResponse<string>.Fail("Fase non trovata"));
        return Ok(ApiResponse<int>.Ok(projectId.Value));
    }
}
