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

    /// <summary>Com'è andato un giro di <see cref="ApplicaAsync"/>.</summary>
    public sealed record Esito(int Inserite, int Aggiornate, int Riagganciate, int Invariate, int Rimosse);

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
            List<Dictionary<string, object?>> remoteRows;
            using (var remote = new MySqlConnection(_remoteCs))
            {
                await remote.OpenAsync();
                remoteRows = (await remote.QueryAsync("SELECT * FROM codici"))
                    .Select(r => SpecchioCodex.Riga((IDictionary<string, object?>)r))
                    .ToList();
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

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            using var local = db.Open();
            using var tx = await local.BeginTransactionAsync();

            Esito esito = await ApplicaAsync(remoteRows, local, tx, _log);

            DateTime syncTime = DateTime.Now;
            await local.ExecuteAsync(@"
                INSERT INTO app_config (config_key, config_value) VALUES ('codex_last_sync', @Val)
                ON DUPLICATE KEY UPDATE config_value = @Val",
                new { Val = syncTime.ToString("yyyy-MM-dd HH:mm:ss") }, tx);

            await tx.CommitAsync();

            TotalRows = remoteRows.Count;
            LastSync = syncTime;

            _log.LogInformation(
                "[CodexSync] Completato: {Total} righe remote ({Inserted} nuove, {Updated} aggiornate, {Unchanged} invariate, {Adopted} riagganciate, {Deleted} rimosse).",
                TotalRows, esito.Inserite, esito.Aggiornate, esito.Invariate, esito.Riagganciate, esito.Rimosse);
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

    // UPSERT nel DB locale per remote_id (migrazione v9): gli id locali restano stabili,
    // così composizioni (codex_compositions) e riferimenti (codex_item_references) — che
    // hanno FK ON DELETE CASCADE verso codex_items — sopravvivono al sync. Il vecchio
    // DELETE+reinsert li svuotava a ogni giro e cancellava anche i codici generati
    // localmente (remote_id NULL) non ancora presenti sul remoto.
    //
    // sync_hash (v121) è l'impronta della versione remota copiata: la riga si riscrive solo se
    // l'impronta cambia (vedi SpecchioCodex), non a ogni giro.
    internal const string UpdateSql = @"
        UPDATE codex_items SET
            remote_id=@id, codice=@codice, code_forn=@code_forn, fornitore=@fornitore,
            prezzo_forn=CASE WHEN (prezzo_forn IS NULL OR prezzo_forn = 0) AND @prezzo_forn > 0 THEN @prezzo_forn ELSE prezzo_forn END,
            iva=@iva, produttore=@produttore, data=@data,
            descr=@descr, note=@note, categoria=@categoria, barcode=@barcode,
            tipologia=@tipologia, extra1=@extra1, extra2=@extra2, extra3=@extra3,
            code_prod=@code_prod, spec=@spec, oper=@oper, um=@um,
            ubicazione=@ubicazione, codexforn=@codexforn, sync_hash=@SyncHash, synced_at=NOW()
        WHERE id=@LocalId";

    internal const string InsertSql = @"
        INSERT INTO codex_items
            (remote_id, codice, code_forn, fornitore, prezzo_forn, iva, produttore,
             data, descr, note, categoria, barcode, tipologia,
             extra1, extra2, extra3, code_prod, spec, oper, um, ubicazione, codexforn, sync_hash)
        VALUES
            (@id, @codice, @code_forn, @fornitore, @prezzo_forn, @iva, @produttore,
             @data, @descr, @note, @categoria, @barcode, @tipologia,
             @extra1, @extra2, @extra3, @code_prod, @spec, @oper, @um, @ubicazione, @codexforn, @SyncHash)";

    /// <summary>
    /// Porta nello specchio locale le righe remote: inserisce le nuove, riaggancia per codice
    /// quelle generate in locale o sparite e ricomparse, riscrive quelle la cui impronta è
    /// cambiata, lascia stare le altre, rimuove quelle sparite dal remoto. Separato da
    /// <see cref="RunSync"/> per provarlo su un database di prova senza un Codex remoto.
    /// </summary>
    internal static async Task<Esito> ApplicaAsync(
        IReadOnlyList<Dictionary<string, object?>> remoteRows, MySqlConnection local, MySqlTransaction? tx, ILogger log)
    {
        var localRows = (await local.QueryAsync<(int Id, int? RemoteId, string Codice, decimal? PrezzoForn, string? SyncHash)>(
            "SELECT id, remote_id, codice, prezzo_forn, sync_hash FROM codex_items", transaction: tx)).ToList();

        var remoteIds = new HashSet<int>();
        foreach (var r in remoteRows)
            remoteIds.Add(Convert.ToInt32(r["id"]));

        // remote_id <= 0 o NULL = codice generato localmente (0 = dato pre-migrazione v9)
        var byRemoteId = new Dictionary<int, (int Id, decimal? Prezzo, string? Hash)>(); // remote_id -> riga locale
        var unsyncedByCodice = new Dictionary<string, int>(); // codici locali in attesa di comparire sul remoto
        var staleIds = new HashSet<int>();                    // righe sincronizzate sparite dal remoto
        var staleByCodice = new Dictionary<string, int>();
        foreach (var row in localRows)
        {
            if (row.RemoteId is > 0)
            {
                byRemoteId[row.RemoteId.Value] = (row.Id, row.PrezzoForn, row.SyncHash);
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

        int inserted = 0, updated = 0, adopted = 0, unchanged = 0;
        foreach (var r in remoteRows)
        {
            int remoteId = Convert.ToInt32(r["id"]);
            string codice = Convert.ToString(r.GetValueOrDefault("codice")) ?? "";
            string impronta = SpecchioCodex.Impronta(r);

            int localId;
            if (byRemoteId.TryGetValue(remoteId, out var locale))
            {
                if (!SpecchioCodex.VaRiscritta(locale.Hash, impronta, locale.Prezzo, SpecchioCodex.PrezzoRemoto(r)))
                {
                    unchanged++;
                    continue;
                }
                localId = locale.Id;
                updated++;
            }
            else if (codice.Length > 0 && unsyncedByCodice.Remove(codice, out localId))
            {
                // Codice generato localmente ora presente sul remoto: riaggancio (id locale
                // e relative composizioni/riferimenti restano validi). Si riscrive sempre:
                // cambia almeno remote_id.
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
                var ins = new DynamicParameters();
                ins.AddDynamicParams(r);
                ins.Add("SyncHash", impronta);
                await local.ExecuteAsync(InsertSql, ins, tx);
                inserted++;
                continue;
            }

            var dp = new DynamicParameters();
            dp.AddDynamicParams(r);
            dp.Add("SyncHash", impronta);
            dp.Add("LocalId", localId);
            await local.ExecuteAsync(UpdateSql, dp, tx);
        }

        // Rimuovi solo le righe davvero sparite dal remoto (evento raro: cancellazione vera).
        // Le CASCADE su composizioni/riferimenti qui sono volute: il padre non esiste più.
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
                log.LogWarning(
                    "[CodexSync] {Count} articoli rimossi dal remoto: la cancellazione elimina in cascata {Comps} righe di composizione e {Refs} riferimenti.",
                    staleIds.Count, linkedComps, linkedRefs);

            deleted = await local.ExecuteAsync(
                "DELETE FROM codex_items WHERE id IN @Ids", new { Ids = staleIds }, tx);
        }

        return new Esito(inserted, updated, adopted, unchanged, deleted);
    }
}
