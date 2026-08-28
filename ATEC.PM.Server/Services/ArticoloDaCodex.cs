using System.Data;
using ATEC.PM.Shared.DTOs;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Da un articolo <b>Codex</b> alla riga di <b>DDP Commerciale</b>: che cosa scrivere in
/// «Codice», «Cod. ATEC», e se si può già dire da chi lo si compra e a che prezzo.
///
/// <para>La convenzione della griglia è: colonna <b>Codice</b> = codice commerciale Danea
/// (<c>catalog_items.code</c>), colonna <b>Cod. ATEC</b> = codice Codex della NUOVA codifica.
/// Un componente di composizione — e il grezzo di un 101 (#135) — nasce però da un codice
/// Codex, non da un articolo Danea, e i due campi vanno riempiti solo se c'è davvero
/// qualcosa da metterci.</para>
///
/// <para>🪤 <b>In <c>atec_code</c> può finire SOLO <c>codex_items.codice_nuovo</c>, mai il
/// codice storico.</b> Non è una preferenza: <c>AssignCore</c> rifiuta le righe senza codice
/// nuovo («in Extra1 vanno SOLO codici nuovi»), quindi <c>catalog_items.atec_code</c>
/// contiene per costruzione solo codici nuovi, e <c>/catalog-mapping/orphans</c> classifica
/// come <b>refuso</b> ogni <c>atec_code</c> che non corrisponde a nessun <c>codice_nuovo</c>.
/// Scriverci il codice storico riempirebbe la colonna con un valore che nessuna query può
/// agganciare — e siccome la griglia mostra l'icona «Assegna codice ATEC» <b>solo quando il
/// campo è vuoto</b>, toglierebbe pure l'unico comando che su quelle righe serve. Sembrerebbe
/// sistemato, e sarebbe peggio.</para>
///
/// <para>Quindi: niente codice nuovo → <c>atec_code</c> resta vuoto (stato «da codificare»,
/// che è la verità) e in <c>part_number</c> resta il codice Codex, che è l'unico
/// identificatore che quel pezzo ha — serve alla richiesta d'offerta, che stampa un codice
/// per riga, e tiene viva l'icona di assegnazione, che pretende un <c>part_number</c> non
/// vuoto oppure un articolo collegato.</para>
///
/// <para>Questa regola stava dentro <c>ProjectsController</c>; è uscita di lì con la #135,
/// che ha un secondo posto da cui chiamarla (<see cref="GrezziDerivazione"/>). Una copia
/// sola: due copie divergono al primo cambiamento e nessuno se ne accorge.</para>
/// </summary>
public static class ArticoloDaCodex
{
    /// <summary>Esito della risoluzione, pronto per l'INSERT in <c>bom_items</c>.</summary>
    public readonly record struct Esito(
        string PartNumber,
        string AtecCode,
        int? CatalogItemId,
        int? SupplierId,
        decimal? UnitCost);

    /// <param name="rawCodice">Codice Codex come sta in <c>codex_items.codice</c> (senza punti).</param>
    /// <param name="codiceNuovo">La nuova codifica dello stesso articolo, se c'è.</param>
    public static Esito Risolvi(IDbConnection c, string? rawCodice, string? codiceNuovo)
    {
        string codexFormattato = CodexListItem.FormatCodice(rawCodice ?? "");
        string atec = (codiceNuovo ?? "").Replace(".", "").Trim();
        if (atec.Length == 0)
            return new Esito(codexFormattato, "", null, null, null);

        // Articolo Danea associato a quel codice ATEC. Si aggancia solo se ce n'è ESATTAMENTE
        // uno: con due fornitori mappati sullo stesso codice la scelta è dell'utente, non
        // nostra (un codice ATEC dice CHE PEZZO È, i codici Danea DA CHI LO COMPRI).
        var articoli = c.Query<(int Id, string Code, decimal? UnitCost, int? SupplierId)>(@"
            SELECT id, COALESCE(code,''), unit_cost, supplier_id
            FROM catalog_items
            WHERE is_active = 1 AND REPLACE(COALESCE(atec_code,''), '.', '') = @Atec
            ORDER BY id", new { Atec = atec }).ToList();

        if (articoli.Count != 1)
            return new Esito(codexFormattato, atec, null, null, null);

        var art = articoli[0];
        return new Esito(
            art.Code.Length > 0 ? art.Code : codexFormattato, atec, art.Id, art.SupplierId, art.UnitCost);
    }
}
