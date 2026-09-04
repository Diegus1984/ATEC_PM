using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;


/// <summary>
/// Documenti della commessa: la cartella su disco (file, sottocartelle, upload, rinomina,
/// spostamento, anteprima, download). Stessa rotta <c>api/projects/{id}/…</c> di prima:
/// spostato da <c>ProjectsController</c> il 04/09/2026, nessun percorso cambiato.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura riguarda UNA commessa (l'id sta nella rotta), quindi il cancello si mette
// una volta sola sulla classe: una commessa in bozza, in stand-by o chiusa si consulta ma non si
// modifica, salvo il permesso di scavalco. E una bozza non si VEDE proprio, letture comprese.
[RequireProjectWritable]
[RequireProjectVisible]
public class ProjectFilesController : ProjectsControllerBase
{
    private readonly ProjectTemplateCopyService _templateCopy;
    public ProjectFilesController(
        DbService db,
        NotificationService notif,
        ProjectTemplateCopyService templateCopy,
        IHubContext<ProjectHub> hub,
        FeatureAccessService access,
        AnagraficheCache cache) : base(db, hub, notif, access, cache)
    {
        _templateCopy = templateCopy;
    }

    // Notifica real-time ai client che guardano la commessa che i documenti (file/cartelle su disco)
    // sono cambiati: le viste aperte (albero a sinistra + elenco a destra) ricaricano in ~1s.
    // Scope per-commessa ("project-{id}"). Niente self-exclusion: l'autore ricarica già localmente,
    // un eventuale secondo refresh è innocuo (nessun editing live da interrompere).
    private void NotifyDocumentsChanged(int projectId, string action)
    {
        var payload = new DocumentsChange { ProjectId = projectId, Action = action };
        _hub.Clients.Group($"project-{projectId}").SendAsync("DocumentsChanged", payload).SenzaAttesa("DocumentsChanged");
    }

    // --- FILE SYSTEM ---
    [RequireFeature("project.documenti")]
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

    [RequireFeature("project.documenti")]
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
            if (!IsPathAllowed(serverPath, targetPath))
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

    [RequireFeature("project.documenti")]
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

    /// <summary>
    /// Il percorso richiesto sta davvero dentro la cartella della commessa, fuori dall'area Chat?
    /// È l'unica barriera che protegge i documenti una volta concesso <c>project.documenti</c>,
    /// quindi qui stanno insieme i due controlli che prima erano sparsi e incoerenti
    /// (segnalazione #63, Fase 1):
    /// <list type="number">
    /// <item><b>Confine della commessa.</b> I confronti precedenti usavano
    /// <c>StartsWith(root)</c> senza separatore finale — e due su otto erano pure
    /// case-sensitive — quindi una cartella sorella con lo stesso prefisso (…\C001 accanto a
    /// …\C0011) superava il controllo. Qui il confronto è sul percorso RELATIVO: fuori dalla
    /// radice <c>GetRelativePath</c> restituisce un percorso che risale (<c>..</c>) o assoluto.</item>
    /// <item><b>Area Chat.</b> Gli allegati dei messaggi vivono in
    /// <c>&lt;cartella commessa&gt;\Chat\{chatId}</c>. L'elenco file la saltava confrontando il
    /// NOME della cartella al livello che stava elencando: bastava chiedere
    /// <c>?subPath=Chat/12</c> per scendere dentro e scaricare gli allegati delle chat private
    /// senza avere <c>project.chat</c> — e saltando l'unico controllo di partecipazione del
    /// modulo, quello di <c>ChatController.GetAttachment</c>. Il confronto sul percorso relativo
    /// chiude l'ingresso a qualunque profondità.</item>
    /// </list>
    /// </summary>
    private static bool IsPathAllowed(string serverPath, string candidatePath)
    {
        string root = Path.GetFullPath(serverPath);
        string full = Path.GetFullPath(candidatePath);

        string relative = Path.GetRelativePath(root, full);

        // Fuori dalla radice: GetRelativePath risale con ".." o resta assoluto (altro disco).
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        if (relative == ".") return true; // è la radice stessa

        string firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return !firstSegment.Equals(ChatFolderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cartella degli allegati chat dentro la commessa: non passa mai dai Documenti.</summary>
    private const string ChatFolderName = "Chat";

    // --- DOWNLOAD FILE ---
    [RequireFeature("project.documenti")]
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
        if (!IsPathAllowed(serverPath, normalizedFull))
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
    [RequireFeature("project.documenti")]
    [HttpGet("{id}/preview")]
    public IActionResult PreviewFile(int id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest("Path richiesto");

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return NotFound("Cartella non trovata");

        var fullPath = Path.GetFullPath(Path.Combine(serverPath, path));
        if (!IsPathAllowed(serverPath, fullPath)) return BadRequest("Path non valido");
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

    // --- UPLOAD FILE ---
    [RequireFeature("project.documenti")]
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

        if (!IsPathAllowed(serverPath, targetDir))
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
    [RequireFeature("project.documenti")]
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

        if (!IsPathAllowed(serverPath, targetDir))
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
    [RequireFeature("project.documenti")]
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

        if (!IsPathAllowed(serverPath, parentDir))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        string newFolder = Path.Combine(parentDir, req.FolderName);
        if (LongPathHelper.DirectoryExists(newFolder))
            return BadRequest(ApiResponse<string>.Fail("La cartella esiste già."));

        LongPathHelper.CreateDirectory(newFolder);
        NotifyDocumentsChanged(id, "create");
        return Ok(ApiResponse<string>.Ok(req.FolderName, "Cartella creata."));
    }

    // --- RINOMINA FILE/CARTELLA ---
    [RequireFeature("project.documenti")]
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

        if (!IsPathAllowed(serverPath, oldPath))
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
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/delete-item")]
    public IActionResult DeleteItem(int id, [FromBody] DeleteItemRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string fullPath = Path.GetFullPath(Path.Combine(serverPath, req.ItemPath));

        if (!IsPathAllowed(serverPath, fullPath))
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
    [RequireFeature("project.documenti")]
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
}
