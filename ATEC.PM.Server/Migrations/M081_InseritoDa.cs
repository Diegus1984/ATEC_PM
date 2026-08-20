using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v81 — Colonne "Inserito da" (created_by) e "Data inserimento" (created_at) nelle DDP (segnalazione #61).
// created_at esisteva già in entrambe le tabelle. Aggiungiamo created_by INT NULL su bom_items
// e ddp_officina_items con FK verso employees(id) ON DELETE SET NULL.
public sealed class M081_InseritoDa : IMigrazione
{
    public int Versione => 81;

    public string Descrizione => "bom_items e ddp_officina_items: created_by (Inserito da) con FK a employees";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (AddColumnIfMissing(c, "bom_items", "created_by", "INT NULL AFTER updated_at"))
        {
            try
            {
                c.Execute("ALTER TABLE bom_items ADD CONSTRAINT fk_bom_items_created_by FOREIGN KEY (created_by) REFERENCES employees(id) ON DELETE SET NULL");
            }
            catch (Exception ex)
            {
                log.LogWarning("[Migration v81] Errore FK bom_items.created_by (non bloccante): {Message}", ex.Message);
            }
        }
        if (AddColumnIfMissing(c, "ddp_officina_items", "created_by", "INT NULL AFTER updated_at"))
        {
            try
            {
                c.Execute("ALTER TABLE ddp_officina_items ADD CONSTRAINT fk_ddp_officina_items_created_by FOREIGN KEY (created_by) REFERENCES employees(id) ON DELETE SET NULL");
            }
            catch (Exception ex)
            {
                log.LogWarning("[Migration v81] Errore FK ddp_officina_items.created_by (non bloccante): {Message}", ex.Message);
            }
        }

        log.LogInformation("[Migration v81] Colonne created_by su bom_items e ddp_officina_items.");
    }
}
