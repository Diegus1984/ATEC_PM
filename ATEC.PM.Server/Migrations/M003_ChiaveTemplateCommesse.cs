using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v3: aggiunge feature key nav.project_templates
public sealed class M003_ChiaveTemplateCommesse : IMigrazione
{
    public int Versione => 3;

    public string Descrizione => "nav.project_templates feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.project_templates', 'Template Commesse', 'navigation', 2, 'HIDDEN')");
        log.LogInformation("[Migration v3] Aggiunta feature key nav.project_templates");
    }
}
