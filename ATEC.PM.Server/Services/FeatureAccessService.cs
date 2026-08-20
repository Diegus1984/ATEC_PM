using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Enforcement server-side dei permessi (sistema VisiWin-style), speculare a quanto fa il
/// client in <c>lib/auth/permissions.ts</c>.
///
/// Due criteri, in ordine:
/// <list type="number">
/// <item>ruoli <b>a livello</b> (<c>access_mode = 'LEVEL'</c>): ruolo→livello
/// (<c>auth_levels</c>) confrontato con feature→livello minimo (<c>auth_features</c>);
/// feature non registrata = accesso libero;</item>
/// <item>ruoli <b>di reparto</b> (<c>access_mode = 'GRANTS'</c>): il livello non
/// concede più nulla, vale solo la lista bianca in <c>auth_role_features</c>;</item>
/// <item>appartenenza al <b>reparto Contabilità</b> (<c>employee_departments</c>): stessa
/// idea, ma legata alla persona e non al ruolo — vedi <see cref="CanAccessUser"/>.</item>
/// </list>
/// La scala dei livelli è lineare e non sa descrivere un reparto: l'amministrazione ha
/// bisogno del SAL (livello dei PM) ma non di MoM/DDP/Check list, che stanno allo stesso
/// livello. Da qui il secondo criterio (migrazione v59).
///
/// Cache in memoria con ricarica esplicita (<see cref="Reload"/>) quando un ADMIN modifica
/// permessi o concessioni.
/// </summary>
public class FeatureAccessService
{
    /// <summary>Concessione in sola lettura: passano solo GET/HEAD/OPTIONS.</summary>
    public const string AccessRead = "READ";
    /// <summary>Concessione piena (lettura e scrittura).</summary>
    public const string AccessFull = "FULL";

    /// <summary>
    /// <b>Diniego esplicito.</b> Una riga con questo valore dice «questa persona NON ha questa
    /// funzione, per decisione», ed è diversa dall'assenza di riga, che dice solo «la sua classe
    /// non gliela dà».
    ///
    /// <para>Serve perché il piano (§4.4) chiede che «Applica classe» non ri-accenda il Timesheet
    /// all'ufficio Acquisti: ma quel Timesheet spento è oggi l'ASSENZA di una riga, e un'assenza
    /// non si può marcare <c>MANO</c>. Senza diniego esplicito la prima applicazione di massa
    /// ridarebbe agli Acquisti il Timesheet e alla Contabilità le commesse — in silenzio, che è
    /// esattamente il difetto che il piano esiste per togliere.</para>
    ///
    /// <para>Una riga di diniego <b>vince sul jolly</b>, come la riga <c>READ</c> vince per la
    /// scrittura: la decisione sulla singola funzione è sempre più specifica del «vale tutto».</para>
    /// </summary>
    public const string AccessNegato = "NO";

    private readonly DbService _db;
    private readonly object _lock = new();

    /// <summary>
    /// Le regole, tutte insieme e immutabili.
    /// <para><b>Un solo riferimento, scambiato in blocco</b> (blocco E4, 15/08/2026). Prima erano
    /// quattro campi che <c>Reload</c> azzerava uno per uno mentre <c>EnsureLoaded</c> ne
    /// controllava due: un <c>Reload</c> che cadeva fra il controllo e l'uso degli altri due
    /// faceva scoppiare la richiesta con un 500 — e capitava proprio quando c'è traffico, cioè
    /// quando un amministratore cambia i permessi mentre l'azienda lavora. Con lo scatto
    /// immutabile chi sta leggendo finisce con le regole di prima (coerenti fra loro) e chi
    /// arriva dopo trova quelle nuove: non esiste un istante in cui mezza decisione viene dalle
    /// regole vecchie e mezza dalle nuove.</para>
    /// </summary>
    private sealed record Regole(
        Dictionary<string, int> LivelliPerRuolo,     // role_name (UPPER) → level_value
        Dictionary<string, int> LivelloMinimo,       // feature_key      → min_level
        HashSet<string> RuoliAListaBianca,           // ruoli GRANTS (UPPER)
        Dictionary<string, string> ConcessioniRuolo  // "ROLE|feature_key" → access
    );

