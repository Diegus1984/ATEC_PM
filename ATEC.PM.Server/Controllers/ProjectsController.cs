using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura di questo controller riguarda UNA commessa (l'id sta nella rotta),
// quindi il cancello si mette una volta sola sulla classe: una commessa in bozza, in
// stand-by o chiusa si consulta ma non si modifica, salvo il permesso di scavalco.
[RequireProjectWritable]
// #88: e una bozza non si VEDE proprio, letture comprese — 404 come se non esistesse.
[RequireProjectVisible]
public class ProjectsController : ControllerBase
{
    /// <summary>
    /// Stati che rendono una commessa "chiusa": non è più lavoro in corso e per default
    /// sparisce dalle liste (l'eliminazione commessa è un soft delete → CANCELLED).
    /// </summary>
    private const string ClosedStatusesSql = ProjectSorting.ClosedStatusesSql;

    /// <summary>
    /// Ordinamento standard degli elenchi commesse (chiuse in fondo). La regola sta in
    /// <see cref="ProjectSorting"/>: la usano anche Bilancio, Dashboard, SAL, Check list,
    /// MoM, Milestones, Trasferta e Risorse. Vedi lì il perché.
    /// </summary>
    private static readonly string ProjectOrderBySql = ProjectSorting.OrderBy("p", "p.status");

    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly ProjectTemplateCopyService _templateCopy;
    private readonly ILogger<ProjectsController> _logger;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly AnagraficheCache _cache;
    private readonly ProjectWriteGuard _guard;
    private readonly FeatureAccessService _access;
    public ProjectsController(
        DbService db,
        NotificationService notif,
        ProjectTemplateCopyService templateCopy,
        ILogger<ProjectsController> logger,
        IHubContext<ProjectHub> hub,
        AnagraficheCache cache,
        ProjectWriteGuard guard,
        FeatureAccessService access)
    {
        _db = db;
        _notif = notif;
        _templateCopy = templateCopy;
        _logger = logger;
        _hub = hub;
        _cache = cache;
        _guard = guard;
        _access = access;
    }

    // Notifica real-time: chi guarda QUESTA commessa (gruppo "project-{id}") + il Gestore DDP (gruppo globale
    // "ddp-all"), escludendo chi ha fatto la modifica (conn) per non auto-ricaricarsi.
    // ddpType ("COMMERCIAL" | "OFFICINA") permette ai client di ignorare la distinta che non li riguarda.
    private void NotifyDdpChange(int projectId, string? conn, string action, int itemId, string ddpType = "COMMERCIAL")
    {
        var payload = new DdpChange { ProjectId = projectId, Action = action, ItemId = itemId, DdpType = ddpType };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
        {
            IClientProxy target = string.IsNullOrEmpty(conn)
                ? _hub.Clients.Group(group)
                : _hub.Clients.GroupExcept(group, conn);
            _ = target.SendAsync("DdpChanged", payload);
        }
    }

    // Il sync DDP Officina → lavorazioni tocca project_work_requests: avvisa il Pannello
    // Lavorazioni (gruppo globale) e la griglia nel dettaglio commessa (stesso contratto
    // del WorkRequestsController).
    private void NotifyWorkRequestsChanged(string action, int projectId)
    {
        _ = _hub.Clients.Group(ProjectHub.WorkRequestsGroup)
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId });
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId });
    }

    // Notifica real-time ai client che guardano la commessa che i documenti (file/cartelle su disco)
    // sono cambiati: le viste aperte (albero a sinistra + elenco a destra) ricaricano in ~1s.
    // Scope per-commessa ("project-{id}"). Niente self-exclusion: l'autore ricarica già localmente,
    // un eventuale secondo refresh è innocuo (nessun editing live da interrompere).
    private void NotifyDocumentsChanged(int projectId, string action)
    {
        var payload = new DocumentsChange { ProjectId = projectId, Action = action };
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("DocumentsChanged", payload);
    }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Chi ha <c>action.ddp_status_override</c> non è vincolato alla matrice degli avanzamenti
    /// (segnalazione #140): amministratori e PM devono poter riportare indietro una riga che un
    /// collega ha mandato nello stato sbagliato, altrimenti l'errore è definitivo. Il client
    /// mostra la finestra completa alle stesse persone, leggendo la stessa chiave da
    /// <c>/features/my</c>; qui il controllo si rifà comunque, perché la finestra ristretta del
    /// menu è un aiuto e non un cancello.
    /// </summary>
    private bool PuoScavalcareMatriceDdp() =>
        _access.CanAccessUser(
            GetCurrentEmployeeId(),
            User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
            DdpTransitionService.FeatureScavalcaMatrice);

    /// <summary>
    /// I parametri di una UPDATE più la firma di chi sta scrivendo (<c>@UpdatedBy</c>, #114).
    /// L'oggetto della richiesta non ha un campo per l'autore — e non deve averlo: lo decide il
    /// token, non il client. Token senza dipendente → NULL, cioè modifica senza firma.
    /// </summary>
    private DynamicParameters ConFirma(object req)
    {
        var parametri = new DynamicParameters(req);
        int me = GetCurrentEmployeeId();
        parametri.Add("UpdatedBy", me > 0 ? me : (int?)null);
        return parametri;
    }

    /// <summary>
    /// Il filtro dell'elenco commesse, costruito UNA volta sola per i due endpoint che lo usano
    /// (<see cref="GetAll"/> con i soldi, <see cref="GetLookup"/> senza). Due liste con le
    /// stesse regole scritte in due punti divergono al primo che si dimentica — e qui in mezzo
    /// c'è il filtro delle bozze, cioè roba che deve restare invisibile a chi non ha la chiave.
    /// </summary>
    private sealed record FiltroElenco(
        string Where, string OrderBy, DynamicParameters CountParams, DynamicParameters ListParams,
        int Page, int PageSize);

    private FiltroElenco CostruisciFiltroElenco(
        int page, int pageSize, string? search, bool includeClosed, int includeId)
    {
        (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);

        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var conditions = new List<string>();
        if (term != null)
        {
            conditions.Add(@"(p.code LIKE @Term OR p.title LIKE @Term OR cu.company_name LIKE @Term
                OR CONCAT(e.first_name,' ',e.last_name) LIKE @Term)");
        }
        if (!includeClosed)
        {
            conditions.Add(includeId > 0
                ? $"(p.status NOT IN {ClosedStatusesSql} OR p.id = @IncludeId)"
                : $"p.status NOT IN {ClosedStatusesSql}");
        }
        // #88: le bozze si vedono solo con la chiave. Il filtro vince anche su includeClosed
        // e includeId: quelle due porte servono al deep-link sulle commesse chiuse, non a
        // far comparire una bozza a chi non deve saperne l'esistenza.
        string filtroBozze = _guard.FiltroBozzeSql(User);
        if (filtroBozze.Length > 0)
            conditions.Add(filtroBozze[" AND ".Length..]);
        string whereClause = conditions.Count == 0
            ? ""
            : " WHERE " + string.Join(" AND ", conditions);

        var countParams = new DynamicParameters();
        var listParams = new DynamicParameters();
        listParams.Add("Limit", pageSize);
        listParams.Add("Offset", offset);
        if (term != null)
        {
            countParams.Add("Term", $"%{term}%");
            listParams.Add("Term", $"%{term}%");
        }
        if (!includeClosed && includeId > 0)
        {
            countParams.Add("IncludeId", includeId);
            listParams.Add("IncludeId", includeId);
        }

        // Le chiuse (quando richieste) finiscono in fondo: le aperte restano a portata di clic.
        // Dentro ai gruppi comanda la DATA letta dal codice (vedi `CodeDateSql`), non
        // l'ordine alfabetico: i due formati di codice in uso non si ordinano fra loro.
        // La riga iniettata dal deep-link (`includeId`) va IN TESTA solo se è CHIUSA: ordinata
        // da chiusa finirebbe nelle ultime pagine dello scroll infinito e l'albero resterebbe
        // senza il nodo di ciò che si vede a destra — proprio il caso che includeId copre.
        // Una commessa APERTA no: è già nell'elenco al suo posto cronologico (vedi
        // `ProjectSorting.DeepLinkChiusaInTesta`).
        string orderBy = !includeClosed && includeId > 0
            ? $"{ProjectSorting.DeepLinkChiusaInTesta()}, {ProjectOrderBySql}"
            : ProjectOrderBySql;

        return new FiltroElenco(whereClause, orderBy, countParams, listParams, page, pageSize);
    }

    private const string ElencoFromSql = @"
        FROM projects p
        LEFT JOIN customers cu ON cu.id = p.customer_id
        LEFT JOIN employees e ON e.id = p.pm_id";

    /// <summary>
    /// Elenco commesse paginato <b>per la pagina Commesse</b>: dentro ci sono i soldi
    /// (<c>revenue</c>) e le ore a budget, quindi sta dietro <c>nav.commesse</c>.
    /// <para>
    /// Di default ritorna solo le commesse <b>aperte</b> (DRAFT · ACTIVE · ON_HOLD): le
    /// COMPLETED/CANCELLED si accumulano negli anni e sommergerebbero le liste (l'eliminazione
    /// è un soft delete → CANCELLED, quindi senza filtro resterebbero visibili anche quelle
    /// "eliminate"). Con <paramref name="includeClosed"/>=true torna tutto.
    /// <paramref name="includeId"/> forza l'inclusione di una singola commessa anche se chiusa
    /// (serve al deep-link: la commessa aperta a destra deve esistere nell'albero a sinistra).
    /// </para>
    /// <para>Chi gli serve solo il nome della commessa per una tendina usa
    /// <see cref="GetLookup"/>: è la ragione per cui questa può essere chiusa.</para>
    /// </summary>
    [HttpGet]
    [RequireFeature("nav.commesse")]
    public IActionResult GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null, [FromQuery] bool includeClosed = false,
        [FromQuery] int includeId = 0)
    {
        try
        {
            FiltroElenco f = CostruisciFiltroElenco(page, pageSize, search, includeClosed, includeId);

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*){ElencoFromSql}{f.Where}", f.CountParams);

            var rows = c.Query<ProjectListItem>($@"
            SELECT p.id, p.code, p.title,
                   COALESCE(cu.company_name, 'CLIENTE MANCANTE') AS CustomerName,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), 'NON ASSEGNATO') AS PmName,
                   p.status, p.priority, p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.revenue, p.budget_hours_total AS BudgetHoursTotal,
                   COALESCE((SELECT q.id FROM quotes q WHERE q.project_id = p.id LIMIT 1), 0) AS LinkedQuoteId
            {ElencoFromSql}{f.Where}
            ORDER BY {f.OrderBy}
            LIMIT @Limit OFFSET @Offset", f.ListParams).ToList();

            return Ok(ApiResponse<PagedResult<ProjectListItem>>.Ok(
                Impagina(rows, total, f.Page, f.PageSize)));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<ProjectListItem>>.Fail($"Errore DB: {ex.Message}"));
        }
    }

    /// <summary>
    /// Le commesse per una <b>tendina</b>: id, codice, titolo, cliente, PM, stato. Nient'altro —
    /// niente importi, niente ore a budget, niente date.
    ///
    /// <para>Aperta a tutti gli autenticati, e per un motivo dichiarato: la commessa si sceglie
    /// da mezzo gestionale (SAL, verbali, chat, milestone, lavorazioni, dashboard), quindi
    /// chiuderla dietro <c>nav.commesse</c> spegnerebbe quelle pagine a chi la commessa la deve
    /// solo nominare. Filtri identici a <see cref="GetAll"/> (bozze comprese) perché il filtro
    /// è lo stesso codice.</para>
    ///
    /// <para>🪤 Prima esisteva solo <see cref="GetAll"/> e la usavano anche le tendine: il
    /// <c>revenue</c> di ogni commessa arrivava così a chiunque fosse autenticato, comprese le
    /// tre persone a cui la voce Commesse è negata apposta. Nessuna pagina lo mostrava — ma
    /// stava nel JSON, e «non lo mostriamo» non è un permesso.</para>
    /// </summary>
    [HttpGet("lookup")]
    public IActionResult GetLookup([FromQuery] int page = 1, [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null, [FromQuery] bool includeClosed = false,
        [FromQuery] int includeId = 0)
    {
        try
        {
            FiltroElenco f = CostruisciFiltroElenco(page, pageSize, search, includeClosed, includeId);

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*){ElencoFromSql}{f.Where}", f.CountParams);

            var rows = c.Query<ProjectLookupItem>($@"
            SELECT p.id, p.code, p.title,
                   COALESCE(cu.company_name, 'CLIENTE MANCANTE') AS CustomerName,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name), 'NON ASSEGNATO') AS PmName,
                   p.status
            {ElencoFromSql}{f.Where}
            ORDER BY {f.OrderBy}
            LIMIT @Limit OFFSET @Offset", f.ListParams).ToList();

            return Ok(ApiResponse<PagedResult<ProjectLookupItem>>.Ok(
                Impagina(rows, total, f.Page, f.PageSize)));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<ProjectLookupItem>>.Fail($"Errore DB: {ex.Message}"));
        }
    }

    private static PagedResult<T> Impagina<T>(List<T> righe, int total, int page, int pageSize) =>
        new()
        {
            Items = righe,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            HasMore = (page - 1) * pageSize + righe.Count < total,
        };

    /// <summary>L'albero delle commesse della pagina Commesse (stessa casa di GetAll).</summary>
    [HttpGet("tree")]
    [RequireFeature("nav.commesse")]
    public IActionResult GetTree()
    {
        try
        {
            using var c = _db.Open();
            List<ProjectTreeItemDto> rows = c.Query<ProjectTreeItemDto>($@"
                SELECT p.id, p.code, p.title,
                       p.status,
                       COALESCE(cu.company_name, '') AS CustomerName
                FROM projects p
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE p.status <> 'CANCELLED'{_guard.FiltroBozzeSql(User)}
                ORDER BY {ProjectSorting.OrderBy("p")}").ToList();
            return Ok(ApiResponse<List<ProjectTreeItemDto>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<ProjectTreeItemDto>>.Fail($"Errore DB: {ex.Message}"));
        }
    }

    /// <summary>Una commessa intera, coi suoi importi: la scheda della pagina Commesse.</summary>
    [HttpGet("{id}")]
    [RequireFeature("nav.commesse")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<ProjectSaveRequest>(@"
            SELECT id, code, title, customer_id AS CustomerId, pm_id AS PmId, description,
                   start_date AS StartDate, end_date_planned AS EndDatePlanned,
                   budget_total AS BudgetTotal, budget_hours_total AS BudgetHoursTotal,
                   revenue, status, priority, server_path AS ServerPath, notes,
                   COALESCE((SELECT q.id FROM quotes q WHERE q.project_id = p.id LIMIT 1), 0) AS LinkedQuoteId
            FROM projects p WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        // #88: la bozza nascosta a chi non ha la chiave è già gestita da [RequireProjectVisible]
        // sulla classe (404 identico alla commessa inesistente).
        return Ok(ApiResponse<ProjectSaveRequest>.Ok(proj));
    }

    [HttpPost]
    public IActionResult Create([FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();

        // Il codice arriva precompilato dal client (progressivo della giornata): se nel frattempo
        // qualcun altro ha creato la stessa commessa, meglio un errore chiaro che due codici uguali
        // (projects.code non ha un indice UNIQUE).
        bool codeTaken = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM projects WHERE code = @Code", new { req.Code }) > 0;
        if (codeTaken)
        {
            return Ok(ApiResponse<int>.Fail(
                $"Il codice {req.Code} è già usato da un'altra commessa: riapri il dialogo per avere il numero successivo."));
        }

        using var trx = c.BeginTransaction();
        int newId;
        try
        {
            // `server_path` NON si prende dal body: lo scrive il server subito dopo il commit
            // (riga più sotto), da BasePath + anno + codice. Prima entrava da qui e veniva
            // sovrascritto — ma solo se `CopyToProject` non lanciava: al primo errore il catch
            // ingoiava l'eccezione e restava dentro il percorso scelto dal chiamante, che è la
            // radice del problema descritto nella #63 (la cartella documenti è l'unica barriera
            // che protegge i file, e non deve poterla scegliere il client).
            newId = c.ExecuteScalar<int>(@"
        INSERT INTO projects (code,title,customer_id,pm_id,description,start_date,end_date_planned,budget_total,budget_hours_total,revenue,status,priority,server_path,notes)
        VALUES (@Code,@Title,@CustomerId,@PmId,@Description,@StartDate,@EndDatePlanned,@BudgetTotal,@BudgetHoursTotal,@Revenue,@Status,@Priority,'',@Notes);
        SELECT LAST_INSERT_ID()", req, trx);

            // Crea fasi di default
            // v73 — le fasi nascono da COPPIE (fase, sezione): «Call Cliente» agganciata sia a
            // Program Manager sia a Progettazione entra DUE volte, una per sezione, così le ore
            // restano separate nel Bilancio. La sezione si scrive sulla riga: prima si deduceva
            // dal template, e con una fase su più sezioni quella deduzione non esiste più.
            // Solo sezioni ATTIVE: sono le stesse che ProjectCostingController porta dentro la
            // commessa, e una fase su una sezione spenta resterebbe appesa a una sezione assente.
            if (req.CreateDefaultPhases)
            {
                var defaults = c.Query(@"
                    SELECT pt.id AS template_id, pt.department_id, pts.cost_section_template_id AS section_id
                    FROM phase_template_sections pts
                    JOIN phase_templates pt ON pt.id = pts.phase_template_id
                    JOIN cost_section_templates cst ON cst.id = pts.cost_section_template_id
                    LEFT JOIN cost_section_groups g ON g.id = cst.group_id
                    WHERE pts.is_default = 1 AND cst.is_active = 1
                    ORDER BY g.sort_order, cst.sort_order, pts.sort_order", transaction: trx);

                // Fasi «trasversali»: predefinite ma senza nessuna sezione. Restano com'erano —
                // toglierle è una decisione di anagrafica, non tecnica (vedi PIANO-FASI-MULTISEZIONE.md §7).
                var crossPhases = c.Query(@"
                    SELECT pt.id AS template_id, pt.department_id, NULL AS section_id
                    FROM phase_templates pt
                    WHERE pt.is_default = 1
                      AND NOT EXISTS (SELECT 1 FROM phase_template_sections s WHERE s.phase_template_id = pt.id)
                    ORDER BY pt.sort_order", transaction: trx);

                int sort = 0;
                foreach (var t in defaults.Concat(crossPhases))
                {
                    sort++;
                    c.Execute(@"INSERT INTO project_phases (project_id, phase_template_id, cost_section_template_id, department_id, sort_order)
                    VALUES (@ProjId, @TplId, @SecId, @DeptId, @Sort)",
                        new
                        {
                            ProjId = newId,
                            TplId = (int)t.template_id,
                            SecId = (int?)t.section_id,
                            DeptId = (int?)t.department_id,
                            Sort = sort
                        }, trx);
                }
            }

            trx.Commit();
        }
        catch
        {
            trx.Rollback();
            throw;
        }

        // Dopo il commit — operazioni non transazionali
        try
        {
            _templateCopy.CopyToProject(req.Code);

            string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
            string year = DateTime.Now.Year.ToString();
            string fullPath = Path.Combine(basePath, year, req.Code);
            c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = newId });
        }
        catch (Exception ex)
        {
            // Log ma non fallire — la commessa è già creata nel DB
            Console.WriteLine($"[Projects] Warning: errore post-creazione commessa {req.Code}: {ex.Message}");
        }

        NotifyProjectsChanged("create", newId, req.Code);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    /// <summary>
    /// La cartella documenti richiesta sta sotto la cartella base configurata (<c>BasePath</c>)?
    ///
    /// Serve perché <c>server_path</c> è l'<b>unica</b> barriera che protegge i documenti: tutte
    /// le action file si limitano a controllare che il file richiesto stia dentro quel percorso,
    /// quindi chi può riscriverlo sposta la barriera dove vuole. Provato il 13/08/2026 con
    /// un'utenza da tecnico: percorso ripuntato sulla cartella del server → la sezione Documenti
    /// elencava 98 file e <c>appsettings.Secrets.json</c> si scaricava (segnalazione #63).
    ///
    /// La radice stessa non è ammessa: punterebbe la commessa sull'elenco di TUTTE le commesse.
    /// </summary>
    private bool IsUnderBasePath(string candidate)
    {
        try
        {
            string root = Path.GetFullPath(_db.GetConfig("BasePath", @"C:\ATEC_Commesse"));
            string relative = Path.GetRelativePath(root, Path.GetFullPath(candidate));
            return relative != "."
                && !relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }
        catch
        {
            return false; // percorso malformato (caratteri non validi, UNC irrisolvibile…)
        }
    }

    // ⚠️ `action.edit_project` («Modifica Commessa», livello 2) esisteva in auth_features dal
    // principio ma non era usata da NESSUNA parte, né qui né nel client. Applicarla qui non
    // toglie niente a nessuno: il pulsante «Modifica» che apre questo salvataggio sta dentro la
    // Dashboard Commessa (`project.dettagli`, livello 2), quindi dall'interfaccia la modifica era
    // già dei soli PM/ADMIN — era l'API a essere aperta a chiunque fosse autenticato.
    [RequireFeature("action.edit_project")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;

        string percorsoAttuale = c.ExecuteScalar<string?>(
            "SELECT server_path FROM projects WHERE id=@Id", new { Id = id }) ?? "";

        // Il percorso si può cambiare solo restando dentro la cartella base. Un percorso
        // INVARIATO passa sempre: il client rimanda il valore che ha letto, e una commessa
        // storica finita fuori base non deve diventare impossibile da salvare.
        if (!string.IsNullOrWhiteSpace(req.ServerPath)
            && !string.Equals(req.ServerPath.Trim(), percorsoAttuale.Trim(), StringComparison.OrdinalIgnoreCase)
            && !IsUnderBasePath(req.ServerPath))
        {
            string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
            return Ok(ApiResponse<int>.Fail(
                $"La cartella documenti dev'essere dentro «{basePath}»: il percorso indicato è fuori e non è stato salvato."));
        }

        // Stesso controllo della POST: `projects.code` non ha un indice UNIQUE, quindi senza
        // questa guardia una rinomina può creare due commesse con lo stesso codice (e il codice
        // è la chiave con cui si agganciano DDP, import e cartelle sul server).
        bool codeTaken = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM projects WHERE code = @Code AND id <> @Id",
            new { req.Code, Id = id }) > 0;
        if (codeTaken)
        {
            return Ok(ApiResponse<int>.Fail(
                $"Il codice {req.Code} è già usato da un'altra commessa: scegline un altro."));
        }

        // Leggi stato precedente per confronto
        string oldStatus = c.ExecuteScalar<string?>(
            "SELECT status FROM projects WHERE id=@Id", new { Id = id }) ?? "";

        c.Execute(@"UPDATE projects SET code=@Code,title=@Title,customer_id=@CustomerId,pm_id=@PmId,
            description=@Description,start_date=@StartDate,end_date_planned=@EndDatePlanned,
            budget_total=@BudgetTotal,budget_hours_total=@BudgetHoursTotal,revenue=@Revenue,
            status=@Status,priority=@Priority,server_path=@ServerPath,notes=@Notes WHERE id=@Id", req);

        // L'ordine può essere scomposto in posizioni (project_order_lines) e `revenue` ne è la
        // somma: senza questa riconciliazione il campo «Ricavo» della scheda commessa e il
        // Totale Ordine del Bilancio direbbero due numeri diversi.
        //  - una sola posizione → il valore digitato qui la aggiorna;
        //  - più posizioni → l'ordine si modifica solo dalla tabella, quindi `revenue` torna
        //    alla somma delle righe e il campo della scheda viene ignorato.
        ReconcileOrderLinesWithRevenue(c, id, req.Revenue);

        // Notifica a tutti i dipendenti se la commessa cambia stato operativo
        if (oldStatus != req.Status && req.Status is "ACTIVE" or "ON_HOLD" or "CANCELLED")
        {
            NotifyProjectStatusChange(id, req.Code, req.Status);
        }

        NotifyProjectsChanged("update", id, req.Code);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    /// <summary>
    /// Cambio di <b>solo stato</b> dalla colonna «Stato» della Dashboard (#88). La PUT completa
    /// riscrive tutta la commessa e chiede <c>action.edit_project</c>: per girare una tendina
    /// serviva un endpoint che tocchi una colonna sola.
    /// <para><b>Chi può</b>: la chiave della #88 («Opera su commesse sospese o chiuse»), perché
    /// ogni transizione reale ha uno stato bloccato da almeno un lato — mettere in stand-by,
    /// chiudere, riaprire, pubblicare una bozza sono tutti gesti da PM/amministratore.</para>
    /// <para><b>CANCELLED è rifiutato</b>: è il soft delete dell'eliminazione, che ha il suo
    /// percorso (DELETE) con la conferma davanti. Da una tendina di stato non si cancella.</para>
    /// </summary>
    [RequireFeature(ProjectWriteGuard.OverrideFeature)]
    [HttpPatch("{id}/status")]
    public IActionResult UpdateStatus(int id, [FromBody] string status)
    {
        string nuovo = (status ?? "").Trim().ToUpperInvariant();
        string[] ammessi =
        {
            ATEC.PM.Shared.ProjectStatuses.Draft, ATEC.PM.Shared.ProjectStatuses.Active,
            ATEC.PM.Shared.ProjectStatuses.OnHold, ATEC.PM.Shared.ProjectStatuses.Completed,
        };
        if (!ammessi.Contains(nuovo))
            return Ok(ApiResponse<int>.Fail($"Stato non valido: {status}"));

        using var c = _db.Open();
        var riga = c.QueryFirstOrDefault<(string Code, string Status)>(
            "SELECT code AS Code, status AS Status FROM projects WHERE id = @Id", new { Id = id });
        if (riga.Code == null) return NotFound(ApiResponse<int>.Fail("Non trovato"));
        if (string.Equals(riga.Status, nuovo, StringComparison.OrdinalIgnoreCase))
            return Ok(ApiResponse<int>.Ok(id, "Stato invariato"));

        c.Execute("UPDATE projects SET status = @Status WHERE id = @Id",
            new { Status = nuovo, Id = id });

        // Stessa campanella della PUT: il passaggio operativo (attiva/sospesa) interessa tutti.
        if (nuovo is "ACTIVE" or "ON_HOLD")
            NotifyProjectStatusChange(id, riga.Code, nuovo);

        NotifyProjectsChanged("update", id, riga.Code);
        return Ok(ApiResponse<int>.Ok(id, "Stato aggiornato"));
    }

    /// <summary>
    /// Promozione di un'<b>Altra Attività</b> a commessa (#88, rivista con la #89): il client
    /// apre il dialog di configurazione precompilato dall'attività e arriva qui con
    /// l'anagrafica completa rivista dall'utente. Il codice nuovo lo genera comunque il
    /// server (<see cref="ProjectCodeGenerator"/>, progressivo della giornata): scriverlo a
    /// mano riaprirebbe la porta ai codici doppi che il generatore esiste per evitare.
    /// <para>Il codice vecchio non si perde: la nota storica la scrive il server in testa alle
    /// note inviate dal dialog (lasciata al client si perderebbe alla prima modifica del campo).
    /// È l'unico posto che sopravvive alla rinomina — DDP, SAL e documenti si agganciano per
    /// id, non per codice.</para>
    /// <para><c>server_path</c> resta quello dell'attività (il dialog lo mostra disabilitato):
    /// la barriera dei documenti si sposta solo dalla PUT, che ha la validazione
    /// <see cref="IsUnderBasePath"/> davanti.</para>
    /// <para>Stessa chiave della #88: la segnalazione mette questo gesto nello stesso elenco di
    /// privilegi «solo PM e Amministratore» del cancello.</para>
    /// </summary>
    [RequireFeature(ProjectWriteGuard.OverrideFeature)]
    [HttpPost("{id}/promote-to-commessa")]
    public IActionResult PromoteToCommessa(int id, [FromBody] ProjectSaveRequest req)
    {
        string nuovoStato = (req.Status ?? "").Trim().ToUpperInvariant();
        string[] ammessi =
        {
            ATEC.PM.Shared.ProjectStatuses.Draft, ATEC.PM.Shared.ProjectStatuses.Active,
            ATEC.PM.Shared.ProjectStatuses.OnHold, ATEC.PM.Shared.ProjectStatuses.Completed,
        };
        if (!ammessi.Contains(nuovoStato))
            return Ok(ApiResponse<string>.Fail($"Stato non valido: {req.Status}"));
        if (string.IsNullOrWhiteSpace(req.Title))
            return Ok(ApiResponse<string>.Fail("Il titolo è obbligatorio."));

        using var c = _db.Open();
        var riga = c.QueryFirstOrDefault<(string Code, string Status)>(
            "SELECT code AS Code, status AS Status FROM projects WHERE id = @Id",
            new { Id = id });
        if (riga.Code == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

        if (ProjectSorting.HasCommessaCode(riga.Code))
            return Ok(ApiResponse<string>.Fail($"{riga.Code} è già un codice commessa: non c'è niente da promuovere."));

        using var tx = c.BeginTransaction();
        string nuovoCodice = ProjectCodeGenerator.Next(c, tx);
        string nota = $"Promossa a commessa il {DateTime.Now:dd/MM/yyyy}: prima si chiamava «{riga.Code}».";
        c.Execute(@"UPDATE projects SET code = @Code, title = @Title, customer_id = @CustomerId,
                        pm_id = @PmId, description = @Description, start_date = @StartDate,
                        end_date_planned = @EndDatePlanned, budget_total = @BudgetTotal,
                        budget_hours_total = @BudgetHoursTotal, revenue = @Revenue,
                        status = @Status, priority = @Priority,
                        notes = TRIM(CONCAT(@Nota, '\n', COALESCE(@Notes,'')))
                    WHERE id = @Id",
            new
            {
                Code = nuovoCodice, req.Title, req.CustomerId, req.PmId, req.Description,
                req.StartDate, req.EndDatePlanned, req.BudgetTotal, req.BudgetHoursTotal,
                req.Revenue, Status = nuovoStato, req.Priority, Nota = nota, req.Notes, Id = id,
            }, tx);
        tx.Commit();

        // Stessa riconciliazione della PUT: l'attività può avere già righe d'ordine dal Bilancio.
        ReconcileOrderLinesWithRevenue(c, id, req.Revenue);

        // Stessa campanella della PUT: il passaggio operativo (attiva/sospesa) interessa tutti.
        if (!string.Equals(riga.Status, nuovoStato, StringComparison.OrdinalIgnoreCase)
            && nuovoStato is "ACTIVE" or "ON_HOLD")
            NotifyProjectStatusChange(id, nuovoCodice, nuovoStato);

        NotifyProjectsChanged("update", id, nuovoCodice);
        return Ok(ApiResponse<string>.Ok(nuovoCodice, $"Ora è la commessa {nuovoCodice}"));
    }

    /// <summary>
    /// Order price della commessa. Dal 04/08/2026 l'ordine può essere scomposto in posizioni
    /// (project_order_lines) e <c>revenue</c> ne è la somma: per non far divergere i due numeri
    /// questo endpoint scrive ANCHE sulla riga d'ordine quando ce n'è una sola, e si rifiuta di
    /// scrivere quando le posizioni sono più di una (lì l'ordine si modifica dalla tabella).
    /// </summary>
    [HttpPatch("{id}/revenue")]
    public IActionResult UpdateRevenue(int id, [FromBody] decimal value)
    {
        using var c = _db.Open();

        int lineCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_order_lines WHERE project_id=@Id", new { Id = id });
        if (lineCount > 1)
            return Ok(ApiResponse<bool>.Fail(
                "L'ordine è scomposto in più posizioni: modificalo dalla tabella «Ordine Commessa» del Bilancio."));

        c.Execute("UPDATE projects SET revenue=@Val WHERE id=@Id", new { Val = value, Id = id });
        ReconcileOrderLinesWithRevenue(c, id, value);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Tiene insieme <c>projects.revenue</c> e le righe di <c>project_order_lines</c> dopo una
    /// scrittura che ha toccato il solo <c>revenue</c>. Con una riga sola la riga segue il
    /// valore; con più righe vince la tabella e <c>revenue</c> torna alla loro somma. Senza
    /// righe non fa nulla: la commessa non ha ancora aperto il Bilancio e resta com'era.
    /// </summary>
    private static void ReconcileOrderLinesWithRevenue(IDbConnection c, int projectId, decimal revenue)
    {
        var lineIds = c.Query<int>(
            "SELECT id FROM project_order_lines WHERE project_id=@Id ORDER BY sort_order, id",
            new { Id = projectId }).ToList();

        if (lineIds.Count == 0) return;

        if (lineIds.Count == 1)
        {
            c.Execute(
                "UPDATE project_order_lines SET amount=@Val, row_version = row_version + 1 WHERE id=@LineId",
                new { Val = revenue, LineId = lineIds[0] });
            return;
        }

        ProjectEconomics.SyncRevenueFromOrderLines(c, projectId);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        string projCode = c.ExecuteScalar<string?>(
            "SELECT code FROM projects WHERE id=@Id", new { Id = id }) ?? "";
        c.Execute("UPDATE projects SET status='CANCELLED' WHERE id=@Id", new { Id = id });
        NotifyProjectStatusChange(id, projCode, "CANCELLED");
        // L'eliminazione è un soft delete → CANCELLED: per gli elenchi (che mostrano solo
        // le aperte) la commessa sparisce, quindi vale come "delete" anche per i client.
        NotifyProjectsChanged("delete", id, projCode);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // Anagrafica commesse cambiata (creata / modificata / eliminata): avvisa il gruppo globale
    // "projects-all" così tutti gli elenchi aperti dai colleghi si ricaricano da soli.
    // Fire-and-forget, niente self-exclusion: chi ha fatto la modifica ricarica comunque già.
    private void NotifyProjectsChanged(string action, int projectId, string code)
    {
        _ = _hub.Clients.Group(ProjectHub.ProjectsGroup)
            .SendAsync("ProjectsChanged", new ProjectChange
            {
                ProjectId = projectId,
                Action = action,
                Code = code
            });
    }

    private void NotifyProjectStatusChange(int projectId, string projectCode, string newStatus)
    {
        try
        {
            string label = newStatus switch
            {
                "ACTIVE" => "ATTIVA",
                "ON_HOLD" => "SOSPESA",
                "CANCELLED" => "ANNULLATA",
                _ => newStatus
            };
            string severity = newStatus == "ACTIVE" ? "INFO" : "WARNING";
            string message = newStatus == "ACTIVE"
                ? $"La commessa {projectCode} e' ora attiva. Le attivita' assegnate sono operative."
                : $"La commessa {projectCode} e' in stato {label}. Tutte le attivita' sono sospese.";

            int currentEmpId = GetCurrentEmployeeId();
            List<int> recipients = _notif.GetProjectEmployeeIds(projectId);
            recipients.Remove(currentEmpId);
            if (recipients.Count == 0) return;

            _notif.Create("PROJECT_STATUS", severity,
                $"Commessa {projectCode} - {label}",
                message,
                "PROJECT", projectId, projectId, currentEmpId,
                recipients);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Errore durante l'invio delle notifiche per cambio stato commessa {ProjectCode}", projectCode);
        }
    }

    /// <summary>
    /// DELETE /api/projects/{id}/hard — Cancellazione definitiva: DB (CASCADE) + cartelle + ripristino offerta.
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireFeature("action.delete_project")]
    public IActionResult HardDelete(int id)
    {
        try
        {
            using var c = _db.Open();
            var proj = c.QueryFirstOrDefault<dynamic>("SELECT id, code, server_path FROM projects WHERE id=@Id", new { Id = id });
            if (proj == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));

            string projectCode = (string)proj.code;
            string? serverPath = (string?)proj.server_path;

            using var tx = c.BeginTransaction();

            // 1. Cancella notifiche collegate
            c.Execute(@"
                DELETE nr FROM notification_recipients nr
                JOIN notifications n ON n.id = nr.notification_id
                WHERE n.project_id = @Pid", new { Pid = id }, tx);
            c.Execute("DELETE FROM notifications WHERE project_id = @Pid", new { Pid = id }, tx);

            // 3. Elimina tabelle con FK non-CASCADE sulle fasi
            c.Execute(@"
                DELETE te FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                WHERE pp.project_id = @Pid", new { Pid = id }, tx);

            c.Execute(@"
                DELETE pa FROM phase_assignments pa
                JOIN project_phases pp ON pp.id = pa.project_phase_id
                WHERE pp.project_id = @Pid", new { Pid = id }, tx);

            // 4. Ripristina preventivi collegati (da "converted" → "accepted", azzera link commessa)
            c.Execute(@"UPDATE quotes SET status='accepted', project_id=NULL, converted_at=NULL
                        WHERE project_id=@Pid AND status='converted'", new { Pid = id }, tx);

            // 5. DELETE progetto (FK CASCADE elimina fasi, bom, costing, pricing, cashflow, chat, docs)
            c.Execute("DELETE FROM projects WHERE id = @Id", new { Id = id }, tx);

            tx.Commit();

            // 4. Cancella cartelle fisiche (dopo commit, non critico)
            try
            {
                if (!string.IsNullOrEmpty(serverPath) && Directory.Exists(serverPath))
                    Directory.Delete(serverPath, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Projects] Warning: impossibile cancellare cartella {serverPath}: {ex.Message}");
            }

            return Ok(ApiResponse<bool>.Ok(true, $"Commessa {projectCode} eliminata definitivamente"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    // --- FASI ---
    [HttpGet("{id}/phases")]
    public IActionResult GetPhases(int id)
    {
        using var c = _db.Open();
        var rows = c.Query<PhaseListItem>(@"
            SELECT pp.id, pp.custom_name AS Name, pp.budget_hours AS BudgetHours,
                   pp.budget_cost AS BudgetCost, pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   COALESCE(SUM(te.hours),0) AS HoursWorked
            FROM project_phases pp
            LEFT JOIN timesheet_entries te ON te.project_phase_id = pp.id
            WHERE pp.project_id = @Id
            GROUP BY pp.id
            ORDER BY pp.sort_order", new { Id = id }).ToList();
        return Ok(ApiResponse<List<PhaseListItem>>.Ok(rows));
    }

    // --- CODICE AUTO ---
    /// <summary>Codice proposto per una nuova commessa: C{aaaammgg}.{progressivo del giorno}.</summary>
    [HttpGet("next-code")]
    [RequireFeature("nav.commesse")]
    public IActionResult NextCode()
    {
        using var c = _db.Open();
        return Ok(ApiResponse<string>.Ok(ProjectCodeGenerator.Next(c)));
    }

    // --- FILE SYSTEM ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/create-folder")]
    public IActionResult CreateFolder(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<dynamic>("SELECT code, server_path FROM projects WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound();

        string code = (string)proj.code;
        string basePath = _db.GetConfig("BasePath", @"C:\ATEC_Commesse");
        string year = DateTime.Now.Year.ToString();
        string fullPath = Path.Combine(basePath, year, code);

        if (!Directory.Exists(fullPath))
        {
            _templateCopy.CopyToProject(code);
        }

        c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = id });
        NotifyDocumentsChanged(id, "create");
        return Ok(ApiResponse<string>.Ok(fullPath));
    }

    [RequireFeature("project.documenti")]
    [HttpGet("{id}/files")]
    public IActionResult GetFiles(int id, [FromQuery] string? subPath)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });

        if (string.IsNullOrEmpty(serverPath))
            return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        // 1. Validazione del percorso di destinazione (Sicurezza)
        var targetPath = serverPath;
        if (!string.IsNullOrEmpty(subPath))
        {
            // Path.Combine pulisce automaticamente eventuali problemi di slash
            targetPath = Path.GetFullPath(Path.Combine(serverPath, subPath));

            // CONTROLLO DI SICUREZZA: Impedisce il "Path Traversal"
            // Verifica che il percorso risultante sia ancora all'interno di serverPath
            if (!IsPathAllowed(serverPath, targetPath))
            {
                return BadRequest(ApiResponse<string>.Fail("Accesso negato al percorso specificate fuori dalla root di progetto."));
            }
        }

        if (!Directory.Exists(targetPath))
            return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        var items = new List<FileItem>();

        try
        {
            // 2. Lettura Directory
            foreach (var dir in Directory.GetDirectories(targetPath).OrderBy(d => d))
            {
                var di = new DirectoryInfo(dir);
                if (di.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new FileItem
                {
                    Name = di.Name,
                    IsFolder = true,
                    // Usiamo Replace per uniformare gli slash per il web/client
                    RelativePath = Path.GetRelativePath(serverPath, dir).Replace("\\", "/")
                });
            }

            // 3. Lettura File
            foreach (var file in Directory.GetFiles(targetPath).OrderBy(f => f))
            {
                var fi = new FileInfo(file);
                items.Add(new FileItem
                {
                    Name = fi.Name,
                    IsFolder = false,
                    Size = fi.Length,
                    RelativePath = Path.GetRelativePath(serverPath, file).Replace("\\", "/"),
                    Modified = fi.LastWriteTime
                });
            }

            return Ok(ApiResponse<List<FileItem>>.Ok(items));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<FileItem>>.Fail($"Errore lettura file: {ex.Message}"));
        }
    }

    [RequireFeature("project.documenti")]
    [HttpGet("{id}/file-tree")]
    public IActionResult GetFileTree(int id)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath) || !Directory.Exists(serverPath))
            return Ok(ApiResponse<List<FileTreeItem>>.Ok(new()));

        var tree = BuildFileTree(serverPath, serverPath);
        return Ok(ApiResponse<List<FileTreeItem>>.Ok(tree));
    }

    private List<FileTreeItem> BuildFileTree(string rootPath, string currentPath)
    {
        var items = new List<FileTreeItem>();

        foreach (var dir in Directory.GetDirectories(currentPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            if (di.Name.Equals("Chat", StringComparison.OrdinalIgnoreCase)) continue;

            var node = new FileTreeItem
            {
                Name = di.Name,
                IsFolder = true,
                RelativePath = Path.GetRelativePath(rootPath, dir),
                Children = BuildFileTree(rootPath, dir)
            };
            items.Add(node);
        }
        foreach (var file in Directory.GetFiles(currentPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            items.Add(new FileTreeItem
            {
                Name = fi.Name,
                IsFolder = false,
                Size = fi.Length,
                RelativePath = Path.GetRelativePath(rootPath, file),
                Modified = fi.LastWriteTime
            });
        }

        return items;
    }

    /// <summary>
    /// Il percorso richiesto sta davvero dentro la cartella della commessa, fuori dall'area Chat?
    /// È l'unica barriera che protegge i documenti una volta concesso <c>project.documenti</c>,
    /// quindi qui stanno insieme i due controlli che prima erano sparsi e incoerenti
    /// (segnalazione #63, Fase 1):
    /// <list type="number">
    /// <item><b>Confine della commessa.</b> I confronti precedenti usavano
    /// <c>StartsWith(root)</c> senza separatore finale — e due su otto erano pure
    /// case-sensitive — quindi una cartella sorella con lo stesso prefisso (…\C001 accanto a
    /// …\C0011) superava il controllo. Qui il confronto è sul percorso RELATIVO: fuori dalla
    /// radice <c>GetRelativePath</c> restituisce un percorso che risale (<c>..</c>) o assoluto.</item>
    /// <item><b>Area Chat.</b> Gli allegati dei messaggi vivono in
    /// <c>&lt;cartella commessa&gt;\Chat\{chatId}</c>. L'elenco file la saltava confrontando il
    /// NOME della cartella al livello che stava elencando: bastava chiedere
    /// <c>?subPath=Chat/12</c> per scendere dentro e scaricare gli allegati delle chat private
    /// senza avere <c>project.chat</c> — e saltando l'unico controllo di partecipazione del
    /// modulo, quello di <c>ChatController.GetAttachment</c>. Il confronto sul percorso relativo
    /// chiude l'ingresso a qualunque profondità.</item>
    /// </list>
    /// </summary>
    private static bool IsPathAllowed(string serverPath, string candidatePath)
    {
        string root = Path.GetFullPath(serverPath);
        string full = Path.GetFullPath(candidatePath);

        string relative = Path.GetRelativePath(root, full);

        // Fuori dalla radice: GetRelativePath risale con ".." o resta assoluto (altro disco).
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        if (relative == ".") return true; // è la radice stessa

        string firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return !firstSegment.Equals(ChatFolderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cartella degli allegati chat dentro la commessa: non passa mai dai Documenti.</summary>
    private const string ChatFolderName = "Chat";

    // --- DOWNLOAD FILE ---
    [RequireFeature("project.documenti")]
    [HttpGet("{id}/download")]
    public IActionResult DownloadFile(int id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest("Path richiesto");

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return NotFound("Cartella commessa non trovata");

        var fullPath = Path.Combine(serverPath, path);

        // Sicurezza: verifica che il path sia dentro la cartella commessa
        var normalizedFull = Path.GetFullPath(fullPath);
        if (!IsPathAllowed(serverPath, normalizedFull))
            return BadRequest("Path non valido");

        if (!System.IO.File.Exists(fullPath))
            return NotFound("File non trovato");

        var fileName = Path.GetFileName(fullPath);
        var ext = Path.GetExtension(fileName).ToLower();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".ogv" => "video/ogg",
            ".wmv" => "video/x-ms-wmv",
            ".dwg" => "application/acad",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".msg" => "application/vnd.ms-outlook",
            ".eml" => "message/rfc822",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, fileName);
    }


    // --- PREVIEW EXCEL/CSV → HTML ---
    [RequireFeature("project.documenti")]
    [HttpGet("{id}/preview")]
    public IActionResult PreviewFile(int id, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest("Path richiesto");

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return NotFound("Cartella non trovata");

        var fullPath = Path.GetFullPath(Path.Combine(serverPath, path));
        if (!IsPathAllowed(serverPath, fullPath)) return BadRequest("Path non valido");
        if (!System.IO.File.Exists(fullPath)) return NotFound("File non trovato");

        var ext = Path.GetExtension(fullPath).ToLower();
        if (ext is not (".xlsx" or ".xls" or ".csv" or ".docx" or ".eml" or ".msg")) return BadRequest("Tipo non supportato");

        try
        {
            var fileName = Path.GetFileName(fullPath);

            // === EMAIL (.eml / .msg) ===
            if (ext is ".eml" or ".msg")
                return Content(EmailPreviewService.RenderHtml(fullPath), "text/html");

            var sb = new System.Text.StringBuilder();

            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
        * { margin:0; padding:0; box-sizing:border-box; }
        body { font-family:Segoe UI,sans-serif; font-size:13px; background:#F7F8FA; padding:12px; }
        .info { padding:8px 12px; background:#fff; border:1px solid #E4E7EC; margin-bottom:8px; font-weight:600; }
        .tabs { display:flex; gap:2px; margin-bottom:8px; }
        .tab { padding:6px 16px; background:#fff; border:1px solid #E4E7EC; cursor:pointer; font-size:12px; }
        .tab.active { background:#4F6EF7; color:#fff; border-color:#4F6EF7; }
        .sheet { display:none; }
        .sheet.active { display:block; }
        table { width:100%; border-collapse:collapse; background:#fff; border:1px solid #E4E7EC; }
        th { background:#F7F8FA; font-weight:600; font-size:12px; text-align:left;
             padding:6px 10px; border:1px solid #E4E7EC; position:sticky; top:0; }
        td { padding:5px 10px; border:1px solid #F3F4F6; font-size:12px; white-space:nowrap; }
        tr:hover td { background:#f0f4ff; }
        .doc-content { background:#fff; border:1px solid #E4E7EC; padding:24px; line-height:1.6; }
        .doc-content h1 { font-size:20px; margin:16px 0 8px; }
        .doc-content h2 { font-size:17px; margin:14px 0 6px; }
        .doc-content h3 { font-size:15px; margin:12px 0 6px; }
        .doc-content p { margin:6px 0; }
        .doc-content table { margin:12px 0; }
        .doc-content ul, .doc-content ol { margin:8px 0 8px 24px; }
    </style></head><body>");

            // === WORD ===
            if (ext is ".doc" or ".docx")
            {
                sb.Append($"<div class='info'>📘 {System.Web.HttpUtility.HtmlEncode(fileName)}</div>");
                using var docStream = System.IO.File.OpenRead(fullPath);
                var converter = new Mammoth.DocumentConverter();
                var result = converter.ConvertToHtml(docStream);
                sb.Append($"<div class='doc-content'>{result.Value}</div>");
                sb.Append("</body></html>");
                return Content(sb.ToString(), "text/html");
            }

            sb.Append($"<div class='info'>📗 {System.Web.HttpUtility.HtmlEncode(fileName)}</div>");

            // === CSV ===
            if (ext == ".csv")
            {
                var lines = System.IO.File.ReadAllLines(fullPath);
                sb.Append("<table><thead><tr>");
                if (lines.Length > 0)
                {
                    var sep = lines[0].Contains(';') ? ';' : ',';
                    var headers = lines[0].Split(sep);
                    foreach (var h in headers)
                        sb.Append($"<th>{System.Web.HttpUtility.HtmlEncode(h.Trim().Trim('"'))}</th>");
                    sb.Append("</tr></thead><tbody>");
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i])) continue;
                        sb.Append("<tr>");
                        foreach (var cell in lines[i].Split(sep))
                            sb.Append($"<td>{System.Web.HttpUtility.HtmlEncode(cell.Trim().Trim('"'))}</td>");
                        sb.Append("</tr>");
                    }
                }
                sb.Append("</tbody></table>");
            }
            // === EXCEL ===
            else
            {
                using var package = new ExcelPackage(new FileInfo(fullPath));
                var sheets = package.Workbook.Worksheets;

                if (sheets.Count > 1)
                {
                    sb.Append("<div class='tabs'>");
                    for (int s = 0; s < sheets.Count; s++)
                        sb.Append($"<div class='tab{(s == 0 ? " active" : "")}' onclick='showSheet({s})'>{System.Web.HttpUtility.HtmlEncode(sheets[s].Name)}</div>");
                    sb.Append("</div>");
                }

                for (int s = 0; s < sheets.Count; s++)
                {
                    var ws = sheets[s];
                    sb.Append($"<div class='sheet{(s == 0 ? " active" : "")}' id='s{s}'>");

                    if (ws.Dimension == null)
                    {
                        sb.Append("<p>Foglio vuoto</p></div>");
                        continue;
                    }

                    int startRow = ws.Dimension.Start.Row;
                    int dimEndRow = ws.Dimension.End.Row;
                    int startCol = ws.Dimension.Start.Column;
                    int dimEndCol = ws.Dimension.End.Column;

                    int endCol = startCol;
                    int scanRows = Math.Min(dimEndRow, 50);
                    for (int col = Math.Min(dimEndCol, 200); col >= startCol; col--)
                    {
                        bool found = false;
                        for (int row = startRow; row <= scanRows; row++)
                        {
                            if (!string.IsNullOrEmpty(ws.Cells[row, col].Text))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found) { endCol = col; break; }
                    }

                    int endRow = startRow;
                    for (int row = dimEndRow; row >= startRow; row--)
                    {
                        bool hasData = false;
                        for (int col = startCol; col <= endCol; col++)
                        {
                            if (!string.IsNullOrEmpty(ws.Cells[row, col].Text))
                            {
                                hasData = true;
                                break;
                            }
                        }
                        if (hasData) { endRow = row; break; }
                    }

                    endRow = Math.Min(endRow, startRow + 500);
                    endCol = Math.Min(endCol, startCol + 50);

                    sb.Append("<table>");

                    var mergeMap = new Dictionary<string, (int rowSpan, int colSpan)>();
                    var skipCells = new HashSet<string>();

                    foreach (var merge in ws.MergedCells)
                    {
                        if (merge == null) continue;
                        var addr = new ExcelAddress(merge);
                        int mr1 = addr.Start.Row, mc1 = addr.Start.Column;
                        int mr2 = addr.End.Row, mc2 = addr.End.Column;
                        mergeMap[$"{mr1},{mc1}"] = (mr2 - mr1 + 1, mc2 - mc1 + 1);
                        for (int r = mr1; r <= mr2; r++)
                            for (int cc = mc1; cc <= mc2; cc++)
                                if (r != mr1 || cc != mc1)
                                    skipCells.Add($"{r},{cc}");
                    }

                    for (int row = startRow; row <= endRow; row++)
                    {
                        sb.Append(row == startRow ? "<thead><tr>" : "<tr>");

                        for (int col = startCol; col <= endCol; col++)
                        {
                            var key = $"{row},{col}";
                            if (skipCells.Contains(key)) continue;

                            var cell = ws.Cells[row, col];
                            var style = cell.Style;
                            var cssStyle = new System.Text.StringBuilder();

                            if (style.Fill.PatternType != OfficeOpenXml.Style.ExcelFillStyle.None &&
                                !string.IsNullOrEmpty(style.Fill.BackgroundColor?.Rgb))
                            {
                                var rgb = style.Fill.BackgroundColor.Rgb;
                                if (rgb.Length == 8) rgb = rgb.Substring(2);
                                cssStyle.Append($"background:#{rgb};");
                            }

                            if (!string.IsNullOrEmpty(style.Font.Color?.Rgb))
                            {
                                var rgb = style.Font.Color.Rgb;
                                if (rgb.Length == 8) rgb = rgb.Substring(2);
                                cssStyle.Append($"color:#{rgb};");
                            }

                            if (style.Font.Bold) cssStyle.Append("font-weight:700;");
                            if (style.Font.Italic) cssStyle.Append("font-style:italic;");
                            if (style.Font.Size > 0) cssStyle.Append($"font-size:{style.Font.Size}px;");

                            if (style.HorizontalAlignment == OfficeOpenXml.Style.ExcelHorizontalAlignment.Center)
                                cssStyle.Append("text-align:center;");
                            else if (style.HorizontalAlignment == OfficeOpenXml.Style.ExcelHorizontalAlignment.Right)
                                cssStyle.Append("text-align:right;");

                            var val = cell.Text ?? "";
                            var tag = row == startRow ? "th" : "td";
                            var attrs = new System.Text.StringBuilder();
                            if (cssStyle.Length > 0) attrs.Append($" style='{cssStyle}'");
                            if (mergeMap.TryGetValue(key, out var span))
                            {
                                if (span.rowSpan > 1) attrs.Append($" rowspan='{span.rowSpan}'");
                                if (span.colSpan > 1) attrs.Append($" colspan='{span.colSpan}'");
                            }

                            sb.Append($"<{tag}{attrs}>{System.Web.HttpUtility.HtmlEncode(val)}</{tag}>");
                        }

                        sb.Append(row == startRow ? "</tr></thead><tbody>" : "</tr>");
                    }

                    sb.Append("</tbody></table></div>");
                }

                if (sheets.Count > 1)
                {
                    sb.Append(@"<script>
        function showSheet(idx) {
            document.querySelectorAll('.sheet').forEach(s => s.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.getElementById('s'+idx).classList.add('active');
            document.querySelectorAll('.tab')[idx].classList.add('active');
        }
    </script>");
                }
            }

            sb.Append("</body></html>");
            return Content(sb.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            return Content($"<html><body><p style='color:red'>Errore: {System.Web.HttpUtility.HtmlEncode(ex.Message)}</p></body></html>", "text/html");
        }
    }


    // --- LOOKUP ---
    [HttpGet("/api/lookup/customers")]
    public IActionResult LookupCustomers()
    {
        using var c = _db.Open();
        // Il cliente tecnico «ATEC — Sistema» esiste solo per la commessa INTERNA:
        // non deve essere assegnabile a una commessa vera.
        var rows = c.Query<LookupItem>(@"
            SELECT id AS Id, company_name AS Name FROM customers
            WHERE is_active=1 AND (vat_number IS NULL OR vat_number <> @SystemVat)
            ORDER BY company_name",
            new { SystemVat = ATEC.PM.Shared.SystemProjects.SystemCustomerVat }).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("/api/lookup/employees")]
    public IActionResult LookupEmployees([FromQuery] string? role = null)
    {
        using var c = _db.Open();
        string sql = "SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status='ACTIVE'";
        if (!string.IsNullOrEmpty(role))
        {
            sql += " AND user_role=@Role";
        }
        sql += " ORDER BY last_name";
        var rows = c.Query<LookupItem>(sql, new { Role = role }).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }
    [HttpGet("template-structure")]
    public IActionResult GetTemplateStructure()
    {
        using MySqlConnector.MySqlConnection c = _db.Open();

        List<(int Id, int? ParentId, string Name)> folderList = c.Query<(int Id, int? ParentId, string Name)>(
            "SELECT id, parent_id, name FROM project_template_folders WHERE is_active=1").ToList();

        var result = new TemplateFolderInfo();
        if (folderList.Count == 0)
            return Ok(ApiResponse<TemplateFolderInfo>.Ok(result));

        Dictionary<int, string> folderPaths = new();
        HashSet<int> unresolved = folderList.Select(f => f.Id).ToHashSet();

        while (unresolved.Count > 0)
        {
            int resolvedThisPass = 0;
            foreach ((int Id, int? ParentId, string Name) f in folderList.Where(x => unresolved.Contains(x.Id)))
            {
                if (f.ParentId == null)
                {
                    folderPaths[f.Id] = f.Name;
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
                else if (folderPaths.TryGetValue(f.ParentId.Value, out string? parentPath))
                {
                    folderPaths[f.Id] = Path.Combine(parentPath, f.Name);
                    unresolved.Remove(f.Id);
                    resolvedThisPass++;
                }
            }
            if (resolvedThisPass == 0)
                break;
        }

        result.Folders = folderPaths.Values
            .OrderBy(p => p.Length)
            .Select(p => p.Replace("\\", "/"))
            .ToList();

        IEnumerable<(int folder_id, string file_name, long file_size)> files = c.Query<(int folder_id, string file_name, long file_size)>(
            "SELECT folder_id, file_name, file_size FROM project_template_files");

        foreach ((int folder_id, string file_name, long file_size) tf in files)
        {
            if (!folderPaths.TryGetValue(tf.folder_id, out string? relFolder))
                continue;

            result.Files.Add(new TemplateFileInfo
            {
                RelativePath = Path.Combine(relFolder, tf.file_name).Replace("\\", "/"),
                FileName = tf.file_name,
                SizeBytes = tf.file_size
            });
        }

        return Ok(ApiResponse<TemplateFolderInfo>.Ok(result));
    }

    // --- DDP (Distinta Di Produzione) ---
    [RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox", "project.ddp_officina")]
    [HttpGet("{id}/ddp")]
    public IActionResult GetDdpItems(int id, [FromQuery] string type = "COMMERCIAL")
    {
        try
        {
            using var c = _db.Open();
            // COALESCE su tutte le colonne testo nullable: righe storiche/importate possono
            // avere NULL (lo schema lo permette) e un null manderebbe in crash le combo web.
            var rows = c.Query<BomItemListItem>(@"
            SELECT b.id, b.project_id AS ProjectId, b.catalog_item_id AS CatalogItemId,
                   COALESCE(b.part_number,'') AS PartNumber, COALESCE(b.description,'') AS Description,
                   COALESCE(b.unit,'') AS Unit, b.quantity,
                   b.unit_cost AS UnitCost,
                   b.supplier_id AS SupplierId,
                   COALESCE(s.company_name, '') AS SupplierName,
                   COALESCE(b.manufacturer,'') AS Manufacturer, COALESCE(b.item_status,'') AS ItemStatus,
                   COALESCE(b.requested_by,'') AS RequestedBy, COALESCE(b.danea_ref,'') AS DaneaRef,
                   b.danea_order_iddoc AS DaneaOrderIdDoc,
                   b.date_needed AS DateNeeded, COALESCE(b.destination,'') AS Destination,
                   b.destination_spec AS DestinationSpec, COALESCE(b.notes,'') AS Notes,
                   -- Codice ATEC effettivo: snapshot di riga, altrimenti mapping vivo dell'articolo.
                   b.ddp_type AS DdpType, COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS AtecCode,
                   b.created_by AS CreatedById,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS CreatedByName,
                   b.created_at AS CreatedAt, b.updated_at AS UpdatedAt,
                   -- #119: raggruppamento per composizione, gemello di quello dell'officina.
                   b.parent_bom_item_id AS ParentBomItemId, b.composition_qty AS CompositionQty,
                   -- #135: grezzo derivato da un 101 (vuoto = riga commerciale normale).
                   COALESCE(b.raw_codex_code,'') AS RawCodexCode,
                   COALESCE(b.raw_sources,'') AS RawSources, b.raw_auto_qty AS RawAutoQty,
                   -- «Consegnato il»: valore salvato sulla riga oppure ultimo passaggio a DISP nella cronistoria.
                   COALESCE(b.delivered_at,
                             (SELECT MAX(ev.changed_at) FROM ddp_item_events ev
                              WHERE ev.item_type = 'COMMERCIAL' AND ev.item_id = b.id
                                AND ev.to_status = 'DISP')) AS DeliveredAt
            FROM bom_items b
            LEFT JOIN suppliers s ON s.id = b.supplier_id
            LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
            LEFT JOIN employees e ON e.id = b.created_by
            WHERE b.project_id = @Id AND b.ddp_type = @Type
            ORDER BY b.id", new { Id = id, Type = type }).ToList();
            foreach (BomItemListItem row in rows)
            {
                if (row.AtecCode.Length > 0)
                    row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
                // #135: in DB il codice del grezzo sta senza punti, in pagina si legge col punto
                // come tutti gli altri codici Codex.
                if (row.RawCodexCode.Length > 0)
                    row.RawCodexCode = CodexListItem.FormatCodice(row.RawCodexCode);
            }

            return Ok(ApiResponse<List<BomItemListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<BomItemListItem>>.Fail(ex.Message));
        }
    }

    // project.ddp_officina in OR: dal picker unico delle DDP un codice commerciale
    // (2xx/3xx) scelto dal lato officina finisce QUI, previa conferma a video.
    [RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox", "project.ddp_officina")]
    [HttpPost("{id}/ddp")]
    public IActionResult AddDdpItem(int id, [FromBody] BomItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            req.ProjectId = id;

            // Finestra di partenza (riga INIZIO della matrice, per tipo): sulla commerciale
            // esclude DC — il materiale commerciale si acquista, non si costruisce.
            string? startError = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, null, req.ItemStatus, _cache, PuoScavalcareMatriceDdp());
            if (startError != null)
                return BadRequest(ApiResponse<int>.Fail(startError));

            // Normalizza atec_code senza punti (formato DB Codex).
            req.AtecCode = (req.AtecCode ?? "").Replace(".", "").Trim();

            int? createdBy = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;

            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO bom_items
                (project_id, catalog_item_id, part_number, description, unit, quantity,
                 unit_cost, supplier_id, manufacturer, item_status, requested_by,
                 danea_ref, date_needed, delivered_at, destination, destination_spec, notes, ddp_type,
                 atec_code, created_by, updated_at)
            VALUES
                (@ProjectId, @CatalogItemId, @PartNumber, @Description, @Unit, @Quantity,
                 @UnitCost, @SupplierId, @Manufacturer, @ItemStatus, COALESCE(@RequestedBy,''),
                 @DaneaRef, @DateNeeded, @DeliveredAt, @Destination, @DestinationSpec, @Notes, @DdpType,
                 NULLIF(@AtecCode,''), @CreatedBy, NOW());
            SELECT LAST_INSERT_ID()", new
            {
                req.ProjectId, req.CatalogItemId, req.PartNumber, req.Description, req.Unit, req.Quantity,
                // Sensibile (§12.3): null = chi crea non vede i prezzi → costo 0, come una riga nata senza costo.
                UnitCost = req.UnitCost ?? 0,
                req.SupplierId, req.Manufacturer, req.ItemStatus, req.RequestedBy,
                req.DaneaRef, req.DateNeeded, req.DeliveredAt, req.Destination, req.DestinationSpec, req.Notes, req.DdpType,
                req.AtecCode, CreatedBy = createdBy
            });

            NotifyDdpChange(id, conn, "create", newId);
            return Ok(ApiResponse<int>.Ok(newId, "Aggiunto"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail(ex.Message));
        }
    }

    [RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox", "project.ddp_officina")]
    [HttpPut("{id}/ddp/{itemId}")]
    public IActionResult UpdateDdpItem(int id, int itemId, [FromBody] BomItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();

            // Concorrenza ottimistica (rete di sicurezza anche col real-time): se il client invia
            // la versione vista (ExpectedUpdatedAt) e la riga è cambiata nel frattempo → 409, niente lost update.
            if (req.ExpectedUpdatedAt.HasValue)
            {
                DateTime? current = c.ExecuteScalar<DateTime?>(
                    "SELECT updated_at FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                    new { ItemId = itemId, Id = id });
                if (current.HasValue && Math.Abs((current.Value - req.ExpectedUpdatedAt.Value).TotalSeconds) > 1)
                    return Conflict(ApiResponse<DateTime?>.Fail(
                        "Riga modificata da un altro utente nel frattempo. Ricarica e riprova."));
            }

            // Leggi stato e quantità precedenti per confronto
            (string? ItemStatus, decimal Quantity) before = c.QueryFirstOrDefault<(string?, decimal)>(
                "SELECT item_status, quantity FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            string? oldStatus = before.ItemStatus;

            // Matrice degli avanzamenti di stato (v7, tipo COMMERCIAL): il server rifiuta le
            // transizioni non ammesse (la UI mostra solo quelle valide, ma qui si coprono
            // client vecchi e modifiche concorrenti).
            string? transitionError = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, oldStatus, req.ItemStatus, _cache, PuoScavalcareMatriceDdp());
            if (transitionError != null)
                return BadRequest(ApiResponse<DateTime?>.Fail(transitionError));

            // Quantità modificabile in VER (ingresso) o DO (da ordinare), o tornando in uno
            // di questi nella stessa save.
            if (req.Quantity != before.Quantity
                && !IsCommercialQtyEditable(oldStatus)
                && !IsCommercialQtyEditable(req.ItemStatus))
            {
                return BadRequest(ApiResponse<DateTime?>.Fail(
                    "La quantità è modificabile solo in stato Verificare magazzino o Da Ordinare."));
            }

            // Auto-fill «Consegnato il» (#139): solo al PASSAGGIO in chiusura positiva (DISP), se ancora vuota → oggi.
            bool closingPositive = string.Equals(req.ItemStatus, "DISP", StringComparison.OrdinalIgnoreCase);
            bool wasClosing = string.Equals(oldStatus, "DISP", StringComparison.OrdinalIgnoreCase);
            DateTime? beforeDeliveredAt = c.ExecuteScalar<DateTime?>(
                "SELECT delivered_at FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            if (closingPositive && !wasClosing && !req.DeliveredAt.HasValue && !beforeDeliveredAt.HasValue)
            {
                req.DeliveredAt = DateTime.Today;
            }

            req.Id = itemId;
            req.ProjectId = id;
            req.AtecCode = (req.AtecCode ?? "").Replace(".", "").Trim();
            c.Execute(@"
            UPDATE bom_items SET
                quantity = @Quantity, item_status = @ItemStatus,
                -- Rif. Danea cambiato/svuotato a mano: il link all'ordine generato non è più
                -- affidabile → si azzera (valutato PRIMA dell'assegnazione di danea_ref: le
                -- SET di MySQL sono applicate in ordine, qui danea_ref è ancora quello vecchio).
                danea_order_iddoc = IF(COALESCE(danea_ref,'') <> @DaneaRef, NULL, danea_order_iddoc),
                danea_ref = @DaneaRef, date_needed = @DateNeeded, delivered_at = @DeliveredAt,
                -- «Inserito da» (#61): NULL = il chiamante non gestisce il campo → autore
                -- invariato. Per svuotarlo si manda la stringa vuota.
                requested_by = COALESCE(@RequestedBy, requested_by),
                destination = @Destination, destination_spec = @DestinationSpec,
                supplier_id = IF(@UpdateSupplier OR @UpdateCatalogSnapshot, @SupplierId, supplier_id),
                catalog_item_id = IF(@UpdateCatalogSnapshot, @CatalogItemId, catalog_item_id),
                part_number = IF(@UpdateCatalogSnapshot, @PartNumber, part_number),
                description = IF(@UpdateCatalogSnapshot, @Description, description),
                unit = IF(@UpdateCatalogSnapshot, @Unit, unit),
                -- Il costo arriva anche dagli edit inline (che rimandano quello della riga):
                -- si scrive solo su richiesta esplicita (snapshot catalogo o prezzo dal dettaglio RDO).
                -- @UnitCost IS NOT NULL: un null (mittente senza il micro prezzi, §12.3)
                -- non deve MAI sovrascrivere un costo vero, nemmeno a flag alzato.
                unit_cost = IF((@UpdateCatalogSnapshot OR @UpdateUnitCost) AND @UnitCost IS NOT NULL, @UnitCost, unit_cost),
                manufacturer = IF(@UpdateCatalogSnapshot, @Manufacturer, manufacturer),
                atec_code = IF(@UpdateCatalogSnapshot OR LENGTH(@AtecCode) > 0,
                               NULLIF(@AtecCode,''), atec_code),
                notes = @Notes, updated_at = NOW(),
                -- Firma dell'ultima modifica (#114): la card «DDP Commesse» della Dashboard
                -- elenca le distinte toccate DA ALTRI, e senza autore non saprebbe distinguerle.
                updated_by = @UpdatedBy
            WHERE id = @Id AND project_id = @ProjectId", ConFirma(req));

            // Cronistoria: da qui esce il «consegnato il …» e la storia completa della riga.
            DdpItemEvents.Registra(c, DdpItemEvents.Commerciale, itemId, id,
                oldStatus, req.ItemStatus, User, log: _logger);

            // «Comanda il padre» (#119): anche da questa griglia. Se la riga toccata è
            // l'intestazione di un gruppo, i componenti la seguono in TUTTE E DUE le DDP e
            // la copia gemella si riallinea. Gira solo sulle intestazioni: un componente ha
            // la quantità bloccata in UI e non arriva mai qui con una quantità diversa.
            if (req.Quantity != before.Quantity)
            {
                int? firma = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;
                var toccati = ComposizioneDdp.PropagaQuantita(
                    c, id, req.PartNumber, req.Quantity - before.Quantity, req.Quantity,
                    firma, ComposizioneDdp.StatiEsclusi(c));
                if (toccati.Count > 0)
                {
                    NotifyWorkRequestsChanged("update", id);
                    NotifyDdpChange(id, conn, "update", 0, "OFFICINA");

                    // #135: fra i componenti d'officina appena moltiplicati possono esserci dei
                    // 101 con derivazione — se cambia la loro quantità cambia anche quella del
                    // grezzo che chiedono, che sta proprio in questa griglia.
                    GrezziDerivazione.Sincronizza(c, id, firma, req.RequestedBy);
                }
            }

            // Trigger notifica se lo stato è cambiato (solo se commessa ACTIVE)
            string projStatus = c.ExecuteScalar<string?>(
                "SELECT status FROM projects WHERE id = @Id", new { Id = id }) ?? "";
            if (!string.IsNullOrEmpty(oldStatus) && oldStatus != req.ItemStatus && projStatus == "ACTIVE")
            {
                try
                {
                    string partNum = c.ExecuteScalar<string?>(
                        "SELECT part_number FROM bom_items WHERE id = @Id", new { Id = itemId }) ?? "";
                    string descr = c.ExecuteScalar<string?>(
                        "SELECT description FROM bom_items WHERE id = @Id", new { Id = itemId }) ?? "";
                    string projCode = c.ExecuteScalar<string?>(
                        "SELECT code FROM projects WHERE id = @Id", new { Id = id }) ?? "";
                    int currentEmpId = GetCurrentEmployeeId();

                    string severity = req.ItemStatus switch
                    {
                        "DISP" => "SUCCESS",  // DISPONIBILE / CONSEGNATO (chiusura positiva v7)
                        "ANN" => "WARNING",   // ANNULLATO
                        "IO" => "INFO",       // IN ORDINE
                        _ => "INFO"
                    };

                    string title = $"Cambio stato DDP — {projCode}";
                    string msg = $"Stato modificato da {DdpStatusMap.ToLabel(oldStatus)} a {DdpStatusMap.ToLabel(req.ItemStatus)}";

                    List<int> recipients = _notif.GetProjectPmIds(id);
                    recipients.AddRange(_notif.GetAcqEmployeeIds());
                    recipients.Remove(currentEmpId);

                    if (recipients.Count > 0)
                        _notif.Create("DDP_STATUS_CHANGED", severity, title, msg, "BOM", itemId, id, currentEmpId, recipients);
                }
                catch { /* non bloccare l'update per errore notifica */ }
            }

            // Real-time: avvisa gli altri che guardano la distinta di questa commessa.
            NotifyDdpChange(id, conn, "update", itemId);

            // Ritorna la nuova versione così il client riallinea il proprio token di concorrenza.
            DateTime? newTs = c.ExecuteScalar<DateTime?>(
                "SELECT updated_at FROM bom_items WHERE id = @Id", new { Id = itemId });
            return Ok(ApiResponse<DateTime?>.Ok(newTs, "Aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DateTime?>.Fail(ex.Message));
        }
    }

    // Cancellazione DEFINITIVA della riga commerciale (l'annullo è un cambio stato → ANN
    // sulla PUT, non passa di qui): serve la chiave `action.delete_ddp_row`, la stessa della
    // gemella officina. Fino alla Fase E il freno era solo il menu del client, che ora la
    // chiave sostituisce anche sull'API. I due [RequireFeature] sono filtri distinti e si
    // sommano in AND: vedere la distinta E poter cancellare. Dentro il primo attributo la
    // chiave sarebbe finita in OR con le altre tre, cioè avrebbe aperto il cancello.
    [RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox")]
    [RequireFeature("action.delete_ddp_row")]
    [HttpDelete("{id}/ddp/{itemId}")]
    public IActionResult DeleteDdpItem(int id, int itemId, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();

            // #119, stesse due regole della griglia officina: un componente non si cancella
            // da solo (la composizione lo rimetterebbe fuori posto), e cancellare
            // l'intestazione porta via i componenti di ENTRAMBE le DDP più la copia gemella.
            var isChild = c.ExecuteScalar<int?>(
                "SELECT parent_bom_item_id FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            if (isChild.HasValue)
                return BadRequest(ApiResponse<bool>.Fail(
                    "Impossibile eliminare direttamente un componente figlio. Eliminare il padre."));

            // #135: il grezzo è la proiezione di un particolare a disegno che sta nell'altra
            // distinta. Cancellarlo qui non servirebbe a niente — il ricalcolo lo rimetterebbe
            // al primo salvataggio in officina — quindi si dice dove si toglie davvero.
            string? grezzoDi = c.ExecuteScalar<string?>(@"
                SELECT COALESCE(raw_sources,'') FROM bom_items
                WHERE id = @ItemId AND project_id = @Id
                  AND COALESCE(raw_codex_code,'') <> ''",
                new { ItemId = itemId, Id = id });
            if (grezzoDi != null)
                return BadRequest(ApiResponse<bool>.Fail(grezzoDi.Length > 0
                    ? $"Questa riga è il grezzo di {grezzoDi}: si toglie eliminando quel particolare dalla DDP Officina, oppure togliendo la derivazione dall'articolo Codex."
                    : "Questa riga è il grezzo di un particolare a disegno: si toglie dalla DDP Officina, oppure togliendo la derivazione dall'articolo Codex."));

            string partNumber = c.ExecuteScalar<string?>(
                "SELECT part_number FROM bom_items WHERE id = @ItemId", new { ItemId = itemId }) ?? "";
            (int figliOfficina, int figliCommerciale) =
                ComposizioneDdp.EliminaComponenti(c, id, partNumber, itemId);

            c.Execute("DELETE FROM bom_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            NotifyDdpChange(id, conn, "delete", itemId);
            if (figliOfficina > 0)
            {
                NotifyDdpChange(id, conn, "delete", 0, "OFFICINA");
                NotifyWorkRequestsChanged("delete", id);

                // #135: fra i componenti d'officina appena cancellati con l'intestazione
                // possono esserci dei 101 con derivazione: i loro grezzi non li chiede più
                // nessuno.
                GrezziDerivazione.Sincronizza(
                    c, id, GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null);
            }

            int figli = figliOfficina + figliCommerciale;
            return Ok(ApiResponse<bool>.Ok(true, figli > 0
                ? $"Eliminata riga + {figli} componenti collegati"
                : "Eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    // --- DDP OFFICINA (distinta particolari meccanici, tabella dedicata ddp_officina_items) ---
    // Stesso contratto della DDP commerciale (concorrenza ottimistica, real-time, notifiche cambio stato),
    // ma campi del template officina: Codice 101, Materiale, Trattamento, fornitore testuale.
    [RequireFeature("project.ddp_officina", "nav.gestore_ddp", "nav.officina_inbox", "project.ddp_commerciale")]
    [HttpGet("{id}/ddp-officina")]
    public IActionResult GetOfficinaItems(int id)
    {
        try
        {
            using var c = _db.Open();
            // COALESCE su tutte le colonne testo nullable: righe storiche/importate possono
            // avere NULL (lo schema lo permette) e un null manderebbe in crash le combo web.
            // 🪤 Le colonne vanno qualificate con l'alias `o`: da quando c'è il LEFT JOIN su
            // `employees` (colonna «Creata da»), quattro nomi esistono in ENTRAMBE le tabelle —
            // id, supplier_id, created_at, updated_at — e MySQL rifiuta la query con
            // «Column '...' in field list is ambiguous». Non si vedeva in sviluppo perché questa
            // action incapsula l'errore in un ApiResponse.Fail dentro un HTTP 200: uno smoke che
            // guarda solo il codice di stato la dà per buona. La query gemella delle DDP
            // commerciali non ne soffriva perché usa già l'alias `b`.
            var rows = c.Query<OfficinaItemListItem>(@"
            SELECT o.id, o.project_id AS ProjectId, COALESCE(o.part_number,'') AS PartNumber,
                   COALESCE(o.description,'') AS Description,
                   o.quantity, o.quantity_produced AS QuantityProduced,
                   o.work_hours AS WorkHours, o.hourly_rate AS HourlyRate,
                   o.unit_cost AS UnitCost, COALESCE(o.material,'') AS Material,
                   COALESCE(o.treatment,'') AS Treatment,
                   o.supplier_id AS SupplierId, COALESCE(o.supplier_name,'') AS SupplierName,
                   COALESCE(o.item_status,'') AS ItemStatus,
                   COALESCE(o.work_type,'') AS WorkType,
                   COALESCE(o.requested_by,'') AS RequestedBy, COALESCE(o.danea_ref,'') AS DaneaRef,
                   o.date_needed AS DateNeeded, o.order_date AS OrderDate,
                   o.delivered_at AS DeliveredAt,
                   COALESCE(o.destination,'') AS Destination,
                   o.destination_spec AS DestinationSpec, COALESCE(o.notes,'') AS Notes,
                   o.parent_officina_item_id AS ParentOfficinaItemId, o.composition_qty AS CompositionQty,
                   o.created_by AS CreatedById,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS CreatedByName,
                   o.created_at AS CreatedAt, o.updated_at AS UpdatedAt
            FROM ddp_officina_items o
            LEFT JOIN employees e ON e.id = o.created_by
            WHERE o.project_id = @Id
            ORDER BY o.id", new { Id = id }).ToList();
            foreach (OfficinaItemListItem row in rows)
            {
                if (row.PartNumber.Length > 0)
                    row.PartNumber = CodexListItem.FormatCodice(row.PartNumber);
            }

            return Ok(ApiResponse<List<OfficinaItemListItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<OfficinaItemListItem>>.Fail(ex.Message));
        }
    }

    // project.ddp_commerciale in OR: dal picker unico un particolare 1xx (o l'intestazione
    // di un gruppo) scelto dal lato commerciale finisce QUI, previa conferma a video.
    [RequireFeature("project.ddp_officina", "nav.gestore_ddp", "nav.officina_inbox", "project.ddp_commerciale")]
    [HttpPost("{id}/ddp-officina")]
    public IActionResult AddOfficinaItem(int id, [FromBody] OfficinaItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            req.ProjectId = id;

            // Finestra di partenza (riga INIZIO della matrice, tipo OFFICINA).
            string? startError = DdpTransitionService.Validate(c, DdpTransitionService.TypeOfficina, null, req.ItemStatus, _cache, PuoScavalcareMatriceDdp());
            if (startError != null)
                return BadRequest(ApiResponse<int>.Fail(startError));

            // Auto-fill data ordine al primo passaggio in IO (anche in create).
            if (string.Equals(req.ItemStatus, "IO", StringComparison.OrdinalIgnoreCase)
                && !req.OrderDate.HasValue)
            {
                req.OrderDate = DateTime.Today;
            }
            int? createdBy = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;
            var newId = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, work_hours, hourly_rate, unit_cost, material,
                 treatment, supplier_id, supplier_name, item_status, requested_by, danea_ref,
                 date_needed, order_date, delivered_at, destination, destination_spec, notes, created_by, updated_at)
            VALUES
                (@ProjectId, @PartNumber, @Description, @Quantity, @WorkHours, @HourlyRate, @UnitCost, @Material,
                 @Treatment, @SupplierId, @SupplierName, @ItemStatus, COALESCE(@RequestedBy,''), @DaneaRef,
                 @DateNeeded, @OrderDate, @DeliveredAt, @Destination, @DestinationSpec, @Notes, @CreatedBy, NOW());
            SELECT LAST_INSERT_ID()", new
            {
                req.ProjectId, req.PartNumber, req.Description, req.Quantity, req.WorkHours, req.HourlyRate,
                // Sensibile (§12.3): null = chi crea non vede i prezzi → costo 0.
                UnitCost = req.UnitCost ?? 0,
                req.Material, req.Treatment, req.SupplierId, req.SupplierName, req.ItemStatus, req.RequestedBy,
                req.DaneaRef, req.DateNeeded, req.OrderDate, req.DeliveredAt, req.Destination, req.DestinationSpec, req.Notes,
                CreatedBy = createdBy
            });

            // Niente più bozze da generare (#83): la riga si vede in Lavorazioni Officine dov'è.
            // Il Tipo parte vuoto (segnalazione #138): lo sceglie l'utente dal menu a tendina.
            NotifyWorkRequestsChanged("create", id);

            // #135: se la riga appena inserita è un 101 con derivazione, il suo grezzo 201 deve
            // comparire (o crescere) in DDP Commerciale — il materiale lo compra qualcun altro.
            var grezzi = GrezziDerivazione.Sincronizza(c, id, createdBy, req.RequestedBy);
            if (!grezzi.NienteDaFare) NotifyDdpChange(id, conn, "update", 0, "COMMERCIAL");

            NotifyDdpChange(id, conn, "create", newId, "OFFICINA");
            return Ok(ApiResponse<int>.Ok(newId, "Aggiunto"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<int>.Fail(ex.Message));
        }
    }

    // Import composizione Codex → DDP di commessa: aggiunto un codice padre (gruppo 5xx),
    // i suoi figli DIRETTI (solo articoli Codex, niente ricorsione sui sotto-gruppi) diventano
    // righe di DDP con snapshot codice/descrizione/costo/fornitore, stato DO.
    //
    // #119 (25/08/2026): i figli NON finiscono più tutti in officina. Ognuno va dove dice
    // DdpSmistamento — 2xx/3xx nella DDP Commerciale, il resto in officina — e la riga del
    // padre fa da intestazione collassabile in ENTRAMBE le griglie, ciascuna coi propri figli.
    // La rotta resta sotto `ddp-officina` per non rompere i client: è cambiato cosa fa, non
    // da dove la si chiama.
    //
    // Dedup per codice normalizzato (senza punti) e PER TABELLA: se il figlio è già in quella
    // distinta si somma la quantità della composizione alla riga esistente. I figli da Catalogo
    // vengono saltati e contati (dal 25/08 non possono più nemmeno esistere in composizione).
    // project.ddp_commerciale in OR: l'import dei gruppi (che smista nelle DUE distinte)
    // dal picker unico parte anche dal lato commerciale.
    [RequireFeature("project.ddp_officina", "nav.gestore_ddp", "nav.officina_inbox", "project.ddp_commerciale")]
    [HttpPost("{id}/ddp-officina/import-composition")]
    public IActionResult ImportOfficinaComposition(int id, [FromBody] OfficinaImportCompositionRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            var children = c.Query<OfficinaCompositionChild>(@"
            SELECT ci.codice AS RawCode, ci.descr AS Descr, ci.fornitore AS Fornitore,
                   ci.prezzo_forn AS PrezzoForn, cc.quantity AS Quantity,
                   -- Il codice NUOVO (ricodifica 201/211/221) è l'unico che vale come «Cod. ATEC»:
                   -- vuoto = quell'articolo non è ancora codificato. Vedi RisolviArticoloCommerciale.
                   COALESCE(ci.codice_nuovo,'') AS CodiceNuovo,
                   (cc.child_catalog_id IS NOT NULL) AS IsCatalog
            FROM codex_compositions cc
            LEFT JOIN codex_items ci ON ci.id = cc.child_codex_id
            WHERE cc.parent_codex_id = @ParentId
            ORDER BY cc.sort_order, ci.codice", new { ParentId = req.CodexParentId }).ToList();

            if (children.Count == 0)
                return Ok(ApiResponse<OfficinaImportCompositionResult>.Fail("L'articolo non ha una composizione."));

            // Riga del padre in distinta: la sua quantità è il moltiplicatore dell'import
            // (es. 4 gruppi → ogni componente ×4) e il suo id collega i figli, che da qui
            // in poi seguono i cambi di quantità del padre («comanda il padre»).
            decimal parentQty = 1;
            int? parentRowId = null;
            var codexPadre = c.QueryFirstOrDefault<(string Codice, string Descr)>(
                "SELECT codice, COALESCE(descr,'') FROM codex_items WHERE id = @Id",
                new { Id = req.CodexParentId });
            string parentKey = (codexPadre.Codice ?? "").Replace(".", "").Trim();
            if (parentKey.Length > 0)
            {
                (int Id, decimal Quantity) parentRow = c.QueryFirstOrDefault<(int, decimal)>(@"
                SELECT id, quantity FROM ddp_officina_items
                WHERE project_id = @Id AND REPLACE(part_number, '.', '') = @Key
                ORDER BY id LIMIT 1", new { Id = id, Key = parentKey });
                if (parentRow.Id > 0)
                {
                    parentRowId = parentRow.Id;
                    if (parentRow.Quantity > 0) parentQty = parentRow.Quantity;
                }
                else
                {
                    // Gruppo di SOLI componenti commerciali (fix 26/08/2026): il padre in
                    // officina non esiste e non deve nascere — il «padre che comanda» è
                    // l'intestazione della DDP Commerciale, quindi il moltiplicatore è la
                    // sua quantità (un import completato dopo un +1 sull'intestazione
                    // genera figli già allineati). Senza nessuna intestazione resta 1.
                    decimal? bomQty = c.ExecuteScalar<decimal?>(@"
                    SELECT quantity FROM bom_items
                    WHERE project_id = @Id AND REPLACE(COALESCE(part_number,''), '.', '') = @Key
                      AND parent_bom_item_id IS NULL
                    ORDER BY id LIMIT 1", new { Id = id, Key = parentKey });
                    if (bomQty is > 0) parentQty = bomQty.Value;
                }
            }

            // Righe già in distinta, indicizzate per codice normalizzato (il part_number è
            // salvato col punto). Un indice PER TABELLA: lo stesso codice può stare in
            // entrambe le DDP ed è giusto così, sono due distinte diverse.
            var existingByCode = LeggiIndiceCodici(c, "ddp_officina_items", id);
            var existingCommerciale = LeggiIndiceCodici(c, "bom_items", id);

            // Intestazione nella DDP Commerciale: creata alla PRIMA riga commerciale, non
            // prima — un gruppo di soli particolari a disegno non deve lasciare un padre
            // orfano in una griglia dove non ha figli.
            int? parentBomId = null;
            int PadreCommerciale()
            {
                if (parentBomId != null) return parentBomId.Value;
                parentBomId = c.ExecuteScalar<int?>(@"
                    SELECT id FROM bom_items
                    WHERE project_id = @Id AND REPLACE(COALESCE(part_number,''), '.', '') = @Key
                      AND parent_bom_item_id IS NULL
                    ORDER BY id LIMIT 1", new { Id = id, Key = parentKey }) ?? 0;
                if (parentBomId == 0)
                {
                    // Costo a zero: il costo del gruppo sono i suoi componenti, che qui sono
                    // righe vere. Metterlo anche sull'intestazione lo conterebbe due volte.
                    parentBomId = c.ExecuteScalar<int>(@"
                        INSERT INTO bom_items
                            (project_id, part_number, description, unit, quantity, unit_cost,
                             item_status, requested_by, ddp_type, created_by, updated_at)
                        VALUES
                            (@ProjectId, @PartNumber, @Description, 'PZ', @Quantity, 0,
                             'DO', @RequestedBy, 'COMMERCIAL', @CreatedBy, NOW());
                        SELECT LAST_INSERT_ID()",
                        new
                        {
                            ProjectId = id,
                            PartNumber = CodexListItem.FormatCodice(codexPadre.Codice ?? ""),
                            Description = codexPadre.Descr ?? "",
                            Quantity = parentQty,
                            RequestedBy = req.RequestedBy,
                            CreatedBy = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : (int?)null
                        });
                }
                return parentBomId.Value;
            }

            var result = new OfficinaImportCompositionResult { ParentQuantity = parentQty };
            int? createdByComp = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;
            foreach (OfficinaCompositionChild child in children)
            {
                // I figli da Catalogo non hanno famiglia, quindi non saprebbero dove andare:
                // saltati e contati (dal 25/08 la composizione non li accetta più).
                if (child.IsCatalog || string.IsNullOrWhiteSpace(child.RawCode)) { result.Skipped++; continue; }

                string key = child.RawCode.Replace(".", "").Trim();

                // ── Componente COMMERCIALE (2xx/3xx): va nella DDP Commerciale ──────────
                if (DdpSmistamento.VaInCommerciale(key))
                {
                    int padreBom = PadreCommerciale();
                    // 🪤 Si somma SOLO su righe nate da una composizione (`composition_qty`
                    // valorizzata). Da quando il componente può assumere il codice Danea
                    // dell'articolo, può coincidere col codice di una riga che Acquisti ha
                    // inserito a mano: adottarla la renderebbe figlia del gruppo, cioè non più
                    // cancellabile, con la quantità comandata dal padre e — se il padre viene
                    // eliminato — cancellata in cascata insieme alla sua riga di RDO
                    // (`purchase_rfq_items.bom_item_id` è ON DELETE CASCADE). Meglio due righe
                    // distinte: quella comprata e quella richiesta dalla distinta.
                    if (existingCommerciale.TryGetValue(key, out int esistenteBom)
                        && c.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM bom_items WHERE id = @Id AND composition_qty IS NOT NULL",
                            new { Id = esistenteBom }) > 0)
                    {
                        c.Execute(@"UPDATE bom_items SET
                                    quantity = quantity + @Add,
                                    parent_bom_item_id = COALESCE(parent_bom_item_id, @ParentRowId),
                                    composition_qty = COALESCE(composition_qty, @CompQty),
                                    updated_at = NOW(), updated_by = @UpdatedBy
                                    WHERE id = @Id",
                            new { Add = child.Quantity * parentQty, ParentRowId = padreBom, CompQty = (decimal)child.Quantity, Id = esistenteBom, UpdatedBy = createdByComp });
                        result.Updated++;
                    }
                    else
                    {
                        // Codice commerciale, Cod. ATEC e link al catalogo: la regola sta in
                        // RisolviArticoloCommerciale, che sa cosa può finire in atec_code.
                        var art = RisolviArticoloCommerciale(c, child);
                        int nuovoBom = c.ExecuteScalar<int>(@"
                        INSERT INTO bom_items
                            (project_id, catalog_item_id, part_number, description, unit, quantity, unit_cost,
                             supplier_id, manufacturer, item_status, requested_by, danea_ref,
                             destination, destination_spec, notes, ddp_type, atec_code,
                             parent_bom_item_id, composition_qty, created_by, updated_at)
                        VALUES
                            (@ProjectId, @CatalogItemId, @PartNumber, @Description, 'PZ', @Quantity, @UnitCost,
                             @SupplierId, '', 'DO', @RequestedBy, '',
                             '', '', '', 'COMMERCIAL', NULLIF(@AtecCode,''),
                             @ParentRowId, @CompQty, @CreatedBy, NOW());
                        SELECT LAST_INSERT_ID()", new
                        {
                            ProjectId = id,
                            art.CatalogItemId,
                            art.PartNumber,
                            AtecCode = art.AtecCode,
                            Description = child.Descr,
                            Quantity = child.Quantity * parentQty,
                            // Col prezzo dell'articolo Danea si resta omogenei al resto della
                            // distinta commerciale; senza articolo vale quello del Codex.
                            UnitCost = art.UnitCost ?? child.PrezzoForn,
                            // Il fornitore del Codex è testo, qui serve la FK: stessa regola
                            // di aggancio della migrazione M105, in un posto solo.
                            SupplierId = art.SupplierId ?? FornitoreLookup.TrovaPerNome(c, child.Fornitore),
                            RequestedBy = req.RequestedBy,
                            ParentRowId = padreBom,
                            CompQty = (decimal)child.Quantity,
                            CreatedBy = createdByComp
                        });
                        existingCommerciale[key] = nuovoBom;
                        result.Added++;
                        result.AddedCommerciale++;
                    }
                    continue;
                }

                // ── Componente d'OFFICINA (1xx e gruppi annidati) ───────────────────────
                if (existingByCode.TryGetValue(key, out int existingId))
                {
                    // Riga già in distinta: somma la quantità e, se libera, la collega al padre
                    // (COALESCE: non ruba righe già collegate a un altro padre).
                    c.Execute(@"UPDATE ddp_officina_items SET
                                quantity = quantity + @Add,
                                parent_officina_item_id = COALESCE(parent_officina_item_id, @ParentRowId),
                                composition_qty = COALESCE(composition_qty, @CompQty),
                                updated_at = NOW(), updated_by = @UpdatedBy
                                WHERE id = @Id",
                        new { Add = child.Quantity * parentQty, ParentRowId = parentRowId, CompQty = (decimal)child.Quantity, Id = existingId, UpdatedBy = createdByComp });
                    result.Updated++;
                }
                else
                {
                    int newId = c.ExecuteScalar<int>(@"
                    INSERT INTO ddp_officina_items
                        (project_id, part_number, description, quantity, unit_cost, material,
                         treatment, supplier_name, item_status, requested_by, danea_ref,
                         date_needed, destination, destination_spec, notes,
                         parent_officina_item_id, composition_qty, created_by, updated_at)
                    VALUES
                        (@ProjectId, @PartNumber, @Description, @Quantity, @UnitCost, '',
                         '', @SupplierName, 'DO', @RequestedBy, '',
                         NULL, '', '', '',
                         @ParentRowId, @CompQty, @CreatedBy, NOW());
                    SELECT LAST_INSERT_ID()", new
                    {
                        ProjectId = id,
                        PartNumber = CodexListItem.FormatCodice(child.RawCode),
                        Description = child.Descr,
                        Quantity = child.Quantity * parentQty,
                        UnitCost = child.PrezzoForn,
                        SupplierName = child.Fornitore,
                        RequestedBy = req.RequestedBy,
                        ParentRowId = parentRowId,
                        CompQty = (decimal)child.Quantity,
                        CreatedBy = createdByComp
                    });
                    existingByCode[key] = newId;
                    result.Added++;
                    result.AddedOfficina++;
                }
            }

            // #135: i 101 appena entrati possono avere una derivazione — i loro grezzi 201
            // vanno in DDP Commerciale. Si fa DOPO tutto l'import, una volta sola: il ricalcolo
            // ragiona sulla commessa intera, non sulla singola riga.
            var grezziImport = GrezziDerivazione.Sincronizza(c, id, createdByComp, req.RequestedBy);

            if (result.Added + result.Updated > 0)
            {
                NotifyWorkRequestsChanged("create", id);
                // Due griglie toccate, due notifiche: chi ha aperta solo la Commerciale non
                // riceverebbe nulla da un avviso marcato OFFICINA e vedrebbe righe vecchie.
                NotifyDdpChange(id, conn, "create", 0, "OFFICINA");
                if (parentBomId != null || !grezziImport.NienteDaFare)
                    NotifyDdpChange(id, conn, "create", 0, "COMMERCIAL");
            }
            string mult = parentQty != 1 ? $" ×{parentQty:0.###}" : "";
            string dove = result.AddedCommerciale > 0 && result.AddedOfficina > 0
                ? $" ({result.AddedOfficina} in officina, {result.AddedCommerciale} in commerciale)"
                : result.AddedCommerciale > 0 ? " (in commerciale)" : "";
            return Ok(ApiResponse<OfficinaImportCompositionResult>.Ok(result,
                $"Composizione importata{mult}: {result.Added} nuove righe{dove}, {result.Updated} aggiornate"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<OfficinaImportCompositionResult>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Come si presenta in DDP Commerciale un componente che arriva dalla composizione Codex.
    ///
    /// <para>La regola vera — che cosa finisce in «Codice» e in «Cod. ATEC», e quando si può
    /// già dire da chi si compra — vive in <see cref="ArticoloDaCodex"/>. È uscita di qui con
    /// la #135, che la chiama dal secondo posto in cui serve: il grezzo 201 di un 101
    /// (<see cref="GrezziDerivazione"/>). Qui resta solo l'adattatore per il figlio di
    /// composizione, che tiene la firma con cui i chiamanti la conoscono.</para>
    /// </summary>
    private static (string PartNumber, string AtecCode, int? CatalogItemId, int? SupplierId, decimal? UnitCost)
        RisolviArticoloCommerciale(IDbConnection c, OfficinaCompositionChild child)
    {
        ArticoloDaCodex.Esito e = ArticoloDaCodex.Risolvi(c, child.RawCode, child.CodiceNuovo);
        return (e.PartNumber, e.AtecCode, e.CatalogItemId, e.SupplierId, e.UnitCost);
    }

    /// <summary>
    /// Codici già presenti in una distinta di commessa, indicizzati senza punti (il
    /// <c>part_number</c> è salvato col punto, il Codex no). Prima riga vince: se lo stesso
    /// codice compare più volte, la composizione somma sulla più vecchia.
    /// </summary>
    private static Dictionary<string, int> LeggiIndiceCodici(IDbConnection c, string tabella, int projectId)
    {
        var indice = new Dictionary<string, int>();
        foreach ((int rowId, string? partNumber) in c.Query<(int, string?)>(
            $"SELECT id, part_number FROM {tabella} WHERE project_id = @Id ORDER BY id", new { Id = projectId }))
        {
            string key = (partNumber ?? "").Replace(".", "").Trim();
            if (key.Length > 0 && !indice.ContainsKey(key)) indice[key] = rowId;
        }
        return indice;
    }

    /// <summary>Figlio della composizione Codex proiettato per l'import in DDP officina.</summary>
    private sealed class OfficinaCompositionChild
    {
        public string RawCode { get; set; } = "";
        /// <summary>Codice NUOVO della ricodifica commerciale (201/211/221); vuoto = non ricodificato.</summary>
        public string CodiceNuovo { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Fornitore { get; set; } = "";
        public decimal PrezzoForn { get; set; }
        public int Quantity { get; set; }
        public bool IsCatalog { get; set; }
    }

    [RequireFeature("project.ddp_officina", "nav.gestore_ddp", "nav.officina_inbox", "project.ddp_commerciale")]
    [HttpPut("{id}/ddp-officina/{itemId}")]
    public IActionResult UpdateOfficinaItem(int id, int itemId, [FromBody] OfficinaItemSaveRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();

            // Concorrenza ottimistica (rete di sicurezza anche col real-time): se il client invia
            // la versione vista (ExpectedUpdatedAt) e la riga è cambiata nel frattempo → 409, niente lost update.
            if (req.ExpectedUpdatedAt.HasValue)
            {
                DateTime? current = c.ExecuteScalar<DateTime?>(
                    "SELECT updated_at FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                    new { ItemId = itemId, Id = id });
                if (current.HasValue && Math.Abs((current.Value - req.ExpectedUpdatedAt.Value).TotalSeconds) > 1)
                    return Conflict(ApiResponse<DateTime?>.Fail(
                        "Riga modificata da un altro utente nel frattempo. Ricarica e riprova."));
            }

            // Leggi stato, quantità, order_date e delivered_at precedenti
            // (notifiche + «comanda il padre» + auto IO / Consegnato il).
            (string? ItemStatus, decimal Quantity, int? ParentOfficinaItemId, DateTime? OrderDate, DateTime? DeliveredAt) before =
                c.QueryFirstOrDefault<(string?, decimal, int?, DateTime?, DateTime?)>(
                @"SELECT item_status, quantity, parent_officina_item_id, order_date, delivered_at
                  FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            string? oldStatus = before.ItemStatus;

            // Matrice degli avanzamenti di stato (v7, tipo OFFICINA): il server rifiuta le
            // transizioni non ammesse (la UI mostra solo quelle valide, ma qui si coprono
            // client vecchi e modifiche concorrenti). Gli auto-avanzamenti successivi
            // (pezzi prodotti → PAR/DISP) sono transizioni di sistema già coerenti con la matrice.
            string? transitionError = DdpTransitionService.Validate(c, DdpTransitionService.TypeOfficina, oldStatus, req.ItemStatus, _cache, PuoScavalcareMatriceDdp());
            if (transitionError != null)
                return BadRequest(ApiResponse<DateTime?>.Fail(transitionError));

            // Se è un componente figlio, blocca la modifica della quantità ripristinando quella precedente.
            if (before.ParentOfficinaItemId.HasValue)
            {
                req.Quantity = before.Quantity;
            }

            // Quantità modificabile solo in «Da Ordinare» (o tornando in DO nella stessa save).
            if (req.Quantity != before.Quantity
                && !string.Equals(oldStatus, "DO", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(req.ItemStatus, "DO", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<DateTime?>.Fail(
                    "La quantità è modificabile solo in stato Da Ordinare."));
            }

            // Auto-fill data ordine: al passaggio a IO, se ancora NULL (body e DB) → oggi (one-shot).
            if (string.Equals(req.ItemStatus, "IO", StringComparison.OrdinalIgnoreCase)
                && !req.OrderDate.HasValue
                && !before.OrderDate.HasValue)
            {
                req.OrderDate = DateTime.Today;
            }
            else if (!req.OrderDate.HasValue && before.OrderDate.HasValue)
            {
                // Non cancellare la data già salvata se il client non la rimanda.
                req.OrderDate = before.OrderDate;
            }

            // Pezzi prodotti: clamp 0…quantity; auto PAR/DISP (salvo ANN).
            // DISP = chiusura positiva unica della matrice v7 (assorbe il vecchio COS).
            if (req.QuantityProduced < 0) req.QuantityProduced = 0;
            if (req.QuantityProduced > req.Quantity) req.QuantityProduced = req.Quantity;
            if (!string.Equals(req.ItemStatus, "ANN", StringComparison.OrdinalIgnoreCase))
            {
                if (req.Quantity > 0 && req.QuantityProduced >= req.Quantity)
                    req.ItemStatus = "DISP";
                else if (req.QuantityProduced > 0 && req.QuantityProduced < req.Quantity)
                    req.ItemStatus = "PAR";
            }

            // Auto-fill «Consegnato il» (#82): solo al PASSAGGIO in chiusura positiva
            // (CON / COS / DISP), se ancora vuota → oggi. Se la riga è già chiusa non si
            // reimpone (così si può svuotare o correggere a mano senza che torni da sola).
            bool closingPositive =
                string.Equals(req.ItemStatus, "CON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(req.ItemStatus, "COS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(req.ItemStatus, "DISP", StringComparison.OrdinalIgnoreCase);
            bool wasClosing =
                string.Equals(oldStatus, "CON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(oldStatus, "COS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(oldStatus, "DISP", StringComparison.OrdinalIgnoreCase);
            if (closingPositive && !wasClosing
                && !req.DeliveredAt.HasValue && !before.DeliveredAt.HasValue)
            {
                req.DeliveredAt = DateTime.Today;
            }

            req.Id = itemId;
            req.ProjectId = id;
            c.Execute(@"
            UPDATE ddp_officina_items SET
                quantity = @Quantity, quantity_produced = @QuantityProduced,
                -- Stessa regola di work_type: NULL = il chiamante non gestisce il campo →
                -- ore invariate. Sono parecchi i punti che salvano una riga officina
                -- (picker Codex, inbox officina, celle in griglia) e nessuno di loro deve
                -- cancellare le ore per il fatto di non conoscerle. Per azzerarle si scrive 0.
                work_hours = COALESCE(@WorkHours, work_hours),
                -- Stessa regola: la tariffa oraria scelta (#87) resta com'è se il chiamante non la manda.
                hourly_rate = COALESCE(@HourlyRate, hourly_rate),
                -- Stessa regola, e in più è un dato SENSIBILE (§12.3): null = costo invariato.
                -- Un mittente senza il micro prezzi non può cancellare un costo vero.
                unit_cost = COALESCE(@UnitCost, unit_cost), material = @Material,
                treatment = @Treatment, supplier_id = @SupplierId, supplier_name = @SupplierName,
                item_status = @ItemStatus,
                -- NULL = il chiamante non gestisce il campo → classificazione invariata.
                work_type = COALESCE(@WorkType, work_type),
                -- «Inserito da» (#61): stessa regola, NULL = autore invariato.
                requested_by = COALESCE(@RequestedBy, requested_by),
                danea_ref = @DaneaRef,
                date_needed = @DateNeeded,
                order_date = @OrderDate, delivered_at = @DeliveredAt,
                destination = @Destination, destination_spec = @DestinationSpec,
                notes = @Notes, updated_at = NOW(),
                -- Firma dell'ultima modifica (#114): la card «DDP Commesse» della Dashboard
                -- elenca le distinte toccate DA ALTRI, e senza autore non saprebbe distinguerle.
                updated_by = @UpdatedBy
            WHERE id = @Id AND project_id = @ProjectId", ConFirma(req));

            // Cronistoria della riga di officina (stessa logica della distinta commerciale).
            DdpItemEvents.Registra(c, DdpItemEvents.Officina, itemId, id,
                oldStatus, req.ItemStatus, User, log: _logger);

            if (req.UpdateCodexPrice == true && req.UnitCost.HasValue && !string.IsNullOrWhiteSpace(req.PartNumber))
            {
                var cleanPartNumber = req.PartNumber.Replace(".", "").Trim();
                c.Execute(
                    "UPDATE codex_items SET prezzo_forn = @Price, data = NOW() WHERE codice = @Code AND (prezzo_forn IS NULL OR prezzo_forn = 0)",
                    new { Price = req.UnitCost, Code = cleanPartNumber });
            }

            // «Comanda il padre»: se la quantità è cambiata, i componenti importati dalla
            // composizione Codex (righe collegate a questa) seguono con delta =
            // composition_qty × ΔQtà, mai sotto zero. Escluse le righe negli stati A9
            // (fuori dai conteggi, quantità bloccata anche in UI).
            if (!string.IsNullOrEmpty(oldStatus) && req.Quantity != before.Quantity)
            {
                int? firmaFiglio = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;
                // #119: la propagazione attraversa le due DDP e allinea la copia gemella
                // dell'intestazione. Regola in un posto solo, condivisa con la commerciale.
                var toccati = ComposizioneDdp.PropagaQuantita(
                    c, id, req.PartNumber, req.Quantity - before.Quantity, req.Quantity,
                    firmaFiglio, ComposizioneDdp.StatiEsclusi(c));
                if (toccati.Count > 0) NotifyWorkRequestsChanged("update", id);
                NotifyDdpChange(id, conn, "update", 0, "COMMERCIAL");
            }

            // Trigger notifica se lo stato è cambiato (solo se commessa ACTIVE)
            string projStatus = c.ExecuteScalar<string?>(
                "SELECT status FROM projects WHERE id = @Id", new { Id = id }) ?? "";
            if (!string.IsNullOrEmpty(oldStatus) && oldStatus != req.ItemStatus && projStatus == "ACTIVE")
            {
                try
                {
                    string projCode = c.ExecuteScalar<string?>(
                        "SELECT code FROM projects WHERE id = @Id", new { Id = id }) ?? "";
                    int currentEmpId = GetCurrentEmployeeId();

                    string severity = req.ItemStatus switch
                    {
                        "DISP" => "SUCCESS",  // DISPONIBILE / CONSEGNATO (chiusura positiva v7)
                        "ANN" => "WARNING",   // ANNULLATO
                        "IO" => "INFO",       // IN ORDINE
                        _ => "INFO"
                    };

                    string title = $"Cambio stato DDP Officina — {projCode}";
                    string msg = $"Stato modificato da {DdpStatusMap.ToLabel(oldStatus)} a {DdpStatusMap.ToLabel(req.ItemStatus)}";

                    List<int> recipients = _notif.GetProjectPmIds(id);
                    recipients.AddRange(_notif.GetAcqEmployeeIds());
                    recipients.Remove(currentEmpId);

                    if (recipients.Count > 0)
                        _notif.Create("DDP_STATUS_CHANGED", severity, title, msg, "BOM_OFFICINA", itemId, id, currentEmpId, recipients);
                }
                catch { /* non bloccare l'update per errore notifica */ }
            }

            // Avvisa chi ha aperto Lavorazioni Officine (la riga si vede lì).
            NotifyWorkRequestsChanged("update", id);

            // #135: quantità e stato di un 101 muovono il suo grezzo 201 in DDP Commerciale.
            // Anche il solo cambio di stato conta: passando a uno stato A9 la riga esce dai
            // conteggi, e con lei il materiale che chiedeva.
            var grezzi = GrezziDerivazione.Sincronizza(
                c, id, GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null, req.RequestedBy);
            if (!grezzi.NienteDaFare) NotifyDdpChange(id, conn, "update", 0, "COMMERCIAL");

            // Real-time: avvisa gli altri che guardano la distinta officina di questa commessa.
            NotifyDdpChange(id, conn, "update", itemId, "OFFICINA");

            // Ritorna la nuova versione così il client riallinea il proprio token di concorrenza.
            DateTime? newTs = c.ExecuteScalar<DateTime?>(
                "SELECT updated_at FROM ddp_officina_items WHERE id = @Id", new { Id = itemId });
            return Ok(ApiResponse<DateTime?>.Ok(newTs, "Aggiornato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DateTime?>.Fail(ex.Message));
        }
    }

    // Cancellazione DEFINITIVA della riga (diversa dall'annullo, che è un cambio stato):
    // serve la chiave `action.delete_ddp_row`, esposta nel menu riga della griglia officina.
    // I due [RequireFeature] sono filtri distinti e si sommano in AND: devi poter vedere la
    // distinta (una delle tre chiavi di sezione) E avere la concessione di cancellazione.
    // Mettere la chiave nuova dentro il primo attributo l'avrebbe messa in OR con le altre,
    // cioè avrebbe aperto il cancello invece di stringerlo.
    // «Comanda il padre» anche qui: eliminare un padre elimina in cascata i componenti
    // importati dalla sua composizione (ognuno con la propria bozza in staging).
    [RequireFeature("project.ddp_officina", "nav.gestore_ddp", "nav.officina_inbox")]
    [RequireFeature("action.delete_ddp_row")]
    [HttpDelete("{id}/ddp-officina/{itemId}")]
    [Authorize]
    public IActionResult DeleteOfficinaItem(int id, int itemId, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            // Blocca la cancellazione diretta dei figli.
            var isChild = c.ExecuteScalar<int?>(
                "SELECT parent_officina_item_id FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });
            if (isChild.HasValue)
                return BadRequest(ApiResponse<bool>.Fail("Impossibile eliminare direttamente un componente figlio. Eliminare il padre."));

            // #119: i componenti del gruppo stanno su DUE tabelle, e l'intestazione esiste in
            // entrambe le griglie. Portarsi via solo i figli d'officina lascerebbe metà
            // composizione viva sotto un padre che non c'è più.
            string partNumber = c.ExecuteScalar<string?>(
                "SELECT part_number FROM ddp_officina_items WHERE id = @ItemId", new { ItemId = itemId }) ?? "";
            (int figliOfficina, int figliCommerciale) =
                ComposizioneDdp.EliminaComponenti(c, id, partNumber, itemId);

            // Niente bozze da portarsi dietro (#83): la riga eliminata sparisce da Lavorazioni
            // Officine perché non esiste più. Le eventuali lavorazioni storiche collegate
            // restano, scollegate dalla FK ON DELETE SET NULL.
            c.Execute("DELETE FROM ddp_officina_items WHERE id = @ItemId AND project_id = @Id",
                new { ItemId = itemId, Id = id });

            // #135: sparito il 101, sparisce anche il grezzo che chiedeva — a meno che nel
            // frattempo sia stato messo in RDO o ordinato, e allora resta a chi compra
            // (sganciato dalla derivazione, vedi GrezziDerivazione).
            var grezzi = GrezziDerivazione.Sincronizza(
                c, id, GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null);

            NotifyWorkRequestsChanged("delete", id);
            NotifyDdpChange(id, conn, "delete", itemId, "OFFICINA");
            if (figliCommerciale > 0 || !grezzi.NienteDaFare)
                NotifyDdpChange(id, conn, "delete", 0, "COMMERCIAL");

            int figli = figliOfficina + figliCommerciale;
            return Ok(ApiResponse<bool>.Ok(true, figli > 0
                ? $"Eliminata riga + {figli} componenti collegati"
                : "Eliminato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// La Dashboard della commessa: <c>revenue</c>, budget, costo consuntivo, materiali,
    /// trasferta e totale. È la stessa roba che <see cref="GetAll"/> tiene dietro
    /// <c>nav.commesse</c>, quindi sta dietro la stessa chiave.
    ///
    /// <para>🪤 Trovata da una revisione il 20/08, un'ora dopo aver chiuso l'elenco: qui non
    /// c'era nessun <c>[RequireFeature]</c>, e gli attributi di classe non coprono
    /// (<c>RequireProjectWritable</c> esce sulle letture, <c>RequireProjectVisible</c> filtra
    /// solo le bozze). Chiusa la porta principale, i soldi uscivano da quella di servizio: un
    /// ciclo su <c>{id}</c> e si riprendeva il valore di ogni commessa, margine compreso.
    /// La lezione: quando si chiude un dato, si chiudono TUTTE le strade che lo portano, non
    /// quella da cui lo si è visto passare.</para>
    ///
    /// <para>Unico chiamante: <c>ProjectDetailsSection</c> dentro <c>CommessePage</c>, già
    /// dietro <c>nav.commesse</c> — chiuderla non toglie niente a nessuno che l'usasse.</para>
    /// </summary>
    [HttpGet("{id}/dashboard")]
    [RequireFeature("nav.commesse")]
    public IActionResult GetDashboard(int id)
    {
        try
        {
            ProjectDashboardData? data = BuildProjectDashboard(id);
            if (data == null) return NotFound(ApiResponse<string>.Fail("Commessa non trovata"));
            return Ok(ApiResponse<ProjectDashboardData>.Ok(data));
        }
        catch (Exception ex)
        {
            // Mai un 500 nudo: il client mostrerebbe solo "Internal Server Error" e il motivo
            // resterebbe sepolto nel log del server. Meglio il messaggio vero in pagina.
            _logger.LogError(ex, "[Dashboard] Commessa {ProjectId}: calcolo dashboard fallito", id);
            return Ok(ApiResponse<ProjectDashboardData>.Fail($"Dashboard non disponibile: {ex.Message}"));
        }
    }

    /// <summary>Calcola la dashboard della commessa. <c>null</c> se la commessa non esiste.</summary>
    private ProjectDashboardData? BuildProjectDashboard(int id)
    {
        using var c = _db.Open();

        // Info commessa + cliente + PM
        var data = c.QueryFirstOrDefault<ProjectDashboardData>(@"
            SELECT p.code AS Code, p.title AS Title, p.status, p.priority,
                   p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.budget_total AS BudgetTotal,
                   COALESCE((SELECT SUM(pa.planned_hours) FROM phase_assignments pa JOIN project_phases pp2 ON pp2.id = pa.project_phase_id WHERE pp2.project_id = p.id), 0) AS BudgetHoursTotal,
                   p.revenue AS Revenue, p.description AS Description,
                   p.server_path AS ServerPath, p.notes AS Notes,
                   COALESCE(cust.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(pm.first_name,' ',pm.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cust ON cust.id = p.customer_id
            LEFT JOIN employees pm ON pm.id = p.pm_id
            WHERE p.id = @Id", new { Id = id });

        if (data == null) return null;

        // Ore lavorate totali + costo consuntivo
        // Fallback robusto: priorità is_primary, poi is_responsible, poi qualsiasi reparto (MIN id).
        // Extra Lavoro (#39): fuori le ore che il PM ha tolto dalla contabilità della commessa,
        // o questa card direbbe un numero e il Bilancio un altro — sulla stessa schermata.
        var totals = c.QueryFirstOrDefault<dynamic>($@"
    SELECT COALESCE(SUM(te.hours), 0) AS HoursWorked,
           COALESCE(SUM(te.hours * COALESCE(d.hourly_cost, 0)), 0) AS CostWorked
    FROM timesheet_entries te
    JOIN employees e ON e.id = te.employee_id
    JOIN project_phases pp ON pp.id = te.project_phase_id
    {ProjectEconomics.ExtraWorkJoin}
    LEFT JOIN (
        SELECT employee_id, department_id,
               ROW_NUMBER() OVER (PARTITION BY employee_id
                                  ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
        FROM employee_departments
    ) ed ON ed.employee_id = e.id AND ed.rn = 1
    LEFT JOIN departments d ON d.id = ed.department_id
    WHERE pp.project_id = @Id AND {ProjectEconomics.ExtraWorkCounts}", new { Id = id });

        data.HoursWorked = (decimal)(totals?.HoursWorked ?? 0m);
        data.CostWorked = (decimal)(totals?.CostWorked ?? 0m);

        // Costo materiali DDP — esclude solo gli stati «esclusi da totale» (aggregazione A9);
        // i materiali consegnati (A2) SONO un costo reale e restano nel totale.
        string[] ddpExcluded = DdpAggregationSet.Load(c, "A9", _cache);
        // La somma commerciale NON si ricopia: la fa ProjectEconomics, che porta con sé le due
        // regole del Bilancio — dedup dei padri di composizione (#119) e quota dei grezzi
        // (#135). Fino al 28/08/2026 qui c'era una terza copia della query, senza la dedup:
        // su ogni commessa con un gruppo Codex importato questa card sommava anche
        // l'intestazione ai suoi figli e diceva un materiale più alto del Bilancio.
        decimal materialCostCommercial =
            ProjectEconomics.GetCommercialMaterialCost(c, id, ddpExcluded);

        decimal materialCostOfficina = c.ExecuteScalar<decimal>($@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM ddp_officina_items
            WHERE project_id = @Id AND COALESCE(item_status,'') NOT IN @Excluded
              AND {ProjectEconomics.OfficinaParentDedup}",
            new { Id = id, Excluded = ddpExcluded });

        data.MaterialCostCommercial = materialCostCommercial;
        data.MaterialCostOfficina = materialCostOfficina;
        data.MaterialCost = materialCostCommercial + materialCostOfficina;

        // La trasferta a consuntivo fa parte del costo totale: senza, il MARGINE del tab
        // Dettagli risultava più alto della Redditività del conto economico, che la include.
        // Stessa regola del conto economico e di /bilancio: se la trasferta è compilata a righe
        // (blocco 6) il costo lo dice il suo foglio, altrimenti il valore digitato a mano.
        data.TravelCost = c.ExecuteScalar<decimal?>(
            $"SELECT {TravelPlanService.ActualTravelCostSql} FROM projects p WHERE p.id=@Id",
            new { Id = id }) ?? 0;
        data.TotalCost = data.CostWorked + data.MaterialCost + data.TravelCost;

        // Conteggio fasi
        var phaseCounts = c.QueryFirstOrDefault<dynamic>(@"
            -- Le fasi spente (#51) stanno fuori dall'elenco e dal Timesheet: contarle
            -- al denominatore dell'avanzamento vorrebbe dire chiedere di completare
            -- fasi che nessuno vede più.
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN status='COMPLETED' THEN 1 ELSE 0 END) AS Completed
            FROM project_phases WHERE is_off = 0 AND project_id = @Id", new { Id = id });

        data.TotalPhases = (int)(phaseCounts?.Total ?? 0);
        data.CompletedPhases = (int)(phaseCounts?.Completed ?? 0);

        // Riepilogo per reparto — 3 livelli: Preventivate / Assegnate / Lavorate
        // Ore preventivate: le ore della sezione si SPEZZANO sui reparti collegati allo
        // snapshot di sezione (fallback al template). Prima il JOIN le ripeteva intere su
        // ogni reparto (segnalazione #74): Prev MEC+INS sulla stessa sezione valeva 2×.
        var costingByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT d.code, d.name,
                   ROUND(SUM(r.work_days * r.hours_per_day / NULLIF(dc.cnt, 0)), 2) AS Hours
            FROM project_cost_resources r
            JOIN project_cost_sections pcs ON pcs.id = r.section_id
            JOIN (
                SELECT section_id, department_id, cnt FROM (
                    SELECT pcsd.project_cost_section_id AS section_id,
                           pcsd.department_id,
                           COUNT(*) OVER (PARTITION BY pcsd.project_cost_section_id) AS cnt
                    FROM project_cost_section_departments pcsd
                ) proj
                UNION ALL
                SELECT pcs2.id AS section_id, cstd.department_id,
                       COUNT(*) OVER (PARTITION BY pcs2.id) AS cnt
                FROM project_cost_sections pcs2
                JOIN cost_section_template_departments cstd
                  ON cstd.section_template_id = pcs2.template_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM project_cost_section_departments x
                    WHERE x.project_cost_section_id = pcs2.id
                )
            ) dc ON dc.section_id = pcs.id
            JOIN departments d ON d.id = dc.department_id
            WHERE pcs.project_id = @Id AND pcs.is_enabled = 1
            GROUP BY d.code, d.name", new { Id = id }).ToList();

        // Ore assegnate (dalle phase_assignments, raggruppate per reparto fase o reparto dipendente).
        // Per il reparto del dipendente: priorità is_primary, poi is_responsible, poi qualsiasi (MIN id).
        var assignedByDept = c.Query<(string Code, string Name, decimal Hours)>(@"
            SELECT COALESCE(d.code, ed.code, 'TRASV') AS code,
                   COALESCE(d.name, ed.name, 'Trasversale') AS name,
                   SUM(pa.planned_hours) AS Hours
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN departments d ON d.id = pp.department_id
            LEFT JOIN (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) empd ON empd.employee_id = pa.employee_id AND empd.rn = 1
            LEFT JOIN departments ed ON ed.id = empd.department_id
            WHERE pp.project_id = @Id
            GROUP BY COALESCE(d.code, ed.code, 'TRASV'), COALESCE(d.name, ed.name, 'Trasversale')", new { Id = id }).ToList();

        // Fasi, completamento, ore lavorate, materiali
        // Extra Lavoro (#39): le ore tolte dalla commessa escono anche da qui, o il grafico
        // «Lavorato per reparto» non somma più al totale della card «Ore totali».
        var phasesByDept = c.Query<DeptSummary>($@"
            SELECT dept_code AS DepartmentCode, dept_name AS DepartmentName,
                   SUM(HoursWorked) AS HoursWorked,
                   SUM(TotalPhases) AS TotalPhases, SUM(CompletedPhases) AS CompletedPhases,
                   SUM(MaterialCost) AS MaterialCost
            FROM (
                -- Fasi con reparto
                SELECT COALESCE(d.code, 'TRASV') AS dept_code,
                       COALESCE(d.name, 'Trasversale') AS dept_name,
                       COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                                 {ProjectEconomics.ExtraWorkJoin}
                                 WHERE te.project_phase_id = pp.id
                                   AND {ProjectEconomics.ExtraWorkCounts}), 0) AS HoursWorked,
                       -- Le fasi spente restano fuori dal conteggio, come nella card
                       -- «Avanzamento»: le ore però ci sono ancora, e restano contate.
                       CASE WHEN pp.is_off = 1 THEN 0 ELSE 1 END AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' AND pp.is_off = 0 THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                LEFT JOIN departments d ON d.id = pp.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NOT NULL

                UNION ALL

                -- Fasi senza department_id (casi tipici in produzione): non usare MIN(department_id)
                -- della sezione — con sezioni multi-reparto scaricava tutto sul primo id
                -- (segnalazione #74: «Installazione elettrica» di Tomasi/INS finiva in MEC).
                -- Ordine: reparto primario del dipendente SE è tra quelli della sezione;
                -- altrimenti il primario comunque; altrimenti il primo della sezione; altrimenti TRASV.
                SELECT COALESCE(d_match.code, ed.code, dsec.code, 'TRASV') AS dept_code,
                       COALESCE(d_match.name, ed.name, dsec.name, 'Trasversale') AS dept_name,
                       te.hours AS HoursWorked,
                       0 AS TotalPhases, 0 AS CompletedPhases, 0 AS MaterialCost
                FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                LEFT JOIN (
                    SELECT employee_id, department_id,
                           ROW_NUMBER() OVER (PARTITION BY employee_id
                                              ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                    FROM employee_departments
                ) empd ON empd.employee_id = te.employee_id AND empd.rn = 1
                LEFT JOIN departments ed ON ed.id = empd.department_id
                LEFT JOIN project_cost_sections pcs
                     ON pcs.project_id = pp.project_id
                    AND pcs.template_id = pp.cost_section_template_id
                LEFT JOIN project_cost_section_departments pcsd_match
                     ON pcsd_match.project_cost_section_id = pcs.id
                    AND pcsd_match.department_id = empd.department_id
                LEFT JOIN departments d_match ON d_match.id = pcsd_match.department_id
                LEFT JOIN (
                    SELECT pcsd.project_cost_section_id, MIN(pcsd.department_id) AS department_id
                    FROM project_cost_section_departments pcsd
                    GROUP BY pcsd.project_cost_section_id
                ) firstdept ON firstdept.project_cost_section_id = pcs.id
                LEFT JOIN departments dsec ON dsec.id = firstdept.department_id
                {ProjectEconomics.ExtraWorkJoin}
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
                  AND {ProjectEconomics.ExtraWorkCounts}

                UNION ALL

                -- Fasi senza reparto proprio: il reparto si ricava con la STESSA cascata
                -- del ramo delle ore qui sopra (segnalazione #107), così fasi e ore di
                -- una stessa fase cadono sullo stesso reparto.
                -- Prima finivano tutte su 'TRASV': con l'anagrafica attività di oggi
                -- `project_phases.department_id` nasce NULL (BulkCreate non lo scrive e
                -- `phase_templates.department_id` è vuoto su tutti i template), quindi il
                -- conteggio fasi di OGNI reparto risultava 0/0 e un reparto che in
                -- anagrafica non esiste si prendeva tutte le fasi della commessa, con
                -- zero ore — mentre le ore andavano ai reparti veri.
                -- Ordine: reparto della persona assegnata alla fase se è fra quelli della
                -- sezione; altrimenti il suo reparto principale; altrimenti il primo
                -- reparto della sezione; altrimenti TRASV (fase senza sezione e senza
                -- nessuno assegnato). Una fase conta SEMPRE una volta sola: spezzarla in
                -- frazioni sui reparti della sezione scriverebbe «3,5 fasi su 7».
                SELECT COALESCE(d_match2.code, ed2.code, dsec2.code, 'TRASV') AS dept_code,
                       COALESCE(d_match2.name, ed2.name, dsec2.name, 'Trasversale') AS dept_name,
                       0 AS HoursWorked,
                       CASE WHEN pp.is_off = 1 THEN 0 ELSE 1 END AS TotalPhases,
                       CASE WHEN pp.status='COMPLETED' AND pp.is_off = 0 THEN 1 ELSE 0 END AS CompletedPhases,
                       COALESCE((SELECT SUM(b.quantity * b.unit_cost) FROM bom_items b WHERE b.project_phase_id = pp.id AND COALESCE(b.item_status,'') NOT IN @Excluded), 0) AS MaterialCost
                FROM project_phases pp
                LEFT JOIN (
                    SELECT project_phase_id, employee_id,
                           ROW_NUMBER() OVER (PARTITION BY project_phase_id ORDER BY id) AS rn
                    FROM phase_assignments
                ) pa1 ON pa1.project_phase_id = pp.id AND pa1.rn = 1
                LEFT JOIN (
                    SELECT employee_id, department_id,
                           ROW_NUMBER() OVER (PARTITION BY employee_id
                                              ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                    FROM employee_departments
                ) empd2 ON empd2.employee_id = pa1.employee_id AND empd2.rn = 1
                LEFT JOIN departments ed2 ON ed2.id = empd2.department_id
                LEFT JOIN project_cost_sections pcs2b
                       ON pcs2b.project_id = pp.project_id
                      AND pcs2b.template_id = pp.cost_section_template_id
                LEFT JOIN project_cost_section_departments pcsd_match2
                       ON pcsd_match2.project_cost_section_id = pcs2b.id
                      AND pcsd_match2.department_id = empd2.department_id
                LEFT JOIN departments d_match2 ON d_match2.id = pcsd_match2.department_id
                LEFT JOIN (
                    SELECT pcsd.project_cost_section_id, MIN(pcsd.department_id) AS department_id
                    FROM project_cost_section_departments pcsd
                    GROUP BY pcsd.project_cost_section_id
                ) firstdept2 ON firstdept2.project_cost_section_id = pcs2b.id
                LEFT JOIN departments dsec2 ON dsec2.id = firstdept2.department_id
                WHERE pp.project_id = @Id AND pp.department_id IS NULL
            ) sub
            GROUP BY dept_code, dept_name", new { Id = id, Excluded = ddpExcluded }).ToList();

        // Merge: unisci costing + assigned + fasi in un unico elenco per reparto.
        // Tollerante ai duplicati: due righe con lo STESSO codice reparto (capita con reparti
        // dal codice NULL/vuoto, che ricadono su 'TRASV' insieme alle fasi trasversali) vanno
        // sommate — con ToDictionary sarebbero un'eccezione e quindi tutta la dashboard in errore.
        static string DeptKey(string? code) =>
            string.IsNullOrWhiteSpace(code) ? "TRASV" : code;

        Dictionary<string, DeptSummary> deptMap = new();
        foreach (DeptSummary row in phasesByDept)
        {
            string code = DeptKey(row.DepartmentCode);
            if (deptMap.TryGetValue(code, out DeptSummary? existing))
            {
                existing.HoursWorked += row.HoursWorked;
                existing.TotalPhases += row.TotalPhases;
                existing.CompletedPhases += row.CompletedPhases;
                existing.MaterialCost += row.MaterialCost;
                if (string.IsNullOrWhiteSpace(existing.DepartmentName))
                    existing.DepartmentName = row.DepartmentName ?? code;
            }
            else
            {
                row.DepartmentCode = code;
                row.DepartmentName = string.IsNullOrWhiteSpace(row.DepartmentName) ? code : row.DepartmentName;
                deptMap[code] = row;
            }
        }
        foreach (var (code, name, _) in costingByDept.Concat(assignedByDept))
        {
            string key = DeptKey(code);
            if (!deptMap.ContainsKey(key))
                deptMap[key] = new DeptSummary { DepartmentCode = key, DepartmentName = name ?? key };
        }
        foreach (var (code, _, hours) in costingByDept)
            if (deptMap.TryGetValue(DeptKey(code), out DeptSummary? ds)) ds.CostingHours += hours;
        foreach (var (code, _, hours) in assignedByDept)
            if (deptMap.TryGetValue(DeptKey(code), out DeptSummary? ds)) ds.AssignedHours += hours;

        // BudgetHours = costing come riferimento principale
        foreach (DeptSummary ds in deptMap.Values)
            ds.BudgetHours = ds.CostingHours;

        data.DepartmentSummaries = deptMap.Values.OrderBy(d2 => d2.DepartmentCode).ToList();

        // Ultimi 10 inserimenti timesheet
        data.RecentEntries = c.Query<RecentTimesheetEntry>(@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType,
                   COALESCE(te.notes, '') AS Notes
            FROM timesheet_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.project_id = @Id
            ORDER BY te.work_date DESC, te.id DESC
            LIMIT 10", new { Id = id }).ToList();

        // Tecnici assegnati alle fasi (non dal timesheet).
        // REPARTO = primario della persona (#74): le fasi in produzione hanno spesso
        // department_id NULL, e usare quello della fase lasciava il badge vuoto.
        // «ORE LAV.»: stesse ore del totale della commessa, senza Extra Lavoro (#39).
        data.ActiveTechnicians = c.Query<ActiveTechSummary>($@"
            SELECT CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   COALESCE(ed.code, '') AS DepartmentCode,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             {ProjectEconomics.ExtraWorkJoin}
                             WHERE te.employee_id = e.id
                             AND te.project_phase_id IN (SELECT pp2.id FROM project_phases pp2 WHERE pp2.project_id = @Id)
                             AND {ProjectEconomics.ExtraWorkCounts}), 0) AS TotalHours,
                   COUNT(DISTINCT pa.project_phase_id) AS PhaseCount
            FROM phase_assignments pa
            JOIN employees e ON e.id = pa.employee_id
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            LEFT JOIN (
                SELECT employee_id, department_id,
                       ROW_NUMBER() OVER (PARTITION BY employee_id
                                          ORDER BY is_primary DESC, is_responsible DESC, id) AS rn
                FROM employee_departments
            ) empd ON empd.employee_id = e.id AND empd.rn = 1
            LEFT JOIN departments ed ON ed.id = empd.department_id
            WHERE pp.project_id = @Id
            GROUP BY e.id, e.first_name, e.last_name, ed.code
            ORDER BY e.last_name", new { Id = id }).ToList();

        // ── Ore settimanali (ultime 12 settimane) ────────────────
        // Anche qui senza Extra Lavoro (#39): le barre devono sommare alle «Ore totali».
        data.WeeklyHours = c.Query<WeeklyHoursSummary>($@"
            SELECT YEAR(te.work_date) AS Year,
                   WEEK(te.work_date, 1) AS Week,
                   SUM(te.hours) AS Hours,
                   CONCAT('S', WEEK(te.work_date, 1)) AS WeekLabel
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            {ProjectEconomics.ExtraWorkJoin}
            WHERE pp.project_id = @Id
              AND {ProjectEconomics.ExtraWorkCounts}
              AND te.work_date >= DATE_SUB(CURDATE(), INTERVAL 12 WEEK)
            GROUP BY YEAR(te.work_date), WEEK(te.work_date, 1),
                     CONCAT('S', WEEK(te.work_date, 1))
            ORDER BY Year, Week", new { Id = id }).ToList();

        // ── Gantt fasi ───────────────────────────────────────────
        // Snapshot-aware: LEFT JOIN + fallback pp.name per fasi locali (phase_template_id NULL)
        // Ore per fase senza Extra Lavoro (#39), come già fa PhasesController sulla stessa cifra.
        data.PhaseGantt = c.Query<PhaseGanttItem>($@"
            SELECT pp.id AS PhaseId,
                   COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct,
                   pp.budget_hours AS BudgetHours,
                   COALESCE((SELECT SUM(te.hours) FROM timesheet_entries te
                             {ProjectEconomics.ExtraWorkJoin}
                             WHERE te.project_phase_id = pp.id
                               AND {ProjectEconomics.ExtraWorkCounts}), 0) AS HoursWorked,
                   pp.start_date AS StartDate,
                   pp.end_date AS EndDate,
                   pp.sort_order AS SortOrder
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
            ORDER BY pp.sort_order", new { Id = id }).ToList();

        // ── Scadenze prossime (fasi non completate con end_date) ─
        data.Deadlines = c.Query<UpcomingDeadline>(@"
            SELECT COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS PhaseName,
                   COALESCE(d.code, 'TRASV') AS DepartmentCode,
                   pp.end_date AS Deadline,
                   DATEDIFF(pp.end_date, CURDATE()) AS DaysRemaining,
                   pp.status AS Status,
                   pp.progress_pct AS ProgressPct
            FROM project_phases pp
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            LEFT JOIN departments d ON d.id = pp.department_id
            WHERE pp.project_id = @Id
              AND pp.end_date IS NOT NULL
              AND pp.status NOT IN ('COMPLETED', 'CANCELLED')
            ORDER BY pp.end_date ASC
            LIMIT 10", new { Id = id }).ToList();

        return data;
    }


    // --- UPLOAD FILE ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/upload")]
    public async Task<IActionResult> UploadFile(int id, [FromQuery] string? subPath, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<string>.Fail("Nessun file selezionato."));

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string targetDir = string.IsNullOrEmpty(subPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, subPath));

        if (!IsPathAllowed(serverPath, targetDir))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        LongPathHelper.CreateDirectory(targetDir);

        string filePath = Path.Combine(targetDir, file.FileName);
        if (LongPathHelper.FileExists(filePath))
        {
            string name = Path.GetFileNameWithoutExtension(file.FileName);
            string ext = Path.GetExtension(file.FileName);
            int counter = 1;
            do { filePath = Path.Combine(targetDir, $"{name}_{counter}{ext}"); counter++; }
            while (LongPathHelper.FileExists(filePath));
        }

        using (var stream = LongPathHelper.CreateFileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        NotifyDocumentsChanged(id, "upload");
        return Ok(ApiResponse<string>.Ok(Path.GetFileName(filePath), "File caricato."));
    }

    // --- UPLOAD MULTIPLO ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/upload-multiple")]
    public async Task<IActionResult> UploadMultiple(int id, [FromQuery] string? subPath, List<IFormFile> files)
    {
        if (files == null || !files.Any())
            return BadRequest(ApiResponse<string>.Fail("Nessun file selezionato."));

        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string targetDir = string.IsNullOrEmpty(subPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, subPath));

        if (!IsPathAllowed(serverPath, targetDir))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        LongPathHelper.CreateDirectory(targetDir);

        int count = 0;
        foreach (var file in files)
        {
            string filePath = Path.Combine(targetDir, file.FileName);
            if (LongPathHelper.FileExists(filePath))
            {
                string name = Path.GetFileNameWithoutExtension(file.FileName);
                string ext = Path.GetExtension(file.FileName);
                int counter = 1;
                do { filePath = Path.Combine(targetDir, $"{name}_{counter}{ext}"); counter++; }
                while (LongPathHelper.FileExists(filePath));
            }
            using var stream = LongPathHelper.CreateFileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            count++;
        }

        NotifyDocumentsChanged(id, "upload");
        return Ok(ApiResponse<string>.Ok($"{count} file caricati."));
    }

    // --- CREA SOTTOCARTELLA ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/create-subfolder")]
    public IActionResult CreateSubfolder(int id, [FromBody] SubfolderRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string parentDir = string.IsNullOrEmpty(req.SubPath)
            ? serverPath
            : Path.GetFullPath(Path.Combine(serverPath, req.SubPath));

        if (!IsPathAllowed(serverPath, parentDir))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        string newFolder = Path.Combine(parentDir, req.FolderName);
        if (LongPathHelper.DirectoryExists(newFolder))
            return BadRequest(ApiResponse<string>.Fail("La cartella esiste già."));

        LongPathHelper.CreateDirectory(newFolder);
        NotifyDocumentsChanged(id, "create");
        return Ok(ApiResponse<string>.Ok(req.FolderName, "Cartella creata."));
    }

    // --- RINOMINA FILE/CARTELLA ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/rename")]
    public IActionResult RenameItem(int id, [FromBody] RenameRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string oldPath = Path.GetFullPath(Path.Combine(serverPath, req.OldPath));
        string parentDir = Path.GetDirectoryName(oldPath) ?? serverPath;
        string newPath = Path.Combine(parentDir, req.NewName);

        if (!IsPathAllowed(serverPath, oldPath))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (LongPathHelper.DirectoryExists(oldPath))
        {
            if (LongPathHelper.DirectoryExists(newPath))
                return BadRequest(ApiResponse<string>.Fail("Una cartella con questo nome esiste già."));
            LongPathHelper.MoveDirectory(oldPath, newPath);
        }
        else if (LongPathHelper.FileExists(oldPath))
        {
            if (LongPathHelper.FileExists(newPath))
                return BadRequest(ApiResponse<string>.Fail("Un file con questo nome esiste già."));
            LongPathHelper.MoveFile(oldPath, newPath);
        }
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "rename");
        return Ok(ApiResponse<string>.Ok(req.NewName, "Rinominato."));
    }

    // --- ELIMINA FILE/CARTELLA ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/delete-item")]
    public IActionResult DeleteItem(int id, [FromBody] DeleteItemRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string fullPath = Path.GetFullPath(Path.Combine(serverPath, req.ItemPath));

        if (!IsPathAllowed(serverPath, fullPath))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (Path.GetFullPath(fullPath) == Path.GetFullPath(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Non è possibile eliminare la cartella root."));

        if (LongPathHelper.DirectoryExists(fullPath))
            LongPathHelper.DeleteDirectory(fullPath, true);
        else if (LongPathHelper.FileExists(fullPath))
            LongPathHelper.DeleteFile(fullPath);
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "delete");
        return Ok(ApiResponse<bool>.Ok(true, "Eliminato."));
    }

    // --- SPOSTA FILE/CARTELLA ---
    [RequireFeature("project.documenti")]
    [HttpPost("{id}/move-item")]
    public IActionResult MoveItem(int id, [FromBody] MoveItemRequest req)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        string sourcePath = Path.GetFullPath(Path.Combine(serverPath, req.SourcePath));
        string destDir = Path.GetFullPath(Path.Combine(serverPath, req.DestinationFolder));
        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(destDir, fileName);

        string rootFull = Path.GetFullPath(serverPath);
        if (!sourcePath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ||
            !destDir.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Percorso non valido."));

        if (!LongPathHelper.DirectoryExists(destDir))
            return BadRequest(ApiResponse<string>.Fail("Cartella destinazione non trovata."));

        if (LongPathHelper.DirectoryExists(sourcePath))
        {
            if (LongPathHelper.DirectoryExists(destPath))
                return BadRequest(ApiResponse<string>.Fail("Una cartella con questo nome esiste già nella destinazione."));
            LongPathHelper.MoveDirectory(sourcePath, destPath);
        }
        else if (LongPathHelper.FileExists(sourcePath))
        {
            if (LongPathHelper.FileExists(destPath))
                return BadRequest(ApiResponse<string>.Fail("Un file con questo nome esiste già nella destinazione."));
            LongPathHelper.MoveFile(sourcePath, destPath);
        }
        else
            return NotFound(ApiResponse<string>.Fail("File o cartella non trovato."));

        NotifyDocumentsChanged(id, "move");
        return Ok(ApiResponse<bool>.Ok(true, "Spostato."));
    }

    /// <summary>Quantità editabile sulla DDP commerciale: VER (ingresso) o DO.</summary>
    private static bool IsCommercialQtyEditable(string? status) =>
        string.Equals(status, "VER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DO", StringComparison.OrdinalIgnoreCase);
}