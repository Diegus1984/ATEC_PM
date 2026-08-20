using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v48: feature key nav pagina Trasferimento catalogo Danea (migrazione Atec_PM).
public sealed class M048_ChiaveMigrazioneDanea : IMigrazione
{
    public int Versione => 48;

    public string Descrizione => "nav.danea_migration feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.danea_migration', 'Trasferimento Danea', 'navigation', 1, 'HIDDEN')");
        log.LogInformation("[Migration v48] feature key nav.danea_migration");
    }
}
