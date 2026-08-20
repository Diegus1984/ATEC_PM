using System.Reflection;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Authorization;

/// <summary>
/// Cancello delle scritture su una commessa non attiva (segnalazione #88): la regola sta in
/// <see cref="ProjectWriteGuard"/>, qui c'è solo il modo di <b>trovare la commessa</b> a cui la
/// richiesta si riferisce.
///
/// <para>È un <b>action filter</b>, non un authorization filter: deve poter guardare anche nel
/// corpo della richiesta, che a quel punto è già stato deserializzato. Parecchie azioni
/// (check list, MoM, fasi) la commessa non ce l'hanno nella rotta — sta nel DTO.</para>
///
/// <para>Dove cerca il <c>projectId</c>, in quest'ordine:</para>
/// <list type="number">
///   <item><description><see cref="Tabella"/>, se indicata: <c>SELECT project_id FROM tabella
///   WHERE id = {rotta}</c> — è il caso delle azioni che ricevono l'id della <i>riga</i>
///   (riga SAL, milestone, voce di check list) e non quello della commessa;</description></item>
///   <item><description>valore di rotta <c>projectId</c>;</description></item>
///   <item><description>parametro di query <c>projectId</c>;</description></item>
///   <item><description>proprietà <c>ProjectId</c> del corpo;</description></item>
///   <item><description>valore di rotta <c>id</c>, ma <b>solo</b> per i controller che stanno
///   sotto <c>api/projects</c>, dove <c>{id}</c> è la commessa per convenzione.</description></item>
/// </list>
///
/// <para>Se non trova nessun id, <b>lascia passare</b> e scrive un avviso nel log. È la scelta
/// meno peggio: bloccare una scrittura che non si è riusciti a collegare a una commessa
/// spegnerebbe funzioni sane senza che nessuno capisca perché. Il test di copertura
/// (<c>CancelloCommessaTests</c>) impedisce che un'azione resti scoperta per distrazione;
/// l'avviso nel log copre il caso in cui l'attributo c'è ma la commessa non si trova.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireProjectWritableAttribute : TypeFilterAttribute
{
    public RequireProjectWritableAttribute() : base(typeof(Filtro))
    {
        Arguments = new object[] { this };
    }

    /// <summary>
    /// Tabella da cui risalire alla commessa quando la rotta porta l'id di una riga
    /// (es. <c>sal_rows</c>, <c>project_milestones</c>). Deve avere una colonna
    /// <c>project_id</c> e una chiave primaria <c>id</c>.
    /// </summary>
    public string? Tabella { get; set; }

    /// <summary>
    /// Risalita scritta a mano, per quando la commessa sta a due tabelle di distanza (la voce
    /// di un MoM passa dal verbale, la voce di timesheet dalla fase). Deve selezionare
    /// <b>una sola colonna</b> — il project_id — e usare il parametro <c>@Id</c>, che vale il
    /// valore di rotta <see cref="ChiaveRotta"/>. La query è scritta qui nel codice, non arriva
    /// mai da fuori.
    /// </summary>
    public string? Sql { get; set; }

    /// <summary>Nome del valore di rotta che contiene la chiave della riga. Default <c>id</c>.</summary>
    public string ChiaveRotta { get; set; } = "id";

    private sealed class Filtro : IActionFilter
    {
        private readonly RequireProjectWritableAttribute _cfg;
        private readonly ProjectWriteGuard _guard;
        private readonly DbService _db;
        private readonly ILogger<RequireProjectWritableAttribute> _log;

        public Filtro(
            RequireProjectWritableAttribute cfg,
            ProjectWriteGuard guard,
            DbService db,
            ILogger<RequireProjectWritableAttribute> log)
        {
            _cfg = cfg;
            _guard = guard;
            _db = db;
            _log = log;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            // Le letture non passano di qui: la commessa sospesa o chiusa si consulta sempre.
            string metodo = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo))
                return;

            using var c = _db.Open();
            int projectId = TrovaProjectId(context, c);
            if (projectId <= 0)
            {
                _log.LogWarning(
                    "Cancello commessa: nessun projectId trovato per {Metodo} {Rotta} — scrittura lasciata passare.",
                    metodo, context.HttpContext.Request.Path);
                return;
            }

            string? motivo = _guard.MotivoDelBlocco(c, projectId, context.HttpContext.User);
            if (motivo == null) return;

            context.Result = new ObjectResult(ApiResponse<string>.Fail(motivo))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private int TrovaProjectId(ActionExecutingContext context, System.Data.IDbConnection c) =>
            RisolutoreCommessa.TrovaProjectId(context, c, _cfg.Tabella, _cfg.Sql, _cfg.ChiaveRotta);
    }
}

