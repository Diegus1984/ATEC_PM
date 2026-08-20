using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v27: quantità sulle composizioni Codex — una riga per componente con colonna
// quantity, invece di N righe duplicate (quantità 4 = 4 insert identici).
// Collassa i duplicati esistenti (stesso padre + stesso figlio) sommando le
// occorrenze sulla riga con id minimo ed eliminando le altre.
public sealed class M027_QuantitaComposizioneCodex : IMigrazione
{
    public int Versione => 27;

    public string Descrizione => "codex_compositions: colonna quantity + collasso righe duplicate";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'codex_compositions'
              AND COLUMN_NAME = 'quantity'");
        if (hasCol == 0)
        {
            c.Execute("ALTER TABLE codex_compositions ADD COLUMN quantity INT NOT NULL DEFAULT 1");
        }

        c.Execute(@"
            UPDATE codex_compositions cc
            JOIN (
                SELECT MIN(id) AS keep_id, SUM(quantity) AS qty
                FROM codex_compositions
                GROUP BY parent_codex_id, child_codex_id, child_catalog_id
                HAVING COUNT(*) > 1
            ) g ON cc.id = g.keep_id
            SET cc.quantity = g.qty");

        // <=> = uguaglianza NULL-safe: uno tra child_codex_id e child_catalog_id è sempre NULL.
        int deleted = c.Execute(@"
            DELETE cc FROM codex_compositions cc
            JOIN (
                SELECT MIN(id) AS keep_id, parent_codex_id, child_codex_id, child_catalog_id
                FROM codex_compositions
                GROUP BY parent_codex_id, child_codex_id, child_catalog_id
                HAVING COUNT(*) > 1
            ) g ON cc.parent_codex_id = g.parent_codex_id
               AND cc.child_codex_id <=> g.child_codex_id
               AND cc.child_catalog_id <=> g.child_catalog_id
               AND cc.id <> g.keep_id");

        log.LogInformation("[Migration v27] Colonna quantity su codex_compositions, {Deleted} righe duplicate collassate", deleted);
    }
}
