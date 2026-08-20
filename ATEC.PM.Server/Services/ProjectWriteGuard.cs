using System.Data;
using System.Security.Claims;
using Dapper;
using ATEC.PM.Shared;

namespace ATEC.PM.Server.Services;

/// <summary>
/// «Su questa commessa si può ancora scrivere?» — segnalazione #88.
///
/// <para>La regola, parola per parola dalla segnalazione: una commessa in <b>Stand-by</b>
/// (<c>ON_HOLD</c>) o <b>Chiusa</b> (<c>COMPLETED</c>) resta «visibile e consultabile ma non
/// modificabile per tutti i colleghi», e «solo PM e Amministratore hanno privilegio di operare
/// come se la commessa fosse attiva». Una <b>Bozza</b> (<c>DRAFT</c>) è «gestibile solo a PM e
/// amministratore», quindi vale lo stesso cancello. <c>CANCELLED</c> è il soft delete
/// dell'eliminazione: lì non scrive più nessuno che non abbia il privilegio.</para>
///
/// <para>Ne esce una regola sola, ed è volutamente la più stretta possibile:
/// <b>si scrive senza privilegi solo su una commessa ATTIVA</b>. Ogni altro stato — compresi
/// quelli che dovessero nascere domani — richiede la chiave. È l'opposto del fallback che
/// lasciava passare tutto quello che non era esplicitamente vietato: uno stato nuovo non deve
/// aprire un varco per distrazione.</para>
///
/// <para><b>«PM e Amministratore» è una chiave, non un ruolo</b> (<see cref="OverrideFeature"/>).
/// Dal 14/08/2026 (v85-v87) la scala di livello non esiste più: cablare <c>ruolo == "PM"</c>
/// funzionerebbe oggi, ma il giorno in cui il privilegio deve andare a un responsabile di reparto
/// servirebbe un deploy. La v96 semina la chiave esattamente su chi ha ruolo PM/ADMIN, così il
/// primo giorno il perimetro è quello chiesto e dopo si sposta dalla pagina «Permessi».</para>
///
/// <para>⚠️ Serve la <b>scrittura</b> sulla chiave (<c>CanWriteUser</c>), non il semplice accesso:
/// una concessione in sola lettura non può sbloccare le scritture altrui — sarebbe una porta
/// aperta con la maniglia dalla parte sbagliata.</para>
/// </summary>
public class ProjectWriteGuard
{
    /// <summary>Chiave che permette di operare su una commessa non attiva (v96, #88).</summary>
    public const string OverrideFeature = "action.project_locked_write";

    /// <summary>Chiave che permette di <b>vedere</b> le commesse in bozza (v96, #88).</summary>
    public const string DraftsFeature = "data.project_drafts";

    private readonly DbService _db;
    private readonly FeatureAccessService _access;

    public ProjectWriteGuard(DbService db, FeatureAccessService access)
    {
        _db = db;
        _access = access;
    }

    /// <summary>
    /// Lo stato lascia scrivere chiunque? Solo <c>ACTIVE</c>: vedi il commento di classe.
    /// Uno stato vuoto o sconosciuto <b>non</b> è attivo — nel dubbio si chiude, non si apre.
    /// </summary>
    public static bool StatoLiberoATutti(string? status) =>
        string.Equals((status ?? "").Trim(), ProjectStatuses.Active, StringComparison.OrdinalIgnoreCase);

