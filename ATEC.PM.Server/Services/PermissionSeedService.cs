using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Porta i permessi dal modello vecchio (livelli + liste bianche di ruolo + elenco cablato della
/// Contabilità) a quello nuovo: <b>una riga per persona e funzione</b> in
/// <c>employee_feature_access</c> — vedi <c>PIANO-PERMESSI.md</c>, Fase A.
///
/// <para>Il punto delicato è che nessuno deve perdere niente nel momento del passaggio. Per
/// ottenerlo il seed non «traduce a mano» le regole vecchie: <b>interroga il motore vecchio</b>
/// (<see cref="FeatureAccessService"/>) funzione per funzione, esattamente come farebbe una
/// richiesta HTTP, e scrive quello che risponde. Così la fotografia è identica per costruzione,
/// non per bravura di chi la ricopia.</para>
///
/// <para><see cref="Diff"/> è il controllo che rende la cosa affidabile: rifà lo stesso confronto
/// e dice dove i due motori NON coincidono. Deve tornare vuoto prima di accendere il modello
/// nuovo — il piano lo chiede esplicitamente, «col diff, non con la fiducia».</para>
/// </summary>
public class PermissionSeedService
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly PermissionChangeService _changes;

    public PermissionSeedService(DbService db, FeatureAccessService access, PermissionChangeService changes)
    {
        _db = db;
        _access = access;
        _changes = changes;
    }

    /// <summary>Dipendente attivo con un'utenza: sono le persone che entrano davvero.</summary>
    private sealed class Persona
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public sealed class RigaDiff
    {
        public int EmployeeId { get; set; }
        public string Nome { get; set; } = "";
        public string FeatureKey { get; set; } = "";
        public string? Vecchio { get; set; }
        public string? Nuovo { get; set; }
    }

    public sealed class Esito
    {
        public int Persone { get; set; }
        public int Funzioni { get; set; }
        public int RigheScritte { get; set; }
        public int RigheTolte { get; set; }
        public List<RigaDiff> Differenze { get; set; } = new();
    }

    private List<Persona> PersoneAttive(System.Data.IDbConnection c) =>
        c.Query<Persona>(@"
            SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Nome, COALESCE(user_role,'') AS Role
            FROM employees
            WHERE status = 'ACTIVE' AND username IS NOT NULL AND username <> ''
            ORDER BY id").ToList();

    private List<string> FunzioniRegistrate(System.Data.IDbConnection c) =>
        c.Query<string>("SELECT feature_key FROM auth_features ORDER BY feature_key").ToList();

    /// <summary>
    /// La riga jolly: vale qualunque funzione, anche quelle che non esistono ancora.
    /// Ce l'ha solo chi amministra i permessi.
    /// </summary>
    public const string Jolly = "*";

    /// <summary>
    /// Chi può amministrare i permessi prende il jolly. Serve perché, una volta invertito il
    /// fallback, una funzione NUOVA nasce invisibile a chiunque: senza jolly il primo deploy che
    /// aggiunge una pagina la nasconderebbe anche all'amministratore, e non resterebbe nessuno in
    /// grado di concederla. Non è un livello mascherato — è una riga come le altre, visibile sulla
    /// scheda della persona e togliibile.
    /// </summary>
    private bool AmministraPermessi(Persona p) =>
        _access.CanWriteUser(p.Id, p.Role, "nav.permessi");

    /// <summary>
    /// Cosa vede questa persona col motore VECCHIO: funzione → <c>READ</c> o <c>FULL</c>.
    /// Le funzioni che non vede non compaiono, perché nel modello nuovo «riga assente = non vede».
    /// </summary>
    private Dictionary<string, string> FotografiaVecchia(Persona p, List<string> funzioni)
    {
        var risultato = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string chiave in funzioni)
        {
            if (!_access.CanAccessUser(p.Id, p.Role, chiave)) continue;
            risultato[chiave] = _access.CanWriteUser(p.Id, p.Role, chiave)
                ? FeatureAccessService.AccessFull
                : FeatureAccessService.AccessRead;
        }

        if (AmministraPermessi(p)) risultato[Jolly] = FeatureAccessService.AccessFull;

        return risultato;
    }

    /// <summary>Cosa ha già scritto in <c>employee_feature_access</c> (modello nuovo).</summary>
    private Dictionary<string, string> FotografiaNuova(System.Data.IDbConnection c, int employeeId)
    {
        var righe = c.Query<(string FeatureKey, string Access)>(
            "SELECT feature_key AS FeatureKey, access AS Access FROM employee_feature_access WHERE employee_id = @Id",
            new { Id = employeeId });
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in righe) d[r.FeatureKey] = r.Access;
        return d;
    }

    /// <summary>
    /// Materializza la fotografia di oggi. Tocca SOLO le righe <c>origin = 'CLASSE'</c>: quelle
    /// messe a mano (<c>MANO</c>) sono decisioni di qualcuno e non si sovrascrivono, nemmeno
    /// rieseguendo il seed.
    /// </summary>
    /// <param name="prova">true = non scrive niente, dice solo cosa farebbe.</param>
    /// <param name="changedBy">
    /// Chi ha lanciato il seed (claim <c>NameIdentifier</c>): finisce nel registro, perché anche
    /// una migrazione è una modifica dei permessi di qualcuno e deve avere un nome.
    /// </param>
    public Esito Seed(bool prova = false, int? changedBy = null)
    {
        using var c = _db.Open();
        var persone = PersoneAttive(c);
        var funzioni = FunzioniRegistrate(c);
        var esito = new Esito { Persone = persone.Count, Funzioni = funzioni.Count };

        foreach (Persona p in persone)
        {
            var vecchia = FotografiaVecchia(p, funzioni);
            var giaScritte = c.Query<(string FeatureKey, string Access, string Origin)>(
                "SELECT feature_key AS FeatureKey, access AS Access, origin AS Origin FROM employee_feature_access WHERE employee_id=@Id",
                new { Id = p.Id }).ToDictionary(x => x.FeatureKey, x => x, StringComparer.OrdinalIgnoreCase);

            // Solo un cambio VERO fa alzare la versione e partire l'avviso: il seed si rilancia
            // quante volte serve, e un avviso a ogni rilancio farebbe ricaricare i permessi a
            // tutto l'ufficio per niente.
            bool cambiato = false;

            foreach ((string chiave, string accesso) in vecchia)
            {
                bool presente = giaScritte.TryGetValue(chiave, out var riga);

                // Una riga messa a mano vince sempre sul seed.
                if (presente && string.Equals(riga.Origin, "MANO", StringComparison.OrdinalIgnoreCase)) continue;

                esito.RigheScritte++;
                if (prova) continue;
                c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
                            VALUES (@Id, @Key, @Access, 'CLASSE')
                            ON DUPLICATE KEY UPDATE access = VALUES(access), origin = 'CLASSE'",
                    new { Id = p.Id, Key = chiave, Access = accesso });

                string? prima = presente ? riga.Access : null;
                if (string.Equals(prima, accesso, StringComparison.OrdinalIgnoreCase)) continue;
                _changes.Registra(c, p.Id, chiave, prima, accesso, PermissionChangeService.OriginClasse, changedBy);
                cambiato = true;
            }

            // Righe di classe che il motore vecchio non concede più: vanno tolte, o il seed
            // rieseguito lascerebbe in giro permessi che nessuno ha più.
            foreach ((string chiave, var riga) in giaScritte)
            {
                if (string.Equals(riga.Origin, "MANO", StringComparison.OrdinalIgnoreCase)) continue;
                if (vecchia.ContainsKey(chiave)) continue;
                esito.RigheTolte++;
                if (prova) continue;
                c.Execute("DELETE FROM employee_feature_access WHERE employee_id=@Id AND feature_key=@Key",
                    new { Id = p.Id, Key = chiave });
                _changes.Registra(c, p.Id, chiave, riga.Access, null, PermissionChangeService.OriginClasse, changedBy);
                cambiato = true;
            }

            // Versione + avviso in tempo reale: senza, la persona si ritrova i permessi nuovi
            // solo al prossimo login (fino a 8 ore dopo).
            if (cambiato) _changes.Propaga(c, p.Id);
        }

        return esito;
    }

    /// <summary>
    /// Confronto fra i due motori, persona per persona e funzione per funzione.
    /// <b>Deve tornare vuoto</b> prima di accendere il modello nuovo: è l'unico modo di accorgersi
    /// di una differenza prima che se ne accorgano gli utenti.
    /// </summary>
    public Esito Diff()
    {
        using var c = _db.Open();
        var persone = PersoneAttive(c);
        var funzioni = FunzioniRegistrate(c);
        var esito = new Esito { Persone = persone.Count, Funzioni = funzioni.Count };

        foreach (Persona p in persone)
        {
            var vecchia = FotografiaVecchia(p, funzioni);
            var nuova = FotografiaNuova(c, p.Id);

            foreach (string chiave in vecchia.Keys.Union(nuova.Keys, StringComparer.OrdinalIgnoreCase))
            {
                vecchia.TryGetValue(chiave, out string? v);
                nuova.TryGetValue(chiave, out string? n);
                if (string.Equals(v, n, StringComparison.OrdinalIgnoreCase)) continue;
                esito.Differenze.Add(new RigaDiff
                {
                    EmployeeId = p.Id,
                    Nome = p.Nome,
                    FeatureKey = chiave,
                    Vecchio = v,
                    Nuovo = n,
                });
            }
        }

        return esito;
    }

    /// <summary>
    /// Chiavi citate nel codice ma assenti dal catalogo. Finché il fallback è permissivo una
    /// chiave scritta male dà <b>accesso libero</b> e nessuno se ne accorge; dopo l'inversione
    /// darebbe 403 a tutti. Va guardata prima di invertire (PIANO-PERMESSI.md §7.2).
    /// </summary>
    public List<string> ChiaviNonRegistrate(IEnumerable<string> usateNelCodice)
    {
        using var c = _db.Open();
        var catalogo = FunzioniRegistrate(c).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return usateNelCodice.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => !catalogo.Contains(k))
            .OrderBy(k => k)
            .ToList();
    }
}
