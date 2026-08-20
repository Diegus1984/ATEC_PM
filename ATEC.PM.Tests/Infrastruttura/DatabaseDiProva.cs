using System.Text.Json;
using ATEC.PM.Server.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Database MySQL usa-e-getta per i test che non possono farne a meno (le migrazioni, per prime).
///
/// <para><b>Niente credenziali qui dentro.</b> La stringa di connessione arriva, in ordine:
/// dalla variabile d'ambiente <c>ATEC_PM_TEST_CS</c>, altrimenti da
/// <c>ATEC.PM.Server/appsettings.Development.json</c>, altrimenti da <c>appsettings.json</c>.
/// Così i test seguono la configurazione di chi li lancia e non aggiungono un secondo posto
/// da cui una password possa uscire.</para>
///
/// <para>Ogni istanza crea un database col proprio nome e lo <b>elimina alla fine</b>, anche se
/// il test fallisce. Il database di lavoro (<c>atec_pm</c>) non viene mai toccato.</para>
/// </summary>
public sealed class DatabaseDiProva : IDisposable
{
    public string Nome { get; }
    public string ConnectionString { get; }

    private readonly string _csServer;

    public DatabaseDiProva(string suffisso)
    {
        _csServer = LeggiConnectionStringServer();
        Nome = $"atec_pm_test_{suffisso}_{DateTime.UtcNow:HHmmssfff}";
        ConnectionString = new MySqlConnectionStringBuilder(_csServer) { Database = Nome }.ConnectionString;
        Elimina();
    }

    /// <summary>Il servizio vero, puntato al database di prova.</summary>
    public DbService Servizio(bool stopOnError = true, int lockTimeoutSeconds = 60)
    {
        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Migrations:StopOnError"] = stopOnError ? "true" : "false",
            ["Migrations:LockTimeoutSeconds"] = lockTimeoutSeconds.ToString(),
        }).Build();

        ILoggerFactory lf = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Error));
        return new DbService(cfg, lf.CreateLogger<DbService>());
    }

    public MySqlConnection Apri()
    {
        var c = new MySqlConnection(ConnectionString);
        c.Open();
        return c;
    }

    /// <summary>
    /// Le versioni <b>applicate davvero</b>, ordinate. <c>success = 1</c> come in
    /// <c>DbService.GetAppliedVersions</c>: dal 15/08/2026 una migrazione fallita lascia comunque
    /// la sua riga, con l'errore dentro, e non deve risultare applicata a nessuno.
    /// </summary>
    public List<int> VersioniApplicate()
    {
        using MySqlConnection c = Apri();
        return c.Query<int>("SELECT version FROM schema_migrations WHERE success = 1 ORDER BY version").ToList();
    }

    /// <summary>La riga del registro, comunque sia andata (null se quella versione non c'è).</summary>
    public RigaMigrazione? Riga(int versione)
    {
        using MySqlConnection c = Apri();
        return c.QueryFirstOrDefault<RigaMigrazione>(@"
            SELECT version AS Versione, description AS Descrizione, success AS Riuscita,
                   error_text AS Errore, duration_ms AS DurataMs
            FROM schema_migrations WHERE version = @V", new { V = versione });
    }

    /// <summary>Una riga di <c>schema_migrations</c> letta dai test.</summary>
    public sealed class RigaMigrazione
    {
        public int Versione { get; set; }
        public string Descrizione { get; set; } = "";
        public bool Riuscita { get; set; }
        public string? Errore { get; set; }
        public int? DurataMs { get; set; }
    }

    public string Descrizione(int versione)
    {
        using MySqlConnection c = Apri();
        return c.ExecuteScalar<string>(
            "SELECT description FROM schema_migrations WHERE version = @V", new { V = versione }) ?? "";
    }

    public int ContaTabelle()
    {
        using MySqlConnection c = Apri();
        return c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()");
    }

    /// <summary>Crea lo schema completo e aggiornato: il punto di partenza di quasi ogni test.</summary>
    public void CreaSchemaCompleto()
    {
        DbService db = Servizio();
        db.EnsureDatabaseExists();
        db.InitDatabase(productionMode: false);
    }

    /// <summary>
    /// Database con il <b>solo registro delle migrazioni</b>, senza le 119 tabelle
    /// dell'applicazione. Serve a chi prova il <c>MigrationRunner</c> con migrazioni finte:
    /// lo schema vero non lo guarda nessuno, e costruirlo costava 5 secondi per test.
    /// Chi prova le migrazioni VERE deve continuare a usare <see cref="CreaSchemaCompleto"/>.
    /// </summary>
    public void CreaSoloRegistroMigrazioni()
    {
        DbService db = Servizio();
        db.EnsureDatabaseExists();
        using MySqlConnection c = Apri();
        DbService.EnsureSchemaMigrationsTable(c);
    }

    public void Esegui(string sql)
    {
        using MySqlConnection c = Apri();
        c.Execute(sql);
    }

    public void Dispose() => Elimina();

    private void Elimina()
    {
        try
        {
            using var c = new MySqlConnection(_csServer);
            c.Open();
            c.Execute($"DROP DATABASE IF EXISTS `{Nome.Replace("`", "``")}`");
        }
        catch { /* la pulizia non deve mai far fallire un test */ }
    }

    // ── configurazione ────────────────────────────────────────────────────────

    /// <summary>true se un MySQL raggiungibile c'è: i test che ne hanno bisogno si saltano da soli.</summary>
    public static bool MySqlDisponibile()
    {
        try
        {
            using var c = new MySqlConnection(LeggiConnectionStringServer());
            c.Open();
            return true;
        }
        catch { return false; }
    }

    private static string LeggiConnectionStringServer()
    {
        string? cs = Environment.GetEnvironmentVariable("ATEC_PM_TEST_CS");
        if (string.IsNullOrWhiteSpace(cs)) cs = DaAppSettings();

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Nessuna stringa di connessione per i test. Impostare ATEC_PM_TEST_CS " +
                "(es. \"Server=localhost;Port=3306;User=...;Password=...;\") oppure lasciare " +
                "leggibile ATEC.PM.Server/appsettings.Development.json.");

        // Senza database: ogni test si crea il suo.
        return new MySqlConnectionStringBuilder(cs) { Database = "" }.ConnectionString;
    }

    private static string? DaAppSettings()
    {
        foreach (string nome in new[] { "appsettings.Development.json", "appsettings.json" })
        {
            string? percorso = CercaRisalendo(Path.Combine("ATEC.PM.Server", nome));
            if (percorso == null) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(percorso));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out JsonElement cs) &&
                    cs.TryGetProperty("Default", out JsonElement def))
                {
                    string? valore = def.GetString();
                    // In produzione la stringa è cifrata e sostituita a runtime: non serve ai test.
                    if (!string.IsNullOrWhiteSpace(valore) && !valore.StartsWith("RUN:") && !valore.StartsWith("__"))
                        return valore;
                }
            }
            catch { /* file illeggibile o non JSON: si prova il successivo */ }
        }
        return null;
    }

    private static string? CercaRisalendo(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, relativo);
            if (File.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        return null;
    }
}
