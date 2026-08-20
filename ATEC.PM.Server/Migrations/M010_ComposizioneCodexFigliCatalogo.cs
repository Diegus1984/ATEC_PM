using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v10: allinea codex_compositions allo schema usato da CodexController (figli da catalogo):
// child_codex_id NULL-able + colonna child_catalog_id, che mancavano nel CREATE TABLE
// (un DB nato da zero rompeva l'Editor Composizione). FK verso catalog_items aggiunta qui
// perché in InitDatabase catalog_items viene creata dopo codex_compositions.
public sealed class M010_ComposizioneCodexFigliCatalogo : IMigrazione
{
    public int Versione => 10;

    public string Descrizione => "codex_compositions.child_catalog_id + child_codex_id NULL";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool childNullable = c.ExecuteScalar<string>(@"
            SELECT IS_NULLABLE FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'codex_compositions'
              AND column_name = 'child_codex_id'") == "YES";
        if (!childNullable)
            c.Execute("ALTER TABLE codex_compositions MODIFY child_codex_id INT NULL");

        bool hasCatalogCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'codex_compositions'
              AND column_name = 'child_catalog_id'") > 0;
        if (!hasCatalogCol)
            c.Execute(@"ALTER TABLE codex_compositions
                ADD COLUMN child_catalog_id INT NULL AFTER child_codex_id,
                ADD INDEX idx_cc_catalog (child_catalog_id)");

        bool hasFk = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.table_constraints
            WHERE table_schema = DATABASE()
              AND table_name = 'codex_compositions'
              AND constraint_name = 'fk_cc_catalog'") > 0;
        if (!hasFk)
        {
            // Pulizia orfani prima della FK (DB esistenti con colonna aggiunta a mano)
            c.Execute(@"DELETE FROM codex_compositions
                WHERE child_catalog_id IS NOT NULL
                  AND child_catalog_id NOT IN (SELECT id FROM catalog_items)");
            c.Execute(@"ALTER TABLE codex_compositions
                ADD CONSTRAINT fk_cc_catalog FOREIGN KEY (child_catalog_id)
                REFERENCES catalog_items(id) ON DELETE CASCADE");
        }

        log.LogInformation("[Migration v10] codex_compositions allineata (child_catalog_id, child_codex_id NULL-able)");
    }
}
