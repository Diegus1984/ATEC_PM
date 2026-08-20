using System.Text.RegularExpressions;
using ATEC.PM.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Il guardiano della cache delle anagrafiche (blocco E4).
///
/// <para><b>Perché esiste.</b> Una cache in memoria è corretta finché <b>ogni</b> scrittura sulla
/// tabella la invalida. Basta un punto dimenticato e quella tabella serve dati vecchi per sempre —
/// molto peggio che rileggerla ogni volta. E dimenticarlo è la norma, non l'eccezione: in questo
/// stesso progetto, delle quattro operazioni di <c>DepartmentsController</c> tre mandano la
/// notifica di aggiornamento e la quarta (la PATCH di un singolo campo) no.</para>
///
/// <para>Quindi la regola non è affidata alla memoria di chi scrive codice: questo test legge i
/// sorgenti, cerca chi scrive sulle tabelle in cache e pretende che quel file invalidi. Il giorno
/// in cui qualcuno aggiunge una scrittura senza invalidare, il test diventa rosso — e i test sono
/// il cancello del deploy, quindi l'aggiornamento del server si ferma lì.</para>
/// </summary>
public class CacheAnagraficheTests
{
    /// <summary>
    /// Chi può scrivere senza invalidare, e perché.
    /// <list type="bullet">
    /// <item><c>Migrations/</c>: girano all'avvio, prima che il server risponda a chiunque — la
    /// cache è ancora vuota.</item>
    /// <item><c>DbService.cs</c>: il seed dello schema, stesso motivo.</item>
    /// <item><c>FullBackupService.cs</c>: il ripristino riscrive tutto e chiama
    /// <c>InvalidaTutto()</c>, che non nomina le singole voci.</item>
    /// <item><c>CatalogoPermessiSync.cs</c>: EnsureCatalogo (rebuild §12, passo 2) gira dentro
    /// <c>InitDatabase</c>, sotto il lock delle migrazioni, prima che il server risponda a
    /// chiunque — la cache è ancora vuota, come per le migrazioni. ⚠️ Se un giorno venisse
    /// chiamata a RUNTIME (es. un endpoint admin «riallinea»), il chiamante deve fare
    /// <c>Reload()</c>: questa esenzione copre solo l'avvio.</item>
    /// </list>
    /// </summary>
    private static readonly string[] Esenti =
    {
        Path.Combine("ATEC.PM.Server", "Migrations"),
        Path.Combine("ATEC.PM.Server", "Services", "DbService.cs"),
        Path.Combine("ATEC.PM.Server", "Services", "FullBackupService.cs"),
        Path.Combine("ATEC.PM.Server", "Services", "CatalogoPermessiSync.cs"),
    };

    [Fact]
    public void OgniScritturaSuUnAnagraficaInCacheDeveInvalidarla()
    {
        string radice = CartellaServer();
        var mancanti = new List<string>();
        int scrittoriTrovati = 0;

        foreach ((string voce, string[] tabelle) in Anagrafica.Tabelle)
        {
            foreach (string file in Directory.EnumerateFiles(radice, "*.cs", SearchOption.AllDirectories))
            {
                if (Esenti.Any(e => file.Contains(e, StringComparison.OrdinalIgnoreCase))) continue;

                string testo = File.ReadAllText(file);
                string[] scritture = tabelle.Where(t => Scrive(testo, t)).ToArray();
                if (scritture.Length == 0) continue;
                scrittoriTrovati++;

                // Il file scrive: deve anche invalidare quella voce (per nome della costante,
                // non per stringa: così rinominare la costante non fa passare il controllo).
                string costante = NomeCostante(voce);
                if (!testo.Contains($"Invalida(Anagrafica.{costante})") &&
                    !testo.Contains("InvalidaTutto()"))
                {
                    mancanti.Add(
                        $"{Path.GetFileName(file)} scrive su {string.Join(", ", scritture)} " +
                        $"ma non chiama _cache.Invalida(Anagrafica.{costante})");
                }
            }
        }

        Assert.True(mancanti.Count == 0,
            "Scritture su anagrafiche in cache che non invalidano (dati vecchi serviti per sempre):\n  " +
            string.Join("\n  ", mancanti) +
            "\n\nAggiungere _cache.Invalida(...) DOPO il commit, oppure togliere la tabella dal " +
            "registro in Anagrafica.Tabelle se non deve più stare in cache.");

        // La guardia deve aver trovato QUALCOSA: un test che non trova nessuno scrittore passa a
        // vuoto e sorveglia il nulla — è così che una rete di sicurezza smette di esserlo senza
        // che nessuno se ne accorga.
        Assert.True(scrittoriTrovati >= 2,
            $"La guardia ha trovato solo {scrittoriTrovati} file che scrivono sulle tabelle in cache: " +
            "o le tabelle non sono più scritte da nessuno (e allora la cache non serve), o il " +
            "riconoscimento delle scritture non funziona più.");
    }

