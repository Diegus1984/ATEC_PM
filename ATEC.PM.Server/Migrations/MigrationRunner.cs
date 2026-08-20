using System.Reflection;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Trova le migrazioni scritte come classi, le ordina e applica quelle che mancano.
///
/// <para><b>Non esiste un elenco da tenere aggiornato</b>: le migrazioni si scoprono
/// dall'assembly. Aggiungerne una vuol dire creare un file — niente costanti da alzare, niente
/// registri da modificare. È il rimedio alla trappola di <c>LatestSchemaVersion</c>, che andava
/// alzata a mano e, dimenticata, faceva saltare la migrazione **in silenzio e solo in
/// produzione** (successo con la v66 il 04/08/2026).</para>
///
/// <para><b>Dal 15/08/2026 le migrazioni sono tutte qui.</b> Le 87 storiche vivevano in blocchi
/// <c>if</c> dentro <c>DbService.ApplyVersionedMigrations</c> e sono state spostate una per una
/// (dalla più alta a scendere, per non invertire l'ordine di esecuzione durante il lavoro). Non
/// c'è più niente da eseguire prima del runner: l'ordine è quello dei numeri di versione, e
/// basta.</para>
/// </summary>
public class MigrationRunner
{
    private readonly ILogger _log;
    private readonly List<IMigrazione> _migrazioni;

    public MigrationRunner(ILogger log, IEnumerable<IMigrazione>? migrazioni = null)
    {
        _log = log;
        _migrazioni = (migrazioni ?? Scopri()).OrderBy(m => m.Versione).ToList();

        var doppie = _migrazioni.GroupBy(m => m.Versione).Where(g => g.Count() > 1).ToList();
        if (doppie.Count > 0)
            throw new InvalidOperationException(
                "Due migrazioni con lo stesso numero di versione: " +
                string.Join(", ", doppie.Select(g => $"v{g.Key} ({string.Join(" e ", g.Select(m => m.GetType().Name))})")) +
                ". I numeri di versione devono essere unici.");
    }

