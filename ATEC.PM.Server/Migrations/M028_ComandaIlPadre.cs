using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v28: «comanda il padre» — i componenti importati dalla composizione Codex in DDP
// Officina restano collegati alla riga del padre (parent_officina_item_id) con la
// quantità unitaria di composizione (composition_qty): al cambio Qtà del padre i
// figli seguono con delta = composition_qty × ΔQtà. FK ON DELETE SET NULL: eliminato
// il padre, i figli restano come righe libere (scollegati).
public sealed class M028_ComandaIlPadre : IMigrazione
{
    public int Versione => 28;

    public string Descrizione => "ddp_officina_items: parent_officina_item_id + composition_qty (comanda il padre)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_officina_items'
              AND COLUMN_NAME = 'parent_officina_item_id'");
        if (hasCol == 0)
        {
            c.Execute(@"ALTER TABLE ddp_officina_items
                ADD COLUMN parent_officina_item_id INT NULL AFTER notes,
                ADD COLUMN composition_qty DECIMAL(10,3) NULL AFTER parent_officina_item_id,
                ADD CONSTRAINT fk_ddpoff_parent FOREIGN KEY (parent_officina_item_id)
                    REFERENCES ddp_officina_items(id) ON DELETE SET NULL");
        }

        log.LogInformation("[Migration v28] Colonne parent_officina_item_id/composition_qty su ddp_officina_items");
    }
}
