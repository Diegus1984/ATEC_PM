using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v38: pezzi prodotti su distinta Officina (RESP / inbox)
public sealed class M038_QuantitaProdotta : IMigrazione
{
    public int Versione => 38;

    public string Descrizione => "ddp_officina_items: quantity_produced";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'quantity_produced'") == 0)
        {
            c.Execute(@"ALTER TABLE ddp_officina_items
                ADD COLUMN quantity_produced DECIMAL(10,3) NOT NULL DEFAULT 0
                AFTER quantity");
        }
        log.LogInformation("[Migration v38] Colonna quantity_produced su ddp_officina_items");
    }
}
