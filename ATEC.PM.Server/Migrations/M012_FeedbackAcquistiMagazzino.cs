using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v12: Feedback Acquisti (nota + nascosto per DDP+stato) e Feedback Magazzino (nascosto per riga).
// Stati tracciati = aggregazioni A6 (acquisti) / A7 (magazzino), già seedate in ddp_aggregations.
public sealed class M012_FeedbackAcquistiMagazzino : IMigrazione
{
    public int Versione => 12;

    public string Descrizione => "ddp_feedback_acquisti + ddp_feedback_magazzino_hidden";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_feedback_acquisti (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            ddp_type VARCHAR(20) NOT NULL,
            status_key VARCHAR(20) NOT NULL,
            note TEXT,
            hidden TINYINT(1) NOT NULL DEFAULT 0,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uq_ddp_feedback_acquisti (project_id, ddp_type, status_key),
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_feedback_magazzino_hidden (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            ddp_type VARCHAR(20) NOT NULL,
            item_id INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_ddp_feedback_magazzino (project_id, ddp_type, item_id),
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        log.LogInformation("[Migration v12] Create tabelle ddp_feedback_acquisti e ddp_feedback_magazzino_hidden");
    }
}
