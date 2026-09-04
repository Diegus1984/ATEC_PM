using System.Data;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Le regole del Registro Offerte: numerazione, numero composto e validazione.
///
/// <para><b>Da dove vengono.</b> Sono il port di <c>mutations.js</c> dell'applicativo autonomo
/// «ATEC Offerte 2.1» (<c>Files To Check/</c>), dove vivevano già isolate e senza dipendenze
/// proprio per poter essere provate da sole. Qui restano allo stesso posto: una classe statica
/// senza stato, con la parte che tocca il database ridotta a due query.</para>
///
/// <para><b>Perché la validazione è pura.</b> <see cref="Validate"/> non apre connessioni: il
/// solo fatto che dipende dal database — «questo numero è già preso?» — glielo passa il
/// chiamante. Così la regola si prova senza MySQL e non può divergere fra creazione e modifica.</para>
/// </summary>
public static class SalesOfferRules
{
    /// <summary>Gli stati ammessi. <c>bozza</c> = numero riservato, dati da completare.</summary>
    public static readonly string[] Stati = { "bozza", "aperta", "presa", "persa", "sospesa" };

    /// <summary>Le categorie dei tipi impianto, quelle su cui si regge l'Analisi di mercato.</summary>
    public static readonly string[] Categorie = { "Impianto", "Intervento", "Ricambio", "Altro" };

    /// <summary>Progressivo a tre cifre; stringa vuota se il numero non c'è (righe storiche).</summary>
    public static string Pad3(int? numero) => numero == null ? "" : numero.Value.ToString("D3");

    /// <summary>
    /// Il numero composto: <c>tipo + progressivo(3) + «-» + anno + «-» + sigla</c> → <c>S097-2026-EC</c>.
    /// I pezzi mancanti spariscono senza rompere il resto, com'era nell'app: una bozza appena
    /// riservata ha solo numero e anno.
    /// </summary>
    public static string Composed(string? tipo, int? numero, int? anno, string? venditore) =>
        $"{tipo ?? ""}{Pad3(numero)}-{(anno == null ? "" : anno.Value.ToString())}-{venditore ?? ""}";

    /// <summary>
    /// Prossimo numero libero dell'anno: <c>max(numero) + 1</c>.
    ///
    /// <para><b>Le righe <c>is_legacy_series</c> non contano.</b> Nel vecchio Excel le nove
    /// offerte 2026 numerate 353–361 proseguivano la serie 2025: se entrassero nel massimo, il
    /// prossimo numero del 2026 salterebbe da 165 a 362 e la numerazione dell'anno sarebbe persa
    /// per sempre.</para>
    /// </summary>
    public static int NextNumber(IDbConnection c, int anno, IDbTransaction? tx = null) =>
        c.ExecuteScalar<int>(
            @"SELECT COALESCE(MAX(number), 0) + 1 FROM sales_offers
              WHERE year = @Anno AND number IS NOT NULL AND is_legacy_series = 0",
            new { Anno = anno }, tx);