    /// <summary>Tutte le <see cref="IMigrazione"/> concrete di questo assembly.</summary>
    private static IEnumerable<IMigrazione> Scopri() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IMigrazione).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IMigrazione)Activator.CreateInstance(t)!);

    /// <summary>La versione più alta fra quelle spostate in classi (0 se non ce n'è ancora).</summary>
    public int VersioneMassima => _migrazioni.Count == 0 ? 0 : _migrazioni.Max(m => m.Versione);

    public IReadOnlyList<IMigrazione> Migrazioni => _migrazioni;

    /// <summary>
    /// Applica le migrazioni che non risultano in <paramref name="applied"/>, in ordine di
    /// versione, e registra ognuna — <b>riuscita o fallita</b> — con esito, durata ed errore.
    /// </summary>
    /// <param name="stopOnError">
    /// false = si prosegue col resto (comportamento d'emergenza). La versione fallita <b>resta
    /// pendente</b> e viene ritentata al riavvio successivo.
    /// </param>
    public EsitoMigrazioni Applica(MySqlConnection c, HashSet<int> applied, bool stopOnError)
    {
        List<IMigrazione> pendenti = _migrazioni.Where(m => !applied.Contains(m.Versione)).ToList();
        if (pendenti.Count == 0) return new EsitoMigrazioni(0, Array.Empty<int>());

        _log.LogInformation("[Migrations] {Count} migrazioni da applicare: {Elenco}",
            pendenti.Count, string.Join(", ", pendenti.Select(m => "v" + m.Versione)));

        int fatte = 0;
        List<int> facoltativeFallite = new();

        foreach (IMigrazione m in pendenti)
        {
            var orologio = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                m.Applica(c, _log);
                orologio.Stop();

                // La riga la scrive il runner, non la migrazione: una migrazione che dimentica di
                // registrarsi tornerebbe a girare a ogni avvio, per sempre.
                Registra(c, m, riuscita: true, errore: null, orologio.ElapsedMilliseconds);

                applied.Add(m.Versione);
                fatte++;
                _log.LogInformation("[Migration v{Versione}] {Descrizione} ({Durata} ms)",
                    m.Versione, m.Descrizione, orologio.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                orologio.Stop();
                _log.LogError(ex, "[Migration v{Versione}] FALLITA dopo {Durata} ms: {Messaggio}",
                    m.Versione, orologio.ElapsedMilliseconds, ex.Message);

                // L'errore resta scritto nel database, non solo in un log che si ruota: al
                // riavvio dopo, chi assiste il gestionale legge da `schema_migrations` quale
                // migrazione si è rotta e perché. La riga ha success = 0, quindi la versione
                // resta pendente e viene ritentata.
                Registra(c, m, riuscita: false, errore: ex.Message, orologio.ElapsedMilliseconds);

                // Le migrazioni FACOLTATIVE (pulizie di dati orfani) non fermano niente: il
                // gestionale funziona lo stesso, e bloccare l'avvio dell'azienda per una pulizia
                // non riuscita sarebbe una cura peggiore del male. Restano pendenti e si
                // ritentano da sole al riavvio dopo.
                if (m.Facoltativa)
                {
                    facoltativeFallite.Add(m.Versione);
                    _log.LogWarning(
                        "[Migration v{Versione}] È FACOLTATIVA ({Descrizione}): l'avvio prosegue, la versione resta pendente e si ritenta al prossimo riavvio.",
                        m.Versione, m.Descrizione);
                    continue;
                }

                if (stopOnError)
                    throw new InvalidOperationException(
                        $"Migrazione v{m.Versione} ({m.GetType().Name}) fallita: {ex.Message}. " +
                        "L'avvio è interrotto per non lavorare su uno schema incompleto. In emergenza, " +
                        "per ripartire subito, impostare Migrations:StopOnError=false in appsettings.json.", ex);

                _log.LogWarning("[Migration v{Versione}] Errore ignorato (StopOnError=false): resta pendente.",
                    m.Versione);
            }
        }

        return new EsitoMigrazioni(fatte, facoltativeFallite);
    }

    /// <summary>
    /// Scrive la riga di <c>schema_migrations</c>: esito, durata e — se è andata male — il
    /// messaggio dell'errore.
    /// <para><b>Non solleva mai.</b> È il caso della migrazione fallita perché il database non
    /// risponde più: lì anche questa scrittura fallirebbe, e lasciarla propagare
    /// <b>sostituirebbe</b> l'errore vero con «connessione persa», facendo cercare il guasto nel
    /// posto sbagliato.</para>
    /// <para><c>ON DUPLICATE KEY UPDATE</c> e non <c>INSERT IGNORE</c>: al ritentativo la riga
    /// del fallimento c'è già, e deve essere sovrascritta dall'esito nuovo — altrimenti una
    /// migrazione riparata resterebbe segnata come rotta per sempre.</para>
    /// </summary>
    private void Registra(MySqlConnection c, IMigrazione m, bool riuscita, string? errore, long durataMs)
    {
        try
        {
            c.Execute(@"
                INSERT INTO schema_migrations (version, description, success, error_text, duration_ms)
                VALUES (@V, @D, @S, @E, @Ms)
                ON DUPLICATE KEY UPDATE
                    description = VALUES(description),
                    success     = VALUES(success),
                    error_text  = VALUES(error_text),
                    duration_ms = VALUES(duration_ms),
                    applied_at  = CURRENT_TIMESTAMP",
                new
                {
                    V = m.Versione,
                    D = Tronca(m.Descrizione, 200),
                    S = riuscita ? 1 : 0,
                    E = riuscita ? null : Tronca(errore ?? "", 4000),
                    Ms = (int)Math.Min(durataMs, int.MaxValue),
                });
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "[Migration v{Versione}] Registrazione dell'esito non riuscita: {Messaggio}. " +
                "L'esito vero della migrazione è quello scritto qui sopra.", m.Versione, ex.Message);
        }
    }

    /// <summary>La colonna description è VARCHAR(200): una descrizione più lunga farebbe fallire la registrazione.</summary>
    private static string Tronca(string testo, int max) =>
        string.IsNullOrEmpty(testo) || testo.Length <= max ? testo ?? "" : testo[..max];
}

/// <summary>
/// Cos'è successo in un giro di migrazioni. <see cref="FacoltativeFallite"/> serve a chi controlla
/// che alla fine non resti nessun buco: quelle versioni <b>mancano apposta</b> e non devono far
/// fallire l'avvio.
/// </summary>
public sealed record EsitoMigrazioni(int Applicate, IReadOnlyList<int> FacoltativeFallite);