    /// <summary>
    /// Il file contiene una scrittura su quella tabella?
    /// <para>Volutamente <b>larga</b>: qualunque comando che non sia una SELECT e che nomini la
    /// tabella entro poche righe conta come scrittura. Un falso positivo costa un'invalidazione in
    /// più (una rilettura da 0,1 ms); un falso negativo costa dati vecchi per sempre. Prende anche
    /// le forme che una regex ingenua si perde: <c>DELETE alias FROM tabella JOIN …</c>,
    /// <c>INSERT … SELECT</c>, <c>REPLACE INTO</c>, il nome fra backtick e l'SQL spezzato su più
    /// righe.</para>
    /// <para>⚠️ Il controllo è per FILE, non per singola istruzione: un file che invalida una volta
    /// sola passa anche se ha due scritture. È il limite accettato — la domanda «questo file si
    /// ricorda della cache?» si può fare a macchina, «questo ramo di codice» no.</para>
    /// </summary>
    private static bool Scrive(string testo, string tabella) =>
        Regex.IsMatch(testo,
            $@"\b(INSERT|UPDATE|DELETE|REPLACE|TRUNCATE)\b[^;]{{0,400}}?`?\b{Regex.Escape(tabella)}\b`?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Ogni voce del registro dichiara almeno una tabella: un registro a metà non sorveglia niente.</summary>
    [Fact]
    public void IlRegistroDelleAnagraficheEcompleto()
    {
        Assert.NotEmpty(Anagrafica.Tabelle);
        Assert.All(Anagrafica.Tabelle, v => Assert.NotEmpty(v.Value));
    }

    /// <summary>
    /// Le regole dei permessi sono l'ALTRA cosa tenuta in memoria (dentro
    /// <c>FeatureAccessService</c>, che ha una sua cache storica) e valgono la stessa regola: chi
    /// le scrive deve ricaricarle. Lì la cache non ha nemmeno una scadenza a fare da rete, quindi
    /// una scrittura che non ricarica lascerebbe in vigore le regole vecchie <b>fino al riavvio
    /// del servizio</b> — sui permessi.
    /// <para>La cache non è stata spostata dentro <see cref="AnagraficheCache"/> apposta: quelle
    /// per-persona sono a scadenza breve e la scadenza copre punti di scrittura non ancora
    /// censiti (i reparti di un dipendente). Toglierla prima di averli tappati trasformerebbe una
    /// finestra di un minuto in una infinita. Ma l'invariante «chi scrive, invalida» si sorveglia
    /// lo stesso, ed è quello che fa questo test.</para>
    /// </summary>
    [Fact]
    public void OgniScritturaSulleRegoleDeiPermessiDeveRicaricarle()
    {
        // Le regole del motore vecchio (livelli e liste di ruolo) E la tabella del motore in
        // vigore oggi, quella per persona: è tenuta in memoria anche lei, e chi la scrive deve
        // dimenticare quella persona o il suo menu resta quello di prima.
        string[] tabelleRegole = { "auth_levels", "auth_features", "auth_role_features", "employee_feature_access" };
        string radice = CartellaServer();
        var mancanti = new List<string>();
        int scrittoriTrovati = 0;

        foreach (string file in Directory.EnumerateFiles(radice, "*.cs", SearchOption.AllDirectories))
        {
            if (Esenti.Any(e => file.Contains(e, StringComparison.OrdinalIgnoreCase))) continue;
            if (file.EndsWith("FeatureAccessService.cs", StringComparison.OrdinalIgnoreCase)) continue;

            string testo = File.ReadAllText(file);
            string[] scritture = tabelleRegole.Where(t => Scrive(testo, t)).ToArray();
            if (scritture.Length == 0) continue;
            scrittoriTrovati++;

            // Valgono la ricarica totale, ApplicaEPropaga (che la fa per conto suo, confrontando i
            // permessi prima e dopo) e DimenticaPersona/Propaga per le righe di una sola persona.
            if (!testo.Contains("Reload()") && !testo.Contains("ApplicaEPropaga(") &&
                !testo.Contains("DimenticaPersona(") && !testo.Contains("Propaga("))
            {
                mancanti.Add($"{Path.GetFileName(file)} scrive su {string.Join(", ", scritture)} " +
                             "ma non ricarica i permessi (Reload / ApplicaEPropaga / DimenticaPersona)");
            }
        }

        Assert.True(mancanti.Count == 0,
            "Scritture sulle regole dei permessi che non ricaricano la cache:\n  " +
            string.Join("\n  ", mancanti));

        Assert.True(scrittoriTrovati >= 2,
            $"Solo {scrittoriTrovati} file scrivono sulle tabelle dei permessi: il riconoscimento " +
            "delle scritture non sta più funzionando.");
    }

    // ── il comportamento della cache ──────────────────────────────────────────

    [Fact]
    public void SenzaInvalidazione_ilValoreArrivaDallaMemoria()
    {
        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        int letture = 0;

        string a = cache.Leggi("prova", () => { letture++; return "primo"; });
        string b = cache.Leggi("prova", () => { letture++; return "secondo"; });

        Assert.Equal("primo", a);
        Assert.Equal("primo", b);
        Assert.Equal(1, letture);
    }

    [Fact]
    public void DopoLInvalidazione_siRileggeDalDatabase()
    {
        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        cache.Leggi("prova", () => "vecchio");

        cache.Invalida("prova");

        Assert.Equal("nuovo", cache.Leggi("prova", () => "nuovo"));
    }

    /// <summary>
    /// La corsa che rende inutile una cache: una lettura partita PRIMA della modifica non deve
    /// poter salvare il proprio risultato dopo che qualcuno ha invalidato — resterebbe lì per
    /// sempre il valore vecchio, ed è esattamente il guasto che non si vede.
    /// </summary>
    [Fact]
    public void LetturaSorpassataDaUnaModifica_nonSporcaLaCache()
    {
        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);

        string risultato = cache.Leggi("prova", () =>
        {
            // «Mentre leggevo, qualcuno ha scritto e invalidato»
            cache.Invalida("prova");
            return "vecchio";
        });

        Assert.Equal("vecchio", risultato);                                   // chi legge ha il suo dato…
        Assert.Equal("nuovo", cache.Leggi("prova", () => "nuovo"));           // …ma la cache non l'ha tenuto
    }

