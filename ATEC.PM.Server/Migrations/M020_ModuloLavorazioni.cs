using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v20: tabella Lavorazioni meccaniche (project_work_requests) — modulo Gestione Lavorazioni
public sealed class M020_ModuloLavorazioni : IMigrazione
{
    public int Versione => 20;

    public string Descrizione => "Tabella project_work_requests (modulo Lavorazioni)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        EnsureWorkRequestsTable(c);
        log.LogInformation("[Migration v20] Creata tabella project_work_requests");
    }
}
