using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ATEC.PM.Server.Services;

/// <summary>
/// La parte «decisionale» del sync Codex, senza database: quali colonne del remoto formano lo
/// specchio, l'impronta di una riga remota e la regola «questa riga va riscritta?».
///
/// <para><b>Perché esiste.</b> Fino al 04/09/2026 il sync riscriveva <b>tutte</b> le righe a
/// ogni giro: misurato in produzione, 375.016 <c>UPDATE codex_items</c> in due giorni su
/// 20.814 righe, quando il Codex remoto cambia qualche decina di articoli al giorno. Ora ogni
/// riga locale porta l'impronta dell'ultima versione remota copiata (<c>codex_items.sync_hash</c>,
/// migrazione v121) e si riscrive solo se l'impronta è cambiata — lo stesso schema di
/// <c>res_sync_map.synced_hash</c> nel sync Risorse.</para>
/// </summary>
public static class SpecchioCodex
{
    /// <summary>
    /// Le colonne della tabella remota <c>codici</c> che il sync copia in <c>codex_items</c>,
    /// nell'ordine in cui entrano nell'impronta. Una colonna nuova va aggiunta QUI e nelle due
    /// query di <see cref="CodexSyncService"/>: un test controlla che le tre liste coincidano,
    /// perché una colonna copiata ma fuori dall'impronta cambierebbe sul remoto senza che il
    /// sync se ne accorga.
    /// </summary>
    public static readonly string[] Colonne =
    {
        "id", "codice", "code_forn", "fornitore", "prezzo_forn", "iva", "produttore", "data",
        "descr", "note", "categoria", "barcode", "tipologia", "extra1", "extra2", "extra3",
        "code_prod", "spec", "oper", "um", "ubicazione", "codexforn",
    };

    /// <summary>
    /// SHA256 (esadecimale minuscolo, 64 caratteri) dei valori di <see cref="Colonne"/> presi
    /// dalla riga remota. Le colonne che il sync non copia non contano: un <c>updated_at</c>
    /// remoto che cambia da solo non deve far riscrivere niente.
    /// </summary>
    public static string Impronta(IDictionary<string, object?> riga)
    {
        var sb = new StringBuilder(256);
        foreach (string colonna in Colonne)
        {
            riga.TryGetValue(colonna, out object? valore);
            // Separatore di unità (U+001F): non può comparire in un dato, quindi "ab|c" e "a|bc"
            // restano impronte diverse anche senza scrivere le lunghezze.
            sb.Append(Normalizza(valore)).Append('');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Riga già agganciata per <c>remote_id</c>: si riscrive se l'impronta è cambiata, oppure se
    /// il prezzo fornitore locale è vuoto e il remoto ne ha uno. La seconda condizione è la
    /// <c>CASE</c> dell'UPDATE (il prezzo remoto entra solo dove manca): senza, un prezzo
    /// azzerato in locale resterebbe a zero fino alla prossima modifica del remoto.
    /// </summary>
    public static bool VaRiscritta(string? improntaLocale, string improntaRemota, decimal? prezzoLocale, decimal? prezzoRemoto) =>
        !string.Equals(improntaLocale, improntaRemota, StringComparison.Ordinal)
        || ((prezzoLocale ?? 0m) == 0m && (prezzoRemoto ?? 0m) > 0m);

    /// <summary>Il prezzo remoto come decimale, qualunque sia il tipo con cui arriva.</summary>
    public static decimal? PrezzoRemoto(IDictionary<string, object?> riga) =>
        riga.TryGetValue("prezzo_forn", out object? v) && v is not null && v is not DBNull
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// Copia della riga con chiavi senza distinzione di maiuscole: il remoto le restituisce
    /// come le ha in tabella, le query le nominano in minuscolo.
    /// </summary>
    public static Dictionary<string, object?> Riga(IEnumerable<KeyValuePair<string, object?>> origine)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in origine) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>
    /// Testo stabile di un valore: NULL e stringa vuota sono la stessa cosa, i decimali
    /// perdono gli zeri finali (<c>1.50</c> e <c>1.5</c> sono lo stesso prezzo), le date
    /// escono in un formato fisso e invariante.
    /// </summary>
    internal static string Normalizza(object? v) => v switch
    {
        null or DBNull => "",
        string s => s,
        decimal d => d.ToString("G29", CultureInfo.InvariantCulture),
        double f => ((decimal)f).ToString("G29", CultureInfo.InvariantCulture),
        float f => ((decimal)f).ToString("G29", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
