using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v34: data ordine su DDP Officina (valorizzata in automatico al passaggio a IO).
public sealed class M034_DataInOrdine : IMigrazione
{
    public int Versione => 34;

    public string Descrizione => "ddp_officina_items: order_date (data In Ordine)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'order_date'");
        if (hasCol == 0)
        {
            c.Execute(@"ALTER TABLE ddp_officina_items
                ADD COLUMN order_date DATE NULL AFTER date_needed");
        }

        log.LogInformation("[Migration v34] Colonna order_date su ddp_officina_items");
    }
}
