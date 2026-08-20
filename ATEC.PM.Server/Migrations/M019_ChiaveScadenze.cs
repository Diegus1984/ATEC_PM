using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v19: feature key nav.scadenze
public sealed class M019_ChiaveScadenze : IMigrazione
{
    public int Versione => 19;

    public string Descrizione => "nav.scadenze feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.scadenze', 'Scadenze', 'navigation', 2, 'HIDDEN')");
        log.LogInformation("[Migration v19] Aggiunta feature key nav.scadenze");
    }
}
