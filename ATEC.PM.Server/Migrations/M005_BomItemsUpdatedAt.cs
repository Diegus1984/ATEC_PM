using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v5: bom_items.updated_at — concorrenza ottimistica della distinta DDP (real-time + anti lost-update)
public sealed class M005_BomItemsUpdatedAt : IMigrazione
{
    public int Versione => 5;

    public string Descrizione => "bom_items.updated_at";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool hasColumn = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'bom_items'
              AND column_name = 'updated_at'") > 0;

        if (!hasColumn)
        {
            c.Execute("ALTER TABLE bom_items ADD COLUMN updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP");
            log.LogInformation("[Migration v5] Aggiunta colonna bom_items.updated_at");
        }
    }
}
