namespace ATEC.PM.Server.Services;

/// <summary>
/// Ordinamento degli elenchi di commesse — <b>una regola sola per tutto il gestionale</b>
/// (segnalazione #41).
/// <para>
/// Il codice commessa esiste in <b>due formati</b>: <c>C{aammgg}_NNN</c> (vecchio gestionale,
/// es. <c>C241204_166</c>) e <c>C{aaaammgg}.NNN</c>. Ordinare per <c>code</c> alfabetico li
/// mescola: <c>C20260805.500</c> (agosto 2026) finiva <b>prima</b> di <c>C241204_166</c>
/// (dicembre 2024), perché <c>'0' &lt; '4'</c>. Qui la data si estrae dal codice e si normalizza
/// a 8 cifre, così i due formati si incolonnano.
/// </para>
/// <para>
/// Le commesse di <b>servizio</b> a codice libero (INTERNA, SERVICE _ SANGRATO…) non hanno data:
/// finiscono in fondo, dove il client le raggruppa sotto «Altre commesse». Fin qui ci finivano
/// per puro caso alfabetico (<c>I</c> e <c>S</c> vengono dopo <c>C</c>): un codice libero che
/// cominciasse per <c>A</c> o <c>B</c> sarebbe comparso in cima.
/// </para>
/// <para>
/// <b>Gemello SQL</b> di <c>projectCodeDate</c> / <c>compareProjectCodes</c> del client
/// (<c>atec-pm-web/src/lib/project-code.ts</c>), usati da <c>buildPmProjectSections</c> nelle
/// barre laterali PM. Se cambia una regola, cambiano tutte e due: gli elenchi che ordina il
/// server e quelli che riordina il client devono dare lo stesso risultato.
/// </para>
/// </summary>
public static class ProjectSorting
{
    /// <summary>
    /// Stati che rendono una commessa "chiusa": non è più lavoro in corso e per default
    /// sparisce dalle liste (l'eliminazione commessa è un soft delete → CANCELLED).
    /// </summary>
    public const string ClosedStatusesSql = "('COMPLETED','CANCELLED')";

    private static string CodeColumn(string alias) =>
        string.IsNullOrWhiteSpace(alias) ? "code" : $"{alias}.code";

    /// <summary>
    /// Espressione SQL che estrae dal codice commessa la data <c>aaaammgg</c>, o <c>''</c> se
    /// il codice non ce l'ha (commessa di servizio).
    /// </summary>
    /// <param name="alias">Alias della tabella <c>projects</c> nella query; stringa vuota se la
    /// query fa <c>FROM projects</c> senza alias.</param>
    public static string CodeDate(string alias = "p")
    {
        string code = CodeColumn(alias);
        // Le due alternative sono nell'ordine 8 → 6 cifre di proposito: la regex a 6 cifre
        // accetterebbe anche le prime 6 di un codice a 8 se la settima non fosse una cifra,
        // ma `[^0-9]|$` lo impedisce — stessa logica del lookahead `(?!\d)` lato client.
        return $@"(CASE
            WHEN {code} REGEXP '^C[0-9]{{8}}([^0-9]|$)' THEN SUBSTRING({code}, 2, 8)
            WHEN {code} REGEXP '^C[0-9]{{6}}([^0-9]|$)' THEN CONCAT('20', SUBSTRING({code}, 2, 6))
            ELSE '' END)";
    }

    /// <summary>
    /// Condizione SQL «questa riga è una commessa vera» (codice <c>C</c> + data), cioè non una
    /// delle attività a codice libero che il client raggruppa sotto «Altre Attività».
    /// Gemella di <c>isCommessaCode</c> del client.
    /// </summary>
    public static string IsCommessa(string alias = "p") => $"{CodeDate(alias)} <> ''";

    /// <summary>
    /// La stessa regola di <see cref="IsCommessa"/> ma in C#, per quando il codice è già in
    /// mano (la promozione di un'Altra Attività, #88). Il lookahead <c>(?!\d)</c> è identico
    /// al client: 6 o 8 cifre esatte, non un prefisso di qualcosa di più lungo.
    /// </summary>
    public static bool HasCommessaCode(string? code) =>
        System.Text.RegularExpressions.Regex.IsMatch(code ?? "", @"^C(\d{6}|\d{8})(?!\d)");

    /// <summary>
    /// Corpo di una <c>ORDER BY</c>: commesse di servizio in fondo, poi data crescente (dalla
    /// meno recente alla più recente), poi codice come spareggio fra commesse dello stesso giorno.
    /// </summary>
    /// <param name="alias">Alias della tabella <c>projects</c> (vuoto = nessun alias).</param>
    /// <param name="statusColumn">Colonna di stato (es. <c>p.status</c>) se le commesse chiuse
    /// devono finire in coda; <c>null</c> se la query le ha già escluse nel WHERE.</param>
    public static string OrderBy(string alias = "p", string? statusColumn = null)
    {
        string code = CodeColumn(alias);
        string date = CodeDate(alias);
        string closedFirst = statusColumn is null
            ? ""
            : $"({statusColumn} IN {ClosedStatusesSql}) ASC, ";
        return $"{closedFirst}({date} = '') ASC, {date} ASC, {code} ASC";
    }

    /// <summary>
    /// Il pezzo di <c>ORDER BY</c> che tiene in testa la commessa <b>chiusa</b> iniettata dal
    /// deep-link (<c>includeId</c>): ordinata da chiusa finirebbe nelle ultime pagine dello
    /// scroll infinito, e l'albero resterebbe senza il nodo della commessa che si vede a destra.
    /// <para>
    /// Vale <b>solo per le chiuse</b>, e la condizione sullo stato è tutto il senso di questo
    /// metodo. Una commessa <b>aperta</b> sta già nell'elenco al suo posto cronologico:
    /// spingerla in testa la sposta e basta. È così che la commessa <b>appena creata</b>
    /// compariva in cima all'albero — il client la passa come <c>includeId</c> appena ci
    /// atterra sopra — invece che in fondo, dove la mette la sua data (segnalazione 24/08/2026).
    /// </para>
    /// </summary>
    /// <param name="alias">Alias della tabella <c>projects</c> (vuoto = nessun alias).</param>
    /// <param name="idParam">Nome del parametro SQL con l'id da tenere in testa.</param>
    public static string DeepLinkChiusaInTesta(string alias = "p", string idParam = "@IncludeId")
    {
        string id = string.IsNullOrWhiteSpace(alias) ? "id" : $"{alias}.id";
        string status = string.IsNullOrWhiteSpace(alias) ? "status" : $"{alias}.status";
        return $"({id} = {idParam} AND {status} IN {ClosedStatusesSql}) DESC";
    }
}
