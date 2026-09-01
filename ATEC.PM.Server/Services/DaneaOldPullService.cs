using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// #129: ripescaggio automatico dal VECCHIO archivio Danea. Finche' un collega codifica
/// ancora li', gli articoli nati dopo lo spartiacque (cursore in app_config) vengono
/// trasferiti in Atec_PM da soli — di default ogni 12 ore — piu' il pulsante in pagina.
///
/// Il criterio e' IDArticolo &gt; cursore, NON «codice mancante»: cosi' non si toccano
/// gli articoli storici mai trasferiti (la migrazione resta selettiva). Al primo giro si
/// fissa solo lo spartiacque al MAX corrente: l'arretrato gia' codificato dal collega
/// si trasferisce UNA volta a mano dalla griglia («Piu' recenti prima»).
///
/// Nello stesso giro passa anche lo SPECCHIO PREZZI (01/09/2026): per gli articoli gia'
/// trasferiti il prezzo torna a seguire il vecchio archivio. Il trasferimento lo copia
/// una volta sola e i ritocchi successivi restavano indietro — FCA00017733 stava a
/// Catalogo 10,64 quando in Danea era gia' 8,78. Solo i campi prezzo: il resto
/// dell'articolo, codice ATEC in testa, resta di Atec_PM. Gira anche quando non c'e'
/// nessun articolo nuovo, e si spegne con DaneaSync:MirrorPricesFromOld=false.
///
/// Errori: un articolo fallito che esiste ancora nel vecchio tiene il cursore fermo
/// prima di se' e si ritenta al giro dopo (i gia' passati vengono skippati per codice);
/// un articolo sparito dal vecchio (ErroreArticoloAssente) non blocca il cursore.
/// Riserva nota: lo skip e' per CODICE — un articolo oltre un cursore fermo che viene
/// RIcodificato nel vecchio risulta nuovo e produce un doppione (limite del dedup).
/// </summary>
public class DaneaOldPullService : BackgroundService
{
    private const string CursorKey = "danea_old_pull_last_seen_id";
    private const int BatchSize = 500;   // stesso tetto del trasferimento manuale

    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<DaneaOldPullService> _log;
    private readonly DaneaMigrationService _migration;
    private readonly DaneaSyncService _sync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval;

    public static DateTime? LastRun { get; private set; }
    public static string LastMessage { get; private set; } = "Mai eseguito";
    public static string? LastError { get; private set; }
    public static bool IsRunning { get; private set; }

    public DaneaOldPullService(
        IServiceProvider sp, IConfiguration config, ILogger<DaneaOldPullService> log,
        DaneaMigrationService migration, DaneaSyncService sync)
    {
        _sp = sp;
        _config = config;
        _log = log;
        _migration = migration;
        _sync = sync;
        int hours = int.TryParse(config["DaneaSync:OldPullIntervalHours"], out int h) ? h : 12;
        // Task.Delay esplode oltre ~49 giorni: un valore assurdo in config non deve
        // buttare giu' il server (BackgroundService in fault = StopHost).
        if (hours > 720) hours = 720;
        _interval = TimeSpan.FromHours(hours);
    }

    /// <summary>Lo specchio prezzi si spegne da config, senza toccare il ripescaggio.</summary>
    public bool MirrorEnabled =>
        !bool.TryParse(_config["DaneaSync:MirrorPricesFromOld"], out bool on) || on;

    public bool Enabled =>
        _interval > TimeSpan.Zero
        && !string.IsNullOrEmpty(_config["DaneaSync:EftFilePathOld"])
        // Con Services:DaneaSync=false il servizio non viene proprio ospitato.
        && (!bool.TryParse(_config["Services:DaneaSync"], out bool svc) || svc);

