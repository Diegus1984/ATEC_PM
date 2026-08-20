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
    /// <param name="cache">
    /// Se c'è, l'intera tabellina (una decina di righe) viene letta una volta sola e tenuta in
    /// memoria finché qualcuno non tocca le aggregazioni. Ometterla non è un errore: si rilegge
    /// dal database, come si è sempre fatto — il modo sbagliato di usarla è più lento, non più
    /// vecchio.
    /// </param>
    public static string[] Load(IDbConnection c, string code, AnagraficheCache? cache = null)
    {
        if (cache == null) return Leggi(c, code);

        IReadOnlyDictionary<string, string[]> tutte = cache.Leggi(
            Anagrafica.AggregazioniDdp, () => LeggiTutte(c));

        return tutte.TryGetValue(code, out string[]? keys) ? keys : Array.Empty<string>();
    }

    private static string[] Leggi(IDbConnection c, string code) =>
        c.Query<string>(@"
            SELECT s.status_key
            FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            WHERE a.code = @Code", new { Code = code }).ToArray();

    /// <summary>Tutte le aggregazioni in un colpo: codice → chiavi di stato.</summary>
    private static IReadOnlyDictionary<string, string[]> LeggiTutte(IDbConnection c) =>
        c.Query<(string Code, string StatusKey)>(@"
            SELECT a.code AS Code, s.status_key AS StatusKey
            FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id")
        .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(r => r.StatusKey).ToArray(), StringComparer.OrdinalIgnoreCase);
}
