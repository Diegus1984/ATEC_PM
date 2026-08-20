using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v51: riferimento ordine Danea sulla singola riga distinta commerciale — la generazione
// ordine dalla RDO scrive danea_ref (numero) + danea_order_iddoc (chiave per il popup
// di rendering dell'ordine). Colonna presente anche nel CREATE TABLE (ramo dev).
public sealed class M051_OrdineDaneaSuRiga : IMigrazione
{
    public int Versione => 51;

    public string Descrizione => "bom_items: riferimento ordine Danea di riga (danea_order_iddoc + backfill danea_ref)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'bom_items'
              AND COLUMN_NAME = 'danea_order_iddoc'") == 0)
        {
            c.Execute(@"ALTER TABLE bom_items
                ADD COLUMN danea_order_iddoc INT NULL AFTER danea_ref");
        }

        // Backfill delle RDO già evase: il riferimento risale dalle righe RDO.
        c.Execute(@"
            UPDATE bom_items b
            JOIN purchase_rfq_items i ON i.bom_item_id = b.id
            JOIN purchase_rfqs r ON r.id = i.rfq_id
            SET b.danea_order_iddoc = r.danea_order_iddoc,
                b.danea_ref = CASE WHEN COALESCE(b.danea_ref,'') = ''
                                   THEN CAST(r.danea_order_num AS CHAR)
                                   ELSE b.danea_ref END
            WHERE r.danea_order_iddoc IS NOT NULL");

        log.LogInformation("[Migration v51] Colonna danea_order_iddoc su bom_items");
    }
}
