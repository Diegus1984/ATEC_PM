using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v76 — «DC» dentro l'aggregazione A1 (Conteggio per stato).
// A1 dichiara di contenere TUTTI gli stati, ma «DA COSTRUIRE» non c'è mai stato: il seed
// della v7 arrivava dall'Excel V53.1, dove DC non esisteva ancora. Le righe da costruire —
// cioè quasi tutto il lavoro dell'officina interna — restavano fuori da ogni vista che
// conta per stato. Trovato guardando le aggregazioni per la #54.
public sealed class M076_DcDentroA1 : IMigrazione
{
    public int Versione => 76;

    public string Descrizione => "DC dentro A1: le righe da costruire ora sono contate dalle viste per stato";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int aggiunti = c.Execute(@"INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key)
            SELECT a.id, 'DC' FROM ddp_aggregations a WHERE a.code = 'A1'");

        log.LogInformation("[Migration v76] DC aggiunto all'aggregazione A1 ({Aggiunti} righe).", aggiunti);
    }
}
