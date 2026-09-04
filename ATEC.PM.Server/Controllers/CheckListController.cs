using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// API del modulo Check list / Attività: attività (item) per commessa reale o gruppo generico.
// Editing aperto a chiunque raggiunga il livello della feature (strumento operativo, non solo ADMIN).
[ApiController]
[Route("api/checklist")]
[Authorize]
// Modulo Check list: stessa chiave della voce di menu e del tab commessa.
// OR albero/menu (split passo 3, §12.8.4): grant fotografati da M103, nessuno cambia.
[RequireFeature("project.checklist", "nav.checklist")]
public class CheckListController : ControllerBase
{
    private readonly CheckListDbService _cdb;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ProjectWriteGuard _guard;

    public CheckListController(CheckListDbService cdb, IHubContext<ProjectHub> hub, ProjectWriteGuard guard)
    {
        _cdb = cdb;
        _hub = hub;
        _guard = guard;
    }

    // Id dipendente dal token (claim NameIdentifier): proprietario dell'inbox "Fissa attività".
    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    public const string ConflictMessage = "CONFLITTO: riga modificata da un altro utente";

    // Notifica realtime best-effort: sempre al gruppo globale (pagina PM aggregata) e, se
    // l'attività è di commessa, anche al gruppo della commessa (tab nel dettaglio commessa).
    private void NotifyChanged(string action, int? projectId = null)
    {
        _hub.Clients.Group(ProjectHub.CheckListGroup)
            .SendAsync("ChecklistChanged", new { action, projectId }).SenzaAttesa("ChecklistChanged");
        if (projectId.HasValue)
            _hub.Clients.Group(ProjectHub.ProjectGroup(projectId.Value))
                .SendAsync("ChecklistChanged", new { action, projectId }).SenzaAttesa("ChecklistChanged");
    }

    private const string ItemSelect = @"
        SELECT i.id AS Id, i.project_id AS ProjectId, i.group_id AS GroupId,
               i.description AS Description, i.priority AS Priority, i.due_date AS DueDate,
               i.is_critical AS IsCritical, i.status AS Status, i.data_close AS DataClose,
               i.sort_order AS SortOrder, i.row_version AS RowVersion,
               i.created_at AS CreatedAt
        FROM checklist_items i";

    // Stati ammessi per un'attività (allineati a mom_action_items). Fallback OPEN se non valido.
    private static string NormalizeStatus(string? s) =>
        (s ?? "").ToUpperInvariant() switch
        {
            "STANDBY" => "STANDBY",
            "CLOSED" => "CLOSED",
            _ => "OPEN"
        };

    // ═══════════════════════════════════════════════════════
    // BOARD — pagina PM aggregata (gruppi generici + commesse con attività)
    // ═══════════════════════════════════════════════════════

