using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Milestone di commessa (project_milestones): pianificazione a righe con date, avanzamento, note.
// Editing aperto a chiunque raggiunga il livello della feature (strumento operativo). Realtime sul gruppo project-{id}.
// Le colonne settimana (W.Inizio/W.Fine/W.Tot) e lo stato/colore sono derivati lato client: qui NON esistono.
[ApiController]
[Route("api/milestones")]
[Authorize]
// Milestone: stessa chiave della voce di menu e del tab commessa.
[RequireFeature("nav.milestones")]
public class MilestonesController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ProjectWriteGuard _guard;

    public MilestonesController(DbService db, IHubContext<ProjectHub> hub, ProjectWriteGuard guard)
    {
        _db = db;
        _hub = hub;
        _guard = guard;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    public const string ConflictMessage = "CONFLITTO: milestone modificata da un altro utente";

    // Notifica realtime best-effort al gruppo della commessa (tab Milestone nel dettaglio commessa).
    private void NotifyChanged(string action, int projectId) =>
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("MilestonesChanged", new { action, projectId });

    private const string Select = @"
        SELECT id AS Id, project_id AS ProjectId, descrizione AS Descrizione,
               data_inizio AS DataInizio, data_fine AS DataFine, avanzamento AS Avanzamento,
               note AS Note, evidenza AS Evidenza, spento AS Spento,
               sort_order AS SortOrder, row_version AS RowVersion, source_catalog_id AS SourceCatalogId
        FROM project_milestones";

    [RequireProjectVisible]
    [HttpGet]
    [HttpGet("/api/projects/{projectId}/milestones")]
    public IActionResult GetForProject([FromQuery] int projectId)
    {
        if (projectId <= 0)
        {
            var routeProjectId = RouteData.Values["projectId"];
            if (routeProjectId != null && int.TryParse(routeProjectId.ToString(), out int parsedId))
            {
                projectId = parsedId;
            }
        }
        if (projectId <= 0) return Ok(ApiResponse<List<MilestoneDto>>.Fail("projectId obbligatorio"));
        using var c = _db.Open();
        var rows = c.Query<MilestoneDto>(Select + " WHERE project_id=@Pid ORDER BY sort_order, id",
            new { Pid = projectId }).ToList();
        return Ok(ApiResponse<List<MilestoneDto>>.Ok(rows));
    }

    // Riepilogo aggregato per la sidebar PM globale: una riga per ogni commessa che ha almeno una
    // milestone ATTIVA (spento=0), con i conteggi di stato. Gli stati rispecchiano la logica client
    // di milestone-utils.msStatus (precedenza done → late → current), calcolati qui in SQL con
    // CURDATE() così la sidebar non deve caricare tutte le milestone (il contenuto card resta lazy).
    // AvgAvanz e PeriodStart/PeriodEnd replicano avgAvanz/periodo client (milestone-utils): media
    // con 0 sulle righe senza valore, periodo = min/max FONDENDO data_inizio e data_fine.
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        using var c = _db.Open();
        var rows = c.Query<MilestoneSummaryDto>($@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                   COUNT(*) AS Active,
                   COALESCE(SUM(m.avanzamento = 100), 0) AS Done,
                   COALESCE(SUM((m.avanzamento IS NULL OR m.avanzamento <> 100)
                                AND m.data_fine IS NOT NULL AND m.data_fine < CURDATE()), 0) AS Late,
                   COALESCE(SUM((m.avanzamento IS NULL OR m.avanzamento <> 100)
                                AND NOT (m.data_fine IS NOT NULL AND m.data_fine < CURDATE())
                                AND ((m.data_inizio IS NOT NULL AND m.data_fine IS NOT NULL
                                      AND m.data_inizio <= CURDATE() AND CURDATE() <= m.data_fine)
                                     OR m.data_inizio = CURDATE())), 0) AS Current,
                   ROUND(AVG(COALESCE(m.avanzamento, 0))) AS AvgAvanz,
                   LEAST(COALESCE(MIN(m.data_inizio), MIN(m.data_fine)),
                         COALESCE(MIN(m.data_fine), MIN(m.data_inizio))) AS PeriodStart,
                   GREATEST(COALESCE(MAX(m.data_fine), MAX(m.data_inizio)),
                            COALESCE(MAX(m.data_inizio), MAX(m.data_fine))) AS PeriodEnd
            FROM project_milestones m
            JOIN projects p ON p.id = m.project_id
            WHERE m.spento = 0{_guard.FiltroBozzeSql(User)}
            GROUP BY p.id, p.code, p.title
            HAVING COUNT(*) > 0
            ORDER BY {ProjectSorting.OrderBy("p")}").ToList();
        return Ok(ApiResponse<List<MilestoneSummaryDto>>.Ok(rows));
    }

    [RequireProjectWritable]
    [HttpPost]
    public IActionResult Create([FromQuery] int projectId, [FromBody] MilestoneSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        int sortOrder = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM project_milestones WHERE project_id=@Pid",
            new { Pid = projectId });

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO project_milestones
                (project_id, descrizione, data_inizio, data_fine, avanzamento, note, evidenza, spento, sort_order, created_by)
            VALUES (@Pid, @Descrizione, @DataInizio, @DataFine, @Avanzamento, @Note, @Evidenza, @Spento, @SortOrder, @CreatedBy);
            SELECT LAST_INSERT_ID()",
            new
            {
                Pid = projectId,
                Descrizione = (req.Descrizione ?? "").Trim(),
                req.DataInizio,
                req.DataFine,
                Avanzamento = ClampAvanz(req.Avanzamento),
                Note = req.Note ?? "",
                Evidenza = req.Evidenza ? 1 : 0,
                Spento = req.Spento ? 1 : 0,
                SortOrder = sortOrder,
                CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
            });
        NotifyChanged("create", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Milestone aggiunta"));
    }

    // Concorrenza ottimistica: se il client invia RowVersion e la riga è cambiata nel frattempo,
    // l'update NON viene applicato e si risponde con un errore riconoscibile.
    [RequireProjectWritable(Tabella = "project_milestones")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] MilestoneSaveRequest req)
    {
        using var c = _db.Open();
        int rows = c.Execute(@"
            UPDATE project_milestones SET
                descrizione=@Descrizione, data_inizio=@DataInizio, data_fine=@DataFine,
                avanzamento=@Avanzamento, note=@Note, evidenza=@Evidenza, spento=@Spento,
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                Descrizione = (req.Descrizione ?? "").Trim(),
                req.DataInizio,
                req.DataFine,
                Avanzamento = ClampAvanz(req.Avanzamento),
                Note = req.Note ?? "",
                Evidenza = req.Evidenza ? 1 : 0,
                Spento = req.Spento ? 1 : 0,
                Id = id,
                req.RowVersion
            });
        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_milestones WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Milestone non trovata"));
        }
        int projectId = c.ExecuteScalar<int>("SELECT project_id FROM project_milestones WHERE id=@Id", new { Id = id });
        NotifyChanged("update", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Milestone aggiornata"));
    }

    [RequireProjectWritable(Tabella = "project_milestones")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        int projectId = c.ExecuteScalar<int>(
            "SELECT COALESCE((SELECT project_id FROM project_milestones WHERE id=@Id), 0)", new { Id = id });
        int rows = c.Execute("DELETE FROM project_milestones WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Milestone non trovata"));
        NotifyChanged("delete", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Milestone eliminata"));
    }

    [RequireProjectWritable]
    [HttpPost("reorder")]
    public IActionResult Reorder([FromQuery] int projectId, [FromBody] MilestoneReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
            c.Execute("UPDATE project_milestones SET sort_order=@Sort WHERE id=@Id AND project_id=@Pid",
                new { Sort = order++, Id = id, Pid = projectId });
        NotifyChanged("reorder", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Ordine aggiornato"));
    }

    // ── Viste salvate del Gantt ────────────────────────────────────────────────
    // Composizione della vista (colonne/righe spente, intervallo, timeline) per commessa.
    // Sta qui e non in localStorage perché è una scelta condivisa: il «Gantt cliente» deve
    // essere lo stesso per tutti. Il payload è opaco lato server.

    [RequireProjectVisible]
    [HttpGet("project/{projectId}/views")]
    public IActionResult GetViews(int projectId)
    {
        using var c = _db.Open();
        var views = c.Query<MilestoneGanttViewDto>(@"
            SELECT v.name AS Name, v.payload AS Payload, v.updated_at AS UpdatedAt,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), '') AS UpdatedByName
            FROM milestone_gantt_views v
            LEFT JOIN employees e ON e.id = v.updated_by
            WHERE v.project_id = @Pid
            ORDER BY v.name", new { Pid = projectId }).ToList();
        return Ok(ApiResponse<List<MilestoneGanttViewDto>>.Ok(views));
    }

    [RequireProjectWritable]
    [HttpPut("project/{projectId}/views/{name}")]
    public IActionResult SaveView(int projectId, string name, [FromBody] MilestoneGanttViewSaveRequest req)
    {
        string viewName = (name ?? "").Trim();
        if (viewName.Length == 0) return Ok(ApiResponse<bool>.Fail("Nome vista mancante"));
        if (string.IsNullOrWhiteSpace(req?.Payload)) return Ok(ApiResponse<bool>.Fail("Composizione mancante"));

        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<bool>.Fail("Commessa non trovata"));

        c.Execute(@"
            INSERT INTO milestone_gantt_views (project_id, name, payload, updated_by)
            VALUES (@Pid, @Name, @Payload, @By)
            ON DUPLICATE KEY UPDATE payload = VALUES(payload), updated_by = VALUES(updated_by)",
            new
            {
                Pid = projectId,
                Name = Truncate(viewName, 60),
                req.Payload,
                By = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
            });

        NotifyChanged("view-saved", projectId);
        return Ok(ApiResponse<bool>.Ok(true, $"Vista «{viewName}» salvata"));
    }

    [RequireProjectWritable]
    [HttpDelete("project/{projectId}/views/{name}")]
    public IActionResult DeleteView(int projectId, string name)
    {
        using var c = _db.Open();
        int n = c.Execute("DELETE FROM milestone_gantt_views WHERE project_id=@Pid AND name=@Name",
            new { Pid = projectId, Name = (name ?? "").Trim() });
        if (n == 0) return Ok(ApiResponse<bool>.Fail("Vista non trovata"));
        NotifyChanged("view-deleted", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Vista eliminata"));
    }

    // Precarico: copia (snapshot) le voci di catalogo scelte come milestone della commessa.
    // COPIA PER VALORE: si copia solo la descrizione; source_catalog_id è solo traccia inerte.
    [RequireProjectWritable]
    [HttpPost("project/{projectId}/seed-from-catalog")]
    [HttpPost("/api/projects/{projectId}/milestones/preload")]
    public IActionResult SeedFromCatalog(int projectId, [FromBody] MilestoneSeedRequest req)
    {
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        // Voci scelte (o tutte le attive se la lista è vuota), nell'ordine del catalogo.
        List<ActivityCatalogItem> voci = (req?.CatalogIds != null && req.CatalogIds.Count > 0)
            ? c.Query<ActivityCatalogItem>(@"
                SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
                FROM activity_catalog WHERE id IN @Ids ORDER BY sort_order, label",
                new { Ids = req.CatalogIds }).ToList()
            : c.Query<ActivityCatalogItem>(@"
                SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
                FROM activity_catalog WHERE is_active=TRUE ORDER BY sort_order, label").ToList();

        int sortOrder = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM project_milestones WHERE project_id=@Pid",
            new { Pid = projectId });

        int count = 0;
        foreach (ActivityCatalogItem v in voci)
        {
            c.Execute(@"
                INSERT INTO project_milestones (project_id, descrizione, sort_order, source_catalog_id, created_by)
                VALUES (@Pid, @Descrizione, @Sort, @Src, @CreatedBy)",
                new
                {
                    Pid = projectId,
                    Descrizione = v.Label,
                    Sort = sortOrder++,
                    Src = v.Id,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });
            count++;
        }
        NotifyChanged("seed", projectId);
        return Ok(ApiResponse<int>.Ok(count, $"{count} milestone precaricate"));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static int? ClampAvanz(int? a) => a == null ? null : (a < 0 ? 0 : (a > 100 ? 100 : a));
}
