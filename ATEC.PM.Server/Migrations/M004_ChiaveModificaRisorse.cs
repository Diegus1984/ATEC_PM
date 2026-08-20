using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v4: feature key resources.edit (gating scrittura allocazioni risorse: RESP_REPARTO+ )
public sealed class M004_ChiaveModificaRisorse : IMigrazione
{
    public int Versione => 4;

    public string Descrizione => "resources.edit feature key";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('resources.edit', 'Modifica Allocazioni Risorse', 'action', 1, 'DISABLED')");
        log.LogInformation("[Migration v4] Aggiunta feature key resources.edit");
    }
}
