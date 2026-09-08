using System.Text.Json;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una tabella da trasformare in un foglio Excel vero (<c>POST /api/export/xlsx</c>).
///
/// <para>Il client decide COSA esportare (le righe che l'utente vede, le colonne che può
/// vedere); il server decide COME: numeri come numeri, date come date, percentuali col
/// formato percentuale, filtro automatico su ogni colonna. È nato con la segnalazione #150:
/// il CSV del Prospetto SAL scriveva <c>27122.172</c> e Excel italiano lo leggeva come
/// ventisette milioni.</para>
/// </summary>
public class TabellaExcelRequest
{
    /// <summary>Nome del foglio (ripulito dai caratteri che Excel vieta, max 31).</summary>
    public string? Foglio { get; set; }

    /// <summary>Nome del file suggerito nel Content-Disposition (il client lo ripete a modo suo).</summary>
    public string? NomeFile { get; set; }

    public List<TabellaExcelColonna> Colonne { get; set; } = new();

    /// <summary>
    /// Una riga = una cella per colonna, nell'ordine di <see cref="Colonne"/>. I valori
    /// arrivano dal JSON così come sono (stringa, numero, booleano, null): li interpreta il
    /// tipo della colonna.
    /// </summary>
    public List<List<JsonElement>> Righe { get; set; } = new();
}

public class TabellaExcelColonna
{
    public string Etichetta { get; set; } = "";

    /// <summary>
    /// <c>testo</c> · <c>intero</c> · <c>numero</c> · <c>euro</c> · <c>percento</c>
    /// (il valore è in punti percentuali: 18,37 = 18,37%) · <c>data</c> (ISO <c>aaaa-mm-gg</c>).
    /// Un tipo sconosciuto vale <c>testo</c>.
    /// </summary>
    public string Tipo { get; set; } = "testo";

    /// <summary>Decimali mostrati per <c>numero</c> e <c>percento</c> (default 2).</summary>
    public int? Decimali { get; set; }
}