    private Regole? _regole;

    public FeatureAccessService(DbService db) => _db = db;

    private class LevelRow { public string RoleName { get; set; } = ""; public int LevelValue { get; set; } public string AccessMode { get; set; } = "LEVEL"; }
    private class FeatureRow { public string FeatureKey { get; set; } = ""; public int MinLevel { get; set; } }
    private class GrantRow { public string RoleName { get; set; } = ""; public string FeatureKey { get; set; } = ""; public string Access { get; set; } = AccessFull; }

    private static string GrantKey(string role, string featureKey) =>
        $"{role.ToUpperInvariant()}|{featureKey.ToLowerInvariant()}";

    /// <summary>Le regole in vigore: se non ci sono ancora, le legge dal database.</summary>
    private Regole Correnti()
    {
        Regole? pronte = Volatile.Read(ref _regole);
        if (pronte != null) return pronte;

        lock (_lock)
        {
            pronte = Volatile.Read(ref _regole);
            if (pronte != null) return pronte;

            using var c = _db.Open();
            var levels = c.Query<LevelRow>(
                "SELECT role_name AS RoleName, level_value AS LevelValue, access_mode AS AccessMode FROM auth_levels").ToList();
            var features = c.Query<FeatureRow>(
                "SELECT feature_key AS FeatureKey, min_level AS MinLevel FROM auth_features").ToList();
            var grants = c.Query<GrantRow>(
                "SELECT role_name AS RoleName, feature_key AS FeatureKey, access AS Access FROM auth_role_features").ToList();

            pronte = new Regole(
                levels.ToDictionary(l => l.RoleName.ToUpperInvariant(), l => l.LevelValue),
                features.ToDictionary(f => f.FeatureKey, f => f.MinLevel, StringComparer.OrdinalIgnoreCase),
                levels
                    .Where(l => string.Equals(l.AccessMode, "GRANTS", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.RoleName.ToUpperInvariant())
                    .ToHashSet(),
                grants
                    .GroupBy(g => GrantKey(g.RoleName, g.FeatureKey))
                    .ToDictionary(g => g.Key, g => g.First().Access.ToUpperInvariant()));

            Volatile.Write(ref _regole, pronte);
            return pronte;
        }
    }

    /// <summary>Invalida la cache: la prossima richiesta ricarica da DB.</summary>
    public void Reload()
    {
        lock (_lock)
        {
            Volatile.Write(ref _regole, null);
            _contabilitaCache.Clear();
            _personCache.Clear();
            _statoCache.Clear();
            Volatile.Write(ref _motore, 0);
        }
    }

    /// <summary>Livello numerico del ruolo (0 se sconosciuto).</summary>
    public int GetLevelForRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return 0;
        return Correnti().LivelliPerRuolo.TryGetValue(role.ToUpperInvariant(), out int lvl) ? lvl : 0;
    }

    /// <summary>
    /// Ruolo di reparto: non eredita nulla dal livello, vede solo le funzioni concesse.
    /// </summary>
    public bool IsGrantsRole(string? role)
    {
        return !string.IsNullOrEmpty(role) && Correnti().RuoliAListaBianca.Contains(role.ToUpperInvariant());
    }

    /// <summary>Concessione esplicita del ruolo su una funzione ('READ'/'FULL'), o null.</summary>
    public string? GetGrant(string? role, string featureKey)
    {
        if (string.IsNullOrEmpty(role)) return null;
        return Correnti().ConcessioniRuolo.TryGetValue(GrantKey(role, featureKey), out string? access) ? access : null;
    }

