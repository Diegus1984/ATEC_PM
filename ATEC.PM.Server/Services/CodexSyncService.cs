using MySqlConnector;
using Dapper;

namespace ATEC.PM.Server.Services;

public class CodexSyncService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<CodexSyncService> _log;
    private readonly string _remoteCs;
    private readonly TimeSpan _interval;

    public static bool IsSyncing { get; private set; }
    public static DateTime? LastSync { get; private set; }
    public static int TotalRows { get; private set; }
    public static string? LastError { get; private set; }

    public CodexSyncService(IServiceProvider sp, IConfiguration config, ILogger<CodexSyncService> log)
    {
        _sp = sp;
        _log = log;
        _remoteCs = config.GetConnectionString("Codex") ?? "";
        int hours = int.TryParse(config["CodexSync:IntervalHours"], out int h) ? h : 6;
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(5000, ct);
        await RunSync();

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_interval, ct);
            await RunSync();
        }
    }

    public async Task RunSync()
    {
        if (IsSyncing) return;
        if (string.IsNullOrWhiteSpace(_remoteCs))
        {
            _log.LogWarning("[CodexSync] Connection string 'Codex' non configurata, skip sync.");
            return;
        }

        IsSyncing = true;
        LastError = null;
        _log.LogInformation("[CodexSync] Inizio sincronizzazione...");

        try
        {
            // 1. Leggi dal DB remoto
            List<dynamic> remoteRows;
            using (var remote = new MySqlConnection(_remoteCs))
            {
                await remote.OpenAsync();
                remoteRows = (await remote.QueryAsync("SELECT * FROM codici")).ToList();
            }

            _log.LogInformation($"[CodexSync] Lette {remoteRows.Count} righe dal DB remoto.");

            // 2. Scrivi nel DB locale
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            using var local = db.Open();

            await local.ExecuteAsync("DELETE FROM codex_items");

            const int batchSize = 500;
            for (int i = 0; i < remoteRows.Count; i += batchSize)
            {
                var batch = remoteRows.Skip(i).Take(batchSize);
                foreach (var r in batch)
                {
                    await local.ExecuteAsync(@"
                        INSERT INTO codex_items 
                            (remote_id, codice, code_forn, fornitore, prezzo_forn, iva, produttore,
                             data, descr, note, categoria, barcode, tipologia,
                             extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn)
                        VALUES 
                            (@id, @codice, @code_forn, @fornitore, @prezzo_forn, @iva, @produttore,
                             @data, @descr, @note, @categoria, @barcode, @tipologia,
                             @extra1, @extra2, @extra3, @code_prod, @spec, @oper, @um, @ubicazione, @codexforn)",
                        (object)r);
                }
            }

            TotalRows = remoteRows.Count;
            LastSync = DateTime.Now;

            await local.ExecuteAsync(@"
                INSERT INTO app_config (config_key, config_value) VALUES ('codex_last_sync', @Val)
                ON DUPLICATE KEY UPDATE config_value = @Val",
                new { Val = LastSync.Value.ToString("yyyy-MM-dd HH:mm:ss") });

            _log.LogInformation($"[CodexSync] Completato: {TotalRows} righe sincronizzate.");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log.LogError(ex, "[CodexSync] Errore durante sincronizzazione");
        }
        finally
        {
            IsSyncing = false;
        }
    }
}