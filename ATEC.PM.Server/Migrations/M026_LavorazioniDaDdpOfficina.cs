using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v26: lavorazioni alimentate dalla DDP Officina — colonna di collegamento
// ddp_officina_item_id (UNIQUE: una lavorazione per riga) + backfill bozze in
// staging per tutte le righe officina esistenti non ancora collegate.
//
// ⚠️ Il backfill delle bozze è stato TOLTO da questa migrazione storica quando la v92 (#83)
// ha eliminato le bozze: generarle qui per poi cancellarle 66 versioni dopo è lavoro sprecato
// su ogni installazione nuova, e teneva in vita il vecchio motore di copia solo per questa
// riga. La colonna di collegamento resta: è quella che serve a tutto il resto.
public sealed class M026_LavorazioniDaDdpOfficina : IMigrazione
{
    public int Versione => 26;

    public string Descrizione => "lavorazioni: link ddp_officina_item_id + backfill bozze da DDP Officina";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int hasCol = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'project_work_requests'
              AND COLUMN_NAME = 'ddp_officina_item_id'");
        if (hasCol == 0)
        {
            c.Execute(@"ALTER TABLE project_work_requests
                ADD COLUMN ddp_officina_item_id INT NULL,
                ADD UNIQUE KEY uq_pwr_ddp_officina (ddp_officina_item_id),
                ADD CONSTRAINT fk_pwr_ddp_officina FOREIGN KEY (ddp_officina_item_id)
                    REFERENCES ddp_officina_items(id) ON DELETE SET NULL");
        }

        log.LogInformation("[Migration v26] Link lavorazioni-DDP Officina.");
    }
}