    [HttpGet("board")]
    public IActionResult GetBoard()
    {
        try
        {
            using var c = _cdb.Open();

            List<ChecklistGroupDto> groups = c.Query<ChecklistGroupDto>(@"
                SELECT id AS Id, name AS Name, sort_order AS SortOrder, row_version AS RowVersion
                FROM checklist_groups
                ORDER BY sort_order, name").ToList();

            List<ChecklistItemDto> groupItems = c.Query<ChecklistItemDto>(
                ItemSelect + " WHERE i.group_id IS NOT NULL ORDER BY i.sort_order, i.id").ToList();
            foreach (ChecklistGroupDto g in groups)
                g.Items = groupItems.Where(i => i.GroupId == g.Id).ToList();

            List<ChecklistProjectDto> projects = c.Query<ChecklistProjectDto>($@"
                SELECT DISTINCT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                       COALESCE(cu.company_name, '') AS CustomerName
                FROM projects p
                INNER JOIN checklist_items i ON i.project_id = p.id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE 1=1{_guard.FiltroBozzeSql(User)}
                ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

            List<ChecklistItemDto> projectItems = c.Query<ChecklistItemDto>(
                ItemSelect + " WHERE i.project_id IS NOT NULL ORDER BY i.sort_order, i.id").ToList();
            foreach (ChecklistProjectDto p in projects)
                p.Items = projectItems.Where(i => i.ProjectId == p.ProjectId).ToList();

            return Ok(ApiResponse<ChecklistBoardDto>.Ok(new ChecklistBoardDto { Projects = projects, Groups = groups }));
        }
        catch (Exception ex) { return Ok(ApiResponse<ChecklistBoardDto>.Fail($"Errore: {ex.Message}")); }
    }

    // Attività di una singola commessa (tab "Check list" nel dettaglio commessa).
    [RequireProjectVisible]
    [HttpGet("project/{projectId}")]
    public IActionResult GetProjectItems(int projectId)
    {
        try
        {
            using var c = _cdb.Open();
            List<ChecklistItemDto> items = c.Query<ChecklistItemDto>(
                ItemSelect + " WHERE i.project_id = @Pid ORDER BY i.sort_order, i.id",
                new { Pid = projectId }).ToList();
            return Ok(ApiResponse<List<ChecklistItemDto>>.Ok(items));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ChecklistItemDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // CRUD ATTIVITÀ
    // ═══════════════════════════════════════════════════════

    [RequireProjectWritable]
    [HttpPost("items")]
    public IActionResult AddItem([FromBody] ChecklistItemSaveRequest req)
    {
        try
        {
            (int? projectId, int? groupId, string? err) = ResolveContainer(req.ProjectId, req.GroupId);
            if (err != null) return Ok(ApiResponse<int>.Fail(err));

            using var c = _cdb.Open();
            if (!ContainerExists(c, projectId, groupId))
                return Ok(ApiResponse<int>.Fail("Commessa o gruppo non trovato"));

            int sortOrder = c.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(sort_order), -1) + 1 FROM checklist_items
                WHERE (@Pid IS NOT NULL AND project_id = @Pid) OR (@Gid IS NOT NULL AND group_id = @Gid)",
                new { Pid = projectId, Gid = groupId });

            int id = c.ExecuteScalar<int>(@"
                INSERT INTO checklist_items
                    (project_id, group_id, description, priority, due_date, is_critical, sort_order, created_by)
                VALUES (@ProjectId, @GroupId, @Description, @Priority, @DueDate, @IsCritical, @SortOrder, @CreatedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    ProjectId = projectId,
                    GroupId = groupId,
                    Description = (req.Description ?? "").Trim(),
                    Priority = ClampPriority(req.Priority),
                    req.DueDate,
                    IsCritical = req.IsCritical ? 1 : 0,
                    SortOrder = sortOrder,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });
            NotifyChanged("item-add", projectId);
            return Ok(ApiResponse<int>.Ok(id, "Attività aggiunta"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // Concorrenza ottimistica: se il client invia RowVersion e la riga nel frattempo è
    // cambiata, l'update NON viene applicato e si risponde con errore riconoscibile.
    [RequireProjectWritable(Tabella = "checklist_items")]
    [HttpPut("items/{id}")]
    public IActionResult UpdateItem(int id, [FromBody] ChecklistItemSaveRequest req)
    {
        try
        {
            using var c = _cdb.Open();
            // Status null = client legacy che non gestisce lo stato → non toccarlo.
            string? status = req.Status == null ? null : NormalizeStatus(req.Status);
            int rows = c.Execute(@"
                UPDATE checklist_items SET
                    description=@Description, priority=@Priority, due_date=@DueDate,
                    is_critical=@IsCritical,
                    status = CASE WHEN @Status IS NULL THEN status ELSE @Status END,
                    data_close = CASE
                        WHEN @Status IS NULL THEN data_close
                        WHEN @Status = 'CLOSED' THEN COALESCE(data_close, CURDATE())
                        ELSE NULL END,
                    row_version = row_version + 1
                 WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
                new
                {
                    Description = (req.Description ?? "").Trim(),
                    Priority = ClampPriority(req.Priority),
                    req.DueDate,
                    IsCritical = req.IsCritical ? 1 : 0,
                    Status = status,
                    Id = id,
                    req.RowVersion
                });
            if (rows == 0)
            {
                int exists = c.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM checklist_items WHERE id=@Id", new { Id = id });
                return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Attività non trovata"));
            }
            int? projectId = c.ExecuteScalar<int?>(
                "SELECT project_id FROM checklist_items WHERE id=@Id", new { Id = id });
            NotifyChanged("item-update", projectId);
            return Ok(ApiResponse<int>.Ok(id, "Attività aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [RequireProjectWritable(Tabella = "checklist_items")]
    [HttpDelete("items/{id}")]
    public IActionResult DeleteItem(int id)
    {
        try
        {
            using var c = _cdb.Open();
            int? projectId = c.ExecuteScalar<int?>(
                "SELECT project_id FROM checklist_items WHERE id=@Id", new { Id = id });
            int rows = c.Execute("DELETE FROM checklist_items WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Attività non trovata"));
            NotifyChanged("item-delete", projectId);
            return Ok(ApiResponse<bool>.Ok(true, "Attività eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // GRUPPI GENERICI (DIREZIONE, VARIE, custom)
    // ═══════════════════════════════════════════════════════

    [RequireProjectWritable]
    [HttpPost("groups")]
    public IActionResult AddGroup([FromBody] ChecklistGroupSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Ok(ApiResponse<int>.Fail("Nome gruppo obbligatorio"));
            using var c = _cdb.Open();
            string name = req.Name.Trim();
            int duplicate = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM checklist_groups WHERE LOWER(TRIM(name)) = LOWER(@Name)",
                new { Name = name });
            if (duplicate > 0)
                return Ok(ApiResponse<int>.Fail("Esiste già un gruppo con questo nome"));
            int sortOrder = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM checklist_groups");
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO checklist_groups (name, sort_order) VALUES (@Name, @SortOrder);
                SELECT LAST_INSERT_ID()",
                new { Name = name, SortOrder = sortOrder });
            NotifyChanged("group-add");
            return Ok(ApiResponse<int>.Ok(id, "Gruppo creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [RequireProjectWritable(Tabella = "checklist_groups")]
    [HttpPut("groups/{id}")]
    public IActionResult UpdateGroup(int id, [FromBody] ChecklistGroupSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Ok(ApiResponse<int>.Fail("Nome gruppo obbligatorio"));
            using var c = _cdb.Open();
            string name = req.Name.Trim();
            int duplicate = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM checklist_groups WHERE LOWER(TRIM(name)) = LOWER(@Name) AND id <> @Id",
                new { Name = name, Id = id });
            if (duplicate > 0)
                return Ok(ApiResponse<int>.Fail("Esiste già un gruppo con questo nome"));
            int rows = c.Execute(@"
                UPDATE checklist_groups SET name=@Name, row_version = row_version + 1
                 WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
                new { Name = name, Id = id, req.RowVersion });
            if (rows == 0)
            {
                int exists = c.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM checklist_groups WHERE id=@Id", new { Id = id });
                return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Gruppo non trovato"));
            }
            NotifyChanged("group-update");
            return Ok(ApiResponse<int>.Ok(id, "Gruppo aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [RequireProjectWritable(Tabella = "checklist_groups")]
    [HttpDelete("groups/{id}")]
    public IActionResult DeleteGroup(int id)
    {
        try
        {
            using var c = _cdb.Open();
            // FK ON DELETE CASCADE rimuove le attività del gruppo (il client conferma prima).
            int rows = c.Execute("DELETE FROM checklist_groups WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Gruppo non trovato"));
            NotifyChanged("group-delete");
            return Ok(ApiResponse<bool>.Ok(true, "Gruppo eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // INBOX "FISSA ATTIVITÀ" — staging personale per dipendente
    // ═══════════════════════════════════════════════════════

    [HttpGet("inbox")]
    public IActionResult GetInbox()
    {
        try
        {
            using var c = _cdb.Open();
            var rows = c.Query<ChecklistInboxDto>(@"
                SELECT id AS Id, note AS Text, sort_order AS SortOrder
                FROM checklist_inbox
                WHERE employee_id = @EmpId
                ORDER BY sort_order, id", new { EmpId = CurrentEmployeeId }).ToList();
            return Ok(ApiResponse<List<ChecklistInboxDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ChecklistInboxDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [ScritturaNonDiCommessa("Appunti personali dell'utente (checklist_inbox): non appartengono a nessuna commessa")]
    [HttpPost("inbox")]
    public IActionResult AddInbox([FromBody] ChecklistInboxSaveRequest req)
    {
        try
        {
            using var c = _cdb.Open();
            int sortOrder = c.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(sort_order), -1) + 1 FROM checklist_inbox WHERE employee_id=@EmpId",
                new { EmpId = CurrentEmployeeId });
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO checklist_inbox (employee_id, note, sort_order)
                VALUES (@EmpId, @Note, @SortOrder);
                SELECT LAST_INSERT_ID()",
                new { EmpId = CurrentEmployeeId, Note = req.Text ?? "", SortOrder = sortOrder });
            return Ok(ApiResponse<int>.Ok(id, "Nota salvata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [ScritturaNonDiCommessa("Appunti personali dell'utente (checklist_inbox): non appartengono a nessuna commessa")]
    [HttpPut("inbox/{id}")]
    public IActionResult UpdateInbox(int id, [FromBody] ChecklistInboxSaveRequest req)
    {
        try
        {
            using var c = _cdb.Open();
            int rows = c.Execute(@"
                UPDATE checklist_inbox SET note=@Note
                 WHERE id=@Id AND employee_id=@EmpId",
                new { Note = req.Text ?? "", Id = id, EmpId = CurrentEmployeeId });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Nota non trovata"));
            return Ok(ApiResponse<int>.Ok(id, "Nota salvata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [ScritturaNonDiCommessa("Appunti personali dell'utente (checklist_inbox): non appartengono a nessuna commessa")]
    [HttpDelete("inbox/{id}")]
    public IActionResult DeleteInbox(int id)
    {
        try
        {
            using var c = _cdb.Open();
            int rows = c.Execute("DELETE FROM checklist_inbox WHERE id=@Id AND employee_id=@EmpId",
                new { Id = id, EmpId = CurrentEmployeeId });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Nota non trovata"));
            return Ok(ApiResponse<bool>.Ok(true, "Nota eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // Assegna la nota a una commessa (ProjectId) o a un gruppo generico (GroupId): il testo
    // diventa una nuova attività (priorità 0=Critica, come nel prototipo). La nota viene rimossa.
    [RequireProjectWritable]
    [HttpPost("inbox/{id}/assign")]
    public IActionResult AssignInbox(int id, [FromBody] ChecklistAssignRequest req)
    {
        try
        {
            (int? projectId, int? groupId, string? err) = ResolveContainer(req.ProjectId, req.GroupId);
            if (err != null) return Ok(ApiResponse<int>.Fail(err));

            using var c = _cdb.Open();
            string? text = c.ExecuteScalar<string?>(
                "SELECT note FROM checklist_inbox WHERE id=@Id AND employee_id=@EmpId",
                new { Id = id, EmpId = CurrentEmployeeId });
            if (text == null) return Ok(ApiResponse<int>.Fail("Nota non trovata"));
            if (string.IsNullOrWhiteSpace(text)) return Ok(ApiResponse<int>.Fail("Nota vuota"));
            if (!ContainerExists(c, projectId, groupId))
                return Ok(ApiResponse<int>.Fail("Commessa o gruppo non trovato"));

            int sortOrder = c.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(sort_order), -1) + 1 FROM checklist_items
                WHERE (@Pid IS NOT NULL AND project_id = @Pid) OR (@Gid IS NOT NULL AND group_id = @Gid)",
                new { Pid = projectId, Gid = groupId });

            int itemId = c.ExecuteScalar<int>(@"
                INSERT INTO checklist_items
                    (project_id, group_id, description, priority, is_critical, sort_order, created_by)
                VALUES (@ProjectId, @GroupId, @Description, 0, 0, @SortOrder, @CreatedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    ProjectId = projectId,
                    GroupId = groupId,
                    Description = text.Trim(),
                    SortOrder = sortOrder,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });

            c.Execute("DELETE FROM checklist_inbox WHERE id=@Id", new { Id = id });
            NotifyChanged("item-add", projectId);
            return Ok(ApiResponse<int>.Ok(itemId, "Attività assegnata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // LOOKUP (commesse) per le combo del client
    // ═══════════════════════════════════════════════════════

    [HttpGet("lookups/projects")]
    public IActionResult GetProjectLookups()
    {
        try
        {
            using var c = _cdb.Open();
            // Solo commesse APERTE (le chiuse si accumulano negli anni): è la lista che
            // popola sia le combo sia la barra laterale della Check list, dove ogni commessa
            // aperta ha la sua tabella dedicata anche prima di avere attività.
            var rows = c.Query<ChecklistProjectLookupDto>($@"
                SELECT id AS Id, code AS Code, title AS Title
                FROM projects
                WHERE status NOT IN {ProjectSorting.ClosedStatusesSql}{_guard.FiltroBozzeSql(User, "projects")}
                ORDER BY {ProjectSorting.OrderBy("")}").ToList();
            return Ok(ApiResponse<List<ChecklistProjectLookupDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ChecklistProjectLookupDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ── helper ─────────────────────────────────────────────

    private static int ClampPriority(int p) => p < 0 ? 0 : (p > 3 ? 3 : p);

    // Normalizza il contenitore: esattamente uno tra ProjectId e GroupId deve essere valorizzato.
    private static (int? projectId, int? groupId, string? error) ResolveContainer(int? projectId, int? groupId)
    {
        bool hasProject = projectId is > 0;
        bool hasGroup = groupId is > 0;
        if (hasProject == hasGroup)
            return (null, null, "Specificare una commessa OPPURE un gruppo (non entrambi)");
        return (hasProject ? projectId : null, hasGroup ? groupId : null, null);
    }

    private static bool ContainerExists(MySqlConnection c, int? projectId, int? groupId)
    {
        if (projectId.HasValue)
            return c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId.Value }) > 0;
        if (groupId.HasValue)
            return c.ExecuteScalar<int>("SELECT COUNT(*) FROM checklist_groups WHERE id=@Id", new { Id = groupId.Value }) > 0;
        return false;
    }
}