    public int IntervalHours => (int)_interval.TotalHours;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_interval <= TimeSpan.Zero)
        {
            _log.LogInformation("[DaneaOldPull] Disattivato (DaneaSync:OldPullIntervalHours <= 0).");
            return;
        }
        // Dopo il primo giro dello specchio (parte a +15s): qui non c'e' fretta.
        await Task.Delay(TimeSpan.FromSeconds(90), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnce();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.LogError(ex, "[DaneaOldPull] Giro fallito");
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<DaneaPullStatus> GetStatus()
    {
        long? cursor = await ReadCursor();
        return new DaneaPullStatus
        {
            Enabled = Enabled,
            IntervalHours = IntervalHours,
            Initialized = cursor.HasValue,
            LastSeenId = cursor,
            LastRunAt = LastRun,
            IsRunning = IsRunning,
            LastMessage = LastMessage,
            LastError = LastError,
        };
    }

    public async Task<DaneaPullReport> RunOnce()
    {
        if (string.IsNullOrEmpty(_config["DaneaSync:EftFilePathOld"]))
            return new DaneaPullReport
            { Message = "Vecchio archivio non configurato (EftFilePathOld): niente da ripescare." };

        if (!await _gate.WaitAsync(0))
            return new DaneaPullReport { Message = "Ripescaggio gia' in corso." };

        IsRunning = true;
        try
        {
            LastError = null;
            long? cursor = await ReadCursor();
            var (maxId, newIds) = _migration.OldArticlesAfter(cursor);

            if (!cursor.HasValue)
            {
                await SaveCursor(maxId);
                return await Done(new DaneaPullReport
                {
                    LastSeenId = maxId,
                    Message = $"Spartiacque impostato all'ID {maxId} del vecchio archivio: da adesso i " +
                              "nuovi articoli arrivano da soli. L'arretrato si trasferisce dalla griglia " +
                              "(«Più recenti prima»).",
                });
            }
            if (newIds.Count == 0)
                return await Done(new DaneaPullReport
                {
                    LastSeenId = cursor.Value,
                    Message = "Nessun articolo nuovo nel vecchio archivio.",
                });

            var total = new DaneaTransferReport();
            foreach (var chunk in newIds.Chunk(BatchSize))
            {
                var report = _migration.Transfer(chunk.ToList());
                total.Ok += report.Ok;
                total.Skipped += report.Skipped;
                total.Errors += report.Errors;
                total.ImagesCopied += report.ImagesCopied;
                total.Results.AddRange(report.Results);

                // Specchio del Catalogo subito, come fa il controller sul trasferimento
                // manuale: senza, gli articoli restano spenti fino al sync delle 6 ore.
                var daAllineare = report.Results
                    .Where(r => r.Outcome != "error")
                    .Select(r => r.IdInAtecPm > 0 ? r.IdInAtecPm : r.IdArticolo)
                    .ToList();
                if (daAllineare.Count > 0)
                {
                    try
                    {
                        total.CatalogAligned += await _sync.AllineaArticoli(daAllineare);
                    }
                    catch (Exception ex)
                    {
                        total.CatalogWarning =
                            $"Catalogo articoli non aggiornato ({ex.Message}): usare «Sincronizza Danea».";
                    }
                }
            }

            // Il cursore si ferma PRIMA del primo errore bloccante (ritentato al giro
            // dopo; i gia' trasferiti oltre quel punto verranno skippati per codice).
            var bloccanti = total.Results
                .Where(r => r.Outcome == "error" && r.Error != DaneaMigrationService.ErroreArticoloAssente)
                .ToList();
            long newCursor = bloccanti.Count > 0 ? bloccanti.Min(r => (long)r.IdArticolo) - 1 : newIds[^1];
            newCursor = Math.Max(newCursor, cursor.Value);
            await SaveCursor(newCursor);

            // Solo i bloccanti: un articolo sparito dal vecchio non e' un guasto da mostrare.
            if (bloccanti.Count > 0)
                LastError = bloccanti[0].Error;
            _log.LogInformation(
                "[DaneaOldPull] {New} nuovi oltre lo spartiacque: {Ok} trasferiti, {Skipped} gia' presenti, " +
                "{Errors} errori. Cursore {Cursor}.",
                newIds.Count, total.Ok, total.Skipped, total.Errors, newCursor);

            return await Done(new DaneaPullReport
            {
                Ran = true,
                NewArticles = newIds.Count,
                LastSeenId = newCursor,
                Transfer = total,
                Message = $"{total.Ok} trasferiti, {total.Skipped} gia' presenti, {total.Errors} errori.",
            });
        }
        finally
        {
            IsRunning = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Chiude il giro: prima lo specchio prezzi — che gira anche quando non e' nato
    /// nessun articolo nuovo, perche' un listino cambia per conto suo — poi il timbro
    /// dell'ultimo esito mostrato in pagina.
    /// </summary>
    private async Task<DaneaPullReport> Done(DaneaPullReport report)
    {
        report.Mirror = await RispecchiaPrezzi();
        if (!string.IsNullOrEmpty(report.Mirror.Message))
            report.Message = $"{report.Message} {report.Mirror.Message}".Trim();

        LastRun = DateTime.Now;
        LastMessage = report.Message;
        return report;
    }

    /// <summary>
    /// Specchio prezzi: i gia' trasferiti seguono il vecchio archivio (vedi la testa di
    /// questa classe). Non deve MAI far fallire il ripescaggio — se salta, gli articoli
    /// nuovi sono passati lo stesso e si ritenta al giro dopo.
    /// </summary>
    private async Task<DaneaMirrorReport> RispecchiaPrezzi()
    {
        if (!MirrorEnabled)
            return new DaneaMirrorReport();

        try
        {
            var mirror = _migration.RispecchiaPrezzi();

            // Stesso specchio del trasferimento: senza, in Catalogo il prezzo vecchio
            // resta fino al sync delle 6 ore.
            var idArticoli = mirror.Changes.Select(c => c.IdInAtecPm).Distinct().ToList();
            if (idArticoli.Count > 0)
            {
                try
                {
                    mirror.CatalogAligned = await _sync.AllineaArticoli(idArticoli);
                }
                catch (Exception ex)
                {
                    mirror.CatalogWarning =
                        $"Catalogo articoli non aggiornato ({ex.Message}): usare «Sincronizza Danea».";
                }
            }

            // Un prezzo che cambia da solo si deve poter ricostruire a posteriori.
            foreach (var c in mirror.Changes)
                _log.LogInformation("[DaneaOldPull] Specchio {Cod} ({Id}): {Campo} {Prima} -> {Dopo}",
                    c.CodArticolo, c.IdInAtecPm, c.Campo, c.Prima, c.Dopo);
            if (mirror.Aligned > 0 || !string.IsNullOrEmpty(mirror.CatalogWarning))
                _log.LogInformation("[DaneaOldPull] {Msg} Catalogo: {N} righe. {Warn}",
                    mirror.Message, mirror.CatalogAligned, mirror.CatalogWarning);

            return mirror;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[DaneaOldPull] Specchio prezzi fallito");
            LastError ??= $"Specchio prezzi non riuscito: {ex.Message}";
            return new DaneaMirrorReport
            { Message = $"Specchio prezzi non riuscito: {ex.Message}" };
        }
    }

    private async Task<long?> ReadCursor()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        using var conn = db.Open();
        string? v = await conn.ExecuteScalarAsync<string?>(
            "SELECT config_value FROM app_config WHERE config_key = @K", new { K = CursorKey });
        return long.TryParse(v, out long id) ? id : null;
    }

    private async Task SaveCursor(long value)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        using var conn = db.Open();
        await conn.ExecuteAsync(@"
            INSERT INTO app_config (config_key, config_value) VALUES (@K, @V)
            ON DUPLICATE KEY UPDATE config_value = @V",
            new { K = CursorKey, V = value.ToString() });
    }
}