    [Fact]
    public void InvalidaTutto_svuotaOgniVoce()
    {
        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        cache.Leggi("uno", () => "a");
        cache.Leggi("due", () => "b");

        cache.InvalidaTutto();

        Assert.Equal("a2", cache.Leggi("uno", () => "a2"));
        Assert.Equal("b2", cache.Leggi("due", () => "b2"));
    }

    // ── utilità ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Da «aggregazioni-ddp» a «AggregazioniDdp»: il nome della costante si chiede alla classe,
    /// non si scrive a mano in un elenco parallelo. Un elenco scritto a mano copre solo le voci
    /// che c'erano il giorno in cui è stato scritto, e la terza anagrafica messa in cache
    /// scatenerebbe un rosso incomprensibile (o, peggio, un verde immeritato).
    /// </summary>
    private static string NomeCostante(string voce)
    {
        System.Reflection.FieldInfo? campo = typeof(Anagrafica)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .FirstOrDefault(f => f.IsLiteral && (f.GetRawConstantValue() as string) == voce);

        Assert.True(campo != null,
            $"La voce '{voce}' non corrisponde a nessuna costante di Anagrafica: il registro e le " +
            "costanti si sono disallineati.");
        return campo!.Name;
    }

    private static string CartellaServer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, "ATEC.PM.Server");
            if (Directory.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Cartella ATEC.PM.Server non trovata risalendo da " + AppContext.BaseDirectory);
    }
}