    /// <summary>Il chiamante ha il privilegio di operare sulle commesse non attive?</summary>
    public bool PuoScavalcare(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
        _ = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int employeeId);
        return _access.CanWriteUser(employeeId, role, OverrideFeature);
    }

    /// <summary>Il chiamante vede le commesse in bozza?</summary>
    public bool VedeLeBozze(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
        _ = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int employeeId);
        return _access.CanAccessUser(employeeId, role, DraftsFeature);
    }

    /// <summary>
    /// Pezzo di <c>WHERE</c> che nasconde le bozze a chi non ha la chiave (#88): vuoto per chi
    /// le vede, <c>AND p.status &lt;&gt; 'DRAFT'</c> per tutti gli altri. Da appendere a una
    /// query che ha già almeno una condizione.
    /// <para>Il filtro sta <b>nella query</b> e non a valle sui risultati: fatto dopo, ogni
    /// COUNT, paginazione e contatore di sidebar direbbe un numero che comprende righe che poi
    /// non si vedono — un «18 commesse» con 16 righe sotto.</para>
    /// </summary>
    public string FiltroBozzeSql(ClaimsPrincipal? user, string alias = "p") =>
        VedeLeBozze(user) ? "" : $" AND {alias}.status <> '{ProjectStatuses.Draft}'";

    /// <summary>
    /// Motivo del rifiuto, o <c>null</c> se si può scrivere. Il messaggio nomina lo stato e la
    /// chiave che manca: «non hai i permessi» manda a cercare nel posto sbagliato.
    /// <para>Una commessa <b>che non esiste</b> non viene bloccata qui: il 404 lo dà l'azione,
    /// che sa cosa stava cercando. Bloccarla qui trasformerebbe ogni id sbagliato in un
    /// «commessa chiusa», che è un errore che manda fuori strada.</para>
    /// </summary>
    public string? MotivoDelBlocco(IDbConnection c, int projectId, ClaimsPrincipal? user)
    {
        if (projectId <= 0) return null;

        var riga = c.QueryFirstOrDefault<(string? Status, string? Code)>(
            "SELECT status AS Status, code AS Code FROM projects WHERE id = @Id", new { Id = projectId });
        if (riga.Status == null && riga.Code == null) return null;   // non esiste: decide l'azione

        if (StatoLiberoATutti(riga.Status)) return null;
        if (PuoScavalcare(user)) return null;

        string etichetta = EtichettaStato(riga.Status);
        string commessa = string.IsNullOrWhiteSpace(riga.Code) ? "La commessa" : $"La commessa {riga.Code}";
        return $"{commessa} è {etichetta}: si può consultare ma non modificare " +
               "(serve il permesso «Opera su commesse sospese o chiuse»).";
    }

    /// <summary>Come si chiama lo stato a video, per il messaggio di rifiuto.</summary>
    private static string EtichettaStato(string? status) =>
        (status ?? "").Trim().ToUpperInvariant() switch
        {
            ProjectStatuses.Draft => "in bozza",
            ProjectStatuses.OnHold => "in stand-by",
            ProjectStatuses.Completed => "chiusa",
            ProjectStatuses.Cancelled => "eliminata",
            _ => "non attiva",
        };

    /// <summary>
    /// Variante che apre la connessione da sé, per le azioni che chiamano il cancello a mano
    /// (quelle in cui la commessa si raggiunge solo con una JOIN — vedi
    /// <c>RequireProjectWritableAttribute</c>).
    /// </summary>
    public string? MotivoDelBlocco(int projectId, ClaimsPrincipal? user)
    {
        if (projectId <= 0) return null;
        using var c = _db.Open();
        return MotivoDelBlocco(c, projectId, user);
    }

    /// <summary>
    /// Cancello per gli <b>import</b>, che scrivono su più commesse in una transazione sola.
    /// <para>Se anche una sola è bloccata l'import <b>non parte</b>, e il messaggio le elenca
    /// tutte. L'alternativa — saltare le bloccate e importare il resto — restituirebbe un
    /// «importate 40 righe» che non dice quali commesse sono rimaste indietro: un esito
    /// parziale che sembra un successo pieno.</para>
    /// </summary>
    public string? MotivoDelBloccoSuPiuCommesse(
        IDbConnection c, IEnumerable<int> projectIds, ClaimsPrincipal? user)
    {
        if (PuoScavalcare(user)) return null;

        List<int> ids = projectIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return null;

        List<string> bloccate = c.Query<string>(
            @"SELECT CONCAT(COALESCE(NULLIF(code,''), CONCAT('#', id)), ' (', LOWER(status), ')')
              FROM projects
              WHERE id IN @Ids AND status <> @Attiva",
            new { Ids = ids, Attiva = ProjectStatuses.Active }).ToList();

        if (bloccate.Count == 0) return null;

        return "Import annullato: queste commesse non sono attive e non si possono modificare — "
             + string.Join(", ", bloccate)
             + " (serve il permesso «Opera su commesse sospese o chiuse»).";
    }
}