/// <summary>
/// Come si trova la commessa a cui una richiesta si riferisce: logica condivisa fra il cancello
/// delle scritture (<see cref="RequireProjectWritableAttribute"/>) e quello di visibilità delle
/// bozze (<see cref="RequireProjectVisibleAttribute"/>). Due copie divergerebbero: uno dei due
/// cancelli smetterebbe di trovare la commessa e nessuno se ne accorgerebbe.
/// </summary>
internal static class RisolutoreCommessa
{
    internal static int TrovaProjectId(
        ActionExecutingContext context, System.Data.IDbConnection c,
        string? tabella, string? sql, string chiaveRotta)
    {
        // 1. Risalita dalla riga: l'azione conosce la riga, non la commessa.
        if (!string.IsNullOrWhiteSpace(tabella) || !string.IsNullOrWhiteSpace(sql))
        {
            int rowId = Intero(context.RouteData.Values.TryGetValue(chiaveRotta, out object? rv) ? rv : null);
            if (rowId <= 0) return 0;

            // Né il nome della tabella né la query arrivano da fuori: stanno scritti negli
            // attributi, quindi l'interpolazione qui non è una via d'ingresso.
            string q = sql ?? $"SELECT project_id FROM `{tabella}` WHERE id = @Id";
            return c.ExecuteScalar<int?>(q, new { Id = rowId }) ?? 0;
        }

        // 2. Rotta esplicita.
        if (context.RouteData.Values.TryGetValue("projectId", out object? fromRoute))
        {
            int id = Intero(fromRoute);
            if (id > 0) return id;
        }

        // 3. Query string.
        if (context.HttpContext.Request.Query.TryGetValue("projectId", out var fromQuery)
            && int.TryParse(fromQuery.ToString(), out int qs) && qs > 0)
        {
            return qs;
        }

        // 4. Corpo della richiesta: qualunque argomento con una proprietà ProjectId.
        foreach (object? arg in context.ActionArguments.Values)
        {
            if (arg == null || arg is string || arg.GetType().IsPrimitive) continue;
            PropertyInfo? p = arg.GetType().GetProperty("ProjectId");
            if (p == null) continue;
            int id = Intero(p.GetValue(arg));
            if (id > 0) return id;
        }

        // 5. `{id}` = la commessa, ma solo sotto api/projects.
        string path = context.HttpContext.Request.Path.Value ?? "";
        if (path.StartsWith("/api/projects", StringComparison.OrdinalIgnoreCase)
            && context.RouteData.Values.TryGetValue("id", out object? routeId))
        {
            return Intero(routeId);
        }

        return 0;
    }

    private static int Intero(object? value) => value switch
    {
        null => 0,
        int i => i,
        string s => int.TryParse(s, out int p) ? p : 0,
        _ => int.TryParse(value.ToString(), out int p2) ? p2 : 0,
    };
}

