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

/// <summary>
/// Pagina «Ore Commessa» (segnalazione #39): tutte le imputazioni di ore di una commessa,
/// riga per riga, con la persona, il giorno, la fase, la sezione di costo e il costo.
///
/// Il PM può spostare una o più righe sulla causale **Extra Lavoro**: da quel momento quelle
/// ore NON pesano più sui costi della commessa. Dalla pagina Extra Lavoro ogni riga si può
/// rimettere dentro, per vedere quanto cambierebbe la redditività se quel lavoro venisse
/// caricato — ed è tutto reversibile.
///
/// Tutto il resto del programma continua a vedere le ore come sono state scritte: qui non si
/// tocca il timesheet di nessuno, si scrive solo in una tabella laterale.
///
/// «Solo il PM»: livello 2 sulla feature <c>nav.ore_commessa</c>; un tecnico o un
/// responsabile di reparto non vedono né la pagina né questi endpoint.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:int}/hours")]
[Authorize]
[RequireFeature("nav.ore_commessa")]
// #88: ogni scrittura di questo controller riguarda UNA commessa (l'id sta nella rotta),
// quindi il cancello si mette una volta sola sulla classe: una commessa in bozza, in
// stand-by o chiusa si consulta ma non si modifica, salvo il permesso di scavalco.
[RequireProjectWritable]
// #88: e una bozza non si VEDE proprio, letture comprese — 404 come se non esistesse.
[RequireProjectVisible]
public class ProjectHoursController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public ProjectHoursController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) && id > 0 ? id : null;

    /// <summary>
    /// Ambiente condiviso: spostare ore su Extra Lavoro cambia i costi della commessa, quindi
    /// chi ha aperto il Bilancio deve vedere il numero muoversi senza ricaricare.
    /// </summary>
    private void NotifyChanged(int projectId)
    {
        object payload = new { projectId };
        _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
        _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
    }

    /// <summary>Tutte le imputazioni della commessa, comprese quelle su Extra Lavoro.</summary>
    [HttpGet]
    public IActionResult Get(int projectId)
    {
        using var c = _db.Open();
        var rows = c.Query<ProjectHourRowDto>(@"
            SELECT vt.entry_id          AS EntryId,
                   vt.employee_id       AS EmployeeId,
                   vt.employee_name     AS EmployeeName,
                   vt.work_date         AS WorkDate,
                   vt.hours             AS Hours,
                   vt.entry_type        AS EntryType,
                   COALESCE(te.notes,'') AS Notes,
                   vt.project_phase_id  AS ProjectPhaseId,
                   vt.phase_name        AS PhaseName,
                   COALESCE(cs.name,'') AS CostSectionName,
                   COALESCE(cs.section_type,'') AS CostSectionType,
                   vt.hourly_cost       AS HourlyCost,
                   vt.is_extra          AS IsExtra,
                   vt.counts_in_project AS CountsInProject,
                   ew.moved_at          AS MovedAt,
                   COALESCE(CONCAT(mv.first_name,' ',mv.last_name), '') AS MovedByName
            FROM v_timesheet_with_section vt
            JOIN timesheet_entries te ON te.id = vt.entry_id
            LEFT JOIN cost_section_templates cs ON cs.id = vt.cost_section_template_id
            LEFT JOIN timesheet_extra_work ew ON ew.entry_id = vt.entry_id
            LEFT JOIN employees mv ON mv.id = ew.moved_by
            WHERE vt.project_id = @Pid
            ORDER BY vt.work_date DESC, vt.employee_name, vt.phase_name",
            new { Pid = projectId }).ToList();

        return Ok(ApiResponse<List<ProjectHourRowDto>>.Ok(rows));
    }

    /// <summary>
    /// Sposta una o più imputazioni su «Extra Lavoro». Nascono ESCLUSE dai costi della
    /// commessa (è il senso della causale); da lì si possono rimettere dentro una per una.
    /// Idempotente: rispostare una riga già spostata non la duplica e non le cambia stato.
    /// </summary>
    [HttpPost("extra-work")]
    public IActionResult MoveToExtra(int projectId, [FromBody] ExtraWorkMoveRequest req)
    {
        if (req.EntryIds.Count == 0)
            return Ok(ApiResponse<int>.Fail("Nessuna riga selezionata."));

        using var c = _db.Open();
        // Le righe devono essere DI QUESTA commessa: l'id arriva dal client.
        List<int> validi = c.Query<int>(@"
            SELECT te.id
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            WHERE pp.project_id = @Pid AND te.id IN @Ids",
            new { Pid = projectId, Ids = req.EntryIds }).ToList();
        if (validi.Count == 0)
            return Ok(ApiResponse<int>.Fail("Le righe indicate non appartengono a questa commessa."));

        int spostate = c.Execute(@"
            INSERT IGNORE INTO timesheet_extra_work (entry_id, counts_in_project, moved_by, note)
            VALUES (@EntryId, 0, @MovedBy, @Note)",
            validi.Select(id => new { EntryId = id, MovedBy = CurrentUserId, Note = req.Note ?? "" }));

        NotifyChanged(projectId);
        return Ok(ApiResponse<int>.Ok(spostate, spostate == 1
            ? "1 riga spostata su Extra Lavoro"
            : $"{spostate} righe spostate su Extra Lavoro"));
    }

    /// <summary>
    /// Riporta le righe nella contabilità normale della commessa (annulla lo spostamento).
    /// È una POST e non una DELETE perché porta un elenco di id nel corpo, e il client
    /// condiviso manda le DELETE senza corpo.
    /// </summary>
    [HttpPost("extra-work/back")]
    public IActionResult BackToProject(int projectId, [FromBody] ExtraWorkMoveRequest req)
    {
        if (req.EntryIds.Count == 0)
            return Ok(ApiResponse<int>.Fail("Nessuna riga selezionata."));

        using var c = _db.Open();
        int tolte = c.Execute(@"
            DELETE ew FROM timesheet_extra_work ew
            JOIN timesheet_entries te ON te.id = ew.entry_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            WHERE pp.project_id = @Pid AND ew.entry_id IN @Ids",
            new { Pid = projectId, Ids = req.EntryIds });

        NotifyChanged(projectId);
        return Ok(ApiResponse<int>.Ok(tolte, "Righe riportate nella commessa"));
    }

    /// <summary>
    /// L'interruttore della pagina Extra Lavoro: questa riga conta o no nei costi della
    /// commessa? Serve a misurare quanto peserebbe caricare quelle ore, senza doverle
    /// rimettere e ritogliere dalla causale.
    /// </summary>
    [HttpPatch("extra-work/{entryId:int}/counts")]
    public IActionResult SetCounts(int projectId, int entryId, [FromBody] bool counts)
    {
        using var c = _db.Open();
        int righe = c.Execute(@"
            UPDATE timesheet_extra_work ew
            JOIN timesheet_entries te ON te.id = ew.entry_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            SET ew.counts_in_project = @Counts
            WHERE pp.project_id = @Pid AND ew.entry_id = @EntryId",
            new { Counts = counts ? 1 : 0, Pid = projectId, EntryId = entryId });

        if (righe == 0) return Ok(ApiResponse<bool>.Fail("Riga non trovata fra quelle su Extra Lavoro."));

        NotifyChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// «Verifica effettuata» (#109): il PM dichiara di aver guardato le ore arrivate finora
    /// su questa commessa. La card smette di essere rossa e la voce di menu si spegne,
    /// finché non ne arrivano di nuove. È una spunta di lettura: non tocca nessuna ora.
    /// </summary>
    [HttpPost("verify")]
    public IActionResult Verify(int projectId)
    {
        using var c = _db.Open();
        ScaricoOre.SegnaVerificato(c, projectId, ScaricoOre.ScopeOre, CurrentUserId);
        NotifyChanged(projectId);
        return Ok(ApiResponse<string>.Ok("ok", "Ore verificate."));
    }

    // ── PAGINA CROSS-COMMESSA (#109) ─────────────────────────────
    // Rotte assolute dentro lo stesso controller, come fa la Trasferta: restano coperte
    // da `nav.ore_commessa` senza dover creare un secondo controller. I cancelli di
    // classe non danno noia — su una GET `RequireProjectWritable` esce subito, e
    // `RequireProjectVisible` senza projectId nella rotta non ha niente da controllare.

    /// <summary>
    /// Le commesse su cui qualcuno ha scaricato ore, con quanto è arrivato, da quando a
    /// quando e se il PM l'ha già guardato. Alimenta le card della pagina, che prima
    /// mostrava soltanto «Scegli una commessa».
    /// </summary>
    [HttpGet("/api/project-hours/summary")]
    public IActionResult Summary([FromQuery] bool includeClosed = false)
    {
        using var c = _db.Open();

        // Stesso perimetro della pagina Trasferta: fuori bozze e annullate, le chiuse
        // solo se richieste esplicitamente.
        string statusFilter = includeClosed
            ? "p.status NOT IN ('DRAFT','CANCELLED')"
            : "p.status NOT IN ('DRAFT','CANCELLED','COMPLETED')";

        var progetti = c.Query<ProjectHoursSummaryDto>($@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title, p.status AS Status,
                   COALESCE(cu.company_name, '') AS CustomerName,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS PmName
            FROM projects p
            LEFT JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN employees e ON e.id = p.pm_id
            WHERE {statusFilter}
            ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

        if (progetti.Count == 0)
            return Ok(ApiResponse<List<ProjectHoursSummaryDto>>.Ok(progetti));

        // Totali dello scarico: TUTTE le ore imputate, Extra Lavoro compreso. La card
        // risponde a «cosa mi è arrivato», non a «cosa pesa sui costi» — quello è il
        // Bilancio, e mescolarli farebbe sparire dalla vista proprio le ore che il PM
        // ha appena spostato fuori.
        var totali = c.Query(@"
            SELECT vt.project_id            AS ProjectId,
                   SUM(vt.hours)            AS TotalHours,
                   SUM(vt.hours * vt.hourly_cost) AS TotalCost,
                   COUNT(DISTINCT vt.employee_id) AS PeopleCount,
                   MIN(vt.work_date)        AS FirstWorkDate,
                   MAX(vt.work_date)        AS LastWorkDate
            FROM v_timesheet_with_section vt
            GROUP BY vt.project_id")
            .ToDictionary(r => (int)r.ProjectId);

        var pendenti = ScaricoOre.PerCommessa(c, ScaricoOre.ScopeOre);
        var verifiche = ScaricoOre.VerificheFatte(c, ScaricoOre.ScopeOre);

        foreach (ProjectHoursSummaryDto p in progetti)
        {
            if (totali.TryGetValue(p.ProjectId, out dynamic? t))
            {
                p.TotalHours = (decimal)(t!.TotalHours ?? 0m);
                p.TotalCost = (decimal)(t.TotalCost ?? 0m);
                p.PeopleCount = (int)(t.PeopleCount ?? 0);
                p.FirstWorkDate = t.FirstWorkDate;
                p.LastWorkDate = t.LastWorkDate;
            }
            if (pendenti.TryGetValue(p.ProjectId, out ScaricoOre.Pendenza? pend))
            {
                p.PendingPeople = pend.Persone;
                p.PendingHours = pend.Ore;
                p.PendingFrom = pend.DalGiorno;
                p.PendingTo = pend.AlGiorno;
            }
            if (verifiche.TryGetValue(p.ProjectId, out ScaricoOre.Verifica? ver))
            {
                p.VerifiedAt = ver.VerifiedAt;
                p.VerifiedByName = ver.VerifiedByName;
            }
        }

        // #112: tornano TUTTE le commesse del perimetro (restano fuori solo bozze e annullate,
        // e le completate salvo includeClosed), non solo quelle con TotalHours > 0.
        // L'utente vuole vedere tutte le cartelle delle commesse con in rosso quelle da verificare.
        return Ok(ApiResponse<List<ProjectHoursSummaryDto>>.Ok(progetti));
    }

    /// <summary>
    /// Numero per il pallino rosso della voce «Ore Commessa» (#109): persone distinte con
    /// ore non ancora verificate, su tutte le commesse.
    /// </summary>
    [HttpGet("/api/project-hours/pending-count")]
    public IActionResult PendingCount()
    {
        using var c = _db.Open();
        return Ok(ApiResponse<int>.Ok(ScaricoOre.PersoneInAttesa(c, ScaricoOre.ScopeOre)));
    }
}
