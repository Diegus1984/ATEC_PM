using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Gate di classe aggiunto con la Fase 2 della segnalazione #63: `nav.timesheet` è a livello 0,
// quindi per i ruoli della gerarchia non cambia NIENTE (lo superano tutti). Serve ai profili a
// lista bianca, dove «l'ufficio acquisti non carica ore» dev'essere una regola vera e non solo
// una voce di menu nascosta: senza questa riga la pagina spariva dal menu ma l'API restava
// aperta a chiunque fosse autenticato.
[ApiController]
[Route("api/timesheet")]
[Authorize]
[RequireFeature("nav.timesheet")]
public class TimesheetController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly ProjectWriteGuard _guard;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ILogger<TimesheetController> _logger;
    public TimesheetController(
        DbService db,
        FeatureAccessService access,
        ProjectWriteGuard guard,
        IHubContext<ProjectHub> hub,
        ILogger<TimesheetController> logger)
    {
        _db = db;
        _access = access;
        _guard = guard;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Ambiente condiviso (segnalazione #49): le ore imputate SONO il consuntivo del Bilancio.
    /// Chi ha aperto «Preventivo vs Consuntivo» di quella commessa deve vedere il numero salire
    /// da solo. Stesso evento di <c>BudgetVsActualController.NotifyBudgetChanged</c>.
    /// </summary>
    private void NotifyBudgetChanged(int projectId)
    {
        if (projectId <= 0) return;
        object payload = new { projectId };
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload);
        _ = _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload);
    }

    /// <summary>
    /// La riga di trasferta è appena nata (o sparita) da un'imputazione di ore: chi ha la
    /// pagina Trasferta aperta deve vederla comparire da sola (#37/#52).
    ///
    /// Serve un evento SUO: la Trasferta ascolta <c>TravelChanged</c>, non <c>BudgetChanged</c>
    /// — mandare solo il secondo lasciava il PM davanti a una tabella ferma mentre il tecnico
    /// scaricava le ore, e la pagina che «si compila da sola» sembrava non compilarsi affatto.
    /// Stesso evento di <c>TravelController.NotifyChanged</c>.
    /// </summary>
    private void NotifyTravelChanged(int projectId)
    {
        if (projectId <= 0) return;
        object payload = new { projectId };
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("TravelChanged", payload);
        _ = _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("TravelChanged", payload);
    }

    /// <summary>Commessa a cui appartiene una fase di commessa.</summary>
    private static int ProjectIdOfPhase(IDbConnection c, int projectPhaseId) =>
        c.ExecuteScalar<int>("SELECT COALESCE(project_id, 0) FROM project_phases WHERE id=@Id",
            new { Id = projectPhaseId });

    // ── Controllo accessi timesheet (anti-IDOR) ─────────────────────────
    // L'employeeId arriva da query/body: senza questo check chiunque potrebbe
    // leggere/scrivere le ore di chiunque. Regola speculare a PermissionEngine:
    //   self  OR  «Ore per chiunque»  OR  «Ore per altri» sul proprio reparto.
    // Due chiavi e non una: chi imputa a chiunque e chi imputa solo dentro il proprio
    // reparto sono due permessi diversi, e una chiave sola avrebbe allargato o ristretto.

    private int CallerId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    private string CallerRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    private bool CanAccessTimesheet(IDbConnection c, int targetEmployeeId)
    {
        int callerId = CallerId();
        if (targetEmployeeId > 0 && targetEmployeeId == callerId) return true;

        string role = CallerRole();
        // Chiave «Ore per chiunque»: si vedono tutti, senza vincolo di reparto.
        if (_access.CanAccessUser(callerId, role, "action.timesheet_any_employee")) return true;

        // Chiave «Ore per altri»: solo dipendenti che condividono un suo reparto.
        if (_access.CanAccessUser(callerId, role, "action.timesheet_for_others") && targetEmployeeId > 0)
        {
            int shared = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM employee_departments ed
                WHERE ed.employee_id = @Target
                  AND ed.department_id IN (SELECT department_id FROM employee_departments WHERE employee_id = @Caller)",
                new { Target = targetEmployeeId, Caller = callerId });
            return shared > 0;
        }
        return false;
    }

    private IActionResult Forbidden() =>
        StatusCode(403, ApiResponse<string>.Fail("Accesso negato: non puoi accedere a questo timesheet."));

    /// <summary>
    /// «Le fasi su cui questa persona può imputare ore»: assegnate a lei **oppure** appartenenti
    /// a una sezione di costo del suo reparto. Da usare con il parametro <c>@EmpId</c> e l'alias
    /// <c>pp</c> per <c>project_phases</c>.
    /// <para>
    /// Segnalazione #57 (10/08/2026). Dal 07/08 il filtro era la sola assegnazione individuale, e
    /// a video si vedeva questo: Paolo aggancia «Capo Cantiere / Coordinatore Attività» alla
    /// sezione del reparto PM in Configurazione Sezioni di Costo, torna nel Timesheet e la
    /// tendina non cambia; Mario Cassano (INS) ne vedeva 2 su 9. La configurazione delle sezioni
    /// **dice già chi lavora dove** (`cost_section_template_departments`, «REPARTI INTERESSATI»
    /// nella pagina): usarla è la risposta alla richiesta di Paolo — «tutte le fasi previste in
    /// configurazione costo, per ciascun profilo, devono essere visibili nel menu a tendina».
    /// </para>
    /// <para>
    /// Le assegnazioni individuali **restano** in OR: chi è assegnato a una fase fuori dal suo
    /// reparto continua a vederla, e nessuno perde una voce che aveva ieri.
    /// I reparti si leggono da entrambe le parti — anagrafica e copia della commessa — perché la
    /// copia è uno **snapshot** fatto alla nascita della commessa: su C260805.500 la sezione
    /// «Coordinamento Attività / Capo Cantiere» aveva ancora QLT/UTE/UTM mentre l'anagrafica
    /// aveva già PM. Guardare solo la copia avrebbe lasciato il difetto identico a prima.
    /// </para>
    /// <para>
    /// Resta fuori solo chi non c'entra: una fase senza sezione di costo la vede unicamente chi
    /// vi è assegnato — non avendo sezione non ha nemmeno un reparto a cui appartenere.
    /// </para>
    /// </summary>
    private const string FiltroFasiDellaPersona = @"
              ( EXISTS (SELECT 1 FROM phase_assignments pa
                        WHERE pa.project_phase_id = pp.id AND pa.employee_id = @EmpId)
                OR EXISTS (SELECT 1 FROM cost_section_template_departments csd
                           JOIN employee_departments ed ON ed.department_id = csd.department_id
                           WHERE csd.section_template_id = pp.cost_section_template_id
                             AND ed.employee_id = @EmpId)
                OR EXISTS (SELECT 1 FROM project_cost_sections pcs
                           JOIN project_cost_section_departments pcsd
                                ON pcsd.project_cost_section_id = pcs.id
                           JOIN employee_departments ed2 ON ed2.department_id = pcsd.department_id
                           WHERE pcs.project_id = pp.project_id
                             AND pcs.template_id = pp.cost_section_template_id
                             AND ed2.employee_id = @EmpId)
                OR EXISTS (SELECT 1 FROM employee_departments ed3
                           WHERE ed3.department_id = pp.department_id
                             AND ed3.employee_id = @EmpId) )";

    [HttpGet("week")]
    public IActionResult GetWeek([FromQuery] int employeeId, [FromQuery] string weekStart)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var start = DateTime.Parse(weekStart);
        var end = start.AddDays(6);

        var entries = c.Query<TimesheetEntryDto>($@"
            SELECT te.id, te.employee_id AS EmployeeId, te.project_phase_id AS ProjectPhaseId,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType, te.notes,
                   CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, {ProjectSorting.OrderBy("p")}",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    /// <summary>Voci timesheet in un intervallo di date (per la vista calendario mese/settimana).</summary>
    [HttpGet("range")]
    public IActionResult GetRange([FromQuery] int employeeId, [FromQuery] string start, [FromQuery] string end)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var startDate = DateTime.Parse(start);
        var endDate = DateTime.Parse(end);

        var entries = c.Query<TimesheetEntryDto>($@"
            SELECT te.id, te.employee_id AS EmployeeId, te.project_phase_id AS ProjectPhaseId,
                   te.work_date AS WorkDate, te.hours, te.entry_type AS EntryType, te.notes,
                   CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            ORDER BY te.work_date, {ProjectSorting.OrderBy("p")}",
            new { EmpId = employeeId, Start = startDate, End = endDate }).ToList();

        return Ok(ApiResponse<List<TimesheetEntryDto>>.Ok(entries));
    }

    /// <summary>Somma ore già registrate per dipendente/giorno (esclude la riga in modifica).</summary>
    [HttpGet("day-total")]
    public IActionResult DayTotal([FromQuery] int employeeId, [FromQuery] string date, [FromQuery] int excludeId = 0)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        DateTime workDate = DateTime.Parse(date).Date;
        decimal sum = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(hours), 0) FROM timesheet_entries
            WHERE employee_id = @Emp AND work_date = @Date AND (@ExcludeId = 0 OR id <> @ExcludeId)",
            new { Emp = employeeId, Date = workDate, ExcludeId = excludeId });
        return Ok(ApiResponse<decimal>.Ok(sum));
    }

    /// <param name="includeProjectId">Commessa da tenere in elenco anche se non è una commessa
    /// vera: serve alla modifica di una registrazione già fatta su un'Altra Attività, che
    /// altrimenti aprirebbe il dialogo con la tendina vuota.</param>
    [HttpGet("projects-for-employee")]
    public IActionResult GetProjectsForEmployee([FromQuery] int employeeId, [FromQuery] int includeProjectId = 0)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        // Stessa regola della tendina delle fasi: solo chi ha «Timesheet su tutte le fasi» vede
        // tutte le commesse attive, gli altri quelle in cui hanno almeno una fase compilabile.
        // Se le due regole divergessero, si sceglierebbe una commessa e si troverebbe l'elenco
        // delle fasi vuoto — o, al contrario, non si troverebbe la commessa in cui si hanno fasi
        // da compilare. La chiave si valuta sul dipendente BERSAGLIO (`employeeId`), non su chi
        // chiama: è il suo timesheet che si sta compilando.
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool vedeTutteLeCommesse = _access.CanAccessUser(employeeId, role, "data.timesheet_all_phases");

        List<TimesheetProjectOption> projects;

        // Segnalazione #86: nella finestra «Registra ore» si scelgono le sole commesse vere.
        // Le Altre Attività a codice libero (INTERNA, SERVICE _ SANGRATO…) restano fuori;
        // resta in elenco quella che si sta modificando, se le ore erano già lì sopra.
        string soloCommesse = $"AND ({ProjectSorting.IsCommessa("p")} OR p.id = @IncludeId)";

        if (vedeTutteLeCommesse)
        {
            // ORDER BY sull'espressione selezionata: con DISTINCT, MySQL in
            // ONLY_FULL_GROUP_BY rifiuta l'ordinamento su p.code perché non è nella
            // SELECT (in produzione la tendina commesse del timesheet dava 500).
            projects = c.Query<TimesheetProjectOption>($@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                WHERE p.status = 'ACTIVE'
                  {soloCommesse}
                ORDER BY Display", new { IncludeId = includeProjectId }).ToList();
        }
        else
        {
            // Le condizioni su `pp` sono le stesse della tendina delle fasi (spente e concluse
            // fuori): una commessa che resta in elenco solo per fasi che poi non si possono
            // scegliere è una commessa che apre una tendina vuota.
            projects = c.Query<TimesheetProjectOption>($@"
                SELECT DISTINCT p.id AS ProjectId, CONCAT(p.code,' - ',p.title) AS Display
                FROM projects p
                JOIN project_phases pp ON pp.project_id = p.id
                WHERE p.status = 'ACTIVE'
                  AND pp.status <> 'COMPLETED' AND pp.is_off = 0
                  AND {FiltroFasiDellaPersona}
                  {soloCommesse}
                ORDER BY Display", new { EmpId = employeeId, IncludeId = includeProjectId }).ToList();
        }

        return Ok(ApiResponse<List<TimesheetProjectOption>>.Ok(projects));
    }

    /// <summary>
    /// Fasi di una commessa che **questa persona può compilare**: quelle assegnate a lei e quelle
    /// del suo reparto secondo la Configurazione Sezioni di Costo (vedi
    /// <see cref="FiltroFasiDellaPersona"/>).
    /// <para>
    /// Fino al 07/08/2026 dal livello PM in su si vedevano TUTTE le fasi della commessa: un
    /// elenco lunghissimo in cui la propria fase andava cercata, e nulla impediva di imputare ore
    /// su una fase di un collega di un altro mestiere. Dal 07/08 al 10/08 il filtro è stato la
    /// sola assegnazione individuale, ed era troppo stretto (#57): le fasi agganciate al proprio
    /// reparto in configurazione non comparivano finché qualcuno non assegnava a mano la persona,
    /// commessa per commessa. Il reparto è la via di mezzo, ed è anche l'unica che l'utente
    /// governa da solo da una pagina sola.
    /// </para>
    /// <para>
    /// Il filtro è sull'impiegato **di cui si sta compilando il timesheet** (<c>employeeId</c>),
    /// non su chi chiama: un PM che compila le ore di un tecnico continua a vedere le fasi di
    /// quel tecnico. Vede tutta la commessa solo chi ha la chiave «Timesheet su tutte le fasi».
    /// </para>
    /// </summary>
    [HttpGet("phases-for-employee")]
    public IActionResult GetPhasesForEmployee([FromQuery] int employeeId, [FromQuery] int projectId)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();

        // La chiave si valuta su CHI riceve le ore (employeeId), non su chi chiama: così un
        // PM che compila il timesheet di un tecnico vede le fasi di quel tecnico.
        string? role = c.QueryFirstOrDefault<string>(
            "SELECT user_role FROM employees WHERE id = @EmpId", new { EmpId = employeeId });
        bool vedeTutteLeFasi = _access.CanAccessUser(employeeId, role, "data.timesheet_all_phases");

        // Snapshot-aware: legge pp.name come fallback per fasi locali (phase_template_id NULL).
        // La sezione di costo (segnalazione #42) è quella scritta sulla riga della fase di
        // commessa. v74: niente più ripiego sul template — la stessa fase di anagrafica può
        // stare su più sezioni, e il template ne indicherebbe una sola, cioè una a caso: nella
        // tendina si vedrebbero due voci uguali sotto la stessa intestazione sbagliata.
        // Ordinamento per sezione: la tendina del timesheet è raggruppata per sezione, e le
        // fasi scollegate finiscono in coda (`ISNULL` prima del sort).
        const string select = @"
                SELECT pp.id AS PhaseId,
                       COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS Display,
                       COALESCE(cs.name, '') AS CostSectionName,
                       COALESCE(cs.section_type, '') AS CostSectionType,
                       COALESCE(csg.name, '') AS CostSectionGroup,
                       -- Colore del gruppo (#105): la tendina delle fasi si tinge come i
                       -- gruppi del Bilancio, e il colore deve arrivare dall'anagrafica —
                       -- una mappa nome-colore scritta nel client si sfaserebbe al primo
                       -- cambio in Conf. sezioni. Stesso grigio di riserva del Bilancio.
                       COALESCE(NULLIF(csg.bg_color,''), '#6B7280') AS CostSectionGroupColor,
                       COALESCE(cs.sort_order, 0) AS CostSectionSort
                FROM project_phases pp
                LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
                LEFT JOIN cost_section_templates cs ON cs.id = pp.cost_section_template_id
                LEFT JOIN cost_section_groups csg ON csg.id = cs.group_id";
        const string orderBy = @"
                ORDER BY cs.id IS NULL, csg.sort_order, cs.sort_order, pp.sort_order";

        // EXISTS e non JOIN: con due assegnazioni della stessa persona sulla stessa fase la
        // JOIN la farebbe comparire due volte nella tendina.
        string filtroAssegnate = vedeTutteLeFasi ? "" : $"\n              AND {FiltroFasiDellaPersona}";

        List<TimesheetPhaseOption> phases = c.Query<TimesheetPhaseOption>(
            // `is_off = 0`: una fase spenta (segnalazione #51) non prende ore nuove. Quelle
            // già imputate restano dove sono e continuano a contare — spegnere nasconde,
            // non cancella.
            $@"{select}
            WHERE pp.project_id = @ProjectId AND pp.status <> 'COMPLETED' AND pp.is_off = 0
            {filtroAssegnate}
            {orderBy}",
            new { EmpId = employeeId, ProjectId = projectId }).ToList();

        return Ok(ApiResponse<List<TimesheetPhaseOption>>.Ok(phases));
    }

    [ScritturaControllataAMano("Le ore arrivano con la FASE, non con la commessa: il cancello si applica dentro l'azione, dopo aver risalito la fase")]
    [HttpPost]
    public IActionResult Save([FromBody] TimesheetSaveRequest req)
    {
        using var c = _db.Open();

        // Anti-IDOR: su update verifica il proprietario reale della riga, su insert l'employeeId richiesto.
        int targetEmp = req.Id > 0
            ? c.ExecuteScalar<int>("SELECT employee_id FROM timesheet_entries WHERE id=@Id", new { req.Id })
            : req.EmployeeId;
        if (!CanAccessTimesheet(c, targetEmp)) return Forbidden();

        // #88: le ore arrivano con la FASE, quindi la commessa si raggiunge solo risalendo.
        // Si controllano ENTRAMBE le commesse coinvolte: quella di destinazione e — se si sta
        // spostando una riga — quella di partenza. Guardare solo la destinazione lascerebbe
        // portare via ore da una commessa chiusa, che è una modifica a tutti gli effetti.
        int commessaDestinazione = ProjectIdOfPhase(c, req.ProjectPhaseId);
        string? blocco = _guard.MotivoDelBlocco(c, commessaDestinazione, User);
        if (blocco == null && req.Id > 0)
        {
            int commessaAttuale = c.ExecuteScalar<int>(@"
                SELECT COALESCE(pp.project_id, 0) FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                WHERE te.id = @Id", new { req.Id });
            if (commessaAttuale != commessaDestinazione)
                blocco = _guard.MotivoDelBlocco(c, commessaAttuale, User);
        }
        if (blocco != null) return StatusCode(403, ApiResponse<int>.Fail(blocco));

        if (req.WorkDate.Date > DateTime.Today)
            return BadRequest(ApiResponse<int>.Fail("Non è possibile registrare o spostare ore in date future."));
        if (req.Hours <= 0 || req.Hours > 24)
            return BadRequest(ApiResponse<int>.Fail("Ogni registrazione deve avere ore maggiori di zero e non oltre 24."));

        // Regola fondamentale: somma giornaliera (altre voci + questa) <= 24h. In modifica la riga corrente è esclusa.
        int excludeId = req.Id > 0 ? req.Id : 0;
        decimal existingDayHours = c.ExecuteScalar<decimal>(@"
            SELECT COALESCE(SUM(hours), 0) FROM timesheet_entries
            WHERE employee_id = @Emp AND work_date = @Date AND (@ExcludeId = 0 OR id <> @ExcludeId)",
            new { Emp = targetEmp, Date = req.WorkDate.Date, ExcludeId = excludeId });
        if (existingDayHours + req.Hours > 24)
        {
            decimal available = Math.Max(0, 24 - existingDayHours);
            return BadRequest(ApiResponse<int>.Fail(
                $"Limite giornaliero 24h: altre registrazioni {existingDayHours:N1}h, massimo consentito per questa voce {available:N1}h."));
        }

        // Fase di PRIMA della modifica: se le ore si spostano su un'altra fase (o su un altro
        // giorno) la trasferta va rigenerata anche di là, o resta una riga senza ore dietro
        // che nessuno segnala. Vedi TravelFromTimesheet.
        int? faseDiPrima = req.Id > 0
            ? c.ExecuteScalar<int?>("SELECT project_phase_id FROM timesheet_entries WHERE id=@Id", new { req.Id })
            : null;

        if (req.Id > 0)
        {
            c.Execute("UPDATE timesheet_entries SET project_phase_id=@ProjectPhaseId, work_date=@WorkDate, hours=@Hours, entry_type=@EntryType, notes=@Notes WHERE id=@Id", req);
        }
        else
        {
            req.Id = c.ExecuteScalar<int>("INSERT INTO timesheet_entries (employee_id,project_phase_id,work_date,hours,entry_type,notes) VALUES (@EmployeeId,@ProjectPhaseId,@WorkDate,@Hours,@EntryType,@Notes); SELECT LAST_INSERT_ID()", req);

            // Auto-avanzamento: NOT_STARTED → IN_PROGRESS al primo versamento ore
            c.Execute(@"UPDATE project_phases SET status = 'IN_PROGRESS'
            WHERE id = @ProjectPhaseId AND status = 'NOT_STARTED'", req);
        }
        int projectId = ProjectIdOfPhase(c, req.ProjectPhaseId);
        RigeneraTrasferta(c, projectId, faseDiPrima);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(req.Id));
    }

    /// <summary>
    /// Riporta le ore appena scritte sulla pagina Trasferta (#37/#52). Tocca solo le commesse
    /// coinvolte, e solo se la fase sta su una sezione di cantiere: sulle ore «in Atec» non fa
    /// niente. Non deve mai far fallire il salvataggio delle ore — se la derivazione si rompe,
    /// le ore restano scritte e il problema finisce nel log.
    /// </summary>
    private void RigeneraTrasferta(IDbConnection c, int projectId, int? faseDiPrima)
    {
        var commesse = new HashSet<int>();
        if (projectId > 0) commesse.Add(projectId);
        if (faseDiPrima is > 0)
        {
            int altra = ProjectIdOfPhase(c, faseDiPrima.Value);
            if (altra > 0) commesse.Add(altra);
        }

        foreach (int pid in commesse)
        {
            try
            {
                TravelFromTimesheet.Esito esito =
                    TravelFromTimesheet.Rebuild(c, pid, CallerId() > 0 ? CallerId() : null);
                // Solo se la trasferta di quella commessa esiste davvero: sulle ore «in sede»
                // la rigenerazione non produce niente e non c'è niente da annunciare.
                if (esito.Step > 0 || esito.Orfane > 0) NotifyTravelChanged(pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[Trasferta] Rigenerazione fallita per la commessa {Project}: {Message}",
                    pid, ex.Message);
            }
        }
    }

    [RequireProjectWritable(Sql = "SELECT pp.project_id FROM timesheet_entries te JOIN project_phases pp ON pp.id = te.project_phase_id WHERE te.id = @Id")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        // Anti-IDOR: si può cancellare solo una riga di cui si ha accesso al proprietario.
        int targetEmp = c.ExecuteScalar<int>("SELECT employee_id FROM timesheet_entries WHERE id=@Id", new { Id = id });
        if (!CanAccessTimesheet(c, targetEmp)) return Forbidden();

        // La commessa va letta PRIMA della DELETE: dopo la riga non c'è più.
        int projectId = c.ExecuteScalar<int>(@"
            SELECT COALESCE(pp.project_id, 0)
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            WHERE te.id = @Id", new { Id = id });

        c.Execute("DELETE FROM timesheet_entries WHERE id=@Id", new { Id = id });
        // La riga di trasferta generata da queste ore NON viene cancellata: resta segnalata
        // «senza ore», perché può portarsi dietro spese scritte a mano (#37/#52).
        RigeneraTrasferta(c, projectId, faseDiPrima: null);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("summary")]
    public IActionResult Summary([FromQuery] int employeeId, [FromQuery] string monthStart)
    {
        using var c = _db.Open();
        if (!CanAccessTimesheet(c, employeeId)) return Forbidden();
        var start = DateTime.Parse(monthStart);
        var end = start.AddMonths(1).AddDays(-1);

        var rows = c.Query<TimesheetSummaryRow>($@"
            SELECT CONCAT(p.code,' - ',COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name)) AS PhaseDisplay,
                   SUM(CASE WHEN te.entry_type='REGULAR' THEN te.hours ELSE 0 END) AS RegularHours,
                   SUM(CASE WHEN te.entry_type='OVERTIME' THEN te.hours ELSE 0 END) AS OvertimeHours,
                   SUM(CASE WHEN te.entry_type='TRAVEL' THEN te.hours ELSE 0 END) AS TravelHours,
                   SUM(te.hours) AS TotalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.employee_id = @EmpId AND te.work_date BETWEEN @Start AND @End
            GROUP BY pp.id, p.code, pp.custom_name, pp.name, pt.name
            ORDER BY {ProjectSorting.OrderBy("p")}",
            new { EmpId = employeeId, Start = start, End = end }).ToList();

        return Ok(ApiResponse<List<TimesheetSummaryRow>>.Ok(rows));
    }

    /// <summary>
    /// Lista dipendenti per cui l'utente corrente può registrare ore. Stessa coppia di chiavi di
    /// <see cref="CanAccessTimesheet"/>, o la tendina offrirebbe nomi che l'API poi respinge.
    /// Restituisce: se stesso + dipendenti EXTERNAL dei propri reparti («Ore per altri»).
    /// Con «Ore per chiunque»: se stesso + tutti gli EXTERNAL.
    /// </summary>
    [HttpGet("registrable-employees")]
    public IActionResult GetRegistrableEmployees()
    {
        using var c = _db.Open();
        int empId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        string? role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        bool oreAChiunque = _access.CanAccessUser(empId, role, "action.timesheet_any_employee");

        var list = new List<LookupItem>();

        // Aggiungi se stesso per primo
        var me = c.QueryFirstOrDefault<LookupItem>(
            "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE id=@Id",
            new { Id = empId });
        if (me != null) list.Add(me);

        if (oreAChiunque)
        {
            // «Ore per chiunque»: tutti gli EXTERNAL attivi
            var externals = c.Query<LookupItem>(@"
                SELECT id, CONCAT(first_name,' ',last_name,' (EXT)') AS Name 
                FROM employees 
                WHERE emp_type='EXTERNAL' AND status='ACTIVE' AND id <> @Id
                ORDER BY last_name", new { Id = empId }).ToList();
            list.AddRange(externals);
        }
        else if (_access.CanAccessUser(empId, role, "action.timesheet_for_others"))
        {
            // «Ore per altri»: EXTERNAL dei propri reparti
            var externals = c.Query<LookupItem>(@"
                SELECT DISTINCT e.id, CONCAT(e.first_name,' ',e.last_name,' (EXT)') AS Name 
                FROM employees e
                JOIN employee_departments ed ON ed.employee_id = e.id
                WHERE e.emp_type='EXTERNAL' AND e.status='ACTIVE' AND e.id <> @Id
                  AND ed.department_id IN (SELECT department_id FROM employee_departments WHERE employee_id = @Id)
                ORDER BY e.last_name", new { Id = empId }).ToList();
            list.AddRange(externals);
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(list));
    }
}
