using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M067_RigheSpente : IMigrazione
{
    public int Versione => 67;

    public string Descrizione => "ddp_row_off: righe escluse dalle stampe di Avanzamento e da Dati Mancanti";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // «Righe spente» della Sintesi DDP (piano V41 §4.1): righe escluse dalle stampe di
        // Avanzamento e righe già gestite in Dati Mancanti. Sono CONDIVISE fra utenti, quindi
        // stanno sul DB e non in localStorage. Nessuna colonna nuova sulle righe di distinta:
        // la presenza del record È lo stato spento, la sua assenza lo stato acceso.
        // Non si riusa ddp_feedback_magazzino_hidden: quella è del modulo Feedback Magazzino
        // e la sua chiave non ha il concetto di sezione.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_row_off (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            ddp_type VARCHAR(20) NOT NULL,
            section_key VARCHAR(20) NOT NULL,
            item_id INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_ddp_row_off (project_id, ddp_type, section_key, item_id),
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        log.LogInformation("[Migration v67] Creata tabella ddp_row_off (righe spente della Sintesi DDP, condivise)");
    }
}
