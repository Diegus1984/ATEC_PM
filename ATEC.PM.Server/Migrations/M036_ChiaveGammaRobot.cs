using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v36: feature key navigazione Gamma Robot (allineata a Preventivi, min_level 2)
public sealed class M036_ChiaveGammaRobot : IMigrazione
{
    public int Versione => 36;

    public string Descrizione => "nav.gamma_robot feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.gamma_robot', 'Gamma Robot', 'navigation', 2, 'HIDDEN')");
        log.LogInformation("[Migration v36] feature key nav.gamma_robot");
    }
}