/// <summary>
/// Cancello di <b>visibilità delle bozze</b> (#88): una commessa in stato DRAFT «è visibile e
/// gestibile solo a PM e amministratore», cioè a chi ha la chiave
/// <see cref="ProjectWriteGuard.DraftsFeature"/>. A differenza del cancello delle scritture,
/// questo lavora su <b>tutte</b> le richieste, letture comprese: la bozza non si consulta, non
/// esiste proprio.
///
/// <para>La risposta per chi non deve vederla è <b>404, non 403</b>: un «non hai il permesso»
/// direbbe a chi non deve saperlo che a quell'id c'è qualcosa.</para>
///
/// <para>Copre gli endpoint raggiunti <b>per id</b> (dettaglio commessa e sue sezioni: fasi,
/// DDP, documenti, SAL, trasferta, bilancio…). Gli <b>elenchi</b> — dove la bozza comparirebbe
/// in mezzo alle altre — non possono passare da qui: lì il filtro sta dentro la query
/// (<see cref="ProjectWriteGuard.FiltroBozzeSql"/>), altrimenti contatori e paginazione
/// conterebbero righe che poi non si vedono.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireProjectVisibleAttribute : TypeFilterAttribute
{
    public RequireProjectVisibleAttribute() : base(typeof(Filtro))
    {
        Arguments = new object[] { this };
    }

    /// <inheritdoc cref="RequireProjectWritableAttribute.Tabella"/>
    public string? Tabella { get; set; }

    /// <inheritdoc cref="RequireProjectWritableAttribute.Sql"/>
    public string? Sql { get; set; }

    /// <inheritdoc cref="RequireProjectWritableAttribute.ChiaveRotta"/>
    public string ChiaveRotta { get; set; } = "id";

    private sealed class Filtro : IActionFilter
    {
        private readonly RequireProjectVisibleAttribute _cfg;
        private readonly ProjectWriteGuard _guard;
        private readonly DbService _db;

        public Filtro(RequireProjectVisibleAttribute cfg, ProjectWriteGuard guard, DbService db)
        {
            _cfg = cfg;
            _guard = guard;
            _db = db;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            // Chi vede le bozze non ha niente da nascondere: via subito, senza query.
            if (_guard.VedeLeBozze(context.HttpContext.User)) return;

            using var c = _db.Open();
            int projectId = RisolutoreCommessa.TrovaProjectId(context, c, _cfg.Tabella, _cfg.Sql, _cfg.ChiaveRotta);
            // Nessun id trovato = non è una richiesta su una commessa precisa (elenchi, lookup,
            // anagrafiche): qui non c'è niente da nascondere, ci pensano i filtri nelle query.
            if (projectId <= 0) return;

            string? status = c.ExecuteScalar<string?>(
                "SELECT status FROM projects WHERE id = @Id", new { Id = projectId });
            if (!string.Equals((status ?? "").Trim(), ATEC.PM.Shared.ProjectStatuses.Draft,
                    StringComparison.OrdinalIgnoreCase))
                return;

            context.Result = new NotFoundObjectResult(ApiResponse<string>.Fail("Non trovato"));
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}

/// <summary>
/// Dichiara che una scrittura <b>non tocca i dati di una commessa</b> e quindi non passa dal
/// cancello: anagrafiche e configurazioni che vivono dentro un controller di commessa
/// (le condizioni di pagamento del SAL, i template delle fasi, le impostazioni del Bilancio).
/// <para>Serve al test di copertura: senza questa dichiarazione un'azione senza cancello
/// risulterebbe una dimenticanza, ed è proprio quella distinzione che si vuole vedere.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScritturaNonDiCommessaAttribute : Attribute
{
    public ScritturaNonDiCommessaAttribute(string motivo) => Motivo = motivo;

    /// <summary>Perché non è roba di commessa. Si legge nel test quando qualcosa non torna.</summary>
    public string Motivo { get; }
}

/// <summary>
/// Dichiara che l'azione chiama <see cref="ProjectWriteGuard"/> <b>da sé</b>: la commessa si
/// raggiunge solo con una JOIN (la voce di timesheet passa dalla fase) o l'azione ne tocca più
/// d'una (gli import). Il cancello c'è, ma sta dentro il metodo.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScritturaControllataAManoAttribute : Attribute
{
    public ScritturaControllataAManoAttribute(string motivo) => Motivo = motivo;

    public string Motivo { get; }
}
