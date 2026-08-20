using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v11: feature key nav.digest_email (voce di menu "Digest Email" — solo ADMIN)
public sealed class M011_ChiaveDigestEmail : IMigrazione
{
    public int Versione => 11;

    public string Descrizione => "nav.digest_email feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.digest_email', 'Digest Email', 'navigation', 3, 'HIDDEN')");
        log.LogInformation("[Migration v11] Aggiunta feature key nav.digest_email");
    }
}
