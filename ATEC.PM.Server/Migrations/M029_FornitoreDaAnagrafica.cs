using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v29: aggiunge supplier_id a ddp_officina_items per supportare la scelta del fornitore da anagrafica
public sealed class M029_FornitoreDaAnagrafica : IMigrazione
{
    public int Versione => 29;

    public string Descrizione => "ddp_officina_items: colonna supplier_id per fornitore da anagrafica";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'supplier_id'");
        if (hasCol == 0)
        {
            c.Execute(@"ALTER TABLE ddp_officina_items
                ADD COLUMN supplier_id INT NULL AFTER treatment,
                ADD CONSTRAINT fk_ddpoff_supplier FOREIGN KEY (supplier_id)
                    REFERENCES suppliers(id) ON DELETE SET NULL");
        }

        log.LogInformation("[Migration v29] Colonna supplier_id su ddp_officina_items");
    }
}
