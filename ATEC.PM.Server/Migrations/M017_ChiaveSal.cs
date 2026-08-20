using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v17: feature key per la pagina PM SAL globale
public sealed class M017_ChiaveSal : IMigrazione
{
    public int Versione => 17;

    public string Descrizione => "nav.sal feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.sal', 'SAL / Fatturazione', 'navigation', 2, 'HIDDEN')");
        log.LogInformation("[Migration v17] feature key nav.sal");
    }
}
