using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v35: priorità default P2 (più bassa) sulle lavorazioni senza priorità.
public sealed class M035_PrioritaP2 : IMigrazione
{
    public int Versione => 35;

    public string Descrizione => "work_requests: default priority P2";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"UPDATE project_work_requests SET priority = 2 WHERE priority IS NULL");
        try
        {
            c.Execute(@"ALTER TABLE project_work_requests
                MODIFY COLUMN priority TINYINT NOT NULL DEFAULT 2");
        }
        catch (Exception alterEx)
        {
            log.LogWarning("[Migration v35] ALTER priority default: {Message}", alterEx.Message);
        }

        log.LogInformation("[Migration v35] Priorità default P2 su project_work_requests");
    }
}
