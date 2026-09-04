using System.Data;
using Microsoft.AspNetCore.SignalR;
using ATEC.PM.Server.Hubs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Quello che deve succedere DOPO che i permessi di qualcuno sono cambiati — registro,
/// contatore di versione, avviso in tempo reale (PIANO-PERMESSI.md §8 e §9).
///
/// <para>Serve perché fin qui un cambio di permessi arrivava all'utente <b>solo al login</b>:
/// l'API stringeva subito, ma il menu restava quello di prima fino al primo F5. Le tre cose
/// stanno insieme in un solo posto perché o si fanno tutte o non se ne fa nessuna: alzare la
/// versione senza avvisare non sveglia nessuno, avvisare senza svuotare la cache della persona
/// fa ricaricare gli stessi permessi di prima, e senza registro dopo il primo incidente non si
/// sa chi ha tolto cosa a chi.</para>
///
/// <para><b>Si registra e si avvisa solo chi cambia davvero.</b> Le scritture di oggi non stanno
/// sulla persona ma sul <i>ruolo</i> o sul <i>catalogo</i> (<c>auth_role_features</c>,
/// <c>auth_features</c>): la stessa modifica può non toccare nessuno (chi aveva già quella
/// funzione per livello) o toccare venti persone. Per saperlo non si indovina —
/// <see cref="ApplicaEPropaga"/> fotografa l'accesso effettivo prima e dopo la scrittura e
/// confronta.</para>
/// </summary>
public class PermissionChangeService
{
    /// <summary>Riga scritta da un pacchetto (ruolo/classe): la riscrive chi ri-applica la classe.</summary>
    public const string OriginClasse = "CLASSE";

    /// <summary>Riga decisa da una persona sulla singola combo: non si sovrascrive mai in automatico.</summary>
    public const string OriginMano = "MANO";

    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ILogger<PermissionChangeService> _log;

    public PermissionChangeService(
        DbService db,
        FeatureAccessService access,
        IHubContext<ProjectHub> hub,
        ILogger<PermissionChangeService> log)
    {
        _db = db;
        _access = access;
        _hub = hub;
        _log = log;
    }

    /// <summary>Dipendente attivo con un'utenza: le persone che entrano davvero.</summary>
    private sealed class PersonaAttiva
    {
        public int Id { get; set; }
        public string Role { get; set; } = "";
    }

    /// <summary>
    /// Cosa vede questa persona su questa funzione, oggi: <c>FULL</c>, <c>READ</c> oppure
    /// <c>null</c> se non la vede. È la stessa risposta che darebbe una richiesta HTTP, presa
    /// dal motore e non ricopiata a mano: qualunque motore sia acceso, il registro dice il vero.
    /// </summary>
    public string? AccessoEffettivo(int employeeId, string? role, string featureKey)
    {
        if (!_access.CanAccessUser(employeeId, role, featureKey)) return null;
        return _access.CanWriteUser(employeeId, role, featureKey)
            ? FeatureAccessService.AccessFull
            : FeatureAccessService.AccessRead;
    }

