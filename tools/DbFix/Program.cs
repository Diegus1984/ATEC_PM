using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;

// Ripristina preventivi IMPIANTO da "converted" → "accepted" (pronto per nuova conversione).
// Opzionale: elimina la commessa collegata (stessa logica di HardDelete).

string serverBin = args.Length > 0 && args[0] == "--bin"
    ? (args.Length > 1 ? args[1] : FindServerBin())
    : FindServerBin();

string connectionString = LoadConnectionString(serverBin);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Connection string non trovata. Specifica: DbFix --bin \"path\\to\\ATEC.PM.Server\\bin\\Debug\\net8.0\"");
    return 1;
}

bool deleteProjects = args.Contains("--delete-project");

using MySqlConnection conn = new(connectionString);
conn.Open();

List<ConvertedQuoteRow> converted = conn.Query<ConvertedQuoteRow>(@"
    SELECT id AS Id, quote_number AS QuoteNumber, title AS Title, status AS Status,
           project_id AS ProjectId, converted_at AS ConvertedAt, quote_type AS QuoteType
    FROM quotes
    WHERE status = 'converted'
    ORDER BY converted_at DESC, id DESC").ToList();

if (converted.Count == 0)
{
    Console.WriteLine("Nessun preventivo con status 'converted' nel database.");
    return 0;
}

Console.WriteLine($"Trovati {converted.Count} preventivo/i convertito/i:\n");
foreach (ConvertedQuoteRow q in converted)
{
    string proj = q.ProjectId.HasValue ? q.ProjectId.Value.ToString() : "-";
    Console.WriteLine($"  [{q.Id}] {q.QuoteNumber} — {q.Title} (tipo {q.QuoteType}, commessa {proj}, convertito {q.ConvertedAt:yyyy-MM-dd HH:mm})");
}

// Se più di uno, ripristina tutti (l'utente ha detto "un preventivo" — in genere è uno solo)
Console.WriteLine();
Console.WriteLine(deleteProjects
    ? "Azione: ripristino preventivo/i + eliminazione commessa/e collegate."
    : "Azione: solo ripristino preventivo/i (commessa collegata resta nel DB).");

using MySqlTransaction tx = conn.BeginTransaction();
try
{
    foreach (ConvertedQuoteRow q in converted)
    {
        if (deleteProjects && q.ProjectId.HasValue)
            HardDeleteProject(conn, tx, q.ProjectId.Value);

        int n = conn.Execute(@"
            UPDATE quotes
            SET status = 'accepted', project_id = NULL, converted_at = NULL
            WHERE id = @Id AND status = 'converted'",
            new { q.Id }, tx);

        Console.WriteLine($"  OK preventivo {q.QuoteNumber} (id={q.Id}): {n} riga aggiornata → accepted");
    }

    tx.Commit();
    Console.WriteLine("\nCompletato. Nel client: Preventivi → stato «Il cliente ha confermato» → «Converti in Commessa».");
    return 0;
}
catch (Exception ex)
{
    tx.Rollback();
    Console.WriteLine($"ERRORE: {ex.Message}");
    return 1;
}

static string FindServerBin()
{
    string[] candidates =
    [
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ATEC.PM.Server", "bin", "Debug", "net8.0")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "ATEC.PM.Server", "bin", "Debug", "net8.0")),
        @"c:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM\ATEC.PM.Server\bin\Debug\net8.0"
    ];
    foreach (string path in candidates)
    {
        if (File.Exists(Path.Combine(path, "appsettings.Secrets.json")))
            return path;
    }
    return candidates[^1];
}

static string LoadConnectionString(string serverBin)
{
    string secretsPath = Path.Combine(serverBin, "appsettings.Secrets.json");
    if (!File.Exists(secretsPath))
        return "";

    string json = File.ReadAllText(secretsPath, Encoding.UTF8);
    Dictionary<string, string>? encrypted = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    if (encrypted == null || !encrypted.TryGetValue("ConnectionStrings:Default", out string? enc) || string.IsNullOrEmpty(enc))
        return "";

    byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
    return Encoding.UTF8.GetString(data);
}

static void HardDeleteProject(MySqlConnection c, MySqlTransaction tx, int projectId)
{
    dynamic? proj = c.QueryFirstOrDefault<dynamic>(
        "SELECT id, code FROM projects WHERE id=@Id", new { Id = projectId }, tx);
    if (proj == null)
    {
        Console.WriteLine($"    (commessa id={projectId} già assente)");
        return;
    }

    string code = (string)proj.code;
    Console.WriteLine($"    Eliminazione commessa {code} (id={projectId})...");

    c.Execute(@"
        DELETE nr FROM notification_recipients nr
        JOIN notifications n ON n.id = nr.notification_id
        WHERE n.project_id = @Pid", new { Pid = projectId }, tx);
    c.Execute("DELETE FROM notifications WHERE project_id = @Pid", new { Pid = projectId }, tx);
    c.Execute(@"
        DELETE te FROM timesheet_entries te
        JOIN project_phases pp ON pp.id = te.project_phase_id
        WHERE pp.project_id = @Pid", new { Pid = projectId }, tx);
    c.Execute(@"
        DELETE pa FROM phase_assignments pa
        JOIN project_phases pp ON pp.id = pa.project_phase_id
        WHERE pp.project_id = @Pid", new { Pid = projectId }, tx);
    c.Execute("DELETE FROM projects WHERE id = @Id", new { Id = projectId }, tx);
}

sealed class ConvertedQuoteRow
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public int? ProjectId { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public string QuoteType { get; set; } = "";
}
