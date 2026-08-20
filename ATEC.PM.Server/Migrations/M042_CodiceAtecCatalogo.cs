using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v42: mapping Danea ↔ codice ATEC (piano Acquisti, 21/07/2026). Sul catalogo (specchio
// degli articoli Danea): atec_code = Extra1 dell'articolo (convenzione: SOLO codici nuovi
// della nuova codifica Codex) + codex_item_id = risoluzione verso la riga Codex.
public sealed class M042_CodiceAtecCatalogo : IMigrazione
{
    public int Versione => 42;

    public string Descrizione => "catalog_items: atec_code + codex_item_id (mapping Danea Extra1)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'catalog_items'
              AND COLUMN_NAME = 'atec_code'") == 0)
        {
            c.Execute("ALTER TABLE catalog_items ADD COLUMN atec_code VARCHAR(15) NULL AFTER easyfatt_id", commandTimeout: 600);
            c.Execute("ALTER TABLE catalog_items ADD COLUMN codex_item_id INT NULL AFTER atec_code", commandTimeout: 600);
            c.Execute("ALTER TABLE catalog_items ADD INDEX IX_CatalogItems_AtecCode (atec_code)", commandTimeout: 600);
        }
        log.LogInformation("[Migration v42] Colonne atec_code/codex_item_id su catalog_items");
    }
}
