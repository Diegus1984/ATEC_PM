using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v6: causali DDP reali — rimuove le 12 chiavi generiche seedate in precedenza (TO_ORDER, ecc.).
// Il seed con le 17 causali reali gira ad ogni avvio (INSERT IGNORE) nella creazione tabelle.
public sealed class M006_CausaliStatiDdp : IMigrazione
{
    public int Versione => 6;

    public string Descrizione => "ddp_statuses causali reali";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"DELETE FROM ddp_statuses WHERE status_key IN
            ('TO_ORDER','ORDERED','DELIVERED','PARTIAL','TO_BUILD','RFQ',
             'TO_CHECK','CANCELLED','ASSIGNED','SHIPPED','TECH_CHECK','TO_MODULA')");
        log.LogInformation("[Migration v6] Rimosse le causali DDP generiche (sostituite dal set reale)");
    }
}