    /// <summary>Tutte le concessioni di un ruolo (feature_key → 'READ'/'FULL').</summary>
    public Dictionary<string, string> GetGrantsForRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return new Dictionary<string, string>();
        string prefix = role.ToUpperInvariant() + "|";
        return Correnti().ConcessioniRuolo
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Accesso (almeno in lettura) consentito? Ruolo di reparto → solo se concesso;
    /// altrimenti feature non registrata → consentito, se registrata livello &gt;= min_level.
    /// </summary>
    public bool CanAccess(string? role, string featureKey)
    {
        // Una fotografia sola per tutta la decisione: se un Reload cade nel mezzo, questa
        // richiesta finisce con le regole di prima invece che metà con le une e metà con le altre.
        // Per questo i tre controlli qui sotto lavorano sulla stessa `regole` e non richiamano i
        // metodi pubblici, che ripescherebbero la fotografia PIÙ RECENTE una per volta.
        Regole regole = Correnti();

        // Concessione esplicita: vale per qualsiasi ruolo (è additiva rispetto al livello).
        if (Concessione(regole, role, featureKey) != null) return true;

        // Ruolo di reparto senza concessione: negato, anche se la feature non è registrata.
        // È l'opposto del fallback dei livelli, ed è voluto: la lista bianca deve essere
        // l'elenco COMPLETO di ciò che quel reparto vede.
        if (!string.IsNullOrEmpty(role) && regole.RuoliAListaBianca.Contains(role.ToUpperInvariant()))
            return false;

        if (!regole.LivelloMinimo.TryGetValue(featureKey, out int min))
            return true; // feature non registrata → accesso libero (stesso fallback del client)
        return Livello(regole, role) >= min;
    }

    /// <summary>
    /// Scrittura consentita? Solo una concessione 'READ' la nega: è il modo per dare una
    /// pagina in sola consultazione (es. Clienti per l'amministrazione).
    /// </summary>
    public bool CanWrite(string? role, string featureKey)
    {
        if (!CanAccess(role, featureKey)) return false;
        return !string.Equals(Concessione(Correnti(), role, featureKey), AccessRead, StringComparison.OrdinalIgnoreCase);
    }

    // Le due letture di sopra, ma su una fotografia GIÀ presa: servono a non rimettere in gioco
    // regole diverse in mezzo a una decisione sola.
    private static string? Concessione(Regole regole, string? role, string featureKey) =>
        string.IsNullOrEmpty(role) ? null
            : regole.ConcessioniRuolo.TryGetValue(GrantKey(role, featureKey), out string? access) ? access : null;

    private static int Livello(Regole regole, string? role) =>
        string.IsNullOrEmpty(role) ? 0
            : regole.LivelliPerRuolo.TryGetValue(role.ToUpperInvariant(), out int lvl) ? lvl : 0;

    // ── Reparto Contabilità ────────────────────────────────────────────────────
    //
    // Non è più un ruolo (il vecchio 'AMM' è stato tolto da auth_levels con la migrazione
    // v66): conta l'appartenenza al reparto, così l'ufficio può avere al suo interno un
    // responsabile e dei tecnici senza inventare un ruolo per ciascuno.
    //
    // L'elenco qui sotto è ESCLUSIVO: chi sta in Contabilità vede queste funzioni e basta,
    // il livello del suo ruolo non gli apre nient'altro (Dashboard, Commesse, Timesheet
    // compresi). Prima era additivo — si sommava a tutto quello che il livello concedeva
    // già — ed è il motivo per cui il reparto vedeva mezzo gestionale.

    /// <summary>Funzioni del reparto Contabilità. Elenco chiuso: fuori da qui non passa nulla.</summary>
    private static readonly string[] ContabilitaFeatures =
        { "nav.sal", "sal.economics", "nav.bug_reports", "nav.clienti" };

    /// <summary>Clienti è una consultazione anche per il responsabile (decisione del 04/08/2026).</summary>
    private const string ContabilitaReadOnlyFeature = "nav.clienti";

    /// <summary>Appartenenza al reparto: interroga il DB a ogni richiesta protetta, quindi si tiene in cache.</summary>
    private readonly Dictionary<int, (bool IsContabilita, DateTime Expires)> _contabilitaCache = new();
    private static readonly TimeSpan ContabilitaCacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Il dipendente appartiene al reparto Contabilità?
    /// </summary>
    /// <param name="employeeId">
    /// Id del dipendente, dal claim <c>NameIdentifier</c> del token. **Non usare il nome**:
    /// <c>ClaimTypes.Name</c> contiene il NOME COMPLETO («Marco Carretta»), non lo username,
    /// quindi un confronto con <c>employees.username</c> non trova mai nessuno e il reparto
    /// resta senza restrizioni (successo il 04/08/2026, visto solo a video).
    /// </param>
    public bool IsContabilitaUser(int employeeId)
    {
        if (employeeId <= 0) return false;

        lock (_lock)
        {
            if (_contabilitaCache.TryGetValue(employeeId, out var cached) && cached.Expires > DateTime.UtcNow)
                return cached.IsContabilita;
        }

        using var c = _db.Open();
        bool isContabilita = c.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM employee_departments ed
            JOIN departments d ON d.id = ed.department_id
            WHERE ed.employee_id = @EmployeeId
              AND (d.name LIKE '%Contabil%' OR d.id = 9)",
            new { EmployeeId = employeeId }) > 0;

