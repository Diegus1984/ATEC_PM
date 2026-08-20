using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v2: rimuove feature keys orfane (nav.preventivi_nuovo, nav.offerte)
public sealed class M002_ChiaviOrfane : IMigrazione
{
    public int Versione => 2;

    public string Descrizione => "cleanup orphan feature keys";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute("DELETE FROM auth_features WHERE feature_key IN ('nav.preventivi_nuovo', 'nav.offerte')");
        log.LogInformation("[Migration v2] Rimosse feature keys orfane");
    }
}
