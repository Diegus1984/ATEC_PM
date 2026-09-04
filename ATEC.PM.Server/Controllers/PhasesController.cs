using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/phases")]
[Authorize]
public class PhasesController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ILogger<PhasesController> _logger;

    public PhasesController(DbService db, NotificationService notif,
        IHubContext<ProjectHub> hub, ILogger<PhasesController> logger)
    {
        _db = db;
        _notif = notif;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Ambiente condiviso (segnalazione #49): fasi, assegnazioni e ore pianificate finiscono
    /// dritte nel Bilancio «Preventivo vs Consuntivo». Chi ha quel tab aperto — o la pagina
    /// /bilancio — deve vedere il numero muoversi senza premere «Aggiorna».
    /// Stesso evento di <c>BudgetVsActualController.NotifyBudgetChanged</c>: il client rilegge
    /// bilancio, fasi, preventivo e righe trasferta in un colpo solo.
    /// </summary>
    private void NotifyBudgetChanged(int projectId)
    {
        if (projectId <= 0) return;
        object payload = new { projectId };
        _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
        _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
    }

    /// <summary>
    /// Avvisa chi ha aperto la Configurazione sezioni: qui si tocca l'ANAGRAFICA delle fasi
    /// (nomi, legami con le sezioni, «nasce da sola»), che è l'albero che l'altro ha davanti.
    /// Stesso evento di <c>CostSectionsController</c>.
    /// </summary>
    private void NotifyCostSectionsChanged(string action) =>
        _hub.Clients.Group(ProjectHub.CostSectionsGroup)
            .SendAsync("CostSectionsChanged", new { action }).SenzaAttesa("CostSectionsChanged");

    /// <summary>Commessa di una fase: le rotte per id fase non la portano nell'URL.</summary>
    private static int ProjectIdOfPhase(System.Data.IDbConnection c, int phaseId) =>
        c.ExecuteScalar<int>("SELECT COALESCE(project_id, 0) FROM project_phases WHERE id=@Id",
            new { Id = phaseId });

    /// <summary>Commessa di un'assegnazione, risalendo alla fase.</summary>
    private static int ProjectIdOfAssignment(System.Data.IDbConnection c, int assignmentId) =>
        c.ExecuteScalar<int>(@"
            SELECT COALESCE(pp.project_id, 0)
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            WHERE pa.id = @Id", new { Id = assignmentId });

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// La fase è stata rinominata: lo step di trasferta che porta il suo nome deve seguirla.
    ///
    /// Lo step nato dal Timesheet mostra il nome della fase e a video promette di seguirlo
    /// («il titolo segue il nome della fase»), ma la rigenerazione partiva SOLO dal salvataggio
    /// di ore: dopo un rinomina la Trasferta restava col nome vecchio finché qualcuno non
    /// toccava un'imputazione. Da chiamare FUORI da una transazione (#37/#52).
    /// </summary>
    private void RiallineaTrasferta(System.Data.IDbConnection c, int projectPhaseId)
    {
        try
        {
            int empId = GetCurrentEmployeeId();
            TravelFromTimesheet.RebuildForPhase(c, projectPhaseId, empId > 0 ? empId : null);
            int projectId = ProjectIdOfPhase(c, projectPhaseId);
            if (projectId > 0)
            {
                object payload = new { projectId };
                _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("TravelChanged", payload).SenzaAttesa("TravelChanged");
                _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("TravelChanged", payload).SenzaAttesa("TravelChanged");
            }
        }
        catch (Exception ex)
        {
            // Il rinomina è già andato a buon fine: un titolo di trasferta vecchio non deve
            // trasformarsi in un errore a video.
            _logger.LogWarning("[Trasferta] Riallineamento del titolo fallito per la fase {Phase}: {Message}",
                projectPhaseId, ex.Message);
        }
    }

    // ── Lista template disponibili (per picker) ───────────────────────
    // v73: ogni fase porta con sé TUTTE le sezioni a cui è agganciata. I tre campi singoli
    // (CostSectionTemplateId, CostSectionName, IsDefault) restano compilati per i client scritti
    // quando una fase stava sotto una sezione sola: sono la PRIMA sezione nell'ordine dell'albero
    // e «nasce da sola in almeno una sezione».
    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        using var c = _db.Open();
        List<PhaseTemplateDto> rows = c.Query<PhaseTemplateDto>(@"
            SELECT pt.id, pt.name, pt.sort_order AS SortOrder
            FROM phase_templates pt
            ORDER BY pt.sort_order").ToList();

        List<PhaseSectionLinkRow> links = c.Query<PhaseSectionLinkRow>(@"
            SELECT pts.phase_template_id AS PhaseTemplateId,
                   pts.cost_section_template_id AS SectionId,
                   cst.name AS SectionName, cst.section_type AS SectionType,
                   COALESCE(g.name,'') AS GroupName,
                   pts.sort_order AS SortOrder, pts.is_default AS IsDefault
            FROM phase_template_sections pts
            JOIN cost_section_templates cst ON cst.id = pts.cost_section_template_id
            LEFT JOIN cost_section_groups g ON g.id = cst.group_id
            ORDER BY g.sort_order, cst.sort_order, pts.sort_order").ToList();

        ILookup<int, PhaseSectionLinkRow> byPhase = links.ToLookup(l => l.PhaseTemplateId);
        foreach (PhaseTemplateDto row in rows)
        {
            row.Sections = byPhase[row.Id].Select(l => new PhaseTemplateSectionDto
            {
                SectionId = l.SectionId,
                SectionName = l.SectionName,
                SectionType = l.SectionType,
                GroupName = l.GroupName,
                SortOrder = l.SortOrder,
                IsDefault = l.IsDefault
            }).ToList();

            PhaseTemplateSectionDto? primary = row.Sections.FirstOrDefault();
            row.CostSectionTemplateId = primary?.SectionId;
            row.CostSectionName = primary?.SectionName ?? "";
            row.IsDefault = row.Sections.Any(s => s.IsDefault);
        }

        return Ok(ApiResponse<List<PhaseTemplateDto>>.Ok(rows));
    }

    private sealed class PhaseSectionLinkRow
    {
        public int PhaseTemplateId { get; set; }
        public int SectionId { get; set; }
        public string SectionName { get; set; } = "";
        public string SectionType { get; set; } = "IN_SEDE";
        public string GroupName { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsDefault { get; set; }
    }

    // ── Fasi di una commessa ──────────────────────────────────────────
    [RequireProjectVisible]
    [HttpGet("project/{projectId}")]
    public IActionResult GetByProject(int projectId)
    {
        using var c = _db.Open();

        // Snapshot-aware: pp.name/category/cost_section_template_id sono denormalizzati.
        // LEFT JOIN su phase_templates per non escludere fasi locali (phase_template_id NULL).
        // v74: la SEZIONE è solo quella scritta sulla riga. Il vecchio ripiego sul template
        // indicherebbe una sola delle N sezioni della fase di anagrafica, cioè una a caso.
        List<PhaseListItem> phases = c.Query<PhaseListItem>(@"
            SELECT pp.id, pp.phase_template_id AS PhaseTemplateId,
                   pp.custom_name AS CustomName,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS Name,
                   COALESCE(pp.category, '') AS Category,
                   COALESCE((SELECT SUM(pa.planned_hours) FROM phase_assignments pa WHERE pa.project_phase_id = pp.id), 0) AS BudgetHours, pp.budget_cost AS BudgetCost,
                   pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   -- Ore lavorate della fase: fuori quelle spostate su «Extra Lavoro» e non
                   -- rimesse dentro (#39). Deciso il 06/08/2026: spostare esclude da TUTTI i
                   -- conteggi, non solo dalla redditività — se qui restassero, la finestra
                   -- della fase direbbe 40 ore e il Bilancio ne conterebbe 32.
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             LEFT JOIN timesheet_extra_work ew ON ew.entry_id = te.id
                             WHERE te.project_phase_id = pp.id
                               AND COALESCE(ew.counts_in_project, 1) = 1), 0) AS HoursWorked,
                   COALESCE(cst.name, '') AS CostSectionName,
                   pp.cost_section_template_id AS CostSectionTemplateId,
                   pp.is_local AS IsLocal,
                   pp.is_off AS IsOff
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN cost_section_templates cst ON cst.id = pp.cost_section_template_id
            WHERE pp.project_id = @ProjectId
            ORDER BY pp.sort_order", new { ProjectId = projectId }).ToList();

        // Carica tutte le assegnazioni in un'unica query
        var phaseIds = phases.Select(p => p.Id).ToList();
        var allAssignments = phaseIds.Count > 0 ? c.Query<PhaseAssignmentDto>(@"
            SELECT pa.id, pa.project_phase_id AS ProjectPhaseId, pa.employee_id AS EmployeeId,
                   CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   pa.assign_role AS AssignRole, pa.planned_hours AS PlannedHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             LEFT JOIN timesheet_extra_work ew ON ew.entry_id = te.id
                             WHERE te.project_phase_id = pa.project_phase_id
                               AND te.employee_id = pa.employee_id
                               AND COALESCE(ew.counts_in_project, 1) = 1), 0) AS HoursWorked
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
    [RequireProjectWritable]
    [HttpPost("local")]
    public IActionResult CreateLocal([FromBody] LocalPhaseRequest req)
    {
        string name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<int>.Fail("Nome fase obbligatorio"));

        using var c = _db.Open();

        // Unicità nome (case-insensitive) DENTRO LA SEZIONE, non nell'intera commessa (v73):
        // la stessa attività può esistere in due sezioni — «Call Cliente» in Program Manager e
        // in Progettazione — ed è esattamente ciò che le fasi multi-sezione producono. Con la
        // regola vecchia, la prima delle due bloccava la creazione della seconda.
        int duplicates = c.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.project_id = @pid
              AND LOWER(COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name, '')) = LOWER(@n)
              AND ((@sid IS NULL AND COALESCE(pp.cost_section_template_id, pt.cost_section_template_id) IS NULL)
                   OR COALESCE(pp.cost_section_template_id, pt.cost_section_template_id) = @sid)",
            new { pid = req.ProjectId, n = name, sid = req.CostSectionTemplateId });
        if (duplicates > 0)
            return Ok(ApiResponse<int>.Fail(
                req.CostSectionTemplateId.HasValue
                    ? $"Esiste già una fase chiamata \"{name}\" in questa sezione di costo."
                    : $"Esiste già una fase chiamata \"{name}\" senza sezione di costo in questa commessa."));

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

        NotifyBudgetChanged(req.ProjectId);
        return Ok(ApiResponse<int>.Ok(phaseId, "Fase locale creata"));
    }

    // ── Promuovi una fase LOCALE a TEMPLATE globale ────────────────
    // POST /api/phases/{phaseId}/promote-to-template
    // - Crea phase_templates con name/category/cost_section_template_id della fase locale
    // - Aggiorna project_phases: phase_template_id = nuovo, is_local = 0
    // - Unicità nome nella stessa sezione (case-insensitive)
    // Protetta come le altre scritture sull'anagrafica: **crea** una fase di anagrafica, quindi
    // vale la stessa regola del CreateTemplate qui sotto, anche se parte da una commessa.
    // Oggi nessun client la chiama (verificato): resta esposta come API, e senza il permesso
    // sarebbe stata la strada laterale per creare template a chiunque.
    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
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

        // Unicità GLOBALE del nome, come in CreateTemplate (v73): la libreria è una sola.
        int duplicates = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM phase_templates WHERE LOWER(name) = LOWER(@n)",
            new { n = name }, tx);
        if (duplicates > 0)
            return Ok(ApiResponse<int>.Fail(
                $"Esiste già una fase di anagrafica «{name}»: aggancia quella alla sezione invece di promuovere questa."));

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0)+1 FROM phase_templates", transaction: tx);

        int newTemplateId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_templates (name, cost_section_template_id, sort_order, is_default)
            VALUES (@Name, @Sid, @Sort, 0);
            SELECT LAST_INSERT_ID()",
            new { Name = name, Sid = secId, Sort = maxSort }, tx);

        if (secId.HasValue)
            LinkSection(c, tx, newTemplateId, secId.Value, isDefault: true);

        c.Execute(@"UPDATE project_phases
                    SET phase_template_id = @Tid, is_local = 0
                    WHERE id = @Id",
            new { Tid = newTemplateId, Id = phaseId }, tx);

        tx.Commit();
        NotifyBudgetChanged(phase.ProjectId);
        NotifyCostSectionsChanged("phase-promoted");
        return Ok(ApiResponse<int>.Ok(newTemplateId, "Fase promossa a template globale"));
    }

    // ── Crea singola fase ─────────────────────────────────────────────
    // Nessun client la chiama (verificato: il web usa `bulk` e `local`), ma resta esposta.
    // v74: **la sezione va scritta sulla riga**, perché da qui in poi nessuno la deduce più dal
    // template. Non essendoci una sezione nel body, si prende la prima della fase di anagrafica
    // — l'unica scelta possibile senza cambiare il contratto, e comunque migliore di NULL, che
    // terrebbe le ore fuori dalla ripartizione del Bilancio.
    [RequireProjectWritable]
    [HttpPost]
    public IActionResult Create([FromBody] PhaseSaveRequest req)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        int? sectionId = c.ExecuteScalar<int?>(@"
            SELECT pts.cost_section_template_id
            FROM phase_template_sections pts
            JOIN cost_section_templates cst ON cst.id = pts.cost_section_template_id
            LEFT JOIN cost_section_groups g ON g.id = cst.group_id
            WHERE pts.phase_template_id = @tid
            ORDER BY g.sort_order, cst.sort_order, pts.sort_order
            LIMIT 1", new { tid = req.PhaseTemplateId }, tx);

        int phaseId = c.ExecuteScalar<int>(@"
            INSERT INTO project_phases
                (project_id, phase_template_id, cost_section_template_id, custom_name,
                 budget_hours, budget_cost, status, progress_pct, sort_order)
            VALUES
                (@ProjectId, @PhaseTemplateId, @SectionId, @CustomName,
                 @BudgetHours, @BudgetCost, @Status, @ProgressPct, @SortOrder);
            SELECT LAST_INSERT_ID()", new
        {
            req.ProjectId,
            req.PhaseTemplateId,
            SectionId = sectionId,
            req.CustomName,
            req.BudgetHours,
            req.BudgetCost,
            req.Status,
            req.ProgressPct,
            req.SortOrder
        }, tx);

        SaveAssignments(c, tx, phaseId, req.ProjectId, req.Assignments);
        tx.Commit();
        NotifyBudgetChanged(req.ProjectId);
        return Ok(ApiResponse<int>.Ok(phaseId, "Fase creata"));
    }

    // ── Inserimento multiplo fasi da template ─────────────────────────
    // v73: si importano COPPIE (fase, sezione). La stessa fase entra una volta per sezione —
    // «Call Cliente» in Program Manager e in Progettazione sono due righe — e la sezione va
    // SCRITTA sulla riga: è lei che dice se le ore sono «in sede» o «da cliente», quindi come
    // si ripartisce il costo nel Bilancio. Prima la riga nasceva senza sezione e la si deduceva
    // dal template: con una fase su più sezioni quella deduzione non esiste più.
    [RequireProjectWritable]
    [HttpPost("bulk")]
    public IActionResult BulkCreate([FromBody] BulkPhaseRequest req)
    {
        // SPA vecchia rimasta in cache: manda solo gli id, la sezione la mettiamo noi.
        List<BulkPhaseItem> items = req.Items.Count > 0
            ? req.Items
            : req.TemplateIds.Select(id => new BulkPhaseItem { TemplateId = id }).ToList();

        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        int inserted = 0;
        foreach (BulkPhaseItem item in items)
        {
            // La sezione del legame; senza sezione richiesta si ricade sulla principale della fase.
            var row = c.QueryFirstOrDefault<(int TemplateId, int? SectionId, int Sort)>(@"
                SELECT pt.id AS TemplateId,
                       COALESCE(@sid, pt.cost_section_template_id) AS SectionId,
                       COALESCE(pts.sort_order, pt.sort_order) AS Sort
                FROM phase_templates pt
                LEFT JOIN phase_template_sections pts
                       ON pts.phase_template_id = pt.id AND pts.cost_section_template_id = @sid
                WHERE pt.id = @tid",
                new { tid = item.TemplateId, sid = item.SectionId }, tx);
            if (row.TemplateId == 0) continue;

            // Stessa fase nella stessa sezione: non si duplica (in sezioni diverse invece sì).
            int already = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM project_phases
                WHERE project_id=@pid AND phase_template_id=@tid
                  AND ((@sid IS NULL AND cost_section_template_id IS NULL)
                       OR cost_section_template_id = @sid)",
                new { pid = req.ProjectId, tid = item.TemplateId, sid = row.SectionId }, tx);
            if (already > 0) continue;

            c.Execute(@"
                INSERT INTO project_phases (project_id, phase_template_id, cost_section_template_id, sort_order)
                VALUES (@ProjId, @TplId, @SecId, @Sort)",
                new { ProjId = req.ProjectId, TplId = item.TemplateId, SecId = row.SectionId, Sort = row.Sort }, tx);
            inserted++;
        }

        tx.Commit();
        int skipped = items.Count - inserted;
        string msg = skipped > 0
            ? $"{inserted} fasi aggiunte ({skipped} erano già in quella sezione)"
            : $"{inserted} fasi aggiunte";
        NotifyBudgetChanged(req.ProjectId);
        return Ok(ApiResponse<bool>.Ok(true, msg));
    }

    // ── Modifica fase completa ────────────────────────────────────────
    [RequireProjectWritable(Tabella = "project_phases")]
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
        // Fuori dalla transazione: la rigenerazione della trasferta ne apre una sua.
        RiallineaTrasferta(c, id);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiorna singolo campo inline ─────────────────────────────────
    [RequireProjectWritable(Tabella = "project_phases")]
    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "budget_hours", "budget_cost", "status", "progress_pct", "custom_name", "sort_order" };
        string? error = _db.UpdateField("project_phases", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));

        using var c = _db.Open();
        // Solo sul nome: gli altri campi non compaiono sulla trasferta e la rigenerazione
        // costa qualche query — non la si fa a ogni % di avanzamento digitata.
        if (req.Field == "custom_name") RiallineaTrasferta(c, id);
        NotifyBudgetChanged(ProjectIdOfPhase(c, id));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Elimina fase ──────────────────────────────────────────────────
    [RequireProjectWritable(Tabella = "project_phases")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        int hasTimesheet = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM timesheet_entries WHERE project_phase_id=@Id", new { Id = id });
        if (hasTimesheet > 0)
            return BadRequest(ApiResponse<string>.Fail("Impossibile eliminare: esistono ore registrate su questa fase."));

        // La commessa va letta PRIMA: dopo la DELETE la riga non c'e' piu'.
        int projectId = ProjectIdOfPhase(c, id);
        c.Execute("DELETE FROM phase_assignments WHERE project_phase_id=@Id", new { Id = id });
        c.Execute("DELETE FROM project_phases WHERE id=@Id", new { Id = id });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Spegni / riaccendi una fase (segnalazione #51) ────────────────
    // Spenta = fuori dall'elenco del Bilancio e dalla tendina del Timesheet, così su una
    // commessa non restano finestre di fasi che lì non si usano.
    // ⚠️ NON esclude niente dai conti: se la fase ha ore imputate, quelle continuano a
    // contare nel Bilancio. È il motivo per cui qui NON si filtra nessuna query economica —
    // chi vuole togliere ore dai costi userà l'Extra Lavoro della #39, che è un'altra cosa.
    // Si torna indietro in qualunque momento: è solo un interruttore.
    [RequireProjectWritable(Tabella = "project_phases")]
    [HttpPatch("{id}/off")]
    public IActionResult SetOff(int id, [FromBody] bool isOff)
    {
        using var c = _db.Open();
        c.Execute("UPDATE project_phases SET is_off = @Off WHERE id = @Id",
            new { Off = isOff ? 1 : 0, Id = id });
        NotifyBudgetChanged(ProjectIdOfPhase(c, id));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiorna solo avanzamento % ───────────────────────────────────
    [RequireProjectWritable(Tabella = "project_phases")]
    [HttpPatch("{id}/progress")]
    public IActionResult UpdateProgress(int id, [FromBody] int progressPct)
    {
        using var c = _db.Open();
        c.Execute("UPDATE project_phases SET progress_pct=@P WHERE id=@Id",
            new { P = progressPct, Id = id });
        NotifyBudgetChanged(ProjectIdOfPhase(c, id));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiungi singola assegnazione ─────────────────────────────────
    [RequireProjectWritable(Tabella = "project_phases", ChiaveRotta = "phaseId")]
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

        NotifyBudgetChanged(ProjectIdOfPhase(c, phaseId));
        return Ok(ApiResponse<int>.Ok(newId));
    }

    // ── Rimuovi singola assegnazione ──────────────────────────────────
    [RequireProjectWritable(Sql = "SELECT pp.project_id FROM phase_assignments pa JOIN project_phases pp ON pp.id = pa.project_phase_id WHERE pa.id = @Id")]
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

        int projectId = ProjectIdOfAssignment(c, id);
        c.Execute("DELETE FROM phase_assignments WHERE id=@Id", new { Id = id });
        NotifyBudgetChanged(projectId);
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

    [RequireProjectWritable(Sql = "SELECT pp.project_id FROM phase_assignments pa JOIN project_phases pp ON pp.id = pa.project_phase_id WHERE pa.id = @Id")]
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

        NotifyBudgetChanged(ProjectIdOfAssignment(c, id));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ══════════════════════════════════════════════════════════════════
    // ANAGRAFICA DELLE FASI (phase_templates) — riservata a Configurazione Sezioni
    // ══════════════════════════════════════════════════════════════════
    // Fino al 06/08/2026 queste scritture avevano il solo [Authorize] della classe: **qualunque
    // utente autenticato, tecnici compresi**, poteva creare, rinominare, spostare di sezione o
    // cancellare una fase dell'anagrafica. Non era teorico: `PATCH templates/{id}/field` con
    // `field = cost_section_template_id` è esattamente il gesto che decide se le ore imputate su
    // quella fase finiscono fra i costi «in sede» o «da cliente» — cioè come si ripartisce il
    // costo nel Bilancio di OGNI commessa che usa quella fase.
    // A video il buco non si vedeva perché la pagina era nascosta ai non-ADMIN, ma bastava la
    // chiamata diretta. Trovato misurando i permessi mentre si abbassava `nav.config_sezioni`
    // al livello PM.
    // Il permesso è quello della pagina da cui si fanno: sono le sole chiamanti (verificato —
    // `features/admin/config-sections/` e nessun altro).
    // NB: le fasi DENTRO una commessa (creazione, assegnazioni, avanzamento) restano com'erano:
    // sono lavoro operativo quotidiano, non anagrafica, e bloccarle qui fermerebbe il gestionale.

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpPost("templates")]
    public IActionResult CreateTemplate([FromBody] PhaseTemplateSaveRequest req)
    {
        string name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<int>.Fail("Nome fase obbligatorio"));

        using var c = _db.Open();

        // Unicità GLOBALE del nome (v73). Prima era «per sezione», ed è esattamente la regola che
        // ha lasciato convivere «Programmazione PLC» e «Programmazione Plc»: chi voleva la stessa
        // attività in due sezioni doveva riscriverne il nome. Ora la libreria è unica e la stessa
        // fase si aggancia a N sezioni, quindi un nome = una fase.
        int duplicates = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM phase_templates WHERE LOWER(name) = LOWER(@n)", new { n = name });
        if (duplicates > 0)
            return Ok(ApiResponse<int>.Fail(
                $"Esiste già una fase «{name}»: agganciala anche a questa sezione invece di ricrearla."));

        using System.Data.IDbTransaction tx = c.BeginTransaction();

        // Niente `category`: era la copia del nome della sezione fatta alla creazione, e con una
        // fase su più sezioni non esiste un valore giusto. La colonna resta a DB, vuota.
        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO phase_templates (name, cost_section_template_id, sort_order, is_default)
            VALUES (@Name, @CostSectionTemplateId, @SortOrder, @IsDefault);
            SELECT LAST_INSERT_ID()",
            new { Name = name, req.CostSectionTemplateId, req.SortOrder, IsDefault = req.IsDefault ? 1 : 0 }, tx);

        if (req.CostSectionTemplateId.HasValue)
            LinkSection(c, tx, newId, req.CostSectionTemplateId.Value, req.IsDefault);

        tx.Commit();
        NotifyCostSectionsChanged("phase-created");
        return Ok(ApiResponse<int>.Ok(newId, "Template creato"));
    }

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpPatch("templates/{id}/field")]
    public IActionResult UpdateTemplateField(int id, [FromBody] FieldUpdateRequest req)
    {
        // v73 — `cost_section_template_id` e `is_default` non descrivono più la fase ma il suo
        // LEGAME con una sezione. La Configurazione sezioni li manda ancora così (la F2 del piano
        // passerà agli endpoint dei legami): qui si traducono, e le colonne vecchie si riallineano
        // subito dopo. Senza questa traduzione il primo drag in produzione farebbe divergere le
        // due fonti in silenzio — e chi legge le colonne vecchie è la creazione di una commessa.
        if (req.Field == "cost_section_template_id" || req.Field == "is_default" || req.Field == "sort_order")
        {
            using var c = _db.Open();
            using System.Data.IDbTransaction tx = c.BeginTransaction();
            LockPhaseRow(c, tx, id);

            switch (req.Field)
            {
                case "cost_section_template_id":
                    // Semantica storica: la fase si SPOSTA (una sezione sola). Resta questa finché
                    // il client non usa POST/DELETE sections.
                    int wasDefault = c.ExecuteScalar<int>(
                        "SELECT COALESCE(is_default,0) FROM phase_templates WHERE id=@pid",
                        new { pid = id }, tx);
                    c.Execute("DELETE FROM phase_template_sections WHERE phase_template_id=@pid",
                        new { pid = id }, tx);
                    if (int.TryParse(req.Value, out int sectionId) && sectionId > 0)
                        LinkSection(c, tx, id, sectionId, wasDefault != 0);
                    break;

                case "is_default":
                    c.Execute("UPDATE phase_template_sections SET is_default=@v WHERE phase_template_id=@pid",
                        new { v = req.Value == "1" ? 1 : 0, pid = id }, tx);
                    // Fase senza legami: nessun legame da marcare, la colonna resta l'unica verità
                    // (sono le «trasversali» che la conversione preventivo→commessa porta dentro).
                    c.Execute(@"UPDATE phase_templates SET is_default=@v WHERE id=@pid
                                AND NOT EXISTS (SELECT 1 FROM phase_template_sections s WHERE s.phase_template_id=@pid)",
                        new { v = req.Value == "1" ? 1 : 0, pid = id }, tx);
                    break;

                case "sort_order":
                    // L'ordine dentro la sezione è del legame; la colonna resta perché ordina la
                    // lista globale dei template (picker e conversione preventivo).
                    int sort = int.TryParse(req.Value, out int parsed) ? parsed : 0;
                    c.Execute("UPDATE phase_templates SET sort_order=@v WHERE id=@pid",
                        new { v = sort, pid = id }, tx);
                    c.Execute("UPDATE phase_template_sections SET sort_order=@v WHERE phase_template_id=@pid",
                        new { v = sort, pid = id }, tx);
                    break;
            }

            SyncLegacyPhaseColumns(c, tx, id);
            tx.Commit();
            NotifyCostSectionsChanged("phase-field");
            return Ok(ApiResponse<bool>.Ok(true));
        }

        // Il nome è l'unica cosa che appartiene alla FASE: sezione, ordine e «nasce da sola»
        // stanno sui legami, `category` è morta con la libreria unica (v74).
        var allowed = new HashSet<string> { "name" };
        string? error = _db.UpdateField("phase_templates", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        NotifyCostSectionsChanged("phase-renamed");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Legami fase ↔ sezione (v73) ───────────────────────────────────
    // Sono il gesto nuovo: agganciare la STESSA fase a più sezioni invece di duplicarne il nome.

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpPost("templates/{id}/sections")]
    public IActionResult AddTemplateSection(int id, [FromBody] PhaseSectionLinkRequest req)
    {
        using var c = _db.Open();

        string? phaseName = c.ExecuteScalar<string?>(
            "SELECT name FROM phase_templates WHERE id=@pid", new { pid = id });
        if (phaseName == null)
            return NotFound(ApiResponse<bool>.Fail("Fase non trovata."));

        var section = c.QueryFirstOrDefault<(string Name, bool IsActive)>(
            "SELECT name AS Name, is_active AS IsActive FROM cost_section_templates WHERE id=@sid",
            new { sid = req.SectionId });
        if (section.Name == null)
            return NotFound(ApiResponse<bool>.Fail("Sezione di costo non trovata."));

        int already = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM phase_template_sections
            WHERE phase_template_id=@pid AND cost_section_template_id=@sid",
            new { pid = id, sid = req.SectionId });
        if (already > 0)
            return Ok(ApiResponse<bool>.Fail($"«{phaseName}» è già nella sezione «{section.Name}»."));

        using System.Data.IDbTransaction tx = c.BeginTransaction();
        LockPhaseRow(c, tx, id);
        LinkSection(c, tx, id, req.SectionId, req.IsDefault);
        SyncLegacyPhaseColumns(c, tx, id);
        tx.Commit();

        // Agganciare a una sezione spenta è lecito (si prepara il lavoro), ma va detto: da lì
        // non nascerà niente finché la sezione non viene riaccesa.
        string msg = section.IsActive
            ? $"«{phaseName}» aggiunta a «{section.Name}»"
            : $"«{phaseName}» aggiunta a «{section.Name}», che però è disattivata: non verrà proposta sulle commesse nuove.";
        NotifyCostSectionsChanged("phase-linked");
        return Ok(ApiResponse<bool>.Ok(true, msg));
    }

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpDelete("templates/{id}/sections/{sectionId}")]
    public IActionResult RemoveTemplateSection(int id, int sectionId)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();
        LockPhaseRow(c, tx, id);

        int rows = c.Execute(@"
            DELETE FROM phase_template_sections
            WHERE phase_template_id=@pid AND cost_section_template_id=@sid",
            new { pid = id, sid = sectionId }, tx);
        if (rows == 0)
        {
            tx.Rollback();
            return NotFound(ApiResponse<bool>.Fail("Questa fase non è in questa sezione."));
        }

        SyncLegacyPhaseColumns(c, tx, id);
        tx.Commit();

        // Le commesse già create non cambiano: la sezione è scritta sulla riga della fase di
        // commessa (snapshot), non letta dall'anagrafica.
        NotifyCostSectionsChanged("phase-unlinked");
        return Ok(ApiResponse<bool>.Ok(true, "Fase tolta dalla sezione. Le commesse già create restano come sono."));
    }

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpPatch("templates/{id}/sections/{sectionId}")]
    public IActionResult UpdateTemplateSection(int id, int sectionId, [FromBody] PhaseSectionLinkUpdate req)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();
        LockPhaseRow(c, tx, id);

        if (req.SortOrder.HasValue)
            c.Execute(@"UPDATE phase_template_sections SET sort_order=@v
                        WHERE phase_template_id=@pid AND cost_section_template_id=@sid",
                new { v = req.SortOrder.Value, pid = id, sid = sectionId }, tx);

        if (req.IsDefault.HasValue)
            c.Execute(@"UPDATE phase_template_sections SET is_default=@v
                        WHERE phase_template_id=@pid AND cost_section_template_id=@sid",
                new { v = req.IsDefault.Value ? 1 : 0, pid = id, sid = sectionId }, tx);

        SyncLegacyPhaseColumns(c, tx, id);
        tx.Commit();
        NotifyCostSectionsChanged("phase-link-updated");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Blocca la riga della fase per tutta la transazione, **prima** di toccare i legami.
    /// <para>
    /// Senza, si va in deadlock e non per un caso raro: l'INSERT sui legami prende un lock
    /// CONDIVISO su <c>phase_templates</c> per il controllo della chiave esterna, e subito dopo
    /// <see cref="SyncLegacyPhaseColumns"/> ne chiede uno ESCLUSIVO sulla stessa riga. Due
    /// richieste sulla stessa fase — due drop di fila, o lo stesso drop mandato due volte —
    /// arrivano entrambe al lock condiviso e poi si aspettano a vicenda per l'esclusivo.
    /// Succede sul serio: MySQL ne ha ucciso una in produzione il 07/08/2026, e a video il
    /// trascinamento sembrava semplicemente non aver funzionato.
    /// Prendendo l'esclusivo per primo le due richieste si mettono in fila invece di scontrarsi.
    /// </para>
    /// </summary>
    private static void LockPhaseRow(System.Data.IDbConnection c, System.Data.IDbTransaction tx, int phaseId) =>
        c.ExecuteScalar<int?>("SELECT id FROM phase_templates WHERE id=@pid FOR UPDATE",
            new { pid = phaseId }, tx);

    /// <summary>Aggancia una fase a una sezione, in coda alle fasi già presenti in quella sezione.</summary>
    private static void LinkSection(System.Data.IDbConnection c, System.Data.IDbTransaction? tx,
        int phaseId, int sectionId, bool isDefault)
    {
        int nextSort = c.ExecuteScalar<int>(@"
            SELECT COALESCE(MAX(sort_order),0)+1 FROM phase_template_sections
            WHERE cost_section_template_id=@sid", new { sid = sectionId }, tx);

        c.Execute(@"
            INSERT IGNORE INTO phase_template_sections
                (phase_template_id, cost_section_template_id, sort_order, is_default)
            VALUES (@pid, @sid, @sort, @def)",
            new { pid = phaseId, sid = sectionId, sort = nextSort, def = isDefault ? 1 : 0 }, tx);
    }

    /// <summary>
    /// Riallinea ai legami le due colonne che descrivevano la fase quando di sezione ne aveva una
    /// sola: <c>cost_section_template_id</c> = la prima sezione nell'ordine dell'albero,
    /// <c>is_default</c> = vero se la fase nasce da sola in almeno una delle sue sezioni.
    /// <para>
    /// Servono finché le leggono la creazione della commessa (<c>ProjectsController</c>), la
    /// conversione preventivo→commessa e i sei <c>COALESCE</c> in giro: le toglie la F4 del piano.
    /// Fino ad allora vanno tenute vere, o due parti del gestionale raccontano cose diverse.
    /// </para>
    /// <para>
    /// <c>is_default</c> si tocca **solo se la fase ha almeno un legame**: le fasi senza sezione
    /// con <c>is_default=1</c> sono le «trasversali» che la conversione di un preventivo porta
    /// dentro la commessa, e azzerarle qui le farebbe sparire da lì senza che nessuno lo chieda.
    /// </para>
    /// </summary>
    private static void SyncLegacyPhaseColumns(System.Data.IDbConnection c,
        System.Data.IDbTransaction? tx, int phaseId)
    {
        c.Execute(@"
            UPDATE phase_templates pt
            SET pt.cost_section_template_id = (
                SELECT pts.cost_section_template_id
                FROM phase_template_sections pts
                JOIN cost_section_templates cst ON cst.id = pts.cost_section_template_id
                LEFT JOIN cost_section_groups g ON g.id = cst.group_id
                WHERE pts.phase_template_id = pt.id
                ORDER BY g.sort_order, cst.sort_order, pts.sort_order
                LIMIT 1)
            WHERE pt.id = @pid", new { pid = phaseId }, tx);

        c.Execute(@"
            UPDATE phase_templates pt
            SET pt.is_default = (
                SELECT MAX(pts.is_default) FROM phase_template_sections pts
                WHERE pts.phase_template_id = pt.id)
            WHERE pt.id = @pid
              AND EXISTS (SELECT 1 FROM phase_template_sections s WHERE s.phase_template_id = pt.id)",
            new { pid = phaseId }, tx);
    }

    [RequireFeature("nav.config_sezioni")]
    [ScritturaNonDiCommessa("Anagrafica fasi (phase_templates): vale per tutte le commesse, non e dati di una")]
    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(int id)
    {
        using var c = _db.Open();
        using System.Data.IDbTransaction tx = c.BeginTransaction();

        // PRIMA di staccarle: congela il nome sulla riga. Le fasi di commessa nate da un
        // template il nome non ce l'hanno scritto, lo leggono da lui — staccarle e basta le
        // lasciava MUTE, righe vuote dentro la commessa e nella tendina delle ore, senza un
        // errore. È successo davvero: 80 righe in produzione (segnalazione #55).
        // Il nome però NON è perso per sempre, al contrario di quanto diceva questa nota: nei
        // backup notturni precedenti alla cancellazione quelle righe hanno ancora il
        // `phase_template_id`, quindi il nome si risale dal template. Le 80 righe sono state
        // riparate così il 10/08/2026 (fonte: `atec_pm_auto_20260801_020000.sql`, chiave
        // reparto + sort_order). Se ricapita, la strada è quella — non ricrearle a mano.
        c.Execute(@"
            UPDATE project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            SET pp.name = pt.name
            WHERE pp.phase_template_id = @Id
              AND (pp.name IS NULL OR pp.name = '')", new { Id = id }, tx);

        // Ora si possono degradare a locali: hanno nome e sezione sulla riga, quindi
        // continuano a funzionare anche senza il template.
        int degraded = c.Execute(@"
            UPDATE project_phases
            SET phase_template_id = NULL, is_local = 1
            WHERE phase_template_id = @Id", new { Id = id }, tx);

        c.Execute("DELETE FROM phase_templates WHERE id=@Id", new { Id = id }, tx);
        tx.Commit();

        NotifyCostSectionsChanged("phase-deleted");
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
