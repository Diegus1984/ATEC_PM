using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly ProjectTemplateCopyService _templateCopy;
    private readonly ILogger<ProjectsController> _logger;
    private readonly IHubContext<ProjectHub> _hub;
    public ProjectsController(
        DbService db,
        NotificationService notif,
        ProjectTemplateCopyService templateCopy,
        ILogger<ProjectsController> logger,
        IHubContext<ProjectHub> hub)
    {
        _db = db;
        _notif = notif;
        _templateCopy = templateCopy;
        _logger = logger;
        _hub = hub;
    }

    // Notifica real-time: chi guarda QUESTA commessa (gruppo "project-{id}") + il Gestore DDP (gruppo globale
    // "ddp-all"), escludendo chi ha fatto la modifica (conn) per non auto-ricaricarsi.
    // ddpType ("COMMERCIAL" | "OFFICINA") permette ai client di ignorare la distinta che non li riguarda.
    private void NotifyDdpChange(int projectId, string? conn, string action, int itemId, string ddpType = "COMMERCIAL")
    {
        var payload = new DdpChange { ProjectId = projectId, Action = action, ItemId = itemId, DdpType = ddpType };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
        {
            IClientProxy target = string.IsNullOrEmpty(conn)
                ? _hub.Clients.Group(group)
                : _hub.Clients.GroupExcept(group, conn);
            _ = target.SendAsync("DdpChanged", payload);
        }
    }

    // Il sync DDP Officina → lavorazioni tocca project_work_requests: avvisa il Pannello
    // Lavorazioni (gruppo globale) e la griglia nel dettaglio commessa (stesso contratto
    // del WorkRequestsController).
    private void NotifyWorkRequestsChanged(string action, int projectId)
    {
        _ = _hub.Clients.Group(ProjectHub.WorkRequestsGroup)
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId });
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId });
    }

    // Notifica real-time ai client che guardano la commessa che i documenti (file/cartelle su disco)
    // sono cambiati: le viste aperte (albero a sinistra + elenco a destra) ricaricano in ~1s.
    // Scope per-commessa ("project-{id}"). Niente self-exclusion: l'autore ricarica già localmente,
    // un eventuale secondo refresh è innocuo (nessun editing live da interrompere).
    private void NotifyDocumentsChanged(int projectId, string action)
    {
        var payload = new DocumentsChange { ProjectId = projectId, Action = action };
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("DocumentsChanged", payload);
    }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    [HttpGet]
    public IActionResult GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 0, [FromQuery] string? search = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);

            string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            string searchClause = term == null
                ? ""
                : @" WHERE (p.code LIKE @Term OR p.title LIKE @Term OR cu.company_name LIKE @Term
                    OR CONCAT(e.first_name,' ',e.last_name) LIKE @Term)";
            object countParams = term == null ? new { } : new { Term = $"%{term}%" };
            object listParams = term == null
                ? new { Limit = pageSize, Offset = offset }
                : new { Term = $"%{term}%", Limit = pageSize, Offset = offset };

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($@"
                SELECT COUNT(*)
                FROM projects p
                LEFT JOIN customers cu ON cu.id = p.customer_id
                LEFT JOIN employees e ON e.id = p.pm_id{searchClause}", countParams);

            var rows = c.Query<ProjectListItem>($@"
            SELECT p.id, p.code, p.title, 
                   COALESCE(cu.company_name, 'CLIENTE MANCANTE') AS CustomerName,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), 'NON ASSEGNATO') AS PmName,
                   p.status, p.priority, p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.revenue, p.budget_hours_total AS BudgetHoursTotal,
                   COALESCE((SELECT q.id FROM quotes q WHERE q.project_id = p.id LIMIT 1), 0) AS LinkedQuoteId
            FROM projects p
            LEFT JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN employees e ON e.id = p.pm_id{searchClause}
            ORDER BY p.created_at DESC
            LIMIT @Limit OFFSET @Offset", listParams).ToList();

            int loaded = (page - 1) * pageSize + rows.Count;
            var result = new PagedResult<ProjectListItem>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total
            };
            return Ok(ApiResponse<PagedResult<ProjectListItem>>.Ok(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<ProjectListItem>>.Fail($"Errore DB: {ex.Message}"));
        }
    }

    [HttpGet("tree")]
    public IActionResult GetTree()
    {
        try
        {
            using var c = _db.Open();
            List<ProjectTreeItemDto> rows = c.Query<ProjectTreeItemDto>(@"
                SELECT p.id, p.code, p.title,
                       p.status,
                       COALESCE(cu.company_name, '') AS CustomerName
                FROM projects p
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE p.status <> 'CANCELLED'
                ORDER BY p.code DESC").ToList();
            return Ok(ApiResponse<List<ProjectTreeItemDto>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<ProjectTreeItemDto>>.Fail($"Errore DB: {ex.Message}"));
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<ProjectSaveRequest>(@"
            SELECT id, code, title, customer_id AS CustomerId, pm_id AS PmId, description,
                   start_date AS StartDate, end_date_planned AS EndDatePlanned,
                   budget_total AS BudgetTotal, budget_hours_total AS BudgetHoursTotal,
                   revenue, status, priority, server_path AS ServerPath, notes,
                   COALESCE((SELECT q.id FROM quotes q WHERE q.project_id = p.id LIMIT 1), 0) AS LinkedQuoteId
            FROM projects p WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<ProjectSaveRequest>.Ok(proj));
    }

    [HttpPost]
    public IActionResult Create([FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();
        using var trx = c.BeginTransaction();
        int newId;
        try
        {
            newId = c.ExecuteScalar<int>(@"
        INSERT INTO projects (code,title,customer_id,pm_id,description,start_date,end_date_planned,budget_total,budget_hours_total,revenue,status,priority,server_path,notes)
        VALUES (@Code,@Title,@CustomerId,@PmId,@Description,@StartDate,@EndDatePlanned,@BudgetTotal,@BudgetHoursTotal,@Revenue,@Status,@Priority,@ServerPath,@Notes);
        SELECT LAST_INSERT_ID()", req, trx);

            // Crea fasi di default
            if (req.CreateDefaultPhases)
            {
                var templates = c.Query("SELECT id, department_id, sort_order FROM phase_templates WHERE is_default=1 ORDER BY sort_order", transaction: trx);
                foreach (var t in templates)
                {
                    c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, department_id, sort_order)
                    VALUES (@ProjId, @TplId, @DeptId, @Sort)",
                        new { ProjId = newId, TplId = (int)t.id, DeptId = (int?)t.department_id, Sort = (int)t.sort_order }, trx);
                }
            }

            trx.Commit();
        }
        catch
        {
            trx.Rollback();
            throw;
        }

        // Dopo il commit — operazioni non transazionali
        try
        {
            _templateCopy.CopyToProject(req.Code);

            string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
            string year = DateTime.Now.Year.ToString();
            string fullPath = Path.Combine(basePath, year, req.Code);
            c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = newId });
        }
        catch (Exception ex)
        {
            // Log ma non fallire — la commessa è già creata nel DB
            Console.WriteLine($"[Projects] Warning: errore post-creazione commessa {req.Code}: {ex.Message}");
        }

        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;

        // Leggi stato precedente per confronto
        string oldStatus = c.ExecuteScalar<string?>(
            "SELECT status FROM projects WHERE id=@Id", new { Id = id }) ?? "";

        c.Execute(@"UPDATE projects SET code=@Code,title=@Title,customer_id=@CustomerId,pm_id=@PmId,
            description=@Description,start_date=@StartDate,end_date_planned=@EndDatePlanned,
            budget_total=@BudgetTotal,budget_hours_total=@BudgetHoursTotal,revenue=@Revenue,
            status=@Status,priority=@Priority,server_path=@ServerPath,notes=@Notes WHERE id=@Id", req);

        // Notifica a tutti i dipendenti se la commessa cambia stato operativo
        if (oldStatus != req.Status && req.Status is "ACTIVE" or "ON_HOLD" or "CANCELLED")
        {
            NotifyProjectStatusChange(id, req.Code, req.Status);
        }

        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpPatch("{id}/revenue")]
    public IActionResult UpdateRevenue(int id, [FromBody] decimal value)
    {
        using var c = _db.Open();
        c.Execute("UPDATE projects SET revenue=@Val WHERE id=@Id", new { Val = value, Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        string projCode = c.ExecuteScalar<string?>(
            "SELECT code FROM projects WHERE id=@Id", new { Id = id }) ?? "";
        c.Execute("UPDATE projects SET status='CANCELLED' WHERE id=@Id", new { Id = id });
        NotifyProjectStatusChange(id, projCode, "CANCELLED");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private void NotifyProjectStatusChange(int projectId, string projectCode, string newStatus)
    {
        try
        {
            string label = newStatus switch
            {
                "ACTIVE" => "ATTIVA",
                "ON_HOLD" => "SOSPESA",
                "CANCELLED" => "ANNULLATA",
                _ => newStatus
            };
            string severity = newStatus == "ACTIVE" ? "INFO" : "WARNING";
            string message = newStatus == "ACTIVE"
                ? $"La commessa {projectCode} e' ora attiva. Le attivita' assegnate sono operative."
                : $"La commessa {projectCode} e' in stato {label}. Tutte le attivita' sono sospese.";

            int currentEmpId = GetCurrentEmployeeId();
            List<int> recipients = _notif.GetProjectEmployeeIds(projectId);
            recipients.Remove(currentEmpId);
            if (recipients.Count == 0) return;

            _notif.Create("PROJECT_STATUS", severity,
                $"Commessa {projectCode} - {label}",
                message,
                "PROJECT", projectId, projectId, currentEmpId,
                recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Errore durante l'invio delle notifiche per cambio stato commessa {ProjectCode}", projectCode);
        }
    }

    /// <summary>
    /// DELETE /api/projects/{id}/hard — Cancellazione definitiva: DB (CASCADE) + cartelle + ripristino offerta.
    /// </summary>
    [HttpDelete("{id}/hard")]
    public IActionResult HardDelete(int id)
    {
        try
        {
            using var c = _db.Open();
            var proj = c.QueryFirstOrDefault<dynamic>("SELECT id, code, server_path FROM projects WHERE id=@Id", new { Id = id });
            if (proj == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));

            string projectCode = (string)proj.code;
            string? serverPath = (string?)proj.server_path;

            using var tx = c.BeginTransaction();

            // 1. Cancella notifiche collegate
            c.Execute(@"
                DELETE nr FROM notification_recipients nr
                JOIN notifications n ON n.id = nr.notification_id
                WHERE n.project_id = @Pid", new { Pid = id }, tx);
            c.Execute("DELETE FROM notifications WHERE project_id = @Pid", new { Pid = id }, tx);

            // 3. Elimina tabelle con FK non-CASCADE sulle fasi
            c.Execute(@"
                DELETE te FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                WHERE pp.project_id = @Pid", new { Pid = id }, tx);

            c.Execute(@"
                DELETE pa FROM phase_assignments pa
                JOIN project_phases pp ON pp.id = pa.project_phase_id
                WHERE pp.project_id = @Pid", new { Pid = id }, tx);

            // 4. Ripristina preventivi collegati (da "converted" → "accepted", azzera link commessa)
            c.Execute(@"UPDATE quotes SET status='accepted', project_id=NULL, converted_at=NULL
                        WHERE project_id=@Pid AND status='converted'", new { Pid = id }, tx);

            // 5. DELETE progetto (FK CASCADE elimina fasi, bom, costing, pricing, cashflow, chat, docs)
            c.Execute("DELETE FROM projects WHERE id = @Id", new { Id = id }, tx);

            tx.Commit();

            // 4. Cancella cartelle fisiche (dopo commit, non critico)
            try
            {
                if (!string.IsNullOrEmpty(serverPath) && Directory.Exists(serverPath))
                    Directory.Delete(serverPath, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Projects] Warning: impossibile cancellare cartella {serverPath}: {ex.Message}");
            }

            return Ok(ApiResponse<bool>.Ok(true, $"Commessa {projectCode} eliminata definitivamente"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    // --- FASI ---
    [HttpGet("{id}/phases")]
    public IActionResult GetPhases(int id)
    {
        using var c = _db.Open();
        var rows = c.Query<PhaseListItem>(@"
            SELECT pp.id, pp.custom_name AS Name, pp.budget_hours AS BudgetHours,
                   pp.budget_cost AS BudgetCost, pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   COALESCE(SUM(te.hours),0) AS HoursWorked
            FROM project_phases pp
            LEFT JOIN timesheet_entries te ON te.project_phase_id = pp.id
            WHERE pp.project_id = @Id
            GROUP BY pp.id
            ORDER BY pp.sort_order", new { Id = id }).ToList();
        return Ok(ApiResponse<List<PhaseListItem>>.Ok(rows));
    }

    // --- CODICE AUTO ---
    [HttpGet("next-code")]
    public IActionResult NextCode()
    {
        using var c = _db.Open();
        var year = DateTime.Now.Year;
        var prefix = $"AT{year}";
        // Cerchiamo l'ultimo numero progressivo per l'anno in corso
        var maxCode = c.ExecuteScalar<string>("SELECT MAX(code) FROM projects WHERE code LIKE @Pref", new { Pref = prefix + "%" });

        int next = 1;
        if (!string.IsNullOrEmpty(maxCode) && maxCode.Length > prefix.Length)
        {
            var suffix = maxCode.Replace(prefix, "");
            if (int.TryParse(suffix, out var n))
                next = n + 1;
        }
        return Ok(ApiResponse<string>.Ok($"{prefix}{next:D3}"));
    }

    // --- FILE SYSTEM ---
    [HttpPost("{id}/create-folder")]
    public IActionResult CreateFolder(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<dynamic>("SELECT code, server_path FROM projects WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound();

        string code = (string)proj.code;
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        string year = DateTime.Now.Year.ToString();
        string fullPath = Path.Combine(basePath, year, code);

        if (!Directory.Exists(fullPath))
        {
            _templateCopy.CopyToProject(code);
        }

        c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = id });
        NotifyDocumentsChanged(id, "create");
        return Ok(ApiResponse<string>.Ok(fullPath));
    }

    [HttpGet("{id}/files")]
    public IActionResult GetFiles(int id, [FromQuery] string? subPath)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });

        if (string.IsNullOrEmpty(serverPath))
            return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        // 1. Validazione del percorso di destinazione (Sicurezza)
        var targetPath = serverPath;
        if (!string.IsNullOrEmpty(subPath))
        {
            // Path.Combine pulisce automaticamente eventuali problemi di slash
            targetPath = Path.GetFullPath(Path.Combine(serverPath, subPath));

            // CONTROLLO DI SICUREZZA: Impedisce il "Path Traversal"
            // Verifica che il percorso risultante sia ancora all'interno di serverPath
            if (!targetPath.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<string>.Fail("Accesso negato al percorso specificate fuori dalla root di progetto."));
            }
        }

        if (!Directory.Exists(targetPath))
            return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        var items = new List<FileItem>();

        try
        {
            // 2. Lettura Directory
            foreach (var dir in Directory.GetDirectories(targetPath).OrderBy(d => d))
            {
                var di = new DirectoryInfo(dir);
                if (di.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new FileItem
                {
                    Name = di.Name,
                    IsFolder = true,
                    // Usiamo Replace per uniformare gli slash per il web/client
                    RelativePath = Path.GetRelativePath(serverPath, dir).Replace("\\", "/")
                });
            }

            // 3. Lettura File
            foreach (var file in Directory.GetFiles(targetPath).OrderBy(f => f))
            {
                var fi = new FileInfo(file);
                items.Add(new FileItem
                {
                    Name = fi.Name,
                    IsFolder = false,
                    Size = fi.Length,
                    RelativePath = Path.GetRelativePath(serverPath, file).Replace("\\", "/"),
                    Modified = fi.LastWriteTime
                });
            }

            return Ok(ApiResponse<List<FileItem>>.Ok(items));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<FileItem>>.Fail($"Errore lettura file: {ex.Message}"));
        }
    }

    [HttpGet("{id}/file-tree")]
    public IActionResult GetFileTree(int id)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath) || !Directory.Exists(serverPath))
            return Ok(ApiResponse<List<FileTreeItem>>.Ok(new()));

        var tree = BuildFileTree(serverPath, serverPath);
        return Ok(ApiResponse<List<FileTreeItem>>.Ok(tree));
    }

    private List<FileTreeItem> BuildFileTree(string rootPath, string currentPath)
    {
        var items = new List<FileTreeItem>();

        foreach (var dir in Directory.GetDirectories(currentPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            if (di.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)) continue;

            var node = new FileTreeItem
            {
                Name = di.Name,
                IsFolder = true,
                RelativePath = Path.GetRelativePath(rootPath, dir),
                Children = BuildFileTree(rootPath, dir)
            };
            items.Add(node);
        }
        foreach (var file in Directory.GetFiles(currentPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            items.Add(new FileTreeItem
            {
                Name = fi.Name,
                IsFolder = false,
                Size = fi.Length,
                RelativePath = Path.GetRelativePath(rootPath, file),
                Modified = fi.LastWriteTime
            });
        }

        return items;
    }

    // --- DOWNLOAD FILE ---
    [HttpGet("{id}/download")]
    public IActionResult DownloadFile(int id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest("Path richiesto");

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return NotFound("Cartella commessa non trovata");

        var fullPath = Path.Combine(serverPath, path);

        // Sicurezza: verifica che il path sia dentro la cartella commessa
        var normalizedFull = Path.GetFullPath(fullPath);
        var normalizedRoot = Path.GetFullPath(serverPath);
        if (!normalizedFull.StartsWith(normalizedRoot))
            return BadRequest("Path non valido");

        if (!System.IO.File.Exists(fullPath))
            return NotFound("File non trovato");

        var fileName = Path.GetFileName(fullPath);
        var ext = Path.GetExtension(fileName).ToLower();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".ogv" => "video/ogg",
            ".wmv" => "video/x-ms-wmv",
            ".dwg" => "application/acad",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".msg" => "application/vnd.ms-outlook",
            ".eml" => "message/rfc822",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, fileName);
    }


    // --- PREVIEW EXCEL/CSV → HTML ---
    [HttpGet("{id}/preview")]
    public IActionResult PreviewFile(int id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest("Path richiesto");

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return NotFound("Cartella non trovata");

        var fullPath = Path.GetFullPath(Path.Combine(serverPath, path));
        var normalizedRoot = Path.GetFullPath(serverPath);
        if (!fullPath.StartsWith(normalizedRoot)) return BadRequest("Path non valido");
        if (!System.IO.File.Exists(fullPath)) return NotFound("File non trovato");

        var ext = Path.GetExtension(fullPath).ToLower();
        if (ext is not (".xlsx" or ".xls" or ".csv" or ".docx" or ".eml" or ".msg")) return BadRequest("Tipo non supportato");

        try
        {
            var fileName = Path.GetFileName(fullPath);

            // === EMAIL (.eml / .msg) ===
            if (ext is ".eml" or ".msg")
                return Content(EmailPreviewService.RenderHtml(fullPath), "text/html");

            var sb = new System.Text.StringBuilder();

            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
        * { margin:0; padding:0; box-sizing:border-box; }
        body { font-family:Segoe UI,sans-serif; font-size:13px; background:#F7F8FA; padding:12px; }
        .info { padding:8px 12px; background:#fff; border:1px solid #E4E7EC; margin-bottom:8px; font-weight:600; }
        .tabs { display:flex; gap:2px; margin-bottom:8px; }
        .tab { padding:6px 16px; background:#fff; border:1px solid #E4E7EC; cursor:pointer; font-size:12px; }
        .tab.active { background:#4F6EF7; color:#fff; border-color:#4F6EF7; }
        .sheet { display:none; }
        .sheet.active { display:block; }
        table { width:100%; border-collapse:collapse; background:#fff; border:1px solid #E4E7EC; }
        th { background:#F7F8FA; font-weight:600; font-size:12px; text-align:left;
             padding:6px 10px; border:1px solid #E4E7EC; position:sticky; top:0; }
        td { padding:5px 10px; border:1px solid #F3F4F6; font-size:12px; white-space:nowrap; }
        tr:hover td { background:#f0f4ff; }
        .doc-content { background:#fff; border:1px solid #E4E7EC; padding:24px; line-height:1.6; }
        .doc-content h1 { font-size:20px; margin:16px 0 8px; }
        .doc-content h2 { font-size:17px; margin:14px 0 6px; }
        .doc-content h3 { font-size:15px; margin:12px 0 6px; }
        .doc-content p { margin:6px 0; }
        .doc-content table { margin:12px 0; }
        .doc-content ul, .doc-content ol { margin:8px 0 8px 24px; }
    </style></head><body>");

            // === WORD ===
            if (ext is ".doc" or ".docx")
            {
                sb.Append($"<div class='info'>📘 {System.Web.HttpUtility.HtmlEncode(fileName)}</div>");
                using var docStream = System.IO.File.OpenRead(fullPath);
                var converter = new Mammoth.DocumentConverter();
                var result = converter.ConvertToHtml(docStream);
                sb.Append($"<div class='doc-content'>{result.Value}</div>");
                sb.Append("</body></html>");
                return Content(sb.ToString(), "text/html");
            }

            sb.Append($"<div class='info'>📗 {System.Web.HttpUtility.HtmlEncode(fileName)}</div>");

            // === CSV ===
            if (ext == ".csv")
            {
                var lines = System.IO.File.ReadAllLines(fullPath);
                sb.Append("<table><thead><tr>");
                if (lines.Length > 0)
                {
                    var sep = lines[0].Contains(';') ? ';' : ',';
                    var headers = lines[0].Split(sep);
                    foreach (var h in headers)
                        sb.Append($"<th>{System.Web.HttpUtility.HtmlEncode(h.Trim().Trim('"'))}</th>");
                    sb.Append("</tr></thead><tbody>");
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i])) continue;
                        sb.Append("<tr>");
                        foreach (var cell in lines[i].Split(sep))
                            sb.Append($"<td>{System.Web.HttpUtility.HtmlEncode(cell.Trim().Trim('"'))}</td>");
                        sb.Append("</tr>");
                    }
                }
                sb.Append("</tbody></table>");
            }
            // === EXCEL ===
            else
            {
                using var package = new ExcelPackage(new FileInfo(fullPath));
                var sheets = package.Workbook.Worksheets;

                if (sheets.Count > 1)
                {
                    sb.Append("<div class='tabs'>");
                    for (int s = 0; s < sheets.Count; s++)
                        sb.Append($"<div class='tab{(s == 0 ? " active" : "")}' onclick='showSheet({s})'>{System.Web.HttpUtility.HtmlEncode(sheets[s].Name)}</div>");
                    sb.Append("</div>");
                }

                for (int s = 0; s < sheets.Count; s++)
                {
                    var ws = sheets[s];
                    sb.Append($"<div class='sheet{(s == 0 ? " active" : "")}' id='s{s}'>");

                    if (ws.Dimension == null)
                    {
                        sb.Append("<p>Foglio vuoto</p></div>");
                        continue;
                    }

                    int startRow = ws.Dimension.Start.Row;
                    int dimEndRow = ws.Dimension.End.Row;
                    int startCol = ws.Dimension.Start.Column;
                    int dimEndCol = ws.Dimension.End.Column;

                    int endCol = startCol;
                    int scanRows = Math.Min(dimEndRow, 50);
                    for (int col = Math.Min(dimEndCol, 200); col >= startCol; col--)
                    {
                        bool found = false;
                        for (int row = startRow; row <= scanRows; row++)
                        {
                            if (!string.IsNullOrEmpty(ws.Cells[row, col].Text))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found) { endCol = col; break; }
                    }

                    int endRow = startRow;
                    for (int row = dimEndRow; row >= startRow; row--)
                    {
                        bool hasData = false;
                        for (int col = startCol; col <= endCol; col++)
                        {
                            if (!string.IsNullOrEmpty(ws.Cells[row, col].Text))
                            {
                                hasData = true;
                                break;
                            }
                        }
                        if (hasData) { endRow = row; break; }
                    }

                    endRow = Math.Min(endRow, startRow + 500);
                    endCol = Math.Min(endCol, startCol + 50);

                    sb.Append("<table>");

                    var mergeMap = new Dictionary<string, (int rowSpan, int colSpan)>();
                    var skipCells = new HashSet<string>();

                    foreach (var merge in ws.MergedCells)
                    {
                        if (merge == null) continue;
                        var addr = new ExcelAddress(merge);
                        int mr1 = addr.Start.Row, mc1 = addr.Start.Column;
                        int mr2 = addr.End.Row, mc2 = addr.End.Column;
                        mergeMap[$"{mr1},{mc1}"] = (mr2 - mr1 + 1, mc2 - mc1 + 1);
                        for (int r = mr1; r <= mr2; r++)
                            for (int cc = mc1; cc <= mc2; cc++)
                                if (r != mr1 || cc != mc1)
                                    skipCells.Add($"{r},{cc}");
                    }

                    for (int row = startRow; row <= endRow; row++)
                    {
                        sb.Append(row == startRow ? "<thead><tr>" : "<tr>");

                        for (int col = startCol; col <= endCol; col++)
                        {
                            var key = $"{row},{col}";
                            if (skipCells.Contains(key)) continue;

                            var cell = ws.Cells[row, col];
                            var style = cell.Style;
                            var cssStyle = new System.Text.StringBuilder();

                            if (style.Fill.PatternType != OfficeOpenXml.Style.ExcelFillStyle.None &&
                                !string.IsNullOrEmpty(style.Fill.BackgroundColor?.Rgb))
                            {
                                var rgb = style.Fill.BackgroundColor.Rgb;
                                if (rgb.Length == 8) rgb = rgb.Substring(2);
                                cssStyle.Append($"background:#{rgb};");
                            }

                            if (!string.IsNullOrEmpty(style.Font.Color?.Rgb))
                            {
                                var rgb = style.Font.Color.Rgb;
                                if (rgb.Length == 8) rgb = rgb.Substring(2);
                                cssStyle.Append($"color:#{rgb};");
                            }

                            if (style.Font.Bold) cssStyle.Append("font-weight:700;");
                            if (style.Font.Italic) cssStyle.Append("font-style:italic;");
                            if (style.Font.Size > 0) cssStyle.Append($"font-size:{style.Font.Size}px;");

                            if (style.HorizontalAlignment == OfficeOpenXml.Style.ExcelHorizontalAlignment.Center)
                                cssStyle.Append("text-align:center;");
                            else if (style.HorizontalAlignment == OfficeOpenXml.Style.ExcelHorizontalAlignment.Right)
                                cssStyle.Append("text-align:right;");

                            var val = cell.Text ?? "";
                            var tag = row == startRow ? "th" : "td";
                            var attrs = new System.Text.StringBuilder();
                            if (cssStyle.Length > 0) attrs.Append($" style='{cssStyle}'");
                            if (mergeMap.TryGetValue(key, out var span))
                            {
                                if (span.rowSpan > 1) attrs.Append($" rowspan='{span.rowSpan}'");
                                if (span.colSpan > 1) attrs.Append($" colspan='{span.colSpan}'");
                            }

                            sb.Append($"<{tag}{attrs}>{System.Web.HttpUtility.HtmlEncode(val)}</{tag}>");
                        }

                        sb.Append(row == startRow ? "</tr></thead><tbody>" : "</tr>");
                    }

                    sb.Append("</tbody></table></div>");
                }

                if (sheets.Count > 1)
                {
                    sb.Append(@"<script>
        function showSheet(idx) {
            document.querySelectorAll('.sheet').forEach(s => s.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.getElementById('s'+idx).classList.add('active');
            document.querySelectorAll('.tab')[idx].classList.add('active');
        }
    </script>");
                }
            }

            sb.Append("</body></html>");
            return Content(sb.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            return Content($"<html><body><p style='color:red'>Errore: {System.Web.HttpUtility.HtmlEncode(ex.Message)}</p></body></html>", "text/html");
        }
    }


    // --- LOOKUP ---
    [HttpGet("/api/lookup/customers")]
    public IActionResult LookupCustomers()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>("SELECT id AS Id, company_name AS Name FROM customers WHERE is_active=1 ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("/api/lookup/employees")]
    public IActionResult LookupEmployees([FromQuery] string? role = null)
    {
        using var c = _db.Open();
        string sql = "SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status='ACTIVE'";
        if (!string.IsNullOrEmpty(role))
        {
            sql += " AND user_role=@Role";
        }
        sql += " ORDER BY last_name";
        var rows = c.Query<LookupItem>(sql, new { Role = role }).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }
    [HttpGet("template-structure")]
    public IActionResult GetTemplateStructure()
    {
        using MySqlConnector.MySqlConnection c = _db.Open();

        List<(int Id, int? ParentId, string Name)> folderList = c.Query<(int Id, int? ParentId, string Name)>(
            "SELECT id, parent_id, name FROM project_template_folders WHERE is_active=1").ToList();

        var result = new TemplateFolderInfo();
        if (folderList.Count == 0)
            return Ok(ApiResponse<TemplateFolderInfo>.Ok(result));

        Dictionary<int, string> folderPaths = new();
        HashSet<int> unresolved = folderList.Select(f => f.Id).ToHashSet();

        while (unresolved.Count > 0)
        {
            int resolvedThisPass = 0;
            foreach ((int Id, int? ParentId, string Name) f in folderList.Where(x => unresolved.Contains(x.Id)))
            {
                if (f.ParentId == null)
                {
                    folderPaths[f.Id] = f.Name;
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
                else if (folderPaths.TryGetValue(f.ParentId.Value, out string? parentPath))
                {
                    folderPaths[f.Id] = Path.Combine(parentPath, f.Name);
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
            }
            if (resolvedThisPass == 0)
                break;
        }

        result.Folders = folderPaths.Values
            .OrderBy(p => p.Length)
            .Select(p => p.Replace("\\", "/"))
            .ToList();

        IEnumerable<(int folder_id, string file_name, long file_size)> files = c.Query<(int folder_id, string file_name, long file_size)>(
            "SELECT folder_id, file_name, file_size FROM project_template_files");

        foreach ((int folder_id, string file_name, long file_size) tf in files)
        {
            if (!folderPaths.TryGetValue(tf.folder_id, out string? relFolder))
                continue;

            result.Files.Add(new TemplateFileInfo
            {
                RelativePath = Path.Combine(relFolder, tf.file_name).Replace("\\", "/"),
                FileName = tf.file_name,
                SizeBytes = tf.file_size
            });
        }

        return Ok(ApiResponse<TemplateFolderInfo>.Ok(result));
    }

    // --- DDP (Distinta Di Produzione) ---
    [HttpGet("{id}/ddp")]
    public IActionResult GetDdpItems(int id, [FromQuery] string type = "COMMERCIAL")
    {
        try
        {
            using var c = _db.Open();
            // COALESCE su tutte le colonne testo nullable: righe storiche/importate possono
            // avere NULL (lo schema lo permette) e un null manderebbe in crash le combo web.
            var rows = c.Query<BomItemListItem>(@"
            SELECT b.id, b.project_id AS ProjectId, b.catalog_item_id AS CatalogItemId,
                   COALESCE(b.part_number,'') AS PartNumber, COALESCE(b.description,'') AS Description,
                   COALESCE(b.unit,'') AS Unit, b.quantity,
                   b.unit_cost AS UnitCost,
                   b.supplier_id AS SupplierId,
                   COALESCE(s.company_name, '') AS SupplierName,
                   COALESCE(b.manufacturer,'') AS Manufacturer, COALESCE(b.item_status,'') AS ItemStatus,
                   COALESCE(b.requested_by,'') AS RequestedBy, COALESCE(b.danea_ref,'') AS DaneaRef,
                   b.danea_order_iddoc AS DaneaOrderIdDoc,
                   b.date_needed AS DateNeeded, COALESCE(b.destination,'') AS Destination,
                   b.destination_spec AS DestinationSpec, COALESCE(b.notes,'') AS Notes,
                   -- Codice ATEC effettivo: snapshot di riga, altrimenti mapping vivo dell'articolo.
                   b.ddp_type AS DdpType, COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS AtecCode,
                   b.created_at AS CreatedAt, b.updated_at AS UpdatedAt
            FROM bom_items b
            LEFT JOIN suppliers s ON s.id = b.supplier_id
            LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
            WHERE b.project_id = @Id AND b.ddp_type = @Type
            ORDER BY b.id", new { Id = id, Type = type }).ToList();
            foreach (BomItemListItem row in rows)
                if (row.AtecCode.Length > 0)
                    row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);

            return Ok(ApiResponse<List<BomItemListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<BomItemListItem>>.Fail(ex.Message));
        }
    }

    [HttpPost("{id}/ddp")]
    public IActionResult AddDdpItem(int id, [FromBody] BomItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            req.ProjectId = id;

            // Finestra di partenza (riga INIZIO della matrice, per tipo): sulla commerciale
            // esclude DC — il materiale commerciale si acquista, non si costruisce.
            string? startError = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeCommercial, null, req.ItemStatus);
            if (startError != null)
                return BadRequest(ApiResponse<int>.Fail(startError));

            // Normalizza atec_code senza punti (formato DB Codex).
            req.AtecCode = (req.AtecCode ?? "").Replace(".", "").Trim();

            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO bom_items
                (project_id, catalog_item_id, part_number, description, unit, quantity,
                 unit_cost, supplier_id, manufacturer, item_status, requested_by,
                 danea_ref, date_needed, destination, destination_spec, notes, ddp_type,
                 atec_code, updated_at)
            VALUES
                (@ProjectId, @CatalogItemId, @PartNumber, @Description, @Unit, @Quantity,
                 @UnitCost, @SupplierId, @Manufacturer, @ItemStatus, @RequestedBy,
                 @DaneaRef, @DateNeeded, @Destination, @DestinationSpec, @Notes, @DdpType,
                 NULLIF(@AtecCode,''), NOW());
            SELECT LAST_INSERT_ID()", req);

            NotifyDdpChange(id, conn, "create", newId);
            return Ok(ApiResponse<int>.Ok(newId, "Aggiunto"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail(ex.Message));
        }
    }

    [HttpPut("{id}/ddp/{itemId}")]
    public IActionResult UpdateDdpItem(int id, int itemId, [FromBody] BomItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();

            // Concorrenza ottimistica (rete di sicurezza anche col real-time): se il client invia
            // la versione vista (ExpectedUpdatedAt) e la riga è cambiata nel frattempo → 409, niente lost update.
            if (req.ExpectedUpdatedAt.HasValue)
            {
                DateTime? current = c.ExecuteScalar<DateTime?>(
                    "SELECT updated_at FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                    new { ItemId = itemId, Id = id });
                if (current.HasValue && Math.Abs((current.Value - req.ExpectedUpdatedAt.Value).TotalSeconds) > 1)
                    return Conflict(ApiResponse<DateTime?>.Fail(
                        "Riga modificata da un altro utente nel frattempo. Ricarica e riprova."));
            }

            // Leggi stato e quantità precedenti per confronto
            (string? ItemStatus, decimal Quantity) before = c.QueryFirstOrDefault<(string?, decimal)>(
                "SELECT item_status, quantity FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            string? oldStatus = before.ItemStatus;

            // Matrice degli avanzamenti di stato (v7, tipo COMMERCIAL): il server rifiuta le
            // transizioni non ammesse (la UI mostra solo quelle valide, ma qui si coprono
            // client vecchi e modifiche concorrenti).
            string? transitionError = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeCommercial, oldStatus, req.ItemStatus);
            if (transitionError != null)
                return BadRequest(ApiResponse<DateTime?>.Fail(transitionError));

            // Quantità modificabile in VER (ingresso) o DO (da ordinare), o tornando in uno
            // di questi nella stessa save.
            if (req.Quantity != before.Quantity
                && !IsCommercialQtyEditable(oldStatus)
                && !IsCommercialQtyEditable(req.ItemStatus))
            {
                return BadRequest(ApiResponse<DateTime?>.Fail(
                    "La quantità è modificabile solo in stato Verificare magazzino o Da Ordinare."));
            }

            req.Id = itemId;
            req.ProjectId = id;
            req.AtecCode = (req.AtecCode ?? "").Replace(".", "").Trim();
            c.Execute(@"
            UPDATE bom_items SET
                quantity = @Quantity, item_status = @ItemStatus,
                -- Rif. Danea cambiato/svuotato a mano: il link all'ordine generato non è più
                -- affidabile → si azzera (valutato PRIMA dell'assegnazione di danea_ref: le
                -- SET di MySQL sono applicate in ordine, qui danea_ref è ancora quello vecchio).
                danea_order_iddoc = IF(COALESCE(danea_ref,'') <> @DaneaRef, NULL, danea_order_iddoc),
                danea_ref = @DaneaRef, date_needed = @DateNeeded,
                destination = @Destination, destination_spec = @DestinationSpec,
                supplier_id = IF(@UpdateSupplier OR @UpdateCatalogSnapshot, @SupplierId, supplier_id),
                catalog_item_id = IF(@UpdateCatalogSnapshot, @CatalogItemId, catalog_item_id),
                part_number = IF(@UpdateCatalogSnapshot, @PartNumber, part_number),
                description = IF(@UpdateCatalogSnapshot, @Description, description),
                unit = IF(@UpdateCatalogSnapshot, @Unit, unit),
                unit_cost = IF(@UpdateCatalogSnapshot, @UnitCost, unit_cost),
                manufacturer = IF(@UpdateCatalogSnapshot, @Manufacturer, manufacturer),
                atec_code = IF(@UpdateCatalogSnapshot OR LENGTH(@AtecCode) > 0,
                               NULLIF(@AtecCode,''), atec_code),
                notes = @Notes, updated_at = NOW()
            WHERE id = @Id AND project_id = @ProjectId", req);

            // Trigger notifica se lo stato è cambiato (solo se commessa ACTIVE)
            string projStatus = c.ExecuteScalar<string?>(
                "SELECT status FROM projects WHERE id = @Id", new { Id = id }) ?? "";
            if (!string.IsNullOrEmpty(oldStatus) && oldStatus != req.ItemStatus && projStatus == "ACTIVE")
            {
                try
                {
                    string partNum = c.ExecuteScalar<string?>(
                        "SELECT part_number FROM bom_items WHERE id = @Id", new { Id = itemId }) ?? "";
                    string descr = c.ExecuteScalar<string?>(
                        "SELECT description FROM bom_items WHERE id = @Id", new { Id = itemId }) ?? "";
                    string projCode = c.ExecuteScalar<string?>(
                        "SELECT code FROM projects WHERE id = @Id", new { Id = id }) ?? "";
                    int currentEmpId = GetCurrentEmployeeId();

                    string severity = req.ItemStatus switch
                    {
                        "DISP" => "SUCCESS",  // DISPONIBILE / CONSEGNATO (chiusura positiva v7)
                        "ANN" => "WARNING",   // ANNULLATO
                        "IO" => "INFO",       // IN ORDINE
                        _ => "INFO"
                    };

                    string title = $"Cambio stato DDP — {projCode}";
                    string msg = $"Stato modificato da {DdpStatusMap.ToLabel(oldStatus)} a {DdpStatusMap.ToLabel(req.ItemStatus)}";

                    List<int> recipients = _notif.GetProjectPmIds(id);
                    recipients.AddRange(_notif.GetAcqEmployeeIds());
                    recipients.Remove(currentEmpId);

                    if (recipients.Count > 0)
                        _notif.Create("DDP_STATUS_CHANGED", severity, title, msg, "BOM", itemId, id, currentEmpId, recipients);
                }
                catch { /* non bloccare l'update per errore notifica */ }
            }

            // Real-time: avvisa gli altri che guardano la distinta di questa commessa.
            NotifyDdpChange(id, conn, "update", itemId);

            // Ritorna la nuova versione così il client riallinea il proprio token di concorrenza.
            DateTime? newTs = c.ExecuteScalar<DateTime?>(
                "SELECT updated_at FROM bom_items WHERE id = @Id", new { Id = itemId });
            return Ok(ApiResponse<DateTime?>.Ok(newTs, "Aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DateTime?>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}/ddp/{itemId}")]
    public IActionResult DeleteDdpItem(int id, int itemId, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            c.Execute("DELETE FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            NotifyDdpChange(id, conn, "delete", itemId);
            return Ok(ApiResponse<bool>.Ok(true, "Eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    // --- DDP OFFICINA (distinta particolari meccanici, tabella dedicata ddp_officina_items) ---
    // Stesso contratto della DDP commerciale (concorrenza ottimistica, real-time, notifiche cambio stato),
    // ma campi del template officina: Codice 101, Materiale, Trattamento, fornitore testuale.
    [HttpGet("{id}/ddp-officina")]
    public IActionResult GetOfficinaItems(int id)
    {
        try
        {
            using var c = _db.Open();
            // COALESCE su tutte le colonne testo nullable: righe storiche/importate possono
            // avere NULL (lo schema lo permette) e un null manderebbe in crash le combo web.
            var rows = c.Query<OfficinaItemListItem>(@"
            SELECT id, project_id AS ProjectId, COALESCE(part_number,'') AS PartNumber,
                   COALESCE(description,'') AS Description,
                   quantity, quantity_produced AS QuantityProduced,
                   unit_cost AS UnitCost, COALESCE(material,'') AS Material,
                   COALESCE(treatment,'') AS Treatment,
                   supplier_id AS SupplierId, COALESCE(supplier_name,'') AS SupplierName,
                   COALESCE(item_status,'') AS ItemStatus,
                   COALESCE(requested_by,'') AS RequestedBy, COALESCE(danea_ref,'') AS DaneaRef,
                   date_needed AS DateNeeded, order_date AS OrderDate,
                   COALESCE(destination,'') AS Destination,
                   destination_spec AS DestinationSpec, COALESCE(notes,'') AS Notes,
                   parent_officina_item_id AS ParentOfficinaItemId, composition_qty AS CompositionQty,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM ddp_officina_items
            WHERE project_id = @Id
            ORDER BY id", new { Id = id }).ToList();

            return Ok(ApiResponse<List<OfficinaItemListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<OfficinaItemListItem>>.Fail(ex.Message));
        }
    }

    [HttpPost("{id}/ddp-officina")]
    public IActionResult AddOfficinaItem(int id, [FromBody] OfficinaItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            req.ProjectId = id;

            // Finestra di partenza (riga INIZIO della matrice, tipo OFFICINA).
            string? startError = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeOfficina, null, req.ItemStatus);
            if (startError != null)
                return BadRequest(ApiResponse<int>.Fail(startError));

            // Auto-fill data ordine al primo passaggio in IO (anche in create).
            if (string.Equals(req.ItemStatus, "IO", StringComparison.OrdinalIgnoreCase)
                && !req.OrderDate.HasValue)
            {
                req.OrderDate = DateTime.Today;
            }
            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, unit_cost, material,
                 treatment, supplier_id, supplier_name, item_status, requested_by, danea_ref,
                 date_needed, order_date, destination, destination_spec, notes, updated_at)
            VALUES
                (@ProjectId, @PartNumber, @Description, @Quantity, @UnitCost, @Material,
                 @Treatment, @SupplierId, @SupplierName, @ItemStatus, @RequestedBy, @DaneaRef,
                 @DateNeeded, @OrderDate, @Destination, @DestinationSpec, @Notes, NOW());
            SELECT LAST_INSERT_ID()", req);

            // Ogni riga officina genera la sua bozza di lavorazione (Pannello Lavorazioni).
            WorkRequestDdpSync.SyncResult? sync = WorkRequestDdpSync.Upsert(c, newId);
            if (sync != null) NotifyWorkRequestsChanged("create", sync.ProjectId);

            NotifyDdpChange(id, conn, "create", newId, "OFFICINA");
            return Ok(ApiResponse<int>.Ok(newId, "Aggiunto"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail(ex.Message));
        }
    }

    // Import composizione Codex → distinta officina: aggiunto un codice padre (es. assieme 5xx),
    // i suoi figli DIRETTI (solo articoli Codex, niente ricorsione sui sotto-assiemi) diventano
    // righe della DDP officina con snapshot codice/descrizione/costo/fornitore, stato DO.
    // Dedup per codice normalizzato (senza punti): se il figlio è già in distinta si somma la
    // quantità della composizione alla riga esistente. I figli da Catalogo vengono saltati e contati.
    [HttpPost("{id}/ddp-officina/import-composition")]
    public IActionResult ImportOfficinaComposition(int id, [FromBody] OfficinaImportCompositionRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            var children = c.Query<OfficinaCompositionChild>(@"
            SELECT ci.codice AS RawCode, ci.descr AS Descr, ci.fornitore AS Fornitore,
                   ci.prezzo_forn AS PrezzoForn, cc.quantity AS Quantity,
                   (cc.child_catalog_id IS NOT NULL) AS IsCatalog
            FROM codex_compositions cc
            LEFT JOIN codex_items ci ON ci.id = cc.child_codex_id
            WHERE cc.parent_codex_id = @ParentId
            ORDER BY cc.sort_order, ci.codice", new { ParentId = req.CodexParentId }).ToList();

            if (children.Count == 0)
                return Ok(ApiResponse<OfficinaImportCompositionResult>.Fail("L'articolo non ha una composizione."));

            // Riga del padre in distinta: la sua quantità è il moltiplicatore dell'import
            // (es. 4 assiemi → ogni componente ×4) e il suo id collega i figli, che da qui
            // in poi seguono i cambi di quantità del padre («comanda il padre»).
            decimal parentQty = 1;
            int? parentRowId = null;
            string parentKey = (c.ExecuteScalar<string?>(
                "SELECT codice FROM codex_items WHERE id = @Id", new { Id = req.CodexParentId }) ?? "")
                .Replace(".", "").Trim();
            if (parentKey.Length > 0)
            {
                (int Id, decimal Quantity) parentRow = c.QueryFirstOrDefault<(int, decimal)>(@"
                SELECT id, quantity FROM ddp_officina_items
                WHERE project_id = @Id AND REPLACE(part_number, '.', '') = @Key
                ORDER BY id LIMIT 1", new { Id = id, Key = parentKey });
                if (parentRow.Id > 0)
                {
                    parentRowId = parentRow.Id;
                    if (parentRow.Quantity > 0) parentQty = parentRow.Quantity;
                }
            }

            // Righe già in distinta, indicizzate per codice normalizzato (il part_number è salvato col punto).
            var existingByCode = new Dictionary<string, int>();
            foreach ((int rowId, string partNumber) in c.Query<(int, string)>(
                "SELECT id, part_number FROM ddp_officina_items WHERE project_id = @Id ORDER BY id", new { Id = id }))
            {
                string key = (partNumber ?? "").Replace(".", "").Trim();
                if (key.Length > 0 && !existingByCode.ContainsKey(key)) existingByCode[key] = rowId;
            }

            var result = new OfficinaImportCompositionResult { ParentQuantity = parentQty };
            foreach (OfficinaCompositionChild child in children)
            {
                // I figli da Catalogo (preventivi) non sono particolari d'officina: saltati e contati.
                if (child.IsCatalog || string.IsNullOrWhiteSpace(child.RawCode)) { result.Skipped++; continue; }

                string key = child.RawCode.Replace(".", "").Trim();
                if (existingByCode.TryGetValue(key, out int existingId))
                {
                    // Riga già in distinta: somma la quantità e, se libera, la collega al padre
                    // (COALESCE: non ruba righe già collegate a un altro padre).
                    c.Execute(@"UPDATE ddp_officina_items SET
                                quantity = quantity + @Add,
                                parent_officina_item_id = COALESCE(parent_officina_item_id, @ParentRowId),
                                composition_qty = COALESCE(composition_qty, @CompQty),
                                updated_at = NOW()
                                WHERE id = @Id",
                        new { Add = child.Quantity * parentQty, ParentRowId = parentRowId, CompQty = (decimal)child.Quantity, Id = existingId });
                    WorkRequestDdpSync.Upsert(c, existingId);
                    result.Updated++;
                }
                else
                {
                    int newId = c.ExecuteScalar<int>(@"
                    INSERT INTO ddp_officina_items
                        (project_id, part_number, description, quantity, unit_cost, material,
                         treatment, supplier_name, item_status, requested_by, danea_ref,
                         date_needed, destination, destination_spec, notes,
                         parent_officina_item_id, composition_qty, updated_at)
                    VALUES
                        (@ProjectId, @PartNumber, @Description, @Quantity, @UnitCost, '',
                         '', @SupplierName, 'DO', @RequestedBy, '',
                         NULL, '', '', '',
                         @ParentRowId, @CompQty, NOW());
                    SELECT LAST_INSERT_ID()", new
                    {
                        ProjectId = id,
                        PartNumber = CodexListItem.FormatCodice(child.RawCode),
                        Description = child.Descr,
                        Quantity = child.Quantity * parentQty,
                        UnitCost = child.PrezzoForn,
                        SupplierName = child.Fornitore,
                        RequestedBy = req.RequestedBy,
                        ParentRowId = parentRowId,
                        CompQty = (decimal)child.Quantity
                    });
                    // Ogni riga officina genera la sua bozza di lavorazione (Pannello Lavorazioni).
                    WorkRequestDdpSync.Upsert(c, newId);
                    existingByCode[key] = newId;
                    result.Added++;
                }
            }

            if (result.Added + result.Updated > 0)
            {
                NotifyWorkRequestsChanged("create", id);
                NotifyDdpChange(id, conn, "create", 0, "OFFICINA");
            }
            string mult = parentQty != 1 ? $" ×{parentQty:0.###}" : "";
            return Ok(ApiResponse<OfficinaImportCompositionResult>.Ok(result,
                $"Composizione importata{mult}: {result.Added} nuove righe, {result.Updated} aggiornate"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<OfficinaImportCompositionResult>.Fail(ex.Message));
        }
    }

    /// <summary>Figlio della composizione Codex proiettato per l'import in DDP officina.</summary>
    private sealed class OfficinaCompositionChild
    {
        public string RawCode { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Fornitore { get; set; } = "";
        public decimal PrezzoForn { get; set; }
        public int Quantity { get; set; }
        public bool IsCatalog { get; set; }
    }

    [HttpPut("{id}/ddp-officina/{itemId}")]
    public IActionResult UpdateOfficinaItem(int id, int itemId, [FromBody] OfficinaItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();

            // Concorrenza ottimistica (rete di sicurezza anche col real-time): se il client invia
            // la versione vista (ExpectedUpdatedAt) e la riga è cambiata nel frattempo → 409, niente lost update.
            if (req.ExpectedUpdatedAt.HasValue)
            {
                DateTime? current = c.ExecuteScalar<DateTime?>(
                    "SELECT updated_at FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                    new { ItemId = itemId, Id = id });
                if (current.HasValue && Math.Abs((current.Value - req.ExpectedUpdatedAt.Value).TotalSeconds) > 1)
                    return Conflict(ApiResponse<DateTime?>.Fail(
                        "Riga modificata da un altro utente nel frattempo. Ricarica e riprova."));
            }

            // Leggi stato, quantità e order_date precedenti (notifiche + «comanda il padre» + auto IO)
            (string? ItemStatus, decimal Quantity, int? ParentOfficinaItemId, DateTime? OrderDate) before =
                c.QueryFirstOrDefault<(string?, decimal, int?, DateTime?)>(
                "SELECT item_status, quantity, parent_officina_item_id, order_date FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            string? oldStatus = before.ItemStatus;

            // Matrice degli avanzamenti di stato (v7, tipo OFFICINA): il server rifiuta le
            // transizioni non ammesse (la UI mostra solo quelle valide, ma qui si coprono
            // client vecchi e modifiche concorrenti). Gli auto-avanzamenti successivi
            // (pezzi prodotti → PAR/DISP) sono transizioni di sistema già coerenti con la matrice.
            string? transitionError = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeOfficina, oldStatus, req.ItemStatus);
            if (transitionError != null)
                return BadRequest(ApiResponse<DateTime?>.Fail(transitionError));

            // Se è un componente figlio, blocca la modifica della quantità ripristinando quella precedente.
            if (before.ParentOfficinaItemId.HasValue)
            {
                req.Quantity = before.Quantity;
            }

            // Quantità modificabile solo in «Da Ordinare» (o tornando in DO nella stessa save).
            if (req.Quantity != before.Quantity
                && !string.Equals(oldStatus, "DO", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(req.ItemStatus, "DO", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<DateTime?>.Fail(
                    "La quantità è modificabile solo in stato Da Ordinare."));
            }

            // Auto-fill data ordine: al passaggio a IO, se ancora NULL (body e DB) → oggi (one-shot).
            if (string.Equals(req.ItemStatus, "IO", StringComparison.OrdinalIgnoreCase)
                && !req.OrderDate.HasValue
                && !before.OrderDate.HasValue)
            {
                req.OrderDate = DateTime.Today;
            }
            else if (!req.OrderDate.HasValue && before.OrderDate.HasValue)
            {
                // Non cancellare la data già salvata se il client non la rimanda.
                req.OrderDate = before.OrderDate;
            }

            // Pezzi prodotti: clamp 0…quantity; auto PAR/DISP (salvo ANN).
            // DISP = chiusura positiva unica della matrice v7 (assorbe il vecchio COS).
            if (req.QuantityProduced < 0) req.QuantityProduced = 0;
            if (req.QuantityProduced > req.Quantity) req.QuantityProduced = req.Quantity;
            if (!string.Equals(req.ItemStatus, "ANN", StringComparison.OrdinalIgnoreCase))
            {
                if (req.Quantity > 0 && req.QuantityProduced >= req.Quantity)
                    req.ItemStatus = "DISP";
                else if (req.QuantityProduced > 0 && req.QuantityProduced < req.Quantity)
                    req.ItemStatus = "PAR";
            }

            req.Id = itemId;
            req.ProjectId = id;
            c.Execute(@"
            UPDATE ddp_officina_items SET
                quantity = @Quantity, quantity_produced = @QuantityProduced,
                unit_cost = @UnitCost, material = @Material,
                treatment = @Treatment, supplier_id = @SupplierId, supplier_name = @SupplierName,
                item_status = @ItemStatus, danea_ref = @DaneaRef, date_needed = @DateNeeded,
                order_date = @OrderDate,
                destination = @Destination, destination_spec = @DestinationSpec,
                notes = @Notes, updated_at = NOW()
            WHERE id = @Id AND project_id = @ProjectId", req);

            if (req.UpdateCodexPrice == true && !string.IsNullOrWhiteSpace(req.PartNumber))
            {
                var cleanPartNumber = req.PartNumber.Replace(".", "").Trim();
                c.Execute(
                    "UPDATE codex_items SET prezzo_forn = @Price, data = NOW() WHERE codice = @Code AND (prezzo_forn IS NULL OR prezzo_forn = 0)",
                    new { Price = req.UnitCost, Code = cleanPartNumber });
            }

            // «Comanda il padre»: se la quantità è cambiata, i componenti importati dalla
            // composizione Codex (righe collegate a questa) seguono con delta =
            // composition_qty × ΔQtà, mai sotto zero. Escluse le righe negli stati A9
            // (fuori dai conteggi, quantità bloccata anche in UI).
            if (!string.IsNullOrEmpty(oldStatus) && req.Quantity != before.Quantity)
            {
                decimal delta = req.Quantity - before.Quantity;
                var excluded = c.Query<string>(@"
                    SELECT s.status_key FROM ddp_aggregation_states s
                    JOIN ddp_aggregations a ON a.id = s.aggregation_id
                    WHERE a.code = 'A9'").ToList();
                if (excluded.Count == 0) excluded.Add("");   // IN su lista vuota non è SQL valido
                var childIds = c.Query<int>(@"
                    SELECT id FROM ddp_officina_items
                    WHERE parent_officina_item_id = @ParentId AND composition_qty IS NOT NULL
                      AND item_status NOT IN @Excluded",
                    new { ParentId = itemId, Excluded = excluded }).ToList();
                foreach (int childId in childIds)
                {
                    c.Execute(@"UPDATE ddp_officina_items
                        SET quantity = GREATEST(0, quantity + composition_qty * @Delta), updated_at = NOW()
                        WHERE id = @Id", new { Delta = delta, Id = childId });
                    WorkRequestDdpSync.Upsert(c, childId);
                }
                if (childIds.Count > 0) NotifyWorkRequestsChanged("update", id);
            }

            // Trigger notifica se lo stato è cambiato (solo se commessa ACTIVE)
            string projStatus = c.ExecuteScalar<string?>(
                "SELECT status FROM projects WHERE id = @Id", new { Id = id }) ?? "";
            if (!string.IsNullOrEmpty(oldStatus) && oldStatus != req.ItemStatus && projStatus == "ACTIVE")
            {
                try
                {
                    string projCode = c.ExecuteScalar<string?>(
                        "SELECT code FROM projects WHERE id = @Id", new { Id = id }) ?? "";
                    int currentEmpId = GetCurrentEmployeeId();

                    string severity = req.ItemStatus switch
                    {
                        "DISP" => "SUCCESS",  // DISPONIBILE / CONSEGNATO (chiusura positiva v7)
                        "ANN" => "WARNING",   // ANNULLATO
                        "IO" => "INFO",       // IN ORDINE
                        _ => "INFO"
                    };

                    string title = $"Cambio stato DDP Officina — {projCode}";
                    string msg = $"Stato modificato da {DdpStatusMap.ToLabel(oldStatus)} a {DdpStatusMap.ToLabel(req.ItemStatus)}";

                    List<int> recipients = _notif.GetProjectPmIds(id);
                    recipients.AddRange(_notif.GetAcqEmployeeIds());
                    recipients.Remove(currentEmpId);

                    if (recipients.Count > 0)
                        _notif.Create("DDP_STATUS_CHANGED", severity, title, msg, "BOM_OFFICINA", itemId, id, currentEmpId, recipients);
                }
                catch { /* non bloccare l'update per errore notifica */ }
            }

            // Riallinea i campi derivati della lavorazione collegata (o crea la bozza se manca).
            WorkRequestDdpSync.SyncResult? sync = WorkRequestDdpSync.Upsert(c, itemId);
            if (sync != null) NotifyWorkRequestsChanged(sync.Created ? "create" : "update", sync.ProjectId);

            // Real-time: avvisa gli altri che guardano la distinta officina di questa commessa.
            NotifyDdpChange(id, conn, "update", itemId, "OFFICINA");

            // Ritorna la nuova versione così il client riallinea il proprio token di concorrenza.
            DateTime? newTs = c.ExecuteScalar<DateTime?>(
                "SELECT updated_at FROM ddp_officina_items WHERE id = @Id", new { Id = itemId });
            return Ok(ApiResponse<DateTime?>.Ok(newTs, "Aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DateTime?>.Fail(ex.Message));
        }
    }

    // Cancellazione DEFINITIVA della riga (diversa dall'annullo, che è un cambio stato):
    // riservata ad ADMIN e PM, esposta nel menu riga della griglia officina.
    // «Comanda il padre» anche qui: eliminare un padre elimina in cascata i componenti
    // importati dalla sua composizione (ognuno con la propria bozza in staging).
    [HttpDelete("{id}/ddp-officina/{itemId}")]
    [Authorize(Roles = "ADMIN,PM")]
    public IActionResult DeleteOfficinaItem(int id, int itemId, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            // Blocca la cancellazione diretta dei figli.
            var isChild = c.ExecuteScalar<int?>(
                "SELECT parent_officina_item_id FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            if (isChild.HasValue)
                return BadRequest(ApiResponse<bool>.Fail("Impossibile eliminare direttamente un componente figlio. Eliminare il padre."));

            var childIds = c.Query<int>(
                "SELECT id FROM ddp_officina_items WHERE parent_officina_item_id = @Pid AND project_id = @Id",
                new { Pid = itemId, Id = id }).ToList();
            bool anyStagingDeleted = false;
            foreach (int childId in childIds)
            {
                anyStagingDeleted |= WorkRequestDdpSync.DeleteStagingFor(c, childId).HasValue;
                c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { Id = childId });
            }

            // La bozza in staging segue la riga; le lavorazioni promosse restano
            // (la FK ON DELETE SET NULL le scollega alla cancellazione della riga).
            anyStagingDeleted |= WorkRequestDdpSync.DeleteStagingFor(c, itemId).HasValue;
            c.Execute("DELETE FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            if (anyStagingDeleted) NotifyWorkRequestsChanged("delete", id);
            NotifyDdpChange(id, conn, "delete", itemId, "OFFICINA");
            return Ok(ApiResponse<bool>.Ok(true, childIds.Count > 0
                ? $"Eliminata riga + {childIds.Count} componenti collegati"
                : "Eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    [HttpGet("{id}/dashboard")]
    public IActionResult GetDashboard(int id)
    {
        using var c = _db.Open();

        // Info commessa + cliente + PM
        var data = c.QueryFirstOrDefault<ProjectDashboardData>(@"
            SELECT p.code AS Code, p.title AS Title, p.status, p.priority,
                   p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.budget_total AS BudgetTotal,
                   COALESCE((SELECT SUM(pa.planned_hours) FROM phase_assignments pa JOIN project_phases pp2 ON pp2.id = pa.project_phase_id WHERE pp2.project_id = p.id), 0) AS BudgetHoursTotal,
                   p.revenue AS Revenue, p.description AS Description,
                   p.server_path AS ServerPath, p.notes AS Notes,
                   COALESCE(cust.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(pm.first_name,' ',pm.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cust ON cust.id = p.customer_id
            LEFT JOIN employees pm ON pm.id = p.pm_id
            WHERE p.id = @Id", new { Id = id });

        if (data == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));

        // Ore lavorate totali + costo consuntivo
        // Fallback robusto: priorità is_primary, poi is_responsible, poi qualsiasi reparto (MIN id).
        var totals = c.QueryFirstOrDefault<dynamic>(@"
    SELECT COALESCE(SUM(te.hours), 0) AS HoursWorked,
           COALESCE(SUM(te.hours * COALESCE(d.hourly_cost, 0)), 0) AS CostWorked
    FROM timesheet_entries te
    JOIN employees e ON e.id = te.employee_id
    JOIN project_phases pp ON pp.id = te.project_phase_id
    LEFT JOIN (
        SELECT employee_id, department_id,
               ROW_NUMBER() OVER (PARTITION BY employee_id
                                  ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
        FROM employee_departments
    ) ed ON ed.employee_id = e.id AND ed.rn = 1
    LEFT JOIN departments d ON d.id = ed.department_id
    WHERE pp.project_id = @Id", new { Id = id });

        data.HoursWorked = (decimal)(totals?.HoursWorked ?? 0m);
        data.CostWorked = (decimal)(totals?.CostWorked ?? 0m);

        // Costo materiali DDP — esclude solo gli stati «esclusi da totale» (aggregazione A9);
        // i materiali consegnati (A2) SONO un costo reale e restano nel totale.
        string[] ddpExcluded = DdpAggregationSet.Load(c, "A9");
        decimal materialCostCommercial = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM bom_items
            WHERE project_id = @Id AND COALESCE(item_status,'') NOT IN @Excluded",
            new { Id = id, Excluded = ddpExcluded });

        decimal materialCostOfficina = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM ddp_officina_items
            WHERE project_id = @Id AND COALESCE(item_status,'') NOT IN @Excluded
              AND id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)",
            new { Id = id, Excluded = ddpExcluded });

        data.MaterialCostCommercial = materialCostCommercial;
        data.MaterialCostOfficina = materialCostOfficina;
        data.MaterialCost = materialCostCommercial + materialCostOfficina;
        data.TotalCost = data.CostWorked + data.MaterialCost;

        // Conteggio fasi
        var phaseCounts = c.QueryFirstOrDefault<dynamic>(@"
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN status='COMPLETED' THEN 1 ELSE 0 END) AS Completed
            FROM project_phases WHERE project_id = @Id", new { Id = id });

        data.TotalPhases = (int)(phaseCounts?.Total ?? 0);
        data.CompletedPhases = (int)(phaseCounts?.Completed ?? 0);

        // Riepilogo per reparto — 3 livelli: Preventivate / Assegnate / Lavorate
        // Ore preventivate dal costing (per reparto della sezione costo → template → reparti)
        var costingByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT d.code, d.name, SUM(r.work_days * r.hours_per_day) AS Hours
            FROM project_cost_resources r
            JOIN project_cost_sections pcs ON pcs.id = r.section_id
            JOIN cost_section_templates cst ON cst.id = pcs.template_id
            JOIN cost_section_template_departments cstd ON cstd.section_template_id = cst.id
            JOIN departments d ON d.id = cstd.department_id
            WHERE pcs.project_id = @Id AND pcs.is_enabled = 1
            GROUP BY d.code, d.name", new { Id = id }).ToList();

        // Ore assegnate (dalle phase_assignments, raggruppate per reparto fase o reparto dipendente).
        // Per il reparto del dipendente: priorità is_primary, poi is_responsible, poi qualsiasi (MIN id).
        var assignedByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT COALESCE(d.code, ed.code, 'TRASV') AS code,
                   COALESCE(d.name, ed.name, 'Trasversale') AS name,
                   SUM(pa.planned_hours) AS Hours
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN departments d ON d.id = pp.department_id
            LEFT JOIN (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) empd ON empd.employee_id = pa.employee_id AND empd.rn = 1
            LEFT JOIN departments ed ON ed.id = empd.department_id
            WHERE pp.project_id = @Id
            GROUP BY COALESCE(d.code, ed.code, 'TRASV'), COALESCE(d.name, ed.name, 'Trasversale')", new { Id = id }).ToList();

        // Fasi, completamento, ore lavorate, materiali
        var phasesByDept = c.Query<DeptSummary>(@"
            SELECT dept_code AS DepartmentCode, dept_name AS DepartmentName,
                   SUM(HoursWorked) AS HoursWorked,
                   SUM(TotalPhases) AS TotalPhases, SUM(CompletedPhases) AS CompletedPhases,
                   SUM(MaterialCost) AS MaterialCost
            FROM (
                -- Fasi con reparto
                SELECT COALESCE(d.code, 'TRASV') AS dept_code,
                       COALESCE(d.name, 'Trasversale') AS dept_name,
                       COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id), 0) AS HoursWorked,
                       1 AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                LEFT JOIN departments d ON d.id = pp.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NOT NULL

                UNION ALL

                -- Fasi senza department_id: ore attribuite al PRIMO reparto della sezione di costo della fase
                -- (snapshot-aware: usa pp.cost_section_template_id, fallback su phase_templates).
                -- Se la sezione non ha reparti o la fase non ha sezione → fallback al reparto primario del dipendente.
                SELECT COALESCE(dsec.code, ed.code, 'TRASV') AS dept_code,
                       COALESCE(dsec.name, ed.name, 'Trasversale') AS dept_name,
                       te.hours AS HoursWorked,
                       0 AS TotalPhases, 0 AS CompletedPhases, 0 AS MaterialCost
                FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
                LEFT JOIN project_cost_sections pcs
                     ON pcs.project_id = pp.project_id
                    AND pcs.template_id = COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)
                LEFT JOIN (
                    SELECT pcsd.project_cost_section_id, MIN(pcsd.department_id) AS department_id
                    FROM project_cost_section_departments pcsd
                    GROUP BY pcsd.project_cost_section_id
                ) firstdept ON firstdept.project_cost_section_id = pcs.id
                LEFT JOIN departments dsec ON dsec.id = firstdept.department_id
                LEFT JOIN (
                    SELECT employee_id, department_id,
                           ROW_NUMBER() OVER (PARTITION BY employee_id
                                              ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                    FROM employee_departments
                ) empd ON empd.employee_id = te.employee_id AND empd.rn = 1
                LEFT JOIN departments ed ON ed.id = empd.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NULL

                UNION ALL

                -- Fasi trasversali: conteggio fasi (come Trasversale)
                SELECT 'TRASV' AS dept_code, 'Trasversale' AS dept_name,
                       0 AS HoursWorked,
                       1 AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
            ) sub
            GROUP BY dept_code, dept_name", new { Id = id, Excluded = ddpExcluded }).ToList();

        // Merge: unisci costing + assigned + fasi in un unico elenco per reparto
        HashSet<string> allDepts = phasesByDept.Select(p => p.DepartmentCode)
            .Union(costingByDept.Select(c2 => c2.Code))
            .Union(assignedByDept.Select(a => a.Code))
            .ToHashSet();

        Dictionary<string, DeptSummary> deptMap = phasesByDept.ToDictionary(p => p.DepartmentCode);
        foreach (string code in allDepts)
        {
            if (!deptMap.ContainsKey(code))
            {
                string name = costingByDept.FirstOrDefault(x => x.Code == code).Name
                    ?? assignedByDept.FirstOrDefault(x => x.Code == code).Name ?? code;
                deptMap[code] = new DeptSummary { DepartmentCode = code, DepartmentName = name };
            }
        }
        foreach (var (code, _, hours) in costingByDept)
            if (deptMap.TryGetValue(code, out DeptSummary? ds)) ds.CostingHours += hours;
        foreach (var (code, _, hours) in assignedByDept)
            if (deptMap.TryGetValue(code, out DeptSummary? ds)) ds.AssignedHours += hours;

        // BudgetHours = costing come riferimento principale
        foreach (DeptSummary ds in deptMap.Values)
            ds.BudgetHours = ds.CostingHours;

        data.DepartmentSummaries = deptMap.Values.OrderBy(d2 => d2.DepartmentCode).ToList();

        // Ultimi 10 inserimenti timesheet
        data.RecentEntries = c.Query<RecentTimesheetEntry>(@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType,
                   COALESCE(te.notes, '') AS Notes
            FROM timesheet_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.project_id = @Id
            ORDER BY te.work_date DESC, te.id DESC
            LIMIT 10", new { Id = id }).ToList();

        // Tecnici assegnati alle fasi (non dal timesheet)
        data.ActiveTechnicians = c.Query<ActiveTechSummary>(@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(d.code, '') AS DepartmentCode,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te 
                             WHERE te.employee_id = e.id 
                             AND te.project_phase_id IN (SELECT pp2.id FROM project_phases pp2 WHERE pp2.project_id = @Id)), 0) AS TotalHours,
                   COUNT(DISTINCT pa.project_phase_id) AS PhaseCount
            FROM phase_assignments pa
            JOIN employees e ON e.id = pa.employee_id
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            GROUP BY e.id, e.first_name, e.last_name, d.code
            ORDER BY e.last_name", new { Id = id }).ToList();

        // ── Ore settimanali (ultime 12 settimane) ────────────────
        data.WeeklyHours = c.Query<WeeklyHoursSummary>(@"
            SELECT YEAR(te.work_date) AS Year,
                   WEEK(te.work_date, 1) AS Week,
                   SUM(te.hours) AS Hours,
                   CONCAT('S', WEEK(te.work_date, 1)) AS WeekLabel
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            WHERE pp.project_id = @Id
              AND te.work_date >= DATE_SUB(CURDATE(), INTERVAL 12 WEEK)
            GROUP BY YEAR(te.work_date), WEEK(te.work_date, 1)
            ORDER BY Year, Week", new { Id = id }).ToList();

        // ── Gantt fasi ───────────────────────────────────────────
        // Snapshot-aware: LEFT JOIN + fallback pp.name per fasi locali (phase_template_id NULL)
        data.PhaseGantt = c.Query<PhaseGanttItem>(@"
            SELECT pp.id AS PhaseId,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct,
                   pp.budget_hours AS BudgetHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id), 0) AS HoursWorked,
                   pp.start_date AS StartDate,
                   pp.end_date AS EndDate,
                   pp.sort_order AS SortOrder
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            ORDER BY pp.sort_order", new { Id = id }).ToList();

        // ── Scadenze prossime (fasi non completate con end_date) ─
        data.Deadlines = c.Query<UpcomingDeadline>(@"
            SELECT COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.end_date AS Deadline,
                   DATEDIFF(pp.end_date, CURDATE()) AS DaysRemaining,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
              AND pp.end_date IS NOT NULL
              AND pp.status NOT IN ('COMPLETED', 'CANCELLED')
            ORDER BY pp.end_date ASC
            LIMIT 10", new { Id = id }).ToList();

        return Ok(ApiResponse<ProjectDashboardData>.Ok(data));
    }


    // --- UPLOAD FILE ---
    [HttpPost("{id}/upload")]
    public async Task<IActionResult> UploadFile(int id, [FromQuery] string? subPath, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.Fail("Nessun file selezionato."));

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string targetDir = string.IsNullOrEmpty(subPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, subPath));

        if (!targetDir.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        LongPathHelper.CreateDirectory(targetDir);

        string filePath = Path.Combine(targetDir, file.FileName);
        if (LongPathHelper.FileExists(filePath))
        {
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string ext = Path.GetExtension(file.FileName);
            int counter = 1;
            do { filePath = Path.Combine(targetDir, $"{name}_{counter}{ext}"); counter++; }
            while (LongPathHelper.FileExists(filePath));
        }

        using (var stream = LongPathHelper.CreateFileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        NotifyDocumentsChanged(id, "upload");
        return Ok(ApiResponse<string>.Ok(Path.GetFileName(filePath), "File caricato."));
    }

    // --- UPLOAD MULTIPLO ---
    [HttpPost("{id}/upload-multiple")]
    public async Task<IActionResult> UploadMultiple(int id, [FromQuery] string? subPath, List<IFormFile> files)
    {
        if (files == null || !files.Any())
            return BadRequest(ApiResponse<string>.Fail("Nessun file selezionato."));

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string targetDir = string.IsNullOrEmpty(subPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, subPath));

        if (!targetDir.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        LongPathHelper.CreateDirectory(targetDir);

        int count = 0;
        foreach (var file in files)
        {
            string filePath = Path.Combine(targetDir, file.FileName);
            if (LongPathHelper.FileExists(filePath))
            {
                string name = Path.GetFileNameWithoutExtension(file.FileName);
                string ext = Path.GetExtension(file.FileName);
                int counter = 1;
                do { filePath = Path.Combine(targetDir, $"{name}_{counter}{ext}"); counter++; }
                while (LongPathHelper.FileExists(filePath));
            }
            using var stream = LongPathHelper.CreateFileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            count++;
        }

        NotifyDocumentsChanged(id, "upload");
        return Ok(ApiResponse<string>.Ok($"{count} file caricati."));
    }

    // --- CREA SOTTOCARTELLA ---
    [HttpPost("{id}/create-subfolder")]
    public IActionResult CreateSubfolder(int id, [FromBody] SubfolderRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string parentDir = string.IsNullOrEmpty(req.SubPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, req.SubPath));

        if (!parentDir.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        string newFolder = Path.Combine(parentDir, req.FolderName);
        if (LongPathHelper.DirectoryExists(newFolder))
            return BadRequest(ApiResponse<string>.Fail("La cartella esiste già."));

        LongPathHelper.CreateDirectory(newFolder);
        NotifyDocumentsChanged(id, "create");
        return Ok(ApiResponse<string>.Ok(req.FolderName, "Cartella creata."));
    }

    // --- RINOMINA FILE/CARTELLA ---
    [HttpPost("{id}/rename")]
    public IActionResult RenameItem(int id, [FromBody] RenameRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string oldPath = Path.GetFullPath(Path.Combine(serverPath, req.OldPath));
        string parentDir = Path.GetDirectoryName(oldPath) ?? serverPath;
        string newPath = Path.Combine(parentDir, req.NewName);

        if (!oldPath.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (LongPathHelper.DirectoryExists(oldPath))
        {
            if (LongPathHelper.DirectoryExists(newPath))
                return BadRequest(ApiResponse<string>.Fail("Una cartella con questo nome esiste già."));
            LongPathHelper.MoveDirectory(oldPath, newPath);
        }
        else if (LongPathHelper.FileExists(oldPath))
        {
            if (LongPathHelper.FileExists(newPath))
                return BadRequest(ApiResponse<string>.Fail("Un file con questo nome esiste già."));
            LongPathHelper.MoveFile(oldPath, newPath);
        }
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "rename");
        return Ok(ApiResponse<string>.Ok(req.NewName, "Rinominato."));
    }

    // --- ELIMINA FILE/CARTELLA ---
    [HttpPost("{id}/delete-item")]
    public IActionResult DeleteItem(int id, [FromBody] DeleteItemRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string fullPath = Path.GetFullPath(Path.Combine(serverPath, req.ItemPath));

        if (!fullPath.StartsWith(Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (Path.GetFullPath(fullPath) == Path.GetFullPath(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Non è possibile eliminare la cartella root."));

        if (LongPathHelper.DirectoryExists(fullPath))
            LongPathHelper.DeleteDirectory(fullPath, true);
        else if (LongPathHelper.FileExists(fullPath))
            LongPathHelper.DeleteFile(fullPath);
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "delete");
        return Ok(ApiResponse<bool>.Ok(true, "Eliminato."));
    }

    // --- SPOSTA FILE/CARTELLA ---
    [HttpPost("{id}/move-item")]
    public IActionResult MoveItem(int id, [FromBody] MoveItemRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string sourcePath = Path.GetFullPath(Path.Combine(serverPath, req.SourcePath));
        string destDir = Path.GetFullPath(Path.Combine(serverPath, req.DestinationFolder));
        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(destDir, fileName);

        string rootFull = Path.GetFullPath(serverPath);
        if (!sourcePath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ||
            !destDir.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (!LongPathHelper.DirectoryExists(destDir))
            return BadRequest(ApiResponse<string>.Fail("Cartella destinazione non trovata."));

        if (LongPathHelper.DirectoryExists(sourcePath))
        {
            if (LongPathHelper.DirectoryExists(destPath))
                return BadRequest(ApiResponse<string>.Fail("Una cartella con questo nome esiste già nella destinazione."));
            LongPathHelper.MoveDirectory(sourcePath, destPath);
        }
        else if (LongPathHelper.FileExists(sourcePath))
        {
            if (LongPathHelper.FileExists(destPath))
                return BadRequest(ApiResponse<string>.Fail("Un file con questo nome esiste già nella destinazione."));
            LongPathHelper.MoveFile(sourcePath, destPath);
        }
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "move");
        return Ok(ApiResponse<bool>.Ok(true, "Spostato."));
    }

    /// <summary>Quantità editabile sulla DDP commerciale: VER (ingresso) o DO.</summary>
    private static bool IsCommercialQtyEditable(string? status) =>
        string.Equals(status, "VER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DO", StringComparison.OrdinalIgnoreCase);
}