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


/// <summary>
/// Anagrafica commesse: elenco, albero, dettaglio, creazione da template, stati, ricavo,
/// cancellazione. Documenti, distinte DDP, cruscotto e lookup stanno nei controller a fianco
/// (stessa rotta <c>api/projects</c>): il 04/09/2026 questo file era di 3.212 righe e cinque
/// domini. I metodi condivisi sono in <see cref="ProjectsControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura riguarda UNA commessa (l'id sta nella rotta), quindi il cancello si mette
// una volta sola sulla classe: una commessa in bozza, in stand-by o chiusa si consulta ma non si
// modifica, salvo il permesso di scavalco. E una bozza non si VEDE proprio, letture comprese.
[RequireProjectWritable]
[RequireProjectVisible]
public class ProjectsController : ProjectsControllerBase
{
    private readonly ProjectTemplateCopyService _templateCopy;
    private readonly ILogger<ProjectsController> _logger;
    private readonly ProjectWriteGuard _guard;
    public ProjectsController(
        DbService db,
        NotificationService notif,
        ProjectTemplateCopyService templateCopy,
        ILogger<ProjectsController> logger,
        IHubContext<ProjectHub> hub,
        ProjectWriteGuard guard,
        FeatureAccessService access,
        AnagraficheCache cache) : base(db, hub, notif, access, cache)
    {
        _templateCopy = templateCopy;
        _logger = logger;
        _guard = guard;
    }

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
        _hub.Clients.Group(ProjectHub.ProjectsGroup)
            .SendAsync("ProjectsChanged", new ProjectChange
            {
                ProjectId = projectId,
                Action = action,
                Code = code
            }).SenzaAttesa("ProjectsChanged");
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
}
