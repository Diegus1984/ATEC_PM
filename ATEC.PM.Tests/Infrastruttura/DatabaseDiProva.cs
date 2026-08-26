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
///
/// <para>«Alla fine» però vale solo se il processo ci arriva: se l'host dei test va in crash o
/// lo si interrompe, il <c>Dispose</c> non gira e il database resta. Per questo alla prima
/// creazione parte anche uno <b>spazzino</b> (<c>PuliziaResidui</c>) che raccoglie i residui
/// delle corse morte, e solo quelli fermi da almeno <see cref="EtaMinimaResiduo"/>: così una
/// corsa viva non viene mai toccata.</para>
/// </summary>
public sealed class DatabaseDiProva : IDisposable
{
    public string Nome { get; }
    public string ConnectionString { get; }

    private readonly string _csServer;

    public DatabaseDiProva(string suffisso)
    {
        _csServer = LeggiConnectionStringServer();
        Nome = ComponiNome(suffisso, DateTime.UtcNow);
        ConnectionString = new MySqlConnectionStringBuilder(_csServer) { Database = Nome }.ConnectionString;
        _ = _spazzino.Value;   // una volta sola per processo, prima di aggiungerne un altro
        Elimina();
    }

    /// <summary>
    /// Nome del database di prova: prefisso riconoscibile, suffisso del test, orario.
    ///
    /// <para><b>TRAPPOLA: resta corto apposta, solo l'orario e niente data.</b> Il lock delle
    /// migrazioni è <c>atec_pm_migrate:</c> + nome del database, e MySQL rifiuta i nomi di lock
    /// oltre i 64 caratteri: per questo <see cref="DbService.NomeLockMigrazioni"/> <b>tronca</b>.
    /// Col suffisso più lungo in casa (<c>ordine_commesse_chiusa</c>, 22 caratteri) si sta a 61
    /// su 64; aggiungendo la data si arriverebbe a 70, il troncamento si mangerebbe proprio i
    /// millisecondi finali e due corse parallele dello stesso test finirebbero sullo <b>stesso
    /// lock</b>, cioè una delle due non riuscirebbe a migrare e si fermerebbe. Per sapere di
    /// quando è un residuo il nome non serve: lo spazzino legge la data vera di creazione delle
    /// tabelle. Sorvegliato da <c>NomeDatabaseDiProvaTests</c>.</para>
    /// </summary>
    public static string ComponiNome(string suffisso, DateTime quando) =>
        $"atec_pm_test_{suffisso}_{quando:HHmmssfff}";

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
        catch (Exception ex)
        {
            // La pulizia non deve mai far fallire un test, ma nemmeno sparire in silenzio: un
            // DROP che non riesce e non dice niente è il modo in cui i residui diventano decine
            // senza che nessuno se ne accorga. Lo spazzino lo riprenderà alla prossima corsa;
            // intanto almeno resta scritto a video.
            Console.Error.WriteLine($"[DatabaseDiProva] non sono riuscito a eliminare {Nome}: {ex.Message}");
        }
    }

    // ── spazzino dei residui ──────────────────────────────────────────────────

    /// <summary>
    /// Ogni istanza si cancella da sola nel <see cref="Dispose"/>, ma quando il <b>processo
    /// muore</b> (host dei test in crash, corsa interrotta a mano) quel Dispose non gira mai e
    /// il database resta lì per sempre. Nessuno lo raccoglieva: il 24/08/2026 se n'erano
    /// accumulati <b>71</b>, il più vecchio del 16/08, ~119 tabelle l'uno. Un processo morto non
    /// può ripulire dopo di sé: a raccogliere deve essere quello dopo.
    ///
    /// <para><c>Lazy</c> statico: gira <b>una volta sola per processo</b>, alla creazione del
    /// primo database di prova, e non rifà il giro a ogni test.</para>
    /// </summary>
    private static readonly Lazy<bool> _spazzino =
        new(() => { PuliziaResidui(EtaMinimaResiduo); return true; }, isThreadSafe: true);

    /// <summary>
    /// Quanto deve essere vecchio un residuo prima di poterlo togliere di mezzo. La suite intera
    /// dura ~3 minuti: due ore sono un margine tale che <b>una corsa viva non viene mai
    /// toccata</b>, nemmeno quando due sessioni provano i test insieme sulla stessa macchina
    /// (succede: è successo il 24/08/2026).
    /// </summary>
    public static readonly TimeSpan EtaMinimaResiduo = TimeSpan.FromHours(2);

    /// <summary>
    /// Decide se un database di prova è un residuo da togliere. Sta a parte, pura e pubblica,
    /// perché la regola che conta è quella di sicurezza (<b>mai toccare roba recente</b>) e si
    /// prova senza database.
    /// </summary>
    /// <param name="ultimaTabella">Creazione della tabella più recente dello schema; <c>null</c>
    /// se lo schema non ha (ancora) tabelle.</param>
    /// <remarks>
    /// L'età si legge dalle <b>tabelle</b>, non dal nome: il nome porta il solo orario, senza
    /// data (vedi <see cref="ComponiNome"/>), e comunque un residuo lasciato da una versione
    /// diversa dei test deve restare riconoscibile. Schema senza tabelle: si lascia stare, è la
    /// finestra di pochi millisecondi fra CREATE DATABASE e la prima tabella, cioè molto
    /// probabilmente una corsa appena nata.
    /// </remarks>
    public static bool ResiduoDaTogliere(DateTime? ultimaTabella, DateTime adesso, TimeSpan etaMinima)
    {
        if (ultimaTabella is null) return false;
        return adesso - ultimaTabella.Value >= etaMinima;
    }

    /// <summary>
    /// Toglie i database <c>atec_pm_test_*</c> lasciati indietro da corse morte e ritorna
    /// quanti ne ha tolti. Non fallisce mai: se MySQL non c'è, o un DROP non riesce, i test
    /// devono partire lo stesso.
    /// </summary>
    /// <param name="etaMinima">Da quanto deve essere fermo un residuo per poterlo togliere.</param>
    /// <param name="soloPrefisso">Se valorizzato, guarda i soli schemi che cominciano così.
    /// Serve ai test dello spazzino, che devono poter usare una soglia corta senza rischiare
    /// di passare sopra ai database delle altre collection in corso.</param>
    /// <exception cref="ArgumentException">Soglia più corta di <see cref="EtaMinimaResiduo"/>
    /// senza un prefisso che limiti il campo: è la combinazione che cancellerebbe il lavoro di
    /// una corsa viva, e non deve essere possibile scriverla per sbaglio.</exception>
    public static int PuliziaResidui(TimeSpan etaMinima, string? soloPrefisso = null)
    {
        if (etaMinima < EtaMinimaResiduo && soloPrefisso is null)
            throw new ArgumentException(
                $"soglia {etaMinima} sotto il minimo di sicurezza ({EtaMinimaResiduo}) senza un " +
                "prefisso: così si cancellano i database delle corse ancora vive.", nameof(etaMinima));

        try
        {
            using var c = new MySqlConnection(LeggiConnectionStringServer());
            c.Open();

            // Una riga per schema di prova con la data dell'ultima tabella creata (NULL se non
            // ne ha). LEFT JOIN e non IN (...): serve anche lo schema vuoto, per saperlo.
            // Alias `NomeSchema` e non `Schema`: SCHEMA è parola chiave di MySQL, ed è il tipo
            // di dettaglio che fa fallire la query solo su certe versioni.
            // Il prefisso si confronta con LEFT(...) e non con LIKE: un nome di schema è pieno
            // di underscore, che in LIKE valgono «un carattere qualsiasi».
            List<(string NomeSchema, DateTime? Ultima)> candidati = c.Query<(string NomeSchema, DateTime? Ultima)>(@"
                SELECT s.schema_name AS NomeSchema, MAX(t.create_time) AS Ultima
                FROM information_schema.schemata s
                LEFT JOIN information_schema.tables t ON t.table_schema = s.schema_name
                WHERE s.schema_name LIKE 'atec\_pm\_test\_%'
                  AND (@Prefisso IS NULL OR LEFT(s.schema_name, CHAR_LENGTH(@Prefisso)) = @Prefisso)
                GROUP BY s.schema_name", new { Prefisso = soloPrefisso }).ToList();

            // «Adesso» lo dà MySQL, non l'orologio del processo: create_time è nel fuso del
            // server, e confrontarlo con un DateTime.Now locale sarebbe giusto solo finché
            // database e test stanno sulla stessa macchina.
            DateTime adesso = c.ExecuteScalar<DateTime>("SELECT NOW()");
            int tolti = 0;
            foreach ((string schema, DateTime? ultima) in candidati)
            {
                if (!ResiduoDaTogliere(ultima, adesso, etaMinima)) continue;
                try
                {
                    c.Execute($"DROP DATABASE IF EXISTS `{schema.Replace("`", "``")}`");
                    tolti++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DatabaseDiProva] residuo {schema} non eliminato: {ex.Message}");
                }
            }

            if (tolti > 0)
                Console.WriteLine($"[DatabaseDiProva] tolti {tolti} database di prova rimasti da corse interrotte.");
            return tolti;
        }
        catch
        {
            // Nessun MySQL raggiungibile (i test che lo richiedono si salteranno da soli), o
            // permessi insufficienti: non è un motivo per non far partire la suite.
            return 0;
        }
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
