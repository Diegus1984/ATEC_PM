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
    private void NotifyDdpChange(int projectId, string? conn, string action, int itemId)
    {
        var payload = new DdpChange { ProjectId = projectId, Action = action, ItemId = itemId };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
        {
            IClientProxy target = string.IsNullOrEmpty(conn)
                ? _hub.Clients.Group(group)
                : _hub.Clients.GroupExcept(group, conn);
            _ = target.SendAsync("DdpChanged", payload);
        }
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
            ".dwg" => "application/acad",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
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
        if (ext is not (".xlsx" or ".xls" or ".csv" or ".docx")) return BadRequest("Tipo non supportato");

        try
        {
            var fileName = Path.GetFileName(fullPath);
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
            var rows = c.Query<BomItemListItem>(@"
            SELECT b.id, b.project_id AS ProjectId, b.catalog_item_id AS CatalogItemId,
                   b.part_number AS PartNumber, b.description, b.unit, b.quantity,
                   b.unit_cost AS UnitCost,
                   COALESCE(s.company_name, '') AS SupplierName,
                   b.manufacturer, b.item_status AS ItemStatus,
                   b.requested_by AS RequestedBy, b.danea_ref AS DaneaRef,
                   b.date_needed AS DateNeeded, b.destination, b.notes,
                   b.ddp_type AS DdpType, b.created_at AS CreatedAt, b.updated_at AS UpdatedAt
            FROM bom_items b
            LEFT JOIN suppliers s ON s.id = b.supplier_id
            WHERE b.project_id = @Id AND b.ddp_type = @Type
            ORDER BY b.id", new { Id = id, Type = type }).ToList();

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
            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO bom_items
                (project_id, catalog_item_id, part_number, description, unit, quantity,
                 unit_cost, supplier_id, manufacturer, item_status, requested_by,
                 danea_ref, date_needed, destination, notes, ddp_type, updated_at)
            VALUES
                (@ProjectId, @CatalogItemId, @PartNumber, @Description, @Unit, @Quantity,
                 @UnitCost, @SupplierId, @Manufacturer, @ItemStatus, @RequestedBy,
                 @DaneaRef, @DateNeeded, @Destination, @Notes, @DdpType, NOW());
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

            // Leggi stato precedente per confronto
            string? oldStatus = c.ExecuteScalar<string?>(
                "SELECT item_status FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });

            req.Id = itemId;
            req.ProjectId = id;
            c.Execute(@"
            UPDATE bom_items SET
                quantity = @Quantity, item_status = @ItemStatus,
                danea_ref = @DaneaRef, date_needed = @DateNeeded,
                destination = @Destination, notes = @Notes, updated_at = NOW()
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
                        "CON" => "SUCCESS",   // CONSEGNATO
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

        // Costo materiali DDP
        decimal materialCost = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM bom_items WHERE project_id = @Id AND item_status <> 'ANN'", new { Id = id });

        data.MaterialCost = materialCost;
        data.TotalCost = data.CostWorked + materialCost;

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
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND b.item_status <> 'ANN'), 0) AS MaterialCost
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
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND b.item_status <> 'ANN'), 0) AS MaterialCost
                FROM project_phases pp
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
            ) sub
            GROUP BY dept_code, dept_name", new { Id = id }).ToList();

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

        return Ok(ApiResponse<bool>.Ok(true, "Spostato."));
    }
}