    /// <summary>
    /// Il numero è già occupato in quell'anno? <paramref name="exceptId"/> esclude l'offerta che
    /// si sta modificando, altrimenti si darebbe del duplicato a sé stessa.
    ///
    /// <para>Questa è una <b>guardia applicativa</b>, non un vincolo di tabella: nei dati storici
    /// i numeri 282–285 del 2025 sono duplicati davvero, e una UNIQUE avrebbe impedito di
    /// importarli. La regola sta qui, dove può dire di sì all'import e di no a chi crea.</para>
    /// </summary>
    public static bool NumberTaken(IDbConnection c, int anno, int numero, int? exceptId = null, IDbTransaction? tx = null) =>
        c.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM sales_offers
              WHERE year = @Anno AND number = @Numero AND (@Except IS NULL OR id <> @Except)",
            new { Anno = anno, Numero = numero, Except = exceptId }, tx) > 0;

    /// <summary>
    /// Valida l'offerta e restituisce gli errori in italiano, campo per campo (chiave = nome del
    /// campo del DTO). Vuoto = valida.
    /// </summary>
    /// <param name="numeroOccupato">
    /// Esito di <see cref="NumberTaken"/>, calcolato dal chiamante. Su <c>null</c> il controllo
    /// si salta (creazione senza numero proposto: lo assegna il server al salvataggio).
    /// </param>
    public static Dictionary<string, string> Validate(SalesOfferSaveRequest o, bool? numeroOccupato)
    {
        var e = new Dictionary<string, string>();

        if (o.Year < 2000 || o.Year > 2100) e["year"] = "Indica l'anno";

        if (o.Number != null && o.Number < 1) e["number"] = "Numero non valido";
        else if (o.Number != null && numeroOccupato == true)
            e["number"] = $"Il numero {Pad3(o.Number)}/{o.Year} esiste già";

        string stato = (o.Status ?? "").Trim();
        if (!Stati.Contains(stato)) e["status"] = "Stato non valido";

        // La bozza è un numero riservato e basta: non le si chiede niente.
        if (stato != "bozza")
        {
            if (string.IsNullOrWhiteSpace(o.CustomerName)) e["customerName"] = "Indica il cliente";
            if (string.IsNullOrWhiteSpace(o.SellerCode)) e["sellerCode"] = "Indica il venditore";
            if (string.IsNullOrWhiteSpace(o.TypeCode)) e["typeCode"] = "Indica il tipo";

            if (o.OfferDate == null) e["offerDate"] = "Indica la data";
            else if (o.Year >= 2000 && o.Year <= 2100 && o.OfferDate.Value.Year != o.Year)
                e["offerDate"] = $"La data non appartiene al {o.Year}";
        }

        if (o.Amount != null && o.Amount < 0) e["amount"] = "L'importo non può essere negativo";
        if (o.AmountMax != null && o.Amount != null && o.AmountMax < o.Amount)
            e["amountMax"] = "Il «fino a» non può essere minore dell'importo";

        if (o.Chance != null && (o.Chance < 0 || o.Chance > 100 || o.Chance % 10 != 0))
            e["chance"] = "La chance va da 0 a 100 a passi di 10";

        // «Presa» vuol dire che c'è un ordine: o a corpo (un importo) o a consuntivo.
        if (stato == "presa" && o.OrderAmount == null && !o.IsTimeMaterial)
            e["orderAmount"] = "Un'offerta presa richiede l'importo dell'ordine oppure «a consuntivo»";

        if (o.OrderAmount != null && o.OrderAmount < 0)
            e["orderAmount"] = "L'importo dell'ordine non può essere negativo";

        if (o.PostponedYear != null && (o.PostponedYear < 2000 || o.PostponedYear > 2100))
            e["postponedYear"] = "Anno di rinvio non valido";

        return e;
    }

    /// <summary>
    /// I campi che finiscono nel registro modifiche. Sono quelli su cui poi si ricostruisce
    /// l'andamento del portafoglio: cambiarli cambia il racconto, quindi si tracciano.
    /// </summary>
    public static readonly string[] CampiTracciati =
    {
        "status", "chance", "amount", "amountMax", "orderAmount", "isTimeMaterial",
        "customerId", "customerName", "offerDate", "number", "year", "typeCode",
        "sellerCode", "postponedYear", "nextContact", "quoteId", "projectId",
    };

    /// <summary>
    /// Lo stato del preventivo suggerisce l'esito del registro: <c>accepted</c>/<c>converted</c>
    /// → presa, <c>rejected</c> → persa. Null = il preventivo non dice niente di utile.
    ///
    /// <para>È un <b>suggerimento</b>: chi lo applica lo fa solo se la riga di registro è ancora
    /// <c>aperta</c>. La fonte di verità dell'esito resta il registro, perché copre anche le
    /// offerte che in ATEC PM non esistono.</para>
    /// </summary>
    public static string? EsitoDaStatoPreventivo(string? statoQuote) => (statoQuote ?? "").ToLowerInvariant() switch
    {
        "accepted" or "converted" => "presa",
        "rejected" => "persa",
        _ => null,
    };
}
