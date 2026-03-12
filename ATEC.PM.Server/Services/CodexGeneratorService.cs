using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Code generator for Codex items with queue-based concurrency control.
/// Format: [PREFIX]ddMMyy.NNN
/// Example: 101280226.001, 101280226.002, etc
/// </summary>
public class CodexGeneratorService
{
    private readonly DbService _db;
    private readonly ILogger<CodexGeneratorService> _log;

    public CodexGeneratorService(DbService db, ILogger<CodexGeneratorService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Generates a new unique Codex code using queue-based locking to prevent duplicates.
    /// </summary>
    /// <param name="prefix">E.g: 101, 102, 201, 301, etc</param>
    /// <param name="description">Item description</param>
    /// <returns>Generated code and inserted item ID</returns>
    public (string Code, int Id) GenerateNewCodex(string prefix, string description)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix is required", nameof(prefix));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));

        using var conn = _db.Open();
        using var transaction = conn.BeginTransaction();
        try
        {
            // 1. Insert into queue with PENDING status
            int queueId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_generation_queue (prefix, date_requested, status)
                VALUES (@Prefix, @Date, 'PENDING')
                ; SELECT LAST_INSERT_ID()",
                new { Prefix = prefix, Date = DateTime.Today }, transaction);

            // 2. Acquire lock
            conn.ExecuteScalar("SELECT GET_LOCK(@LockKey, 30)",
                new { LockKey = $"codex_queue_{prefix}" }, transaction);

            // 3. Verify this is first in queue
            int firstInQueue = conn.ExecuteScalar<int>(@"
                SELECT id FROM codex_generation_queue 
                WHERE prefix = @Prefix AND status = 'PENDING' 
                ORDER BY created_at ASC LIMIT 1",
                new { Prefix = prefix }, transaction);

            if (firstInQueue != queueId)
                throw new InvalidOperationException("Request is not first in queue");

            // 4. Generate code
            string today = DateTime.Now.ToString("ddMMyy");
            string codePrefix = $"{prefix}{today}";

            string maxCode = conn.ExecuteScalar<string>(@"
                SELECT MAX(codice) FROM codex_items 
                WHERE codice LIKE @Pattern",
                new { Pattern = codePrefix + ".%" }, transaction);

            int nextSequence = 1;
            if (!string.IsNullOrEmpty(maxCode))
            {
                // Extract sequence (last 3 digits after dot)
                int dotIndex = maxCode.LastIndexOf('.');
                if (dotIndex >= 0 && dotIndex < maxCode.Length - 1)
                {
                    string suffix = maxCode.Substring(dotIndex + 1);
                    if (int.TryParse(suffix, out int lastSeq))
                        nextSequence = lastSeq + 1;
                }
            }

            string newCode = $"{codePrefix}.{nextSequence:D3}";

            // 5. Insert code into codex_items
            int newId = conn.ExecuteScalar<int>(@"
                INSERT INTO codex_items 
                (remote_id, codice, descr, code_forn, fornitore, prezzo_forn, iva, 
                 produttore, data, note, categoria, barcode, tipologia, 
                 extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn)
                VALUES 
                (0, @Code, @Description, '', '', 0, '', '', @Date, '', '', '', '', '', '', '', '', '', 0, '', '', '')
                ; SELECT LAST_INSERT_ID()",
                new { Code = newCode, Description = description, Date = DateTime.Today }, transaction);

            // 6. Update queue to COMPLETED
            conn.Execute(@"
                UPDATE codex_generation_queue 
                SET status = 'COMPLETED', code_generated = @Code 
                WHERE id = @Id",
                new { Code = newCode, Id = queueId }, transaction);

            transaction.Commit();
            
            // Release lock
            conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)",
                new { LockKey = $"codex_queue_{prefix}" });

            _log.LogInformation($"[CodexGenerator] Generated code {newCode} (ID: {newId})");

            return (newCode, newId);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            try
            {
                conn.ExecuteScalar("SELECT RELEASE_LOCK(@LockKey)",
                    new { LockKey = $"codex_queue_{prefix}" });
            }
            catch { }
            
            _log.LogError(ex, "[CodexGenerator] Error generating code");
            throw;
        }
    }

    /// <summary>
    /// Returns available prefixes with descriptions.
    /// </summary>
    public List<(string Code, string Description)> GetAvailablePrefixes()
    {
        return new List<(string, string)>
        {
            ("101", "Generic design part"),
            ("102", "Stamped design part"),
            ("201", "Generic commercial part"),
            ("202", "Reworked commercial part"),
            ("203", "Robot commercial part"),
            ("301", "Fastening element"),
            ("401", "Raw material"),
            ("501", "Generic mechanical group"),
            ("601", "Generic mechanical assembly"),
            ("701", "Generic mechanical layout")
        };
    }
}
