namespace ATEC.PM.Server.Services;

/// <summary>
/// Le poche anagrafiche tenute in memoria (blocco E4). Elenchi chiusi di configurazione, letti
/// di continuo e cambiati una volta al mese: la matrice delle transizioni DDP viene interrogata
/// <b>una volta per riga</b> quando si aggiudica una RDO da 200 righe, e le aggregazioni le
/// rilegge ogni utente ogni minuto perché la campanella delle scadenze si aggiorna da sola.
///
/// <para><b>Niente scadenza, mai.</b> Una voce resta valida finché qualcuno non scrive su quella
/// tabella. È la regola del progetto: chi modifica un'anagrafica deve vedere la modifica subito,
/// e i colleghi pure — una cache che serve dati vecchi «solo per qualche secondo» è un difetto,
/// non un'ottimizzazione. Un TTL sarebbe più comodo da scrivere e sbagliato da usare.</para>
///
/// <para><b>Ci sta solo ciò di cui si conoscono TUTTE le scritture.</b> Non è un criterio
/// estetico: se un punto di scrittura sfugge, quella tabella serve dati vecchi per sempre — molto
/// peggio che rileggerla. Perciò qui dentro entrano solo tabelle con un <b>unico endpoint</b> che
/// le scrive, e ci pensa un test a sorvegliare che restino tali
/// (<c>CacheAnagraficheTests</c>): il giorno in cui qualcuno aggiunge una scrittura senza
/// invalidare, il test diventa rosso e l'aggiornamento del server si ferma lì.</para>
///
/// <para>🪤 <b>Invalidare DOPO il commit, non dentro la transazione.</b> InnoDB lavora in
/// REPEATABLE READ: se si invalidasse a metà transazione, un'altra richiesta rileggerebbe i
/// valori <b>di prima</b> (per lei il commit non è ancora avvenuto) e ripopolerebbe la cache con
/// quelli — che resterebbero lì per sempre. Il contatore di versione qui sotto para il caso
/// gemello: una lettura partita prima dell'invalidazione non salva più il suo risultato.</para>
/// </summary>
public sealed class AnagraficheCache
{
    private sealed class Voce
    {
        public object? Valore;
        public long Versione;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Voce> _voci = new();
    private readonly ILogger<AnagraficheCache> _log;

    public AnagraficheCache(ILogger<AnagraficheCache> log) => _log = log;

    /// <summary>
    /// Il valore in cache, oppure quello che <paramref name="carica"/> legge dal database.
    /// </summary>
    public T Leggi<T>(string nome, Func<T> carica) where T : class
    {
        Voce v = _voci.GetOrAdd(nome, _ => new Voce());

        if (Volatile.Read(ref v.Valore) is T pronto) return pronto;

        // La versione si legge PRIMA di andare sul database: se nel frattempo qualcuno invalida,
        // il valore che sto per leggere è già vecchio e non deve finire in cache.
        long versionePrima = Interlocked.Read(ref v.Versione);
        T caricato = carica();

        lock (v)
        {
            if (Interlocked.Read(ref v.Versione) == versionePrima)
                Volatile.Write(ref v.Valore, caricato);
        }
        return caricato;
    }

    /// <summary>
    /// Butta via una voce. Va chiamata <b>dopo il commit</b> della scrittura.
    /// </summary>
    public void Invalida(string nome)
    {
        Voce v = _voci.GetOrAdd(nome, _ => new Voce());
        lock (v)
        {
            Interlocked.Increment(ref v.Versione);
            Volatile.Write(ref v.Valore, null);
        }
        _log.LogDebug("[Cache] Anagrafica '{Nome}' invalidata.", nome);
    }

    /// <summary>
    /// Svuota tutto. Serve al ripristino da backup, che riscrive l'intero database sotto i piedi
    /// dell'applicazione accesa: lì non ha senso ragionare tabella per tabella.
    /// </summary>
    public void InvalidaTutto()
    {
        foreach (string nome in _voci.Keys.ToArray()) Invalida(nome);
        _log.LogInformation("[Cache] Tutte le anagrafiche in memoria sono state invalidate.");
    }
}

/// <summary>
/// Il registro delle anagrafiche in cache: <b>un posto solo</b>. Il nome della voce e le tabelle
/// da cui nasce stanno qui, così il test che sorveglia le scritture ha qualcosa di solido da
/// leggere invece di un elenco sparso nei commenti.
/// </summary>
public static class Anagrafica
{
    /// <summary>Aggregazioni DDP (A1…A9) e i loro stati. Scritte solo da <c>DdpAggregationsController</c>.</summary>
    public const string AggregazioniDdp = "aggregazioni-ddp";

    /// <summary>Matrice delle transizioni di stato DDP. Scritta solo da <c>DdpStatusesController</c>.</summary>
    public const string TransizioniDdp = "transizioni-ddp";

    /// <summary>
    /// Voce → tabelle che la compongono. Il test <c>CacheAnagraficheTests</c> parte da qui:
    /// per ogni tabella cerca chi la scrive e pretende che quel file invalidi la voce.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Tabelle = new Dictionary<string, string[]>
    {
        [AggregazioniDdp] = new[] { "ddp_aggregations", "ddp_aggregation_states" },
        [TransizioniDdp] = new[] { "ddp_status_transitions" },
    };
}
