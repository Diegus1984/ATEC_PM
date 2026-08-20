using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v16: modulo "SAL / Fatturazione" (sal_conditions + project_sal + sal_rows) + anagrafica condizioni.
public sealed class M016_ModuloSal : IMigrazione
{
    public int Versione => 16;

    public string Descrizione => "sal_conditions + project_sal + sal_rows + nav.sal_condizioni";

    public void Applica(MySqlConnection c, ILogger log)
    {
        SalDbService.InitTables(c, log);
        SalDbService.SeedConditions(c, log);
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.sal_condizioni', 'Condizioni Pagamento SAL', 'navigation', 2, 'HIDDEN')");
        log.LogInformation("[Migration v16] Tabelle SAL + seed condizioni + feature key nav.sal_condizioni");
    }
}
