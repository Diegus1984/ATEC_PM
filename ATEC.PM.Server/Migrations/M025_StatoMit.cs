using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v25: stato MIT (Materiale in trattamento, tipico Officina) — presente nella legenda
// DDP di ATEC ma assente dal seed storico. INSERT IGNORE: chi lo ha già creato/ritoccato
// da Conf. DDP non viene toccato. MIT entra in A1 (conteggio per stato) e A4 (in consegna).
public sealed class M025_StatoMit : IMigrazione
{
    public int Versione => 25;

    public string Descrizione => "ddp: stato MIT + membership aggregazioni A1/A4";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order)
            VALUES ('MIT', 'MATERIALE IN TRATTAMENTO', '#7FC1B0', '#000000', 18)");

        foreach (string aggCode in new[] { "A1", "A4" })
        {
            int aggId = c.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM ddp_aggregations WHERE code=@C", new { C = aggCode });
            if (aggId > 0)
                c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A, 'MIT')", new { A = aggId });
        }

        log.LogInformation("[Migration v25] Stato MIT seedato in ddp_statuses + aggregazioni A1/A4");
    }
}
