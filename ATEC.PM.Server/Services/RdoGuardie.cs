namespace ATEC.PM.Server.Services;

/// <summary>
/// Guardie del ciclo RDO Acquisti: l'unica copia delle regole che decidono se una gara
/// può nascere o essere aggiudicata in blocco. Le usa <c>PurchaseRfqController</c>
/// (POST di creazione e SelectWinner); i test stanno in
/// <c>ATEC.PM.Tests/Calcoli/RdoGuardieTests.cs</c>.
///
/// <para>Perché esistono: aggiudicare una RDO riscrive su OGNI riga l'identità
/// dell'articolo del vincitore (codice, descrizione, articolo Danea, codice ATEC).
/// Se in gara sono finite righe di articoli diversi, le righe dell'altro articolo
/// diventano quello del vincitore e nella distinta non resta traccia di cos'erano.
/// La regola è: <b>una gara = un articolo</b>, e in dubbio si RIFIUTA — mai saltare
/// righe (resterebbero prigioniere in una RDO chiusa) né aggiustare in silenzio.</para>
/// </summary>
public static class RdoGuardie
{
    /// <summary>Codice ATEC normalizzato per confronto: via i punti della formattazione, trim.</summary>
    public static string Normalizza(string? codice) => (codice ?? "").Replace(".", "").Trim();

    /// <summary>Codici distinti (normalizzati, case-insensitive) delle righe in gara.</summary>
    public static List<string> CodiciInGara(IEnumerable<string?> codiciRighe) =>
        codiciRighe.Select(Normalizza).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Messaggio d'errore se la gara NON è aggiudicabile in blocco, altrimenti null.
    ///
    /// <para>Due regole: (1) righe con codici efficaci <b>diversi</b> = gara mista;
    /// (2) <b>più</b> righe tutte <b>senza</b> codice = altrettanto mista — due righe non
    /// mappate sono per definizione articoli che nessuno può giurare siano lo stesso
    /// (RequestOffers infatti le mette una per RDO). Una riga sola senza codice resta
    /// aggiudicabile: non c'è nessun'altra riga da confondere.</para>
    ///
    /// <para>Il confronto è TRA le righe, mai con la testata: il codice di testata è
    /// congelato alla creazione e diverge legittimamente quando il buyer mappa
    /// l'articolo mentre aspetta le offerte.</para>
    /// </summary>
    public static string? GaraMista(IReadOnlyList<string> codiciInGara, int numeroRighe)
    {
        if (codiciInGara.Count > 1)
        {
            var mostrati = codiciInGara.Select(x => x.Length > 0 ? x : "(senza Cod. ATEC)").Take(4);
            return $"Questa RDO contiene righe di articoli diversi ({string.Join(", ", mostrati)}" +
                (codiciInGara.Count > 4 ? " …" : "") + "): aggiudicarla riscriverebbe le righe di un " +
                "articolo con quelle di un altro. Annulla la RDO e rifai una gara per ogni articolo.";
        }
        if (numeroRighe > 1 && codiciInGara.Count == 1 && codiciInGara[0].Length == 0)
        {
            return "Questa RDO ha più righe tutte senza Cod. ATEC: nessuno può garantire che siano " +
                "lo stesso articolo, e aggiudicarle in blocco le riscriverebbe tutte con l'identità " +
                "del vincitore. Annulla la RDO e rifai una gara per riga (o assegna prima i codici).";
        }
        return null;
    }

    /// <summary>
    /// Messaggio d'errore se il prezzo dell'offerta non è aggiudicabile, altrimenti null.
    /// Senza prezzo non si aggiudica (il ripiego sul costo di riga inventava prezzi che
    /// nessun fornitore aveva offerto); a zero o negativo nemmeno — quel numero finisce
    /// dritto in <c>bom_items.unit_cost</c>, nel Bilancio e nell'ordine Danea.
    /// </summary>
    public static string? PrezzoNonAggiudicabile(decimal? unitPrice)
    {
        if (!unitPrice.HasValue)
            return "Registrare il prezzo dell'offerta prima di scegliere il vincitore.";
        if (unitPrice.Value <= 0)
            return "Il prezzo dell'offerta è zero o negativo: non si aggiudica una gara a un " +
                "prezzo che finirebbe dritto nella distinta, nel Bilancio e nell'ordine Danea. " +
                "Correggere il prezzo dell'offerta.";
        return null;
    }

    /// <summary>
    /// Etichette delle righe il cui codice efficace NON coincide col codice della gara
    /// che si sta creando (vuoto compreso). La POST di creazione rifiuta in blocco se la
    /// lista non è vuota: il raggruppamento per codice lo fa il client, ma un bundle web
    /// vecchio in cache (o una chiamata diretta) ricreerebbe le gare miste che
    /// CreateRfqDialog ha smesso di produrre — la regola deve valere anche qui.
    /// </summary>
    public static List<string> RigheFuoriCodice(
        string codiceGara, IEnumerable<(string Etichetta, string? Codice)> righe)
    {
        string atteso = Normalizza(codiceGara);
        return righe
            .Where(r => !string.Equals(Normalizza(r.Codice), atteso, StringComparison.OrdinalIgnoreCase))
            .Select(r => string.IsNullOrWhiteSpace(r.Etichetta)
                ? (Normalizza(r.Codice).Length > 0 ? Normalizza(r.Codice) : "(riga senza descrizione)")
                : r.Etichetta.Trim())
            .ToList();
    }
}