        lock (_lock)
        {
            _contabilitaCache[employeeId] = (isContabilita, DateTime.UtcNow.Add(ContabilitaCacheTtl));
        }
        return isContabilita;
    }

    /// <summary>
    /// L'utente è vincolato alla lista del reparto Contabilità? Gli ADMIN restano fuori: un
    /// amministratore che lavora anche in contabilità non deve perdere il resto del gestionale.
    /// </summary>
    public bool IsRestrictedToContabilita(int employeeId, string? role)
    {
        int adminLevel = Correnti().LivelliPerRuolo.TryGetValue("ADMIN", out int lvl) ? lvl : int.MaxValue;
        if (GetLevelForRole(role) >= adminLevel) return false;
        return IsContabilitaUser(employeeId);
    }

    /// <summary>
    /// Concessioni del reparto Contabilità (feature_key → 'READ'/'FULL'): il responsabile
    /// (livello ≥ 1) modifica, il tecnico consulta soltanto. Clienti è sempre in lettura.
    /// </summary>
    public Dictionary<string, string> GetContabilitaGrants(string? role)
    {
        bool isTech = GetLevelForRole(role) == 0 || string.Equals(role, "TECH", StringComparison.OrdinalIgnoreCase);
        var grants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in ContabilitaFeatures)
        {
            grants[key] = isTech || string.Equals(key, ContabilitaReadOnlyFeature, StringComparison.OrdinalIgnoreCase)
                ? AccessRead
                : AccessFull;
        }
        return grants;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // MOTORE NUOVO — i permessi stanno sulla PERSONA (PIANO-PERMESSI.md, Fase A)
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Una riga per (persona, funzione) in `employee_feature_access`. Riga assente = non vede.
    // Niente livelli, niente liste di ruolo, niente elenco cablato della Contabilità.
    //
    // 🔑 L'INTERRUTTORE non è un vezzo: è ciò che separa il *deploy* dall'*accensione*. Se il
    // motore nuovo partisse da solo appena il codice arriva in produzione, e lì il seed non fosse
    // ancora stato eseguito, la tabella sarebbe vuota e **tutti perderebbero tutto insieme** —
    // amministratore compreso, quindi senza nemmeno il modo di rimediare dall'app. Con
    // l'interruttore l'ordine è obbligato: deploy → seed → diff a zero → accensione.
    // Si accende scrivendo 'NEW' in app_config('PermissionsEngine') e ricaricando la cache.

    private const string ConfigChiaveMotore = "PermissionsEngine";
    private const string MotoreNuovo = "NEW";

    /// <summary>
    /// Interruttore del motore: 0 = ancora da leggere, 1 = motore nuovo, 2 = motore vecchio.
    /// <para>Un <c>int</c> e non un <c>bool?</c>: <c>Nullable&lt;bool&gt;</c> sono <b>due</b> campi
    /// (valore e «c'è»), quindi due thread possono vederne uno aggiornato e l'altro no, e non si
    /// può nemmeno leggere con una barriera. Su un intero la lettura è atomica per definizione, e
    /// il valore peggiore che si può vedere è lo zero — che vuol dire soltanto «rileggilo».</para>
    /// </summary>
    private int _motore;

    /// <summary>Il motore «permessi sulla persona» è acceso? Default: no (resta il vecchio).</summary>
    public bool IsMotoreNuovoAttivo()
    {
        int noto = Volatile.Read(ref _motore);
        if (noto != 0) return noto == 1;

        // ⚠️ La query sta FUORI dal lock. Questo metodo gira su OGNI richiesta autenticata, e
        // `_lock` è lo stesso che protegge le cache dei permessi: tenerlo mentre si aspetta il
        // database vorrebbe dire mettere in fila tutte le richieste dietro a una SELECT.
        // Nel caso peggiore due richieste leggono la stessa riga di configurazione insieme e
        // scrivono lo stesso identico valore: nessun danno.
        using var c = _db.Open();
        string? valore = c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key = @K", new { K = ConfigChiaveMotore });
        bool acceso = string.Equals(valore, MotoreNuovo, StringComparison.OrdinalIgnoreCase);

        Volatile.Write(ref _motore, acceso ? 1 : 2);
        return acceso;
    }

    /// <summary>Righe di una persona: funzione → READ/FULL. Tenute in cache come l'appartenenza al reparto.</summary>
    private readonly Dictionary<int, (Dictionary<string, string> Grants, DateTime Expires)> _personCache = new();

    /// <summary>
    /// Le funzioni concesse a una persona.
    /// <para>⚠️ Il dizionario è <b>quello in cache</b>, condiviso da tutte le richieste di quella
    /// persona: va letto e basta. Chi ha bisogno di modificarlo se ne fa una copia — scriverci
    /// dentro cambierebbe i permessi di tutte le richieste in corso, e nessun errore lo direbbe.
    /// </para>
    /// </summary>
    public Dictionary<string, string> GetPersonGrants(int employeeId)
    {
        lock (_lock)
        {
            if (_personCache.TryGetValue(employeeId, out var cached) && cached.Expires > DateTime.UtcNow)
                return cached.Grants;
        }

        using var c = _db.Open();
        var righe = c.Query<GrantRow>(
            "SELECT feature_key AS FeatureKey, access AS Access FROM employee_feature_access WHERE employee_id = @Id",
            new { Id = employeeId });

        var grants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (GrantRow r in righe) grants[r.FeatureKey] = r.Access.ToUpperInvariant();

        lock (_lock)
        {
            _personCache[employeeId] = (grants, DateTime.UtcNow.Add(ContabilitaCacheTtl));
        }
        return grants;
    }

    /// <summary>La riga jolly vale qualunque funzione, anche quelle che non esistono ancora.</summary>
    public const string JollyKey = "*";

    /// <summary>
    /// La regola dell'accesso, isolata: è <b>pubblica</b> perché la usa anche l'invariante
    /// «non ci si chiude fuori» (<c>PermissionAdminService</c>), che deve rispondere sulle righe
    /// di venti persone insieme e non può passare dalla cache. Riscriverla in SQL sarebbe una
    /// seconda copia della regola, e la prima volta che le due divergono l'invariante dice di sì
    /// mentre il motore dice di no — cioè si perde l'ultimo amministratore credendo di no.
    /// </summary>
    public static bool ConcedeAccesso(Dictionary<string, string> grants, string featureKey)
    {
        // La riga della singola funzione decide sempre, in positivo come in negativo: è più
        // specifica del jolly. È quel che rende possibile togliere UNA cosa a chi vede tutto.
        if (grants.TryGetValue(featureKey, out string? a)) return !Negato(a);
        return grants.ContainsKey(JollyKey) && !Negato(grants[JollyKey]);
    }

    /// <summary>Come <see cref="ConcedeAccesso"/>, ma per la scrittura. Stesso motivo per cui è pubblica.</summary>
    public static bool ConcedeScrittura(Dictionary<string, string> grants, string featureKey)
    {
        if (grants.TryGetValue(featureKey, out string? a))
            return !Negato(a) && !string.Equals(a, AccessRead, StringComparison.OrdinalIgnoreCase);
        // Nessuna riga sulla funzione: decide il jolly — che però NON è sempre pieno. Può valere
        // READ, e allora fa vedere tutto senza far scrivere niente: per questo il controllo è
        // uguale a quello della riga specifica, non un semplice `ContainsKey`. Chi lo
        // semplificasse darebbe la scrittura sull'intero gestionale a chi ha un jolly di sola
        // lettura, senza che nessuna schermata cambi. (Congelato in RegoleAccessoTests.)
        return grants.TryGetValue(JollyKey, out string? j)
               && !Negato(j)
               && !string.Equals(j, AccessRead, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Riga di diniego esplicito: c'è, e dice di no.</summary>
    public static bool Negato(string? access) =>
        string.Equals(access, AccessNegato, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Accesso per utente specifico. Col motore nuovo contano SOLO le righe della persona;
    /// col vecchio, nel reparto Contabilità vale la sua lista chiusa.
    /// </summary>
    public bool CanAccessUser(int employeeId, string? role, string featureKey)
    {
        if (IsMotoreNuovoAttivo())
            return ConcedeAccesso(GetPersonGrants(employeeId), featureKey);

        if (IsRestrictedToContabilita(employeeId, role))
            return GetContabilitaGrants(role).ContainsKey(featureKey);

        return CanAccess(role, featureKey);
    }

    /// <summary>
    /// Permesso di scrittura per utente specifico: in Contabilità il tecnico è in sola
    /// lettura su tutto, il responsabile scrive tranne che sui Clienti.
    /// </summary>
    public bool CanWriteUser(int employeeId, string? role, string featureKey)
    {
        if (IsMotoreNuovoAttivo())
            return ConcedeScrittura(GetPersonGrants(employeeId), featureKey);

        if (IsRestrictedToContabilita(employeeId, role))
        {
            return GetContabilitaGrants(role).TryGetValue(featureKey, out string? access)
                && string.Equals(access, AccessFull, StringComparison.OrdinalIgnoreCase);
        }

        return CanWrite(role, featureKey);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // STATO DELLA PERSONA — verificato a OGNI richiesta autenticata
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Il token vive 8 ore e fin qui `status` si leggeva SOLO al login: chi veniva cessato
    // continuava a lavorare per mezza giornata, e il gesto «se ne va, ~5 s» del piano
    // (PIANO-PERMESSI.md §4.3 e §8) non buttava fuori nessuno. Ora il controllo sta
    // nell'evento OnTokenValidated (Program.cs), cioè su ogni richiesta e su ogni
    // connessione agli hub: quindi deve costare quanto una lettura da dizionario, non una
    // query. Stessa cache a scadenza breve dell'appartenenza al reparto Contabilità.

    private readonly Dictionary<int, (bool Attivo, DateTime Expires)> _statoCache = new();

    /// <summary>Mezzo minuto: il tempo massimo che un cessato può ancora restare dentro.</summary>
    private static readonly TimeSpan StatoCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Il dipendente è ancora in forza (<c>status = 'ACTIVE'</c>)? Chi disattiva può togliere
    /// anche l'attesa della cache chiamando <see cref="DimenticaPersona"/>.
    /// </summary>
    public bool IsUtenteAttivo(int employeeId)
    {
        if (employeeId <= 0) return false;

        lock (_lock)
        {
            if (_statoCache.TryGetValue(employeeId, out var cached) && cached.Expires > DateTime.UtcNow)
                return cached.Attivo;
        }

        bool attivo;
        try
        {
            using var c = _db.Open();
            attivo = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM employees WHERE id = @Id AND status = 'ACTIVE'",
                new { Id = employeeId }) > 0;
        }
        catch
        {
            // Database irraggiungibile: non si butta fuori nessuno. Un riavvio di MySQL
            // manderebbe in 401 tutti insieme — il client torna al login, e da lì non
            // rientrerebbe comunque perché anche il login vuole il database. Si lascia passare
            // e NON si mette in cache: il controllo si rifà alla richiesta successiva.
            return true;
        }

        lock (_lock)
        {
            _statoCache[employeeId] = (attivo, DateTime.UtcNow.Add(StatoCacheTtl));
        }
        return attivo;
    }

    /// <summary>
    /// Butta via quello che sappiamo di UNA persona (permessi, reparto, stato): la prossima
    /// richiesta rilegge dal database. Da chiamare quando i suoi permessi cambiano o quando
    /// viene disattivata — senza, la modifica arriva solo alla scadenza della cache.
    /// </summary>
    public void DimenticaPersona(int employeeId)
    {
        lock (_lock)
        {
            _personCache.Remove(employeeId);
            _contabilitaCache.Remove(employeeId);
            _statoCache.Remove(employeeId);
        }
    }
}