    /// <summary>
    /// Una riga di storia in <c>employee_feature_access_log</c>. Non scrive nulla se l'accesso
    /// non è cambiato davvero: una storia piena di righe identiche non risponde più alla
    /// domanda per cui esiste.
    /// </summary>
    /// <param name="changedBy">Id di chi ha fatto la modifica (claim <c>NameIdentifier</c>).</param>
    /// <param name="tx">
    /// La transazione in corso, se la modifica ne ha una. <b>Va passata</b>: MySqlConnector
    /// rifiuta un comando che non la dichiara quando la connessione ha una transazione aperta, e
    /// il rifiuto finisce nel <c>catch</c> qui sotto — cioè in una riga di warning e in NESSUNA
    /// riga di registro. È successo davvero: la scheda permessi scriveva i permessi e non
    /// registrava niente, e il difetto si vedeva solo nel log del server.
    /// </param>
    public void Registra(
        IDbConnection c,
        int employeeId,
        string featureKey,
        string? accessoPrima,
        string? accessoDopo,
        string origin,
        int? changedBy,
        IDbTransaction? tx = null)
    {
        if (string.Equals(accessoPrima, accessoDopo, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            c.Execute(@"
                INSERT INTO employee_feature_access_log
                    (employee_id, feature_key, access_before, access_after, origin, changed_by, changed_at)
                VALUES
                    (@EmployeeId, @FeatureKey, @Prima, @Dopo, @Origin, @ChangedBy, NOW())",
                new
                {
                    EmployeeId = employeeId,
                    FeatureKey = featureKey,
                    Prima = accessoPrima,
                    Dopo = accessoDopo,
                    Origin = origin,
                    ChangedBy = changedBy
                }, tx);
        }
        catch (Exception ex)
        {
            // Il registro non deve MAI far fallire la modifica dei permessi (stessa scelta di
            // DdpItemEvents), ma qui è una traccia di sicurezza: se si perde deve almeno
            // restare scritto nel log del server, non sparire in silenzio.
            LogFallimento(ex, "[Permessi] Registro non scritto ({Id} / {Key}): {Msg}",
                employeeId, featureKey, ex.Message);
        }
    }

    /// <summary>
    /// Un registro perso è sempre da segnalare, ma non tutti i motivi hanno lo stesso peso.
    /// <para>
    /// <b>Error</b> se è una <see cref="InvalidOperationException"/>: è il caso della transazione
    /// non dichiarata («The transaction associated with this command is not the connection's active
    /// transaction»), cioè un <b>difetto di programmazione</b>, non un guaio del database. È il
    /// motivo per cui il 14/08/2026 la pagina Permessi ha scritto permessi senza registrarne
    /// nemmeno uno: a livello Warning, in mezzo agli altri, non se n'era accorto nessuno.
    /// </para>
    /// <para><b>Warning</b> per tutto il resto (database irraggiungibile, timeout): capita, e
    /// non c'è una riga di codice da correggere.</para>
    /// </summary>
    private void LogFallimento(Exception ex, string template, params object?[] args)
    {
        if (ex is InvalidOperationException)
            _log.LogError(ex, template, args);
        else
            _log.LogWarning(template, args);
    }

    /// <summary>
    /// Chiude il giro su una persona: alza <c>employees.permissions_version</c>, dimentica la
    /// sua cache e le manda l'avviso sull'hub. Torna la nuova versione.
    /// </summary>
    /// <param name="tx">
    /// La transazione in corso, se chi chiama ne ha una. Vale la stessa regola di
    /// <see cref="Registra"/>: su una connessione con transazione aperta un comando che non la
    /// dichiara viene rifiutato, e qui il rifiuto diventa un warning e un contatore non alzato —
    /// cioè il client non si accorge del cambio di permessi fino al login dopo.
    /// <para>Resta comunque preferibile chiamare <c>Propaga</c> <b>dopo</b> il commit e su una
    /// connessione nuova (come fa <c>PermissionAdminService.EseguiConGaranzia</c>): avvisare di una
    /// modifica ancora annullabile è peggio che avvisare tardi. Il parametro serve a non fallire in
    /// silenzio se qualcuno lo chiamerà dentro una transazione.</para>
    /// </param>
    public int Propaga(IDbConnection c, int employeeId, IDbTransaction? tx = null)
    {
        int versione = 0;
        try
        {
            c.Execute(
                "UPDATE employees SET permissions_version = permissions_version + 1 WHERE id = @Id",
                new { Id = employeeId }, tx);
            versione = c.ExecuteScalar<int>(
                "SELECT permissions_version FROM employees WHERE id = @Id", new { Id = employeeId }, tx);
        }
        catch (Exception ex)
        {
            // Il contatore è un di più: se non si riesce ad alzarlo l'avviso parte lo stesso e
            // il client rilegge i permessi. Fermare qui la modifica sarebbe il danno maggiore.
            LogFallimento(ex, "[Permessi] Versione non aggiornata per {Id}: {Msg}", employeeId, ex.Message);
        }

        // La cache va svuotata PRIMA dell'avviso: il client rilegge subito /features/my e
        // deve trovare i permessi nuovi, non quelli tenuti da parte fino a un minuto.
        _access.DimenticaPersona(employeeId);

        // Fire-and-forget come gli altri avvisi real-time del progetto: un client che non c'è
        // non deve rallentare (né far fallire) la modifica dei permessi.
        _hub.Clients.Group(ProjectHub.PermissionsGroup(employeeId))
            .SendAsync("PermissionsChanged", new { employeeId, version = versione }).SenzaAttesa("PermissionsChanged");

        return versione;
    }

    /// <summary>
    /// Esegue una scrittura sulle regole (lista bianca di un ruolo, catalogo delle funzioni) e
    /// ne porta l'effetto sulle PERSONE: registro, versione e avviso, ma <b>solo per chi cambia
    /// davvero</b>.
    /// </summary>
    /// <param name="featureKey">La funzione toccata: è l'unica su cui l'esito può cambiare.</param>
    /// <param name="soloRuolo">
    /// Nome del ruolo se la modifica riguarda la sua lista bianca (allora le persone da guardare
    /// sono solo quelle con quel ruolo); <c>null</c> se tocca il catalogo, che vale per tutti.
    /// </param>
    /// <param name="scrittura">La modifica vera e propria, eseguita fra le due fotografie.</param>
    public void ApplicaEPropaga(string featureKey, string? soloRuolo, int? changedBy, Action scrittura)
    {
        using var c = _db.Open();
        List<PersonaAttiva> persone = PersoneAttive(c, soloRuolo);

        // Fotografia PRIMA: cosa vede oggi ciascuno su quella funzione.
        Dictionary<int, string?> prima = persone.ToDictionary(
            p => p.Id, p => AccessoEffettivo(p.Id, p.Role, featureKey));

        scrittura();

        // Senza Reload la fotografia DOPO leggerebbe la cache, cioè le regole di prima: il
        // confronto tornerebbe sempre «non è cambiato niente» e non partirebbe nessun avviso.
        _access.Reload();

        foreach (PersonaAttiva p in persone)
        {
            string? dopo = AccessoEffettivo(p.Id, p.Role, featureKey);
            if (string.Equals(prima[p.Id], dopo, StringComparison.OrdinalIgnoreCase)) continue;

            Registra(c, p.Id, featureKey, prima[p.Id], dopo, OriginClasse, changedBy);
            Propaga(c, p.Id);
        }
    }

    /// <summary>
    /// Le persone su cui la modifica può avere effetto. I cessati e chi non ha un'utenza restano
    /// fuori: non hanno un menu da aggiornare né un token da far scadere.
    /// </summary>
    private static List<PersonaAttiva> PersoneAttive(IDbConnection c, string? soloRuolo)
    {
        string filtroRuolo = string.IsNullOrWhiteSpace(soloRuolo)
            ? ""
            : "AND UPPER(COALESCE(user_role,'')) = UPPER(@Role) ";

        return c.Query<PersonaAttiva>($@"
            SELECT id AS Id, COALESCE(user_role,'') AS Role
            FROM employees
            WHERE status = 'ACTIVE' AND username IS NOT NULL AND username <> ''
              {filtroRuolo}
            ORDER BY id", new { Role = soloRuolo ?? "" }).ToList();
    }
}
