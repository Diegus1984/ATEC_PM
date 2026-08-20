using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v50: tracciamento ordine fornitore Danea generato dalla RDO (strada B, 22/07/2026).
public sealed class M050_OrdineDaneaSuRdo : IMigrazione
{
    public int Versione => 50;

    public string Descrizione => "purchase_rfqs: riferimento ordine fornitore Danea (iddoc + numero)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'purchase_rfqs'
              AND COLUMN_NAME = 'danea_order_iddoc'") == 0)
        {
            c.Execute(@"ALTER TABLE purchase_rfqs
                ADD COLUMN danea_order_iddoc INT NULL AFTER closed_at,
                ADD COLUMN danea_order_num INT NULL AFTER danea_order_iddoc");
        }
        log.LogInformation("[Migration v50] Colonne ordine Danea su purchase_rfqs");
    }
}
