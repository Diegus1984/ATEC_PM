using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M060_VisteGanttSalvate : IMigrazione
{
    public int Versione => 60;

    public string Descrizione => "viste salvate del Gantt milestone";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Viste salvate del Gantt milestone («Vista Interna» / «Vista Cliente»).
        // Stanno LATO SERVER, non in localStorage: la composizione con cui si manda
        // il Gantt al cliente deve essere la stessa per tutti e sopravvivere al PC.
        // `payload` è il JSON della composizione (colonne spente, righe spente,
        // intervallo date, timeline on/off): il formato lo conosce solo il client,
        // il server lo tratta come opaco così aggiungere una voce non è una migrazione.
        c.Execute(@"CREATE TABLE IF NOT EXISTS milestone_gantt_views (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            name VARCHAR(60) NOT NULL,
            payload JSON NOT NULL,
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            updated_by INT NULL,
            UNIQUE KEY uk_project_view (project_id, name),
            CONSTRAINT fk_gantt_view_project FOREIGN KEY (project_id)
                REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        log.LogInformation("[Migration v60] Tabella milestone_gantt_views creata");
    }
}
