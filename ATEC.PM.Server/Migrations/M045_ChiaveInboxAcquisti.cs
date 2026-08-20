using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v45: feature key nav inbox Acquisti.
public sealed class M045_ChiaveInboxAcquisti : IMigrazione
{
    public int Versione => 45;

    public string Descrizione => "nav.acquisti_inbox feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.acquisti_inbox', 'Acquisti — Inbox', 'navigation', 1, 'HIDDEN')");
        log.LogInformation("[Migration v45] feature key nav.acquisti_inbox");
    }
}
