using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Helper condiviso per leggere l'insieme di stati che compone un'aggregazione DDP
/// (es. "A9" = escluso da totale/conteggi, "A2" = consegnato). Restituisce le chiavi di stato
/// configurate dall'admin in «Aggregazioni DDP».
/// </summary>
public static class DdpAggregationSet
{
    /// <summary>
    /// Chiavi di stato membri dell'aggregazione con il codice indicato.
    /// Array VUOTO se l'aggregazione non esiste o non ha membri → semantica "nessuna esclusione"
    /// (Dapper su <c>item_status NOT IN @Empty</c> include tutte le righe).
    /// </summary>
    public static string[] Load(IDbConnection c, string code) =>
        c.Query<string>(@"
            SELECT s.status_key
            FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            WHERE a.code = @Code", new { Code = code }).ToArray();
}
