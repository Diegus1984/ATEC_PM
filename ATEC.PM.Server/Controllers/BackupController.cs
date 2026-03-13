using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize]
public class BackupController : ControllerBase
{
    private readonly DbService _db;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupController> _log;

    public BackupController(DbService db, IConfiguration config, ILogger<BackupController> log)
    {
        _db = db;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Esegue un backup completo del database.
    /// </summary>
    [HttpPost("now")]
    public IActionResult RunBackup()
    {
        try
        {
            string path = ExecuteBackup("manual");
            return Ok(ApiResponse<string>.Ok(path, $"Backup completato: {Path.GetFileName(path)}"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Backup] Errore durante il backup");
            return StatusCode(500, ApiResponse<string>.Fail($"Errore backup: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lista dei backup disponibili.
    /// </summary>
    [HttpGet("list")]
    public IActionResult ListBackups()
    {
        string backupDir = GetBackupDir();
        if (!Directory.Exists(backupDir))
            return Ok(ApiResponse<List<object>>.Ok(new List<object>()));

        var files = Directory.GetFiles(backupDir, "atec_pm_*.sql")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new
            {
                FileName = f.Name,
                SizeMB = Math.Round(f.Length / 1024.0 / 1024.0, 2),
                Date = f.CreationTime.ToString("dd/MM/yyyy HH:mm:ss")
            })
            .ToList<object>();

        return Ok(ApiResponse<List<object>>.Ok(files));
    }

    /// <summary>
    /// Ripristina il database da un file di backup.
    /// ATTENZIONE: cancella tutti i dati attuali e li sostituisce col backup.
    /// </summary>
    [HttpPost("restore/{fileName}")]
    public IActionResult RestoreBackup(string fileName)
    {
        try
        {
            // Validazione nome file (sicurezza: no path traversal)
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                return BadRequest(ApiResponse<string>.Fail("Nome file non valido"));

            if (!fileName.EndsWith(".sql"))
                return BadRequest(ApiResponse<string>.Fail("Solo file .sql accettati"));

            string backupDir = GetBackupDir();
            string fullPath = Path.Combine(backupDir, fileName);

            if (!System.IO.File.Exists(fullPath))
                return NotFound(ApiResponse<string>.Fail($"File {fileName} non trovato"));

            // 1. Backup preventivo prima del ripristino
            string safetyBackup = ExecuteBackup("pre_restore");
            _log.LogInformation("[Restore] Backup preventivo creato: {Path}", safetyBackup);

            // 2. Leggi il file SQL
            string sql = System.IO.File.ReadAllText(fullPath, System.Text.Encoding.UTF8);

            // 3. Svuota tutte le tabelle nell'ordine corretto (FK)
            using var c = _db.Open();

            c.Execute("SET FOREIGN_KEY_CHECKS=0");

            var tables = c.Query<string>("SHOW TABLES").ToList();
            foreach (string table in tables)
            {
                c.Execute($"DELETE FROM `{table}`");
                // Reset auto_increment
                try { c.Execute($"ALTER TABLE `{table}` AUTO_INCREMENT = 1"); } catch { }
            }

            // 4. Esegui gli INSERT dal backup
            // Split per singolo statement (ogni riga che finisce con ;)
            var statements = sql.Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line)
                    && !line.TrimStart().StartsWith("--")
                    && !line.TrimStart().StartsWith("SET FOREIGN_KEY_CHECKS"))
                .Where(line => line.TrimEnd().EndsWith(";"));

            int insertCount = 0;
            int errorCount = 0;

            foreach (string stmt in statements)
            {
                try
                {
                    c.Execute(stmt);
                    insertCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _log.LogWarning("[Restore] Errore su statement: {Error}", ex.Message);
                }
            }

            c.Execute("SET FOREIGN_KEY_CHECKS=1");

            string msg = $"Ripristino completato: {insertCount} record importati";
            if (errorCount > 0)
                msg += $" ({errorCount} errori ignorati)";

            _log.LogInformation("[Restore] {Message} da {File}", msg, fileName);
            return Ok(ApiResponse<string>.Ok(safetyBackup, msg));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Restore] Errore durante il ripristino");
            return StatusCode(500, ApiResponse<string>.Fail($"Errore ripristino: {ex.Message}"));
        }
    }

    /// <summary>
    /// Elimina un file di backup.
    /// </summary>
    [HttpDelete("{fileName}")]
    public IActionResult DeleteBackup(string fileName)
    {
        if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            return BadRequest(ApiResponse<string>.Fail("Nome file non valido"));

        string fullPath = Path.Combine(GetBackupDir(), fileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(ApiResponse<string>.Fail("File non trovato"));

        System.IO.File.Delete(fullPath);
        return Ok(ApiResponse<string>.Ok("", "Backup eliminato"));
    }

    /// <summary>
    /// Scarica un file di backup.
    /// </summary>
    [HttpGet("download/{fileName}")]
    public IActionResult DownloadBackup(string fileName)
    {
        if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            return BadRequest("Nome file non valido");

        string fullPath = Path.Combine(GetBackupDir(), fileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound("File non trovato");

        byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
        return File(bytes, "application/sql", fileName);
    }

    // ══════════════════════════════════════════════════════════════
    // HELPER
    // ══════════════════════════════════════════════════════════════

    private string GetBackupDir()
    {
        return _config["Backup:Path"] ?? @"C:\ATEC_Backups";
    }

    /// <summary>
    /// Esegue il backup e restituisce il path del file creato.
    /// Usato sia dall'endpoint che dal backup automatico.
    /// </summary>
    public string ExecuteBackup(string prefix = "manual")
    {
        string backupDir = GetBackupDir();
        if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

        string filename = $"atec_pm_{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
        string fullPath = Path.Combine(backupDir, filename);

        using var c = _db.Open();
        var tables = c.Query<string>("SHOW TABLES").ToList();

        using var sw = new StreamWriter(fullPath, false, System.Text.Encoding.UTF8);
        sw.WriteLine($"-- ATEC PM Backup ({prefix}) {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sw.WriteLine($"-- Tabelle: {tables.Count}");
        sw.WriteLine();
        sw.WriteLine("SET FOREIGN_KEY_CHECKS=0;");
        sw.WriteLine();

        int totalRows = 0;
        foreach (string table in tables)
        {
            var rows = c.Query($"SELECT * FROM `{table}`").ToList();
            if (rows.Count == 0) continue;

            sw.WriteLine($"-- ── {table}: {rows.Count} righe ──");

            foreach (IDictionary<string, object> row in rows)
            {
                var cols = row.Keys.Select(k => $"`{k}`");
                var vals = row.Values.Select(FormatValue);
                sw.WriteLine($"INSERT INTO `{table}` ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)});");
            }
            sw.WriteLine();
            totalRows += rows.Count;
        }

        sw.WriteLine("SET FOREIGN_KEY_CHECKS=1;");
        sw.WriteLine($"-- Totale: {totalRows} righe");

        // Pulizia: tiene ultimi 30 backup manuali + 30 automatici
        CleanOldBackups(backupDir, "manual", 30);
        CleanOldBackups(backupDir, "auto", 30);
        CleanOldBackups(backupDir, "pre_restore", 10);

        _log.LogInformation("[Backup] {Prefix}: {File} — {Rows} righe, {Tables} tabelle",
            prefix, filename, totalRows, tables.Count);

        return fullPath;
    }

    private static string FormatValue(object v)
    {
        if (v == null || v is DBNull) return "NULL";
        if (v is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
        if (v is DateOnly d) return $"'{d:yyyy-MM-dd}'";
        if (v is bool b) return b ? "1" : "0";
        if (v is byte[] bytes) return $"X'{Convert.ToHexString(bytes)}'";
        if (v is decimal or int or long or double or float or short or byte)
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;

        string s = v.ToString()!
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n")
            .Replace("\t", "\\t");
        return $"'{s}'";
    }

    private static void CleanOldBackups(string dir, string prefix, int keep)
    {
        try
        {
            var old = Directory.GetFiles(dir, $"atec_pm_{prefix}_*.sql")
                .OrderByDescending(f => f)
                .Skip(keep);
            foreach (string f in old) System.IO.File.Delete(f);
        }
        catch { }
    }
}
