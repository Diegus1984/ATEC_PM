using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v14: aggregazione A9 «Escluso da totale/conteggi» (membri default ANN/SOSP/RAM/SOST).
// Gli stati membri di A9 non contano nei totali € né nei conteggi di scadenza. Configurabile
// dall'admin in «Aggregazioni DDP» come le altre. Idempotente (INSERT IGNORE), non sovrascrive
// eventuali membri già modificati.
public sealed class M014_AggregazioneA9 : IMigrazione
{
    public int Versione => 14;

    public string Descrizione => "ddp aggregazione A9 (escluso da totale)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO ddp_aggregations (code, name, description, kind, sort_order) VALUES
            ('A9','Escluso da totale/conteggi','Stati esclusi dai totali € e dai conteggi (annullato, sospeso, ecc.)','SET',9)");

        int a9Id = c.ExecuteScalar<int>("SELECT id FROM ddp_aggregations WHERE code='A9'");
        foreach (string st in new[] { "ANN", "SOSP", "RAM", "SOST" })
            c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,@S)",
                new { A = a9Id, S = st });

        log.LogInformation("[Migration v14] Aggregazione A9 (escluso da totale/conteggi) + membri default");
    }
}
