using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/project-templates")]
[Authorize]
[RequireFeature("nav.project_templates")]
public class TemplateController : ControllerBase
{
    private readonly DbService _db;
    public TemplateController(DbService db) => _db = db;

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // Estensioni accettate per i file template (CAD, documenti, immagini)
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".rtf",
        ".dwg", ".dxf", ".step", ".stp", ".stl", ".iges", ".igs", ".obj",
        ".sldprt", ".sldasm", ".slddrw", ".easm", ".eprt", ".edrw",
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
        ".zip", ".rar", ".7z"
    };
    private const long MaxFileSizeBytes = 500L * 1024 * 1024; // 500 MB

    private string GetTemplatesRoot()
    {
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        return Path.Combine(basePath, "TEMPLATES");
    }

    [HttpGet("tree")]
    public IActionResult GetTree()
    {
        try
        {
            using var c = _db.Open();

            List<TemplateFolderNode> folders = c.Query<TemplateFolderNode>(@"
                SELECT id, parent_id AS ParentId, name, sort_order AS SortOrder
                FROM project_template_folders
                WHERE is_active = TRUE
                ORDER BY sort_order, name").ToList();

            // Query tipizzata: evita problemi di case-sensitivity sui dynamic di Dapper
            List<TemplateFileRow> allFiles = c.Query<TemplateFileRow>(@"
                SELECT id AS Id, folder_id AS FolderId, file_name AS FileName,
                       file_size AS FileSize, uploaded_at AS UploadedAt
                FROM project_template_files").ToList();

            Dictionary<int, TemplateFolderNode> lookup = folders.ToDictionary(f => f.Id);

            foreach (TemplateFileRow row in allFiles)
            {
                if (lookup.TryGetValue(row.FolderId, out TemplateFolderNode? folder))
                {
                    folder.Files.Add(new TemplateFileItem
                    {
                        Id = row.Id,
                        FileName = row.FileName,
                        FileSize = row.FileSize,
                        UploadedAt = row.UploadedAt
                    });
                }
            }

            List<TemplateFolderNode> roots = new();
            foreach (TemplateFolderNode folder in folders)
            {
                if (folder.ParentId.HasValue && lookup.TryGetValue(folder.ParentId.Value, out TemplateFolderNode? parent))
                    parent.Children.Add(folder);
                else
                    roots.Add(folder);
            }

            return Ok(ApiResponse<List<TemplateFolderNode>>.Ok(roots));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<TemplateFolderNode>>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("folders")]
    public IActionResult CreateFolder([FromBody] CreateTemplateFolderRequest req)
    {
        try
        {
            using var c = _db.Open();

            // Se il client non specifica un sortOrder valido (>0), assegna MAX+1 sui fratelli
            int sortOrder = req.SortOrder;
            if (sortOrder <= 0)
            {
                sortOrder = c.ExecuteScalar<int?>(@"
                    SELECT COALESCE(MAX(sort_order), 0) + 1
                    FROM project_template_folders
                    WHERE (@ParentId IS NULL AND parent_id IS NULL)
                       OR parent_id = @ParentId",
                    new { req.ParentId }) ?? 1;
            }

            int newId = c.ExecuteScalar<int>(@"
                INSERT INTO project_template_folders (parent_id, name, sort_order)
                VALUES (@ParentId, @Name, @SortOrder);
                SELECT LAST_INSERT_ID()",
                new { req.ParentId, req.Name, SortOrder = sortOrder });
            return Ok(ApiResponse<int>.Ok(newId, "Cartella creata"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("folders/{id}")]
    public IActionResult UpdateFolder(int id, [FromBody] UpdateTemplateFolderRequest req)
    {
        try
        {
            using var c = _db.Open();
            c.Execute(@"
                UPDATE project_template_folders
                SET name = @Name, parent_id = @ParentId, sort_order = @SortOrder
                WHERE id = @Id",
                new { req.Name, req.ParentId, req.SortOrder, Id = id });
            return Ok(ApiResponse<int>.Ok(id, "Cartella aggiornata"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpDelete("folders/{id}")]
    public IActionResult DeleteFolder(int id)
    {
        try
        {
            using var c = _db.Open();

            List<string> diskPaths = CollectDiskPaths(c, id);

            c.Execute("DELETE FROM project_template_folders WHERE id = @Id", new { Id = id });

            foreach (string path in diskPaths)
            {
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
                catch { /* log se necessario */ }
            }

            return Ok(ApiResponse<bool>.Ok(true, "Cartella eliminata"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("folders/{id}/upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadFile(int id, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(ApiResponse<string>.Fail("Nessun file selezionato."));

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(ApiResponse<string>.Fail(
                    $"File troppo grande. Massimo consentito: {MaxFileSizeBytes / (1024 * 1024)} MB."));

            // Strip eventuali path/separators: solo il nome base, mai sottocartelle.
            string safeName = Path.GetFileName(file.FileName ?? "");
            if (string.IsNullOrWhiteSpace(safeName))
                return BadRequest(ApiResponse<string>.Fail("Nome file non valido."));

            string safeExt = Path.GetExtension(safeName);
            if (string.IsNullOrEmpty(safeExt) || !AllowedExtensions.Contains(safeExt))
                return BadRequest(ApiResponse<string>.Fail(
                    $"Estensione '{safeExt}' non consentita per i template."));

            using var c = _db.Open();
            bool folderExists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_template_folders WHERE id = @Id", new { Id = id }) > 0;
            if (!folderExists)
                return NotFound(ApiResponse<string>.Fail("Cartella template non trovata."));

            string templatesRoot = GetTemplatesRoot();
            Directory.CreateDirectory(templatesRoot);

            string diskFileName = $"{id}_{safeName}";
            string diskPath = Path.Combine(templatesRoot, diskFileName);

            if (System.IO.File.Exists(diskPath))
            {
                string name = Path.GetFileNameWithoutExtension(safeName);
                string ext = Path.GetExtension(safeName);
                int counter = 1;
                do
                {
                    diskFileName = $"{id}_{name}_{counter}{ext}";
                    diskPath = Path.Combine(templatesRoot, diskFileName);
                    counter++;
                }
                while (System.IO.File.Exists(diskPath));
            }

            using (FileStream stream = new FileStream(diskPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            int empId = GetCurrentEmployeeId();
            int fileId = c.ExecuteScalar<int>(@"
                INSERT INTO project_template_files (folder_id, file_name, disk_path, file_size, uploaded_by)
                VALUES (@FolderId, @FileName, @DiskPath, @FileSize, @UploadedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    FolderId = id,
                    FileName = safeName,
                    DiskPath = diskPath,
                    FileSize = file.Length,
                    UploadedBy = empId > 0 ? (int?)empId : null
                });

            return Ok(ApiResponse<int>.Ok(fileId, "File caricato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore upload: {ex.Message}"));
        }
    }

    [HttpPut("files/{id}")]
    public IActionResult UpdateFile(int id, [FromBody] UpdateTemplateFileRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(ApiResponse<int>.Fail("Nome file vuoto."));

            using var c = _db.Open();
            int rows = c.Execute(
                "UPDATE project_template_files SET file_name = @Name WHERE id = @Id",
                new { req.Name, Id = id });
            if (rows == 0)
                return NotFound(ApiResponse<int>.Fail("File non trovato."));
            return Ok(ApiResponse<int>.Ok(id, "File rinominato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("files/{id}/move")]
    public IActionResult MoveFile(int id, [FromBody] MoveOrCopyFileRequest req)
    {
        try
        {
            using var c = _db.Open();
            bool targetExists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_template_folders WHERE id = @Id", new { Id = req.FolderId }) > 0;
            if (!targetExists)
                return NotFound(ApiResponse<int>.Fail("Cartella destinazione non trovata."));

            int rows = c.Execute(
                "UPDATE project_template_files SET folder_id = @FolderId WHERE id = @Id",
                new { req.FolderId, Id = id });
            if (rows == 0)
                return NotFound(ApiResponse<int>.Fail("File non trovato."));
            return Ok(ApiResponse<int>.Ok(id, "File spostato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("files/{id}/copy")]
    public IActionResult CopyFile(int id, [FromBody] MoveOrCopyFileRequest req)
    {
        try
        {
            using var c = _db.Open();
            // Carica record sorgente
            TemplateFileRow? src = c.QuerySingleOrDefault<TemplateFileRow>(
                "SELECT id AS Id, folder_id AS FolderId, file_name AS FileName, " +
                "file_size AS FileSize, uploaded_at AS UploadedAt, disk_path AS DiskPath " +
                "FROM project_template_files WHERE id = @Id", new { Id = id });
            if (src == null)
                return NotFound(ApiResponse<int>.Fail("File sorgente non trovato."));

            // Sicurezza: il file su disco DEVE esistere — altrimenti non creo un record orfano.
            if (string.IsNullOrEmpty(src.DiskPath) || !System.IO.File.Exists(src.DiskPath))
                return StatusCode(500, ApiResponse<int>.Fail(
                    "File sorgente mancante su disco: copia annullata."));

            bool targetExists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_template_folders WHERE id = @Id", new { Id = req.FolderId }) > 0;
            if (!targetExists)
                return NotFound(ApiResponse<int>.Fail("Cartella destinazione non trovata."));

            string safeFileName = Path.GetFileName(src.FileName);

            // Nuovo record (disk_path provvisorio, poi aggiornato)
            int empId = GetCurrentEmployeeId();
            int newId = c.ExecuteScalar<int>(@"
                INSERT INTO project_template_files (folder_id, file_name, disk_path, file_size, uploaded_by)
                VALUES (@FolderId, @FileName, '', @FileSize, @UploadedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    req.FolderId,
                    FileName = safeFileName,
                    src.FileSize,
                    UploadedBy = empId > 0 ? (int?)empId : null
                });

            // Copia file su disco con nuovo nome {newId}_{filename}
            string templatesRoot = GetTemplatesRoot();
            Directory.CreateDirectory(templatesRoot);
            string newDiskFilename = $"{newId}_{safeFileName}";
            string newDiskPath = Path.Combine(templatesRoot, newDiskFilename);
            int counter = 1;
            while (System.IO.File.Exists(newDiskPath))
            {
                string stem = Path.GetFileNameWithoutExtension(safeFileName);
                string ext = Path.GetExtension(safeFileName);
                newDiskFilename = $"{newId}_{stem}_{counter}{ext}";
                newDiskPath = Path.Combine(templatesRoot, newDiskFilename);
                counter++;
            }

            try
            {
                System.IO.File.Copy(src.DiskPath, newDiskPath, overwrite: false);
            }
            catch
            {
                // Rollback DB se la copia disco fallisce, per evitare record orfano
                c.Execute("DELETE FROM project_template_files WHERE id = @Id", new { Id = newId });
                throw;
            }

            c.Execute("UPDATE project_template_files SET disk_path = @DiskPath WHERE id = @Id",
                new { DiskPath = newDiskPath, Id = newId });

            return Ok(ApiResponse<int>.Ok(newId, "File copiato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("folders/{id}/copy")]
    public IActionResult CopyFolder(int id, [FromBody] MoveOrCopyFileRequest req)
    {
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();

            // Carica la cartella sorgente
            var srcFolder = c.QuerySingleOrDefault<TemplateFolderNode>(@"
                SELECT id, parent_id AS ParentId, name, sort_order AS SortOrder
                FROM project_template_folders
                WHERE id = @Id AND is_active = TRUE", new { Id = id }, tx);
            if (srcFolder == null)
                return NotFound(ApiResponse<int>.Fail("Cartella sorgente non trovata."));

            // Verifica destinazione
            bool targetExists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_template_folders WHERE id = @Id AND is_active = TRUE", 
                new { Id = req.FolderId }, tx) > 0;
            if (!targetExists)
                return NotFound(ApiResponse<int>.Fail("Cartella destinazione non trovata."));

            // Copia ricorsiva
            int newFolderId = CopyFolderRecursive(c, tx, id, req.FolderId);

            tx.Commit();
            return Ok(ApiResponse<int>.Ok(newFolderId, "Cartella copiata con successo"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail($"Errore copia cartella: {ex.Message}"));
        }
    }

    private int CopyFolderRecursive(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx, int srcFolderId, int targetParentId)
    {
        // 1. Leggi cartella sorgente
        var src = c.QuerySingle<TemplateFolderNode>(@"
            SELECT id, name, sort_order AS SortOrder
            FROM project_template_folders
            WHERE id = @Id", new { Id = srcFolderId }, tx);

        // 2. Crea nuova cartella nella destinazione
        int maxSort = c.ExecuteScalar<int>(@"
            SELECT COALESCE(MAX(sort_order), 0)
            FROM project_template_folders
            WHERE parent_id = @ParentId AND is_active = TRUE",
            new { ParentId = targetParentId }, tx);

        int newFolderId = c.ExecuteScalar<int>(@"
            INSERT INTO project_template_folders (parent_id, name, sort_order)
            VALUES (@ParentId, @Name, @SortOrder);
            SELECT LAST_INSERT_ID()",
            new { ParentId = targetParentId, src.Name, SortOrder = maxSort + 1 }, tx);

        // 3. Copia tutti i file in questa cartella
        var files = c.Query<TemplateFileRow>(@"
            SELECT id, file_name AS FileName, file_size AS FileSize, disk_path AS DiskPath
            FROM project_template_files
            WHERE folder_id = @FolderId", new { FolderId = srcFolderId }, tx).ToList();

        string templatesRoot = GetTemplatesRoot();
        Directory.CreateDirectory(templatesRoot);

        foreach (var file in files)
        {
            // Genera record DB
            int empId = GetCurrentEmployeeId();
            int newFileId = c.ExecuteScalar<int>(@"
                INSERT INTO project_template_files (folder_id, file_name, disk_path, file_size, uploaded_by)
                VALUES (@FolderId, @FileName, '', @FileSize, @UploadedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    FolderId = newFolderId,
                    file.FileName,
                    file.FileSize,
                    UploadedBy = empId > 0 ? (int?)empId : null
                }, tx);

            // Copia file fisico su disco
            string newDiskFilename = $"{newFileId}_{file.FileName}";
            string newDiskPath = Path.Combine(templatesRoot, newDiskFilename);
            int counter = 1;
            while (System.IO.File.Exists(newDiskPath))
            {
                string stem = Path.GetFileNameWithoutExtension(file.FileName);
                string ext = Path.GetExtension(file.FileName);
                newDiskFilename = $"{newFileId}_{stem}_{counter}{ext}";
                newDiskPath = Path.Combine(templatesRoot, newDiskFilename);
                counter++;
            }

            try
            {
                if (System.IO.File.Exists(file.DiskPath))
                    System.IO.File.Copy(file.DiskPath, newDiskPath, overwrite: false);
            }
            catch
            {
                // Rollback DB se fallisce
                c.Execute("DELETE FROM project_template_files WHERE id = @Id", new { Id = newFileId }, tx);
                throw;
            }

            c.Execute("UPDATE project_template_files SET disk_path = @DiskPath WHERE id = @Id",
                new { DiskPath = newDiskPath, Id = newFileId }, tx);
        }

        // 4. Copia ricorsiva sotto-cartelle
        List<int> childFolderIds = c.Query<int>(@"
            SELECT id
            FROM project_template_folders
            WHERE parent_id = @ParentId AND is_active = TRUE",
            new { ParentId = srcFolderId }, tx).ToList();

        foreach (int childId in childFolderIds)
        {
            CopyFolderRecursive(c, tx, childId, newFolderId);
        }

        return newFolderId;
    }

    [HttpDelete("files/{id}")]
    public IActionResult DeleteFile(int id)
    {
        try
        {
            using var c = _db.Open();
            string? diskPath = c.ExecuteScalar<string?>(
                "SELECT disk_path FROM project_template_files WHERE id = @Id", new { Id = id });

            if (diskPath == null)
                return NotFound(ApiResponse<bool>.Fail("File non trovato."));

            c.Execute("DELETE FROM project_template_files WHERE id = @Id", new { Id = id });

            try { if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath); }
            catch { /* disco non bloccante */ }

            return Ok(ApiResponse<bool>.Ok(true, "File eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("folders/reorder")]
    public IActionResult ReorderFolders([FromBody] List<ReorderItem> items)
    {
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            foreach (ReorderItem item in items)
            {
                c.Execute("UPDATE project_template_folders SET sort_order = @SortOrder WHERE id = @Id",
                    new { item.SortOrder, item.Id }, tx);
            }
            tx.Commit();
            return Ok(ApiResponse<bool>.Ok(true, "Ordinamento aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    private sealed class TemplateFileRow
    {
        public int Id { get; set; }
        public int FolderId { get; set; }
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        public string DiskPath { get; set; } = "";
    }

    private List<string> CollectDiskPaths(MySqlConnector.MySqlConnection c, int folderId)
    {
        List<string> paths = c.Query<string>(
            "SELECT disk_path FROM project_template_files WHERE folder_id = @Id",
            new { Id = folderId }).ToList();

        List<int> childIds = c.Query<int>(
            "SELECT id FROM project_template_folders WHERE parent_id = @Id",
            new { Id = folderId }).ToList();

        foreach (int childId in childIds)
            paths.AddRange(CollectDiskPaths(c, childId));

        return paths;
    }
}
