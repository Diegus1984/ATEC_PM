using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services.RisorseSync;

/// <summary>
/// Accesso a <c>res_sync_map</c> (M119, PIANO-SYNC-RISORSE.md §4.2): per ogni oggetto
/// (<c>kind</c>) l'id in PM, l'id sul VPS e l'impronta dei campi all'ultimo allineamento.
///
/// <para>La mappa è una biiezione per <c>kind</c> (due UNIQUE, una per lato): chi salva una
/// coppia deve avere già controllato che l'id remoto non sia in mano a un altro id locale,
/// altrimenti l'<c>ON DUPLICATE KEY UPDATE</c> aggiornerebbe la riga sbagliata. Il controllo
/// sta nel motore, che ha la mappa in memoria (vedi <c>RisorseSyncService</c>).</para>
///
/// <para>Solo SQL, niente logica: le decisioni su cosa mappare stanno in
/// <see cref="AnagraficheSync"/>, che è pura e si prova senza database.</para>
/// </summary>
public static class RisorseSyncMap
{
    public const string Employee = "EMPLOYEE";
    public const string Department = "DEPARTMENT";
    public const string Project = "PROJECT";
    public const string Assignment = "ASSIGNMENT";

    /// <summary>Una riga della mappa vista dal lato PM: l'id sul VPS e l'impronta dell'ultimo invio (null = mai inviato).</summary>
    public readonly record struct Voce(int RemoteId, string? SyncedHash);

    /// <summary>Tutte le coppie di un <paramref name="kind"/>: local_id → (remote_id, synced_hash).</summary>
    public static Dictionary<int, Voce> Carica(MySqlConnection c, string kind) =>
        c.Query<(int LocalId, int RemoteId, string? SyncedHash)>(
                "SELECT local_id, remote_id, synced_hash FROM res_sync_map WHERE kind = @Kind",
                new { Kind = kind })
            .ToDictionary(r => r.LocalId, r => new Voce(r.RemoteId, r.SyncedHash));

    /// <summary>
    /// Come <see cref="Carica(MySqlConnection, string)"/>, ma solo le coppie il cui
    /// <c>local_id</c> esiste ancora in <paramref name="tabellaLocale"/> (<c>employees</c>,
    /// <c>projects</c>…): la mappa non ha FK, e una coppia orfana (oggetto cancellato in PM)
    /// farebbe scrivere in <c>res_assignments</c> un id che viola la FK, a ogni giro.
    /// <paramref name="tabellaLocale"/> è un nome scelto dal codice, mai dall'utente.
    /// </summary>
    public static Dictionary<int, Voce> Carica(MySqlConnection c, string kind, string tabellaLocale) =>
        c.Query<(int LocalId, int RemoteId, string? SyncedHash)>(
                $"SELECT m.local_id, m.remote_id, m.synced_hash FROM res_sync_map m JOIN `{tabellaLocale}` t ON t.id = m.local_id WHERE m.kind = @Kind",
                new { Kind = kind })
            .ToDictionary(r => r.LocalId, r => new Voce(r.RemoteId, r.SyncedHash));

    /// <summary>
    /// Scrive o aggiorna la coppia di <paramref name="localId"/>: id remoto, impronta e
    /// <c>synced_at</c> (UTC). Un hash vuoto/null vuol dire «mappato ma mai inviato»: al
    /// giro dopo la riga parte comunque. Con <paramref name="tx"/> la scrittura entra nella
    /// transazione di chi chiama (le allocazioni: righe e mappa insieme, o niente).
    /// </summary>
    public static void Salva(MySqlConnection c, string kind, int localId, int remoteId, string? hash, MySqlTransaction? tx = null) =>
        c.Execute(@"
            INSERT INTO res_sync_map (kind, local_id, remote_id, synced_hash, synced_at)
            VALUES (@Kind, @LocalId, @RemoteId, @Hash, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE
                remote_id = VALUES(remote_id),
                synced_hash = VALUES(synced_hash),
                synced_at = UTC_TIMESTAMP()",
            new { Kind = kind, LocalId = localId, RemoteId = remoteId, Hash = string.IsNullOrEmpty(hash) ? null : hash }, tx);

    /// <summary>Toglie la coppia di <paramref name="localId"/> (se c'è). Non tocca nient'altro.</summary>
    public static void Rimuovi(MySqlConnection c, string kind, int localId, MySqlTransaction? tx = null) =>
        c.Execute("DELETE FROM res_sync_map WHERE kind = @Kind AND local_id = @LocalId",
            new { Kind = kind, LocalId = localId }, tx);
}
