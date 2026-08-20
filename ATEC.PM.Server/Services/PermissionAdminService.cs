using System.Data;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// La scheda permessi di una persona e i gesti che ci si fanno sopra — PIANO-PERMESSI.md §4 e §5,
/// Fase B. È l'unico posto da cui <c>employee_feature_access</c> viene scritta a mano: prima di
/// questo servizio i permessi si cambiavano solo dal database.
///
/// <para><b>Le tre regole che tengono in piedi il tutto</b>, e che non vanno allentate:</para>
/// <list type="number">
/// <item><b>«Applica classe» riscrive solo le righe <c>origin = CLASSE</c>.</b> Le combo cambiate
/// a mano restano dove sono. Senza questa regola il gesto da trenta secondi «tutti i tecnici
/// vedano le Lavorazioni» ri-accenderebbe il Timesheet all'ufficio Acquisti e ridarebbe le
/// commesse alla Contabilità — in silenzio, perché il piano si regge proprio sulle eccezioni per
/// persona e un timbro di massa gliele cancellerebbe.</item>
/// <item><b>Anteprima obbligatoria</b> prima di ogni applicazione multipla: si conferma l'elenco
/// dei cambi, non il pulsante.</item>
/// <item><b>Non ci si chiude fuori</b> (§5.1): deve restare almeno un dipendente ACTIVE capace di
/// scrivere <c>nav.permessi</c>. Il controllo è dentro la transazione di OGNI scrittura, e non
/// sparso nei singoli endpoint: la rev. 1 del piano lo copriva su un gesto su cinque, ed è
/// esattamente il modo in cui si perde l'accesso all'amministrazione.</item>
/// </list>
/// </summary>
public class PermissionAdminService
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly PermissionChangeService _changes;

    public PermissionAdminService(DbService db, FeatureAccessService access, PermissionChangeService changes)
    {
        _db = db;
        _access = access;
        _changes = changes;
    }

    /// <summary>Chiave della funzione che amministra i permessi: è quella che l'invariante protegge.</summary>
    public const string ChiavePermessi = "nav.permessi";

    /// <summary>Sollevata quando una modifica lascerebbe il gestionale senza amministratori.</summary>
    public sealed class UltimoAmministratoreException : Exception
    {
        public UltimoAmministratoreException(string messaggio) : base(messaggio) { }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // LE 9 AREE DELLA MASCHERA DI PAOLO (§6)
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Stanno qui e non nel client perché sono la stessa cosa che i pacchetti-classe scrivono:
    // due elenchi da tenere allineati divergono al primo che si dimentica, e il risultato è una
    // combo che dice «lettura» e concede la scrittura.
    //
    // Il catalogo del gestionale è molto più largo di queste nove voci (71 chiavi): il resto sta
    // sotto «Funzioni avanzate», chiuse di default. Non si scaricano quaranta combo su un
    // tecnico, ma ci devono essere tutte — «non vedo la pagina X» si risponde dalla scheda, non
    // leggendo il database.

    private sealed record Area(
        string Id,
        string Titolo,
        string[] Chiavi,
        string[] ChiaviSoloScrittura,
        bool DueStati,
        string EtichettaAcceso);

    private static readonly Area[] Aree =
    {
        new("commesse",   "Commesse",             new[] { "nav.commesse" },      Array.Empty<string>(), true,  "vede l'elenco"),
        new("timesheet",  "TimeSheet",            new[] { "nav.timesheet" },     Array.Empty<string>(), true,  "carica le ore"),
        new("risorse",    "Risorse",              new[] { "nav.risorse" },       new[] { "resources.edit" }, false, "lettura e scrittura"),
        new("chat",       "Chat",                 new[] { "project.chat" },      Array.Empty<string>(), true,  "la usa"),
        new("mom",        "Verbali MoM",          new[] { "nav.mom" },           Array.Empty<string>(), false, "lettura e scrittura"),
        new("milestones", "Milestones",           new[] { "nav.milestones" },    Array.Empty<string>(), false, "lettura e scrittura"),
        new("ddp",        "DDP Comm. + Officine", new[] { "project.ddp_commerciale", "project.ddp_officina" }, Array.Empty<string>(), false, "lettura e scrittura"),
        new("lavorazioni","Lavorazioni",          new[] { "nav.work_requests" }, Array.Empty<string>(), false, "lettura e scrittura"),
        new("documenti",  "Documenti",            new[] { "project.documenti" }, Array.Empty<string>(), true,  "accede e carica i file"),
    };

    /// <summary>Chiave → area che la governa. Serve a non ripeterla fra le «Funzioni avanzate».</summary>
    private static readonly Dictionary<string, string> AreaDiChiave =
        Aree.SelectMany(a => a.Chiavi.Concat(a.ChiaviSoloScrittura).Select(k => (k, a.Id)))
            .ToDictionary(x => x.k, x => x.Id, StringComparer.OrdinalIgnoreCase);

    // ══════════════════════════════════════════════════════════════════════════════
    // LETTURA
    // ══════════════════════════════════════════════════════════════════════════════

    private sealed class RigaAccesso
    {
        public string FeatureKey { get; set; } = "";
        public string Access { get; set; } = "";
        public string Origin { get; set; } = "";
    }

    private sealed class Persona
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public string Username { get; set; } = "";
        public string Status { get; set; } = "";
        public string Classe { get; set; } = "";
    }

    private static Persona? LeggiPersona(IDbConnection c, int id, IDbTransaction? tx = null) =>
        c.QueryFirstOrDefault<Persona>(@"
            SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Nome,
                   COALESCE(username,'') AS Username, COALESCE(status,'') AS Status,
                   COALESCE(user_role,'') AS Classe
            FROM employees WHERE id = @Id", new { Id = id }, tx);

    private static Dictionary<string, RigaAccesso> RighePersona(IDbConnection c, int id, IDbTransaction? tx = null) =>
        c.Query<RigaAccesso>(
            "SELECT feature_key AS FeatureKey, access AS Access, origin AS Origin FROM employee_feature_access WHERE employee_id=@Id",
            new { Id = id }, tx)
         .ToDictionary(r => r.FeatureKey, r => r, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Il pacchetto di una classe: funzione → READ/FULL. Se la classe ha il jolly, il pacchetto
    /// è il jolly e basta (vale tutto, comprese le funzioni non ancora inventate).
    /// </summary>
    private static Dictionary<string, string> PacchettoClasse(IDbConnection c, string classe, IDbTransaction? tx = null)
    {
        if (string.IsNullOrWhiteSpace(classe)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return c.Query<(string FeatureKey, string Access)>(
                "SELECT feature_key AS FeatureKey, access AS Access FROM auth_class_features WHERE UPPER(class_name)=UPPER(@C)",
                new { C = classe }, tx)
            .ToDictionary(x => x.FeatureKey, x => x.Access.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Cosa dice la classe su questa funzione: NO / READ / FULL (il jolly vale FULL su tutto).</summary>
    private static string StatoDaPacchetto(Dictionary<string, string> pacchetto, string featureKey)
    {
        if (pacchetto.TryGetValue(featureKey, out string? a))
            return string.Equals(a, StatoCombo.Read, StringComparison.OrdinalIgnoreCase) ? StatoCombo.Read : StatoCombo.Full;
        if (pacchetto.TryGetValue(FeatureAccessService.JollyKey, out string? j))
            return string.Equals(j, StatoCombo.Read, StringComparison.OrdinalIgnoreCase) ? StatoCombo.Read : StatoCombo.Full;
        return StatoCombo.No;
    }

    /// <summary>
    /// Cosa vede DAVVERO la persona su questa funzione. Deve rispondere come il motore, jolly
    /// compreso: chi ha la riga <c>*</c> vede tutto, anche le funzioni per cui non ha una riga
    /// propria.
    ///
    /// <para>🪤 Trovato a video il 13/08: senza l'espansione del jolly la scheda
    /// dell'amministratore diceva «21 funzioni diverse dalla classe», perché confrontava le sue
    /// righe ESPLICITE con l'accesso EFFETTIVO del suo pacchetto. Due nozioni diverse messe a
    /// confronto: il numero era falso proprio sulla persona che deve fidarsi di quel numero.</para>
    /// </summary>
    private static string StatoDaRighe(Dictionary<string, RigaAccesso> righe, string featureKey)
    {
        if (righe.TryGetValue(featureKey, out RigaAccesso? r)) return Livello(r.Access);

        // Nessuna riga propria: decide il jolly, che vale tutto. La riga specifica, quando c'è,
        // vince sul jolly — anche quando dice di no. È la stessa precedenza di
        // FeatureAccessService.ConcedeAccesso.
        if (featureKey != FeatureAccessService.JollyKey
            && righe.TryGetValue(FeatureAccessService.JollyKey, out RigaAccesso? j))
            return Livello(j.Access);

        return StatoCombo.No;
    }

    /// <summary>Il valore di una riga come stato di combo: <c>NO</c> è un diniego, non un'assenza.</summary>
    private static string Livello(string access)
    {
        if (FeatureAccessService.Negato(access)) return StatoCombo.No;
        return string.Equals(access, StatoCombo.Read, StringComparison.OrdinalIgnoreCase)
            ? StatoCombo.Read
            : StatoCombo.Full;
    }

    /// <summary>
    /// Come <see cref="StatoDaRighe"/> ma SENZA il jolly: serve a «Applica classe» e
    /// «Riallinea», che devono ragionare sulle righe vere. Confondere i due significa scrivere
    /// settanta righe esplicite a chi ha il jolly, o non accorgersi che una riga c'è.
    /// </summary>
    private static string StatoRigaEsplicita(Dictionary<string, RigaAccesso> righe, string featureKey) =>
        righe.TryGetValue(featureKey, out RigaAccesso? r) ? Livello(r.Access) : StatoCombo.No;

    private static int Peso(string stato) => stato switch
    {
        StatoCombo.Full => 2,
        StatoCombo.Read => 1,
        _ => 0,
    };

    private static string DaPeso(int peso) => peso switch { 2 => StatoCombo.Full, 1 => StatoCombo.Read, _ => StatoCombo.No };

    /// <summary>
    /// Lo stato di un'area: il MINIMO delle sue chiavi principali, ma <b>declassato a sola
    /// lettura se manca una chiave di sola scrittura</b>.
    ///
    /// <para>🪤 Trovato a video il 13/08. Senza il secondo pezzo l'area «Risorse» diceva
    /// «lettura e scrittura» a un tecnico che ha <c>nav.risorse</c> ma NON <c>resources.edit</c>
    /// — cioè apre il planner e non può toccare niente. La combo dichiarava un permesso che la
    /// persona non ha: esattamente il genere di bugia a video che questo piano esiste per
    /// togliere, e per giunta sulla schermata da cui si decidono i permessi degli altri.</para>
    /// </summary>
    private static string StatoArea(Area a, Func<string, string> stato)
    {
        int principale = a.Chiavi.Select(k => Peso(stato(k))).Min();
        if (principale < 2 || a.ChiaviSoloScrittura.Length == 0) return DaPeso(principale);

        bool scriveDavvero = a.ChiaviSoloScrittura.All(k => Peso(stato(k)) == 2);
        return scriveDavvero ? StatoCombo.Full : StatoCombo.Read;
    }

    public SchedaPermessiDto Scheda(int employeeId)
    {
        using var c = _db.Open();
        Persona? p = LeggiPersona(c, employeeId)
            ?? throw new KeyNotFoundException("Dipendente non trovato");

        var righe = RighePersona(c, employeeId);
        var pacchetto = PacchettoClasse(c, p.Classe);
        var catalogo = c.Query<AuthFeatureDto>(
            "SELECT feature_key AS FeatureKey, display_name AS DisplayName, category AS Category FROM auth_features ORDER BY category, display_name").ToList();

        var scheda = new SchedaPermessiDto
        {
            EmployeeId = p.Id,
            Nome = p.Nome,
            Username = p.Username,
            Status = p.Status,
            Classe = p.Classe,
            ClasseDisplay = c.ExecuteScalar<string?>(
                "SELECT display_name FROM auth_levels WHERE UPPER(role_name)=UPPER(@C) LIMIT 1", new { C = p.Classe }) ?? p.Classe,
            Reparti = c.Query<string>(@"
                SELECT d.name FROM employee_departments ed JOIN departments d ON d.id=ed.department_id
                WHERE ed.employee_id=@Id ORDER BY d.name", new { Id = employeeId }).ToList(),
            Jolly = righe.ContainsKey(FeatureAccessService.JollyKey),
        };

        foreach (Area a in Aree)
        {
            var stati = a.Chiavi.Select(k => Peso(StatoDaRighe(righe, k))).ToList();
            scheda.Aree.Add(new AreaPermessoDto
            {
                Id = a.Id,
                Titolo = a.Titolo,
                Chiavi = a.Chiavi.ToList(),
                ChiaviSoloScrittura = a.ChiaviSoloScrittura.ToList(),
                DueStati = a.DueStati,
                EtichettaAcceso = a.EtichettaAcceso,
                Stato = StatoArea(a, k => StatoDaRighe(righe, k)),
                StatoClasse = StatoArea(a, k => StatoDaPacchetto(pacchetto, k)),
                AMano = a.Chiavi.Concat(a.ChiaviSoloScrittura)
                    .Any(k => righe.TryGetValue(k, out RigaAccesso? r)
                              && string.Equals(r.Origin, PermissionChangeService.OriginMano, StringComparison.OrdinalIgnoreCase)),
                Incoerente = stati.Distinct().Count() > 1,
            });
        }

        // Il catalogo intero, per «Funzioni avanzate» e per il conteggio delle differenze.
        foreach (AuthFeatureDto f in catalogo)
        {
            string stato = StatoDaRighe(righe, f.FeatureKey);
            string statoClasse = StatoDaPacchetto(pacchetto, f.FeatureKey);
            righe.TryGetValue(f.FeatureKey, out RigaAccesso? r);
            AreaDiChiave.TryGetValue(f.FeatureKey, out string? areaId);

            scheda.Funzioni.Add(new FunzionePermessoDto
            {
                FeatureKey = f.FeatureKey,
                DisplayName = f.DisplayName,
                Categoria = f.Category,
                Stato = stato,
                StatoClasse = statoClasse,
                Origin = r?.Origin ?? "",
                AreaId = areaId,
            });

            if (!string.Equals(stato, statoClasse, StringComparison.OrdinalIgnoreCase)) scheda.DiverseDallaClasse++;
        }

        scheda.Storico = Storico(c, employeeId, 20);
        return scheda;
    }

    private static List<StoricoPermessoDto> Storico(IDbConnection c, int employeeId, int quante) =>
        c.Query<StoricoPermessoDto>(@"
            SELECT l.id AS Id, l.feature_key AS FeatureKey,
                   COALESCE(f.display_name, l.feature_key) AS DisplayName,
                   l.access_before AS AccessBefore, l.access_after AS AccessAfter,
                   l.origin AS Origin,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), '—') AS ChangedBy,
                   l.changed_at AS ChangedAt
            FROM employee_feature_access_log l
            LEFT JOIN auth_features f ON f.feature_key = l.feature_key
            LEFT JOIN employees e ON e.id = l.changed_by
            WHERE l.employee_id = @Id
            ORDER BY l.changed_at DESC, l.id DESC
            LIMIT @Quante", new { Id = employeeId, Quante = quante }).ToList();

    /// <summary>
    /// Utenza segnaposto di reparto: <c>acq.generico</c>, <c>ute.generico</c>… Non è una persona.
    /// Il riconoscimento è sull'utenza perché in anagrafica non esiste un campo che le distingua
    /// (sono <c>emp_type = 'INTERNAL'</c> come tutti gli altri). Se un domani nascesse quel campo,
    /// è qui che va sostituito — in un posto solo.
    /// </summary>
    private static bool ESegnaposto(string username) =>
        username.EndsWith(".generico", StringComparison.OrdinalIgnoreCase);

    /// <summary>Elenco della pagina «Permessi»: le persone ATTIVE con un'utenza.</summary>
    public List<RigaPermessiDto> Elenco()
    {
        using var c = _db.Open();
        // Fuori le utenze segnaposto di reparto: non sono persone, e in un elenco che serve a
        // rispondere «chi vede cosa» fanno solo rumore. Restano raggiungibili per id, se un
        // domani servisse configurarne una.
        var persone = c.Query<Persona>(@"
            SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Nome, COALESCE(username,'') AS Username,
                   COALESCE(status,'') AS Status, COALESCE(user_role,'') AS Classe
            FROM employees
            WHERE status='ACTIVE' AND COALESCE(username,'') <> ''
            ORDER BY last_name, first_name")
            .Where(p => !ESegnaposto(p.Username))
            .ToList();

        var classi = c.Query<(string RoleName, string DisplayName)>(
                "SELECT role_name AS RoleName, display_name AS DisplayName FROM auth_levels")
            .ToDictionary(x => x.RoleName, x => x.DisplayName, StringComparer.OrdinalIgnoreCase);

        var reparti = c.Query<(int EmployeeId, string Nome)>(@"
                SELECT ed.employee_id AS EmployeeId, d.name AS Nome
                FROM employee_departments ed JOIN departments d ON d.id = ed.department_id")
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Nome).OrderBy(n => n).ToList());

        var catalogo = c.Query<string>("SELECT feature_key FROM auth_features").ToList();
        var pacchetti = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var righe = new List<RigaPermessiDto>();
        foreach (Persona p in persone)
        {
            if (!pacchetti.TryGetValue(p.Classe, out var pacchetto))
            {
                pacchetto = PacchettoClasse(c, p.Classe);
                pacchetti[p.Classe] = pacchetto;
            }

            var sue = RighePersona(c, p.Id);
            int diverse = catalogo.Count(k =>
                !string.Equals(StatoDaRighe(sue, k), StatoDaPacchetto(pacchetto, k), StringComparison.OrdinalIgnoreCase));

            righe.Add(new RigaPermessiDto
            {
                EmployeeId = p.Id,
                Nome = p.Nome,
                Username = p.Username,
                Classe = p.Classe,
                ClasseDisplay = classi.TryGetValue(p.Classe, out string? d) ? d : p.Classe,
                Reparti = reparti.TryGetValue(p.Id, out var r) ? r : new List<string>(),
                // Solo le righe che CONCEDONO: una riga di diniego è l'opposto di una funzione
                // in mano a qualcuno. Contandole, la scheda della Contabilità dichiarava
                // «71 funzioni» — cioè l'intero catalogo — proprio per la persona che ne vede
                // quattro. Visto a video il 14/08.
                Funzioni = sue.Values.Count(r => !FeatureAccessService.Negato(r.Access)),
                DiverseDallaClasse = diverse,
                // Le eccezioni decise a mano: il numero che la matrioska mette in vetrina
                // al posto del vecchio «diverse dalla classe» (§5.9 rebuild).
                AMano = sue.Values.Count(r => string.Equals(r.Origin, "MANO", StringComparison.OrdinalIgnoreCase)),
                Jolly = sue.ContainsKey(FeatureAccessService.JollyKey),
                Segnaposto = ESegnaposto(p.Username),
            });
        }
        return righe;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // L'INVARIANTE — «non ci si chiude fuori» (§5.1)
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Esegue una scrittura sui permessi e la annulla se ha lasciato il gestionale senza nessuno
    /// che possa amministrare i permessi.
    ///
    /// <para>Il controllo sta QUI, dopo la scrittura e dentro la transazione, e non nei singoli
    /// endpoint: così vale per tutti i percorsi — combo singola, applica classe, copia da, bulk,
    /// e la disattivazione in anagrafica — senza doversene ricordare uno per uno. Ed è l'unico
    /// modo di essere esatti: «questa modifica lascia qualcuno?» si può rispondere solo guardando
    /// il risultato, perché a togliere l'ultimo amministratore può essere una combinazione di
    /// cambi su persone diverse, non un singolo gesto riconoscibile.</para>
    /// </summary>
    /// <param name="scrittura">
    /// Riceve connessione, transazione e l'insieme delle persone da <b>avvisare</b>: chi cambia
    /// davvero ci mette dentro il proprio id, e la propagazione parte DOPO il commit. Farla
    /// dentro la transazione sarebbe sbagliato due volte — si avviserebbe di una modifica che
    /// potrebbe ancora essere annullata, e la cache svuotata verrebbe ripopolata da una
    /// richiesta concorrente con i dati di prima, restando vecchia fino alla scadenza.
    /// </param>
    public T EseguiConGaranzia<T>(
        Func<IDbConnection, IDbTransaction, ISet<int>, T> scrittura, string messaggioRifiuto)
    {
        var daAvvisare = new HashSet<int>();
        T esito;

        using (MySqlConnection c = _db.Open())
        {
            using IDbTransaction tx = c.BeginTransaction();
            try
            {
                esito = scrittura(c, tx, daAvvisare);

                if (!RestaUnAmministratore(c, tx))
                {
                    tx.Rollback();
                    throw new UltimoAmministratoreException(messaggioRifiuto);
                }

                tx.Commit();
            }
            catch (UltimoAmministratoreException)
            {
                throw;
            }
            catch
            {
                try { tx.Rollback(); } catch { /* la connessione può essere già andata */ }
                throw;
            }
        }

        // Fuori dalla transazione e su una connessione nuova: la modifica è definitiva, quindi
        // svuotare la cache e avvisare il client non può più raccontare qualcosa di non vero.
        if (daAvvisare.Count > 0)
        {
            using MySqlConnection c2 = _db.Open();
            foreach (int id in daAvvisare) _changes.Propaga(c2, id);
        }

        return esito;
    }

    /// <summary>
    /// C'è ancora almeno un dipendente in forza che può SCRIVERE <c>nav.permessi</c>?
    /// La risposta la dà il motore vero (<see cref="FeatureAccessService.ConcedeScrittura"/>),
    /// non una copia della regola in SQL: il jolly, la precedenza della riga specifica sul jolly
    /// e il significato di READ devono valere qui esattamente come valgono a ogni richiesta.
    /// </summary>
    private static bool RestaUnAmministratore(IDbConnection c, IDbTransaction tx)
    {
        var candidati = c.Query<(int EmployeeId, string FeatureKey, string Access)>(@"
            SELECT a.employee_id AS EmployeeId, a.feature_key AS FeatureKey, a.access AS Access
            FROM employee_feature_access a
            JOIN employees e ON e.id = a.employee_id
            WHERE e.status = 'ACTIVE' AND COALESCE(e.username,'') <> ''
              AND a.feature_key IN (@Chiave, @Jolly)",
            new { Chiave = ChiavePermessi, Jolly = FeatureAccessService.JollyKey }, tx);

        foreach (var gruppo in candidati.GroupBy(x => x.EmployeeId))
        {
            var grants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in gruppo) grants[r.FeatureKey] = r.Access.ToUpperInvariant();
            if (FeatureAccessService.ConcedeScrittura(grants, ChiavePermessi)) return true;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // SCRITTURA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scrive (o toglie) una riga e ne registra il cambio. Torna <c>true</c> se qualcosa è
    /// cambiato davvero: una riga riscritta con lo stesso valore non deve finire nel registro
    /// né svegliare il client.
    /// </summary>
    private bool ScriviRiga(
        IDbConnection c, IDbTransaction tx, int employeeId, string featureKey,
        string stato, string origin, int? changedBy)
    {
        string? prima = c.ExecuteScalar<string?>(
            "SELECT access FROM employee_feature_access WHERE employee_id=@Id AND feature_key=@K",
            new { Id = employeeId, K = featureKey }, tx);

        bool nega = string.Equals(stato, StatoCombo.No, StringComparison.OrdinalIgnoreCase);

        // «Non abilitato» si scrive in due modi diversi, e la differenza è tutta la Fase D:
        //  · deciso A MANO  → riga di DINIEGO (access='NO'), che sopravvive a «Applica classe»;
        //  · deciso dalla CLASSE → nessuna riga, perché il pacchetto semplicemente non la dà.
        // Se il diniego a mano fosse un'assenza, la prima applicazione di massa lo cancellerebbe
        // e nessuno se ne accorgerebbe (§4.4: il Timesheet che si riaccende agli Acquisti).
        bool dinegoAMano = nega && string.Equals(origin, PermissionChangeService.OriginMano, StringComparison.OrdinalIgnoreCase);
        string? dopo = nega
            ? (dinegoAMano ? FeatureAccessService.AccessNegato : null)
            : stato.ToUpperInvariant();

        if (dopo == null)
        {
            c.Execute("DELETE FROM employee_feature_access WHERE employee_id=@Id AND feature_key=@K",
                new { Id = employeeId, K = featureKey }, tx);
        }
        else
        {
            c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
                        VALUES (@Id, @K, @A, @O)
                        ON DUPLICATE KEY UPDATE access = VALUES(access), origin = VALUES(origin)",
                new { Id = employeeId, K = featureKey, A = dopo, O = origin }, tx);
        }

        if (string.Equals(prima, dopo, StringComparison.OrdinalIgnoreCase)) return false;
        _changes.Registra(c, employeeId, featureKey, prima, dopo, origin, changedBy, tx);
        return true;
    }

    /// <summary>
    /// Cambia una combo: una delle 9 aree (che può comandare più chiavi) oppure una singola
    /// funzione dalle «Funzioni avanzate». Quello che si tocca qui diventa <c>MANO</c> e da quel
    /// momento «Applica classe» non lo sovrascrive più — finché non si preme «Riallinea».
    /// </summary>
    public void Imposta(ImpostaPermessoRequest req, int? changedBy)
    {
        string stato = Normalizza(req.Stato);

        EseguiConGaranzia<object?>((c, tx, daAvvisare) =>
        {
            bool cambiato = false;

            if (!string.IsNullOrWhiteSpace(req.AreaId))
            {
                Area a = Aree.FirstOrDefault(x => x.Id == req.AreaId)
                    ?? throw new KeyNotFoundException($"Area '{req.AreaId}' inesistente");

                foreach (string k in a.Chiavi)
                    cambiato |= ScriviRiga(c, tx, req.EmployeeId, k, stato, PermissionChangeService.OriginMano, changedBy);

                // Le chiavi di sola scrittura (resources.edit) esistono solo a combo piena:
                // in lettura vanno tolte, o il planner resterebbe modificabile.
                foreach (string k in a.ChiaviSoloScrittura)
                {
                    string statoExtra = string.Equals(stato, StatoCombo.Full, StringComparison.OrdinalIgnoreCase)
                        ? StatoCombo.Full
                        : StatoCombo.No;
                    cambiato |= ScriviRiga(c, tx, req.EmployeeId, k, statoExtra, PermissionChangeService.OriginMano, changedBy);
                }
            }
            else if (!string.IsNullOrWhiteSpace(req.FeatureKey))
            {
                cambiato = ScriviRiga(c, tx, req.EmployeeId, req.FeatureKey, stato, PermissionChangeService.OriginMano, changedBy);
            }
            else
            {
                throw new ArgumentException("Serve AreaId oppure FeatureKey");
            }

            if (cambiato) daAvvisare.Add(req.EmployeeId);
            return null;
        }, "Questa modifica toglierebbe l'ultimo amministratore dei permessi: deve restare almeno una persona in forza che può gestirli.");
    }

    private static string Normalizza(string stato)
    {
        string s = (stato ?? "").Trim().ToUpperInvariant();
        return s is StatoCombo.No or StatoCombo.Read or StatoCombo.Full
            ? s
            : throw new ArgumentException("Stato ammesso: NO, READ o FULL");
    }

    /// <summary>
    /// Applica il pacchetto della classe a una o più persone. Con <paramref name="req"/>
    /// <c>.Anteprima</c> non scrive niente e dice solo cosa cambierebbe: è l'elenco che si
    /// conferma prima di premere davvero.
    /// </summary>
    public EsitoApplicaClasseDto ApplicaClasse(ApplicaClasseRequest req, int? changedBy)
    {
        if (req.Anteprima)
        {
            using var c = _db.Open();
            return CalcolaApplicaClasse(c, null, req.EmployeeIds, null, null);
        }

        return EseguiConGaranzia(
            (c, tx, daAvvisare) => CalcolaApplicaClasse(c, tx, req.EmployeeIds, changedBy, daAvvisare),
            "Applicare la classe a queste persone toglierebbe l'ultimo amministratore dei permessi.");
    }

    /// <summary>
    /// Il cuore di «Applica classe»: calcola i cambi e, se <paramref name="tx"/> non è null, li
    /// scrive. Lo stesso codice fa l'anteprima e l'applicazione, perché un'anteprima calcolata
    /// da un'altra strada è un'anteprima che prima o poi mente.
    /// </summary>
    private EsitoApplicaClasseDto CalcolaApplicaClasse(
        IDbConnection c, IDbTransaction? tx, List<int> employeeIds, int? changedBy, ISet<int>? daAvvisare = null)
    {
        var esito = new EsitoApplicaClasseDto();
        var visti = new HashSet<int>();

        foreach (int id in employeeIds)
        {
            if (!visti.Add(id)) continue;
            Persona? p = LeggiPersona(c, id, tx);
            if (p == null) continue;

            esito.Persone++;
            var righe = RighePersona(c, id, tx);
            var pacchetto = PacchettoClasse(c, p.Classe, tx);
            var chiavi = c.Query<(string FeatureKey, string DisplayName)>(
                "SELECT feature_key AS FeatureKey, display_name AS DisplayName FROM auth_features", transaction: tx).ToList();

            bool cambiato = false;

            // Il jolly è una riga come le altre e va trattata come le altre, o la classe Admin
            // non si applicherebbe mai: il pacchetto Admin È il jolly.
            var daConsiderare = chiavi.Select(x => (x.FeatureKey, x.DisplayName)).ToList();
            if (pacchetto.ContainsKey(FeatureAccessService.JollyKey) || righe.ContainsKey(FeatureAccessService.JollyKey))
                daConsiderare.Add((FeatureAccessService.JollyKey, "Tutte le funzioni (jolly)"));

            foreach ((string chiave, string nome) in daConsiderare)
            {
                righe.TryGetValue(chiave, out RigaAccesso? r);

                // ⚠️ LA REGOLA: una riga messa a mano non si tocca. È ciò che permette al gesto
                // di massa di convivere con le eccezioni per persona (Acquisti col Timesheet
                // spento, Contabilità senza commesse) invece di cancellarle in silenzio.
                if (r != null && string.Equals(r.Origin, PermissionChangeService.OriginMano, StringComparison.OrdinalIgnoreCase))
                {
                    esito.RispettateAMano++;
                    continue;
                }

                // Qui si ragiona sulle righe VERE, non sull'accesso effettivo: applicare una
                // classe scrive righe, e il jolly è una di quelle — non un modo di leggerle.
                string ora = StatoRigaEsplicita(righe, chiave);
                string atteso = chiave == FeatureAccessService.JollyKey
                    ? (pacchetto.ContainsKey(FeatureAccessService.JollyKey) ? StatoCombo.Full : StatoCombo.No)
                    : StatoDaPacchettoSenzaJolly(pacchetto, chiave);

                if (string.Equals(ora, atteso, StringComparison.OrdinalIgnoreCase)) continue;

                esito.Combo++;
                esito.Cambi.Add(new CambioPrevistoDto
                {
                    EmployeeId = id,
                    Nome = p.Nome,
                    FeatureKey = chiave,
                    DisplayName = nome,
                    Da = ora,
                    A = atteso,
                });

                if (tx == null) continue;
                cambiato |= ScriviRiga(c, tx, id, chiave, atteso, PermissionChangeService.OriginClasse, changedBy);
            }

            if (tx != null && cambiato) daAvvisare?.Add(id);
        }

        return esito;
    }

    /// <summary>
    /// Come <see cref="StatoDaPacchetto"/> ma <b>senza</b> l'espansione del jolly: applicando la
    /// classe Admin si scrive la riga <c>*</c>, non settanta righe esplicite che direbbero la
    /// stessa cosa rendendo illeggibile la scheda di chi amministra.
    /// </summary>
    private static string StatoDaPacchettoSenzaJolly(Dictionary<string, string> pacchetto, string featureKey)
    {
        if (pacchetto.ContainsKey(FeatureAccessService.JollyKey)) return StatoCombo.No;
        return pacchetto.TryGetValue(featureKey, out string? a)
            ? (string.Equals(a, StatoCombo.Read, StringComparison.OrdinalIgnoreCase) ? StatoCombo.Read : StatoCombo.Full)
            : StatoCombo.No;
    }

    /// <summary>
    /// Copia i permessi di un collega. Tutto quello che arriva è marcato <c>MANO</c>: chi lo fa
    /// sta dicendo «voglio esattamente le sue», non «voglio la sua classe» — e infatti i due
    /// possono avere classi diverse.
    /// </summary>
    public void CopiaDa(CopiaPermessiRequest req, int? changedBy)
    {
        EseguiConGaranzia<object?>((c, tx, daAvvisare) =>
        {
            if (LeggiPersona(c, req.DaEmployeeId, tx) == null || LeggiPersona(c, req.AEmployeeId, tx) == null)
                throw new KeyNotFoundException("Dipendente non trovato");

            var sorgente = RighePersona(c, req.DaEmployeeId, tx);
            var destinazione = RighePersona(c, req.AEmployeeId, tx);
            bool cambiato = false;

            foreach ((string chiave, RigaAccesso r) in sorgente)
                cambiato |= ScriviRiga(c, tx, req.AEmployeeId, chiave, r.Access.ToUpperInvariant(),
                    PermissionChangeService.OriginMano, changedBy);

            // Le righe che il collega NON ha vanno tolte: «copia da» vuol dire «come lui», non
            // «come lui più quello che avevo già» — altrimenti non si può usare per ridurre.
            foreach (string chiave in destinazione.Keys.Where(k => !sorgente.ContainsKey(k)).ToList())
                cambiato |= ScriviRiga(c, tx, req.AEmployeeId, chiave, StatoCombo.No,
                    PermissionChangeService.OriginMano, changedBy);

            if (cambiato) daAvvisare.Add(req.AEmployeeId);
            return null;
        }, "Copiare questi permessi toglierebbe l'ultimo amministratore dei permessi.");
    }

    /// <summary>
    /// Riporta al valore della classe una funzione (o tutte): toglie la decisione a mano e
    /// rimette la riga sotto il pacchetto. È il gesto esplicito che serve perché una combo
    /// toccata a mano resta <c>MANO</c> per sempre — e a un certo punto qualcuno deve poterla
    /// rimettere a posto senza passare dal database.
    /// </summary>
    public EsitoApplicaClasseDto Riallinea(RiallineaPermessoRequest req, int? changedBy)
    {
        return EseguiConGaranzia((c, tx, daAvvisare) =>
        {
            Persona p = LeggiPersona(c, req.EmployeeId, tx)
                ?? throw new KeyNotFoundException("Dipendente non trovato");

            var pacchetto = PacchettoClasse(c, p.Classe, tx);
            var righe = RighePersona(c, req.EmployeeId, tx);
            var esito = new EsitoApplicaClasseDto { Persone = 1 };
            bool cambiato = false;

            var chiavi = string.IsNullOrWhiteSpace(req.FeatureKey)
                ? righe.Keys.Union(pacchetto.Keys, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { req.FeatureKey! };

            foreach (string chiave in chiavi)
            {
                string ora = StatoRigaEsplicita(righe, chiave);
                string atteso = chiave == FeatureAccessService.JollyKey
                    ? (pacchetto.ContainsKey(FeatureAccessService.JollyKey) ? StatoCombo.Full : StatoCombo.No)
                    : StatoDaPacchettoSenzaJolly(pacchetto, chiave);

                if (!string.Equals(ora, atteso, StringComparison.OrdinalIgnoreCase))
                {
                    esito.Combo++;
                    esito.Cambi.Add(new CambioPrevistoDto
                    {
                        EmployeeId = p.Id, Nome = p.Nome, FeatureKey = chiave,
                        DisplayName = chiave, Da = ora, A = atteso,
                    });
                }

                cambiato |= ScriviRiga(c, tx, req.EmployeeId, chiave, atteso,
                    PermissionChangeService.OriginClasse, changedBy);
            }

            if (cambiato) daAvvisare.Add(req.EmployeeId);
            return esito;
        }, "Riallineare questa persona alla sua classe toglierebbe l'ultimo amministratore dei permessi.");
    }

    /// <summary>
    /// Il pacchetto di una classe, per la pagina che lo mostra (e che in Fase D lo modificherà).
    /// </summary>
    public List<AuthRoleFeatureDto> Pacchetto(string classe)
    {
        using var c = _db.Open();
        return c.Query<AuthRoleFeatureDto>(@"
            SELECT class_name AS RoleName, feature_key AS FeatureKey, access AS Access
            FROM auth_class_features WHERE UPPER(class_name)=UPPER(@C) ORDER BY feature_key",
            new { C = classe }).ToList();
    }
}
