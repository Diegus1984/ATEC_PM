using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v9: sync Codex non distruttivo. remote_id diventa NULL-able (NULL = codice generato
// localmente, non ancora presente sul Codex remoto) e UNIQUE, così CodexSyncService può fare
// upsert per remote_id invece di DELETE+reinsert: gli id locali restano stabili tra i sync e
// le FK ON DELETE CASCADE di codex_compositions/codex_item_references non svuotano più
// composizioni e riferimenti a ogni sincronizzazione.
public sealed class M009_CodexRemoteIdUnico : IMigrazione
{
    public int Versione => 9;

    public string Descrizione => "codex_items.remote_id NULL + UNIQUE (sync upsert)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Dedup difensivo per remote_id prima dell'indice UNIQUE (esclusi i codici locali,
        // che condividono remote_id=0 e NON sono duplicati). La DELETE con self-join è O(n²)
        // senza indice su remote_id e col timeout default (30s) andava in timeout già a ~18k
        // righe: si esegue solo se servono davvero dedup, e con timeout esteso.
        int dupGroups = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM (
                SELECT remote_id FROM codex_items
                WHERE remote_id <> 0
                GROUP BY remote_id HAVING COUNT(*) > 1) d");
        if (dupGroups > 0)
            c.Execute(@"DELETE ci FROM codex_items ci
                JOIN codex_items keep ON keep.remote_id = ci.remote_id AND keep.id < ci.id
                WHERE ci.remote_id <> 0", commandTimeout: 600);

        c.Execute("ALTER TABLE codex_items MODIFY remote_id INT NULL", commandTimeout: 600);
        c.Execute("UPDATE codex_items SET remote_id = NULL WHERE remote_id = 0", commandTimeout: 600);

        bool hasIndex = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = 'codex_items'
              AND index_name = 'uq_codex_remote_id'") > 0;
        if (!hasIndex)
            c.Execute("ALTER TABLE codex_items ADD UNIQUE KEY uq_codex_remote_id (remote_id)", commandTimeout: 600);

        log.LogInformation("[Migration v9] codex_items.remote_id NULL-able + UNIQUE: il sync Codex ora fa upsert");
    }
}
