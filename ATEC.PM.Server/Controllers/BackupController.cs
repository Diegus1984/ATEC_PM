using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/backup")]
// Solo a chi ha la funzione «Backup DB»: questi endpoint creano/cancellano backup e possono
// RIPRISTINARE l'intero database (restore svuota tutte le tabelle). Il solo
// [Authorize] non basta mai qui: serve sempre anche la chiave.
[Authorize]
[RequireFeature("nav.backup")]
public class BackupController : ControllerBase
{
    private readonly DbService _db;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupController> _log;
    private readonly FullBackupService _full;

    public BackupController(DbService db, IConfiguration config, ILogger<BackupController> log, FullBackupService full)
    {
        _db = db;
        _config = config;
        _log = log;
        _full = full;
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

            // 2. Svuota le tabelle e riesegue gli INSERT del backup (stessa procedura del
            //    ripristino dal pacchetto completo).
            using var sr = new StreamReader(fullPath, System.Text.Encoding.UTF8);
            (int insertCount, int errorCount) = _full.RipristinaDatabase(sr);

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
    // BACKUP COMPLETO (database + cartelle) — pacchetto .zip
    // ══════════════════════════════════════════════════════════════

    /// <summary>Cosa finirebbe nel pacchetto e quanto peserebbe, senza crearlo.</summary>
    [HttpGet("full/stima")]
    public IActionResult StimaPacchetto() => Ok(ApiResponse<object>.Ok(_full.StimaDimensione()));

    [HttpGet("full/list")]
    public IActionResult ListaPacchetti() => Ok(ApiResponse<List<object>>.Ok(_full.ListaPacchetti()));

    /// <summary>Avvia la creazione del pacchetto completo. Torna subito: l'avanzamento
    /// si segue con full/stato/{id} (con GB di documenti ci vogliono minuti).</summary>
    [HttpPost("full/start")]
    public IActionResult AvviaPacchetto()
    {
        FullBackupService.BackupJob job = _full.AvviaBackup();
        return Ok(ApiResponse<object>.Ok(job, "Backup completo avviato"));
    }

    [HttpGet("full/stato/{jobId}")]
    public IActionResult StatoOperazione(string jobId)
    {
        FullBackupService.BackupJob? job = _full.GetJob(jobId);
        if (job == null) return NotFound(ApiResponse<string>.Fail("Operazione non trovata"));
        return Ok(ApiResponse<object>.Ok(job));
    }

    /// <summary>Operazione in corso (se c'è): serve alla pagina per riagganciarsi
    /// all'avanzamento dopo un F5 o da un altro computer.</summary>
    [HttpGet("full/stato-corrente")]
    public IActionResult OperazioneCorrente()
    {
        FullBackupService.BackupJob? job = _full.GetJobAttivo();
        return Ok(ApiResponse<object?>.Ok(job));
    }

    /// <summary>
    /// Ripristina database e/o cartelle da un pacchetto completo.
    /// ATTENZIONE: sostituisce i dati attuali. Le cartelle esistenti non vengono
    /// cancellate ma spostate accanto con suffisso ".prima-ripristino-<data>".
    /// </summary>
    [HttpPost("full/restore/{fileName}")]
    public IActionResult RipristinaPacchetto(string fileName, [FromQuery] bool database = true, [FromQuery] bool file = true)
    {
        if (!NomeFileValido(fileName, ".zip"))
            return BadRequest(ApiResponse<string>.Fail("Nome file non valido"));
        if (!database && !file)
            return BadRequest(ApiResponse<string>.Fail("Non è stato scelto nulla da ripristinare"));
        if (!System.IO.File.Exists(Path.Combine(_full.GetPackageDir(), fileName)))
            return NotFound(ApiResponse<string>.Fail($"Pacchetto {fileName} non trovato"));

        // Backup preventivo del database: se il ripristino è sbagliato si torna indietro.
        string? sicurezza = null;
        if (database)
        {
            sicurezza = ExecuteBackup("pre_restore");
            _log.LogInformation("[RipristinoCompleto] Backup preventivo: {Path}", sicurezza);
        }

        FullBackupService.BackupJob job = _full.AvviaRipristino(fileName, database, file, sicurezza);
        return Ok(ApiResponse<object>.Ok(job, "Ripristino avviato"));
    }

    /// <summary>
    /// Carica un pacchetto creato su un'altra macchina (tipicamente: backup fatto sul PC
    /// di sviluppo e ripristinato qui). Senza questo il ripristino potrebbe usare solo i
    /// pacchetti già presenti sul server.
    /// </summary>
    [HttpPost("full/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> CaricaPacchetto(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.Fail("Nessun file ricevuto"));

        string nome = Path.GetFileName(file.FileName);
        if (!NomeFileValido(nome, ".zip"))
            return BadRequest(ApiResponse<string>.Fail("Serve un pacchetto .zip"));

        string dir = _full.GetPackageDir();

        // Nome già visto: si aggiunge un suffisso invece di sovrascrivere un backup esistente.
        string destinazione = Path.Combine(dir, nome);
        int n = 1;
        while (System.IO.File.Exists(destinazione))
        {
            destinazione = Path.Combine(dir,
                $"{Path.GetFileNameWithoutExtension(nome)}_({n++}){Path.GetExtension(nome)}");
        }

        // Si scrive prima come .parziale: un caricamento interrotto non deve lasciare in
        // elenco un pacchetto rotto che sembra buono.
        string temporaneo = destinazione + ".parziale";
        try
        {
            await using (FileStream fs = System.IO.File.Create(temporaneo))
            {
                await file.CopyToAsync(fs);
            }

            // Controllo minimo: dev'essere davvero un pacchetto di ATEC PM.
            using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(temporaneo))
            {
                if (zip.GetEntry("database.sql") == null && zip.GetEntry("manifest.json") == null)
                    throw new InvalidOperationException(
                        "Il file non sembra un pacchetto di backup di ATEC PM (manca database.sql).");
            }

            System.IO.File.Move(temporaneo, destinazione);
        }
        catch (Exception ex)
        {
            try { if (System.IO.File.Exists(temporaneo)) System.IO.File.Delete(temporaneo); } catch { }
            _log.LogWarning(ex, "[BackupCompleto] Caricamento pacchetto non riuscito");
            return BadRequest(ApiResponse<string>.Fail($"Caricamento non riuscito: {ex.Message}"));
        }

        var info = new FileInfo(destinazione);
        _log.LogInformation("[BackupCompleto] Pacchetto caricato: {File} ({MB} MB)",
            info.Name, Math.Round(info.Length / 1024.0 / 1024.0, 1));

        return Ok(ApiResponse<string>.Ok(info.Name,
            $"Pacchetto caricato: {info.Name} ({Math.Round(info.Length / 1024.0 / 1024.0, 1)} MB)"));
    }

    [HttpDelete("full/{fileName}")]
    public IActionResult EliminaPacchetto(string fileName)
    {
        if (!NomeFileValido(fileName, ".zip"))
            return BadRequest(ApiResponse<string>.Fail("Nome file non valido"));

        string percorso = Path.Combine(_full.GetPackageDir(), fileName);
        if (!System.IO.File.Exists(percorso)) return NotFound(ApiResponse<string>.Fail("Pacchetto non trovato"));

        System.IO.File.Delete(percorso);
        return Ok(ApiResponse<string>.Ok("", "Pacchetto eliminato"));
    }

    /// <summary>Scarica il pacchetto. In streaming: può pesare GB, caricarlo in memoria
    /// metterebbe in ginocchio il server.</summary>
    [HttpGet("full/download/{fileName}")]
    public IActionResult ScaricaPacchetto(string fileName)
    {
        if (!NomeFileValido(fileName, ".zip")) return BadRequest("Nome file non valido");

        string percorso = Path.Combine(_full.GetPackageDir(), fileName);
        if (!System.IO.File.Exists(percorso)) return NotFound("Pacchetto non trovato");

        return PhysicalFile(percorso, "application/zip", fileName, enableRangeProcessing: true);
    }

    private static bool NomeFileValido(string fileName, string estensione) =>
        !fileName.Contains("..") && !fileName.Contains('/') && !fileName.Contains('\\')
        && fileName.EndsWith(estensione, StringComparison.OrdinalIgnoreCase);

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

        int totalRows;
        int tableCount;
        using (var sw = new StreamWriter(fullPath, false, System.Text.Encoding.UTF8))
        {
            // Stessa scrittura usata dal backup completo: un solo posto dove sta la
            // logica del dump (e quindi un solo formato da mantenere).
            (tableCount, totalRows) = _full.ScriviDumpDatabase(sw, prefix);
        }

        // Pulizia: tiene ultimi 30 backup manuali + 30 automatici
        CleanOldBackups(backupDir, "manual", 30, _log);
        CleanOldBackups(backupDir, "auto", 30, _log);
        CleanOldBackups(backupDir, "pre_restore", 10, _log);

        _log.LogInformation("[Backup] {Prefix}: {File} — {Rows} righe, {Tables} tabelle",
            prefix, filename, totalRows, tableCount);

        return fullPath;
    }

    private static void CleanOldBackups(string dir, string prefix, int keep, ILogger log)
    {
        try
        {
            var old = Directory.GetFiles(dir, $"atec_pm_{prefix}_*.sql")
                .OrderByDescending(f => f)
                .Skip(keep);
            foreach (string f in old) System.IO.File.Delete(f);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[Backup] Impossibile eliminare vecchi file di backup con prefisso {Prefix} in {Dir}", prefix, dir);
        }
    }
}
