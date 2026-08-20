using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v37: inbox Officina (Responsabile) — visibile da RESP_REPARTO in su
public sealed class M037_ChiaveInboxOfficina : IMigrazione
{
    public int Versione => 37;

    public string Descrizione => "nav.officina_inbox feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.officina_inbox', 'Officina — Inbox', 'navigation', 1, 'HIDDEN')");
        log.LogInformation("[Migration v37] feature key nav.officina_inbox");
    }
}
