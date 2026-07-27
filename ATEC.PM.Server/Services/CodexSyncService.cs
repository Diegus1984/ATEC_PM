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

            // Guardia: 0 righe dal remoto è quasi certamente un problema lato remoto, non un Codex
            // svuotato davvero. Meglio saltare il sync che cancellare l'intera copia locale.
            if (remoteRows.Count == 0)
            {
                LastError = "Il DB remoto ha restituito 0 righe: sync annullato per sicurezza.";
                _log.LogWarning("[CodexSync] {Error}", LastError);
                return;
            }

            // 2. UPSERT nel DB locale per remote_id (migrazione v9): gli id locali restano stabili,
            //    così composizioni (codex_compositions) e riferimenti (codex_item_references) — che
            //    hanno FK ON DELETE CASCADE verso codex_items — sopravvivono al sync. Il vecchio
            //    DELETE+reinsert li svuotava a ogni giro e cancellava anche i codici generati
            //    localmente (remote_id NULL) non ancora presenti sul remoto.
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            using var local = db.Open();
            using var tx = await local.BeginTransactionAsync();

            var localRows = (await local.QueryAsync<(int Id, int? RemoteId, string Codice)>(
                "SELECT id, remote_id, codice FROM codex_items", transaction: tx)).ToList();

            var remoteIds = new HashSet<int>();
            foreach (var r in remoteRows)
                remoteIds.Add(Convert.ToInt32(r.id));

            // remote_id <= 0 o NULL = codice generato localmente (0 = dato pre-migrazione v9)
            var byRemoteId = new Dictionary<int, int>();          // remote_id -> id locale
            var unsyncedByCodice = new Dictionary<string, int>(); // codici locali in attesa di comparire sul remoto
            var staleIds = new HashSet<int>();                    // righe sincronizzate sparite dal remoto
            var staleByCodice = new Dictionary<string, int>();
            foreach (var row in localRows)
            {
                if (row.RemoteId is > 0)
                {
                    byRemoteId[row.RemoteId.Value] = row.Id;
                    if (!remoteIds.Contains(row.RemoteId.Value))
                    {
                        staleIds.Add(row.Id);
                        if (!string.IsNullOrEmpty(row.Codice))
                            staleByCodice.TryAdd(row.Codice, row.Id);
                    }
                }
                else if (!string.IsNullOrEmpty(row.Codice))
                {
                    unsyncedByCodice.TryAdd(row.Codice, row.Id);
                }
            }

            const string updateSql = @"
                UPDATE codex_items SET
                    remote_id=@id, codice=@codice, code_forn=@code_forn, fornitore=@fornitore,
                    prezzo_forn=CASE WHEN (prezzo_forn IS NULL OR prezzo_forn = 0) AND @prezzo_forn > 0 THEN @prezzo_forn ELSE prezzo_forn END,
                    iva=@iva, produttore=@produttore, data=@data,
                    descr=@descr, note=@note, categoria=@categoria, barcode=@barcode,
                    tipologia=@tipologia, extra1=@extra1, extra2=@extra2, extra3=@extra3,
                    code_prod=@code_prod, spec=@spec, oper=@oper, um=@um,
                    ubicazione=@ubicazione, codexforn=@codexforn, synced_at=NOW()
                WHERE id=@LocalId";

            const string insertSql = @"
                INSERT INTO codex_items
                    (remote_id, codice, code_forn, fornitore, prezzo_forn, iva, produttore,
                     data, descr, note, categoria, barcode, tipologia,
                     extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn)
                VALUES
                    (@id, @codice, @code_forn, @fornitore, @prezzo_forn, @iva, @produttore,
                     @data, @descr, @note, @categoria, @barcode, @tipologia,
                     @extra1, @extra2, @extra3, @code_prod, @spec, @oper, @um, @ubicazione, @codexforn)";

            int inserted = 0, updated = 0, adopted = 0;
            foreach (var r in remoteRows)
            {
                int remoteId = Convert.ToInt32(r.id);
                string codice = Convert.ToString(r.codice) ?? "";

                int localId;
                if (byRemoteId.TryGetValue(remoteId, out localId))
                {
                    updated++;
                }
                else if (codice.Length > 0 && unsyncedByCodice.Remove(codice, out localId))
                {
                    // Codice generato localmente ora presente sul remoto: riaggancio (id locale
                    // e relative composizioni/riferimenti restano validi)
                    adopted++;
                }
                else if (codice.Length > 0 && staleByCodice.Remove(codice, out localId))
                {
                    // Stesso codice con remote_id nuovo (re-import sul remoto): riaggancio
                    staleIds.Remove(localId);
                    adopted++;
                }
                else
                {
                    await local.ExecuteAsync(insertSql, (object)r, tx);
                    inserted++;
                    continue;
                }

                var dp = new DynamicParameters();
                dp.AddDynamicParams((object)r);
                dp.Add("LocalId", localId);
                await local.ExecuteAsync(updateSql, dp, tx);
            }

            // 3. Rimuovi solo le righe davvero sparite dal remoto (evento raro: cancellazione vera).
            //    Le CASCADE su composizioni/riferimenti qui sono volute: il padre non esiste più.
            int deleted = 0;
            if (staleIds.Count > 0)
            {
                int linkedComps = await local.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM codex_compositions
                    WHERE parent_codex_id IN @Ids OR child_codex_id IN @Ids",
                    new { Ids = staleIds }, tx);
                int linkedRefs = await local.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM codex_item_references
                    WHERE source_codex_id IN @Ids OR ref_codex_id IN @Ids",
                    new { Ids = staleIds }, tx);
                if (linkedComps > 0 || linkedRefs > 0)
                    _log.LogWarning(
                        "[CodexSync] {Count} articoli rimossi dal remoto: la cancellazione elimina in cascata {Comps} righe di composizione e {Refs} riferimenti.",
                        staleIds.Count, linkedComps, linkedRefs);

                deleted = await local.ExecuteAsync(
                    "DELETE FROM codex_items WHERE id IN @Ids", new { Ids = staleIds }, tx);
            }

            DateTime syncTime = DateTime.Now;
            await local.ExecuteAsync(@"
                INSERT INTO app_config (config_key, config_value) VALUES ('codex_last_sync', @Val)
                ON DUPLICATE KEY UPDATE config_value = @Val",
                new { Val = syncTime.ToString("yyyy-MM-dd HH:mm:ss") }, tx);

            await tx.CommitAsync();

            TotalRows = remoteRows.Count;
            LastSync = syncTime;

            _log.LogInformation(
                "[CodexSync] Completato: {Total} righe remote ({Inserted} nuove, {Updated} aggiornate, {Adopted} riagganciate, {Deleted} rimosse).",
                TotalRows, inserted, updated, adopted, deleted);
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