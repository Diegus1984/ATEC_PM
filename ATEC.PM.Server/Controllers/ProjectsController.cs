using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    public ProjectsController(DbService db, NotificationService notif) { _db = db; _notif = notif; }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            using var c = _db.Open();
            // Utilizziamo LEFT JOIN per evitare che la mancanza di un PM o Cliente rompa la lista
            // COALESCE gestisce i valori NULL per PM e Cliente trasformandoli in stringhe vuote
            var rows = c.Query<ProjectListItem>(@"
            SELECT p.id, p.code, p.title, 
                   COALESCE(cu.company_name, 'CLIENTE MANCANTE') AS CustomerName,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), 'NON ASSEGNATO') AS PmName,
                   p.status, p.priority, p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.revenue, p.budget_hours_total AS BudgetHoursTotal
            FROM projects p
            LEFT JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN employees e ON e.id = p.pm_id
            ORDER BY p.created_at DESC").ToList();
            return Ok(ApiResponse<List<ProjectListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            // Se c'è un errore SQL, restituiamo un oggetto ApiResponse valido con errore 
            // invece di lasciar crashare il server (che manderebbe HTML al client)
            return Ok(ApiResponse<List<ProjectListItem>>.Fail($"Errore DB: {ex.Message}"));
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
                   revenue, status, priority, server_path AS ServerPath, notes
            FROM projects WHERE id=@Id", new { Id = id });
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
            CopyTemplateToProject(req.Code);

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
        c.Execute(@"UPDATE projects SET code=@Code,title=@Title,customer_id=@CustomerId,pm_id=@PmId,
            description=@Description,start_date=@StartDate,end_date_planned=@EndDatePlanned,
            budget_total=@BudgetTotal,budget_hours_total=@BudgetHoursTotal,revenue=@Revenue,
            status=@Status,priority=@Priority,server_path=@ServerPath,notes=@Notes WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE projects SET status='CANCELLED' WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
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

            // 4. DELETE progetto (FK CASCADE elimina fasi, bom, costing, pricing, cashflow, chat, docs)
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
            CopyTemplateToProject(code);
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
    public IActionResult LookupEmployees()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>("SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status='ACTIVE' ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }
    [HttpGet("template-structure")]
    public IActionResult GetTemplateStructure()
    {
        string templatePath = @"C:\ATEC_Commesse\MASTER_TEMPLATE";

        if (!Directory.Exists(templatePath))
            return NotFound(ApiResponse<string>.Fail("Cartella MASTER_TEMPLATE non trovata"));

        var result = new TemplateFolderInfo();

        // Tutte le sottocartelle (percorsi relativi)
        foreach (string dir in Directory.GetDirectories(templatePath, "*", SearchOption.AllDirectories))
        {
            result.Folders.Add(Path.GetRelativePath(templatePath, dir).Replace("\\", "/"));
        }
        result.Folders.Sort();

        // Tutti i file (percorsi relativi + dimensione)
        foreach (string file in Directory.GetFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            FileInfo fi = new FileInfo(file);
            result.Files.Add(new TemplateFileInfo
            {
                RelativePath = Path.GetRelativePath(templatePath, file).Replace("\\", "/"),
                FileName = fi.Name,
                SizeBytes = fi.Length
            });
        }

        return Ok(ApiResponse<TemplateFolderInfo>.Ok(result));
    }

    private void CopyTemplateToProject(string projectCode)
    {
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        string templatePath = _db.GetConfig("TemplatePath", @"C:\ATEC_Commesse\MASTER_TEMPLATE");
        string year = DateTime.Now.Year.ToString();
        string targetPath = Path.Combine(basePath, year, projectCode);

        if (!Directory.Exists(templatePath))
        {
            Directory.CreateDirectory(targetPath);
            return;
        }

        Directory.CreateDirectory(targetPath);

        foreach (string dir in Directory.GetDirectories(templatePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(templatePath, dir);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (string file in Directory.GetFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(templatePath, file);
            string destFile = Path.Combine(targetPath, relativePath);
            System.IO.File.Copy(file, destFile, overwrite: false);
        }
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
                   b.ddp_type AS DdpType, b.created_at AS CreatedAt
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
    public IActionResult AddDdpItem(int id, [FromBody] BomItemSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            req.ProjectId = id;
            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO bom_items 
                (project_id, catalog_item_id, part_number, description, unit, quantity,
                 unit_cost, supplier_id, manufacturer, item_status, requested_by,
                 danea_ref, date_needed, destination, notes, ddp_type)
            VALUES 
                (@ProjectId, @CatalogItemId, @PartNumber, @Description, @Unit, @Quantity,
                 @UnitCost, @SupplierId, @Manufacturer, @ItemStatus, @RequestedBy,
                 @DaneaRef, @DateNeeded, @Destination, @Notes, @DdpType);
            SELECT LAST_INSERT_ID()", req);

            return Ok(ApiResponse<int>.Ok(newId, "Aggiunto"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail(ex.Message));
        }
    }

    [HttpPut("{id}/ddp/{itemId}")]
    public IActionResult UpdateDdpItem(int id, int itemId, [FromBody] BomItemSaveRequest req)
    {
        try
        {
            using var c = _db.Open();

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
                destination = @Destination, notes = @Notes
            WHERE id = @Id AND project_id = @ProjectId", req);

            // Trigger notifica se lo stato è cambiato
            if (!string.IsNullOrEmpty(oldStatus) && oldStatus != req.ItemStatus)
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
                        "DELIVERED" => "SUCCESS",
                        "CANCELLED" => "WARNING",
                        "ORDERED" => "INFO",
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

            return Ok(ApiResponse<bool>.Ok(true, "Aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    [HttpDelete("{id}/ddp/{itemId}")]
    public IActionResult DeleteDdpItem(int id, int itemId)
    {
        try
        {
            using var c = _db.Open();
            c.Execute("DELETE FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
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
                   p.budget_total AS BudgetTotal, p.budget_hours_total AS BudgetHoursTotal,
                   p.revenue AS Revenue, p.server_path AS ServerPath, p.notes AS Notes,
                   COALESCE(cust.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(pm.first_name,' ',pm.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cust ON cust.id = p.customer_id
            LEFT JOIN employees pm ON pm.id = p.pm_id
            WHERE p.id = @Id", new { Id = id });

        if (data == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));

        // Ore lavorate totali + costo consuntivo
        var totals = c.QueryFirstOrDefault<dynamic>(@"
    SELECT COALESCE(SUM(te.hours), 0) AS HoursWorked,
           COALESCE(SUM(te.hours * COALESCE(d.hourly_cost, 0)), 0) AS CostWorked
    FROM timesheet_entries te
    JOIN employees e ON e.id = te.employee_id
    JOIN project_phases pp ON pp.id = te.project_phase_id
    LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
    LEFT JOIN departments d ON d.id = ed.department_id
    WHERE pp.project_id = @Id", new { Id = id });

        data.HoursWorked = (decimal)(totals?.HoursWorked ?? 0m);
        data.CostWorked = (decimal)(totals?.CostWorked ?? 0m);

        // Costo materiali DDP
        decimal materialCost = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM bom_items WHERE project_id = @Id AND item_status <> 'CANCELLED'", new { Id = id });

        data.MaterialCost = materialCost;
        data.TotalCost = data.CostWorked + materialCost;

        // Conteggio fasi
        var phaseCounts = c.QueryFirstOrDefault<dynamic>(@"
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN status='COMPLETED' THEN 1 ELSE 0 END) AS Completed
            FROM project_phases WHERE project_id = @Id", new { Id = id });

        data.TotalPhases = (int)(phaseCounts?.Total ?? 0);
        data.CompletedPhases = (int)(phaseCounts?.Completed ?? 0);

        // Riepilogo per reparto
        data.DepartmentSummaries = c.Query<DeptSummary>(@"
            SELECT COALESCE(d.code, 'TRASV') AS DepartmentCode,
                    COALESCE(d.name, 'Trasversale') AS DepartmentName,
                    SUM(pp.budget_hours) AS BudgetHours,
                    COALESCE(SUM((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id)), 0) AS HoursWorked,
                    COUNT(*) AS TotalPhases,
                    SUM(CASE WHEN pp.status='COMPLETED' THEN 1 ELSE 0 END) AS CompletedPhases,
                    COALESCE(SUM((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND b.item_status <> 'CANCELLED')), 0) AS MaterialCost
            FROM project_phases pp
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            GROUP BY d.code, d.name
            ORDER BY d.code", new { Id = id }).ToList();

        // Ultimi 10 inserimenti timesheet
        data.RecentEntries = c.Query<RecentTimesheetEntry>(@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType,
                   COALESCE(te.notes, '') AS Notes
            FROM timesheet_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
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
        data.PhaseGantt = c.Query<PhaseGanttItem>(@"
            SELECT pp.id AS PhaseId,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct,
                   pp.budget_hours AS BudgetHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te WHERE te.project_phase_id = pp.id), 0) AS HoursWorked,
                   pp.start_date AS StartDate,
                   pp.end_date AS EndDate,
                   pp.sort_order AS SortOrder
            FROM project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            ORDER BY pp.sort_order", new { Id = id }).ToList();

        // ── Scadenze prossime (fasi non completate con end_date) ─
        data.Deadlines = c.Query<UpcomingDeadline>(@"
            SELECT COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.end_date AS Deadline,
                   DATEDIFF(pp.end_date, CURDATE()) AS DaysRemaining,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct
            FROM project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
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