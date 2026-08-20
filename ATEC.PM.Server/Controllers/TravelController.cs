using System.Security.Claims;
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
/// Gestione Trasferta (blocco 6): step di trasferta e righe-persona di una commessa.
///
/// Ogni scrittura fa due cose oltre a salvare: risincronizza la voce «Spese Trasferta» del
/// Riepilogo Costi (<see cref="TravelPlanService.SyncToBudget"/>) e avvisa chi ha la pagina
/// aperta. Ambiente condiviso: la trasferta la compila il PM ma la guardano in tanti.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/travel")]
[Authorize]
[RequireFeature("nav.trasferta")]
// #88: ogni scrittura di questo controller riguarda UNA commessa (l'id sta nella rotta),
// quindi il cancello si mette una volta sola sulla classe: una commessa in bozza, in
// stand-by o chiusa si consulta ma non si modifica, salvo il permesso di scavalco.
[RequireProjectWritable]
// #88: e una bozza non si VEDE proprio, letture comprese — 404 come se non esistesse.
[RequireProjectVisible]
public class TravelController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public TravelController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) && id > 0 ? id : null;

    private void NotifyChanged(int projectId)
    {
        object payload = new { projectId };
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("TravelChanged", payload);
        _ = _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("TravelChanged", payload);
        // Il Bilancio legge la voce «Spese Trasferta»: chi lo ha aperto deve vederla muoversi.
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload);
        _ = _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload);
    }

    /// <summary>Salva, risincronizza il Bilancio e avvisa. Da chiamare fuori da una transazione.</summary>
    private void AfterWrite(System.Data.IDbConnection c, int projectId)
    {
        TravelPlanService.SyncToBudget(c, projectId, CurrentUserId);
        NotifyChanged(projectId);
    }

    // ── LETTURA ──────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Get(int projectId)
    {
        using var c = _db.Open();
        return Ok(ApiResponse<TravelPlanDto>.Ok(TravelPlanService.Load(c, projectId)));
    }

    /// <summary>
    /// Rilegge il Timesheet e rifà le righe di cantiere di questa commessa (#37/#52).
    ///
    /// Normalmente non serve: la derivazione parte da sola a ogni ora salvata e a ogni fase
    /// rinominata. Serve quando cambia qualcosa che sta FUORI dalla commessa e che nessuna
    /// scrittura di ore va a toccare — il caso vero è il tag di una sezione di costo spostato
    /// da «in sede» a «da cliente» in Configurazione sezioni: da quel momento le fasi che ci
    /// stanno sotto dovrebbero generare trasferta, ma nelle commesse già aperte non succede
    /// finché qualcuno non ritocca un'imputazione. Con questo pulsante si riallinea.
    ///
    /// È la stessa rigenerazione idempotente di sempre: le righe scritte a mano non si toccano,
    /// e quelle rimaste senza ore vengono segnalate, non cancellate.
    /// </summary>
    [HttpPost("rebuild-from-timesheet")]
    public IActionResult RebuildFromTimesheet(int projectId)
    {
        using var c = _db.Open();
        TravelFromTimesheet.Esito esito = TravelFromTimesheet.Rebuild(c, projectId, CurrentUserId);
        NotifyChanged(projectId);

        string giornate = esito.Righe == 1 ? "1 giornata di cantiere" : $"{esito.Righe} giornate di cantiere";
        string fasi = esito.Step == 1 ? "1 fase" : $"{esito.Step} fasi";
        string nuove = esito.Nuove switch
        {
            0 => ", nessuna nuova",
            1 => ", di cui 1 nuova",
            _ => $", di cui {esito.Nuove} nuove",
        };
        string orfane = esito.Orfane switch
        {
            0 => "",
            1 => " · 1 riga resta senza ore",
            _ => $" · {esito.Orfane} righe restano senza ore",
        };

        string messaggio = esito.Righe == 0
            ? "Nessuna ora di cantiere su questa commessa: niente da riportare."
            : $"{giornate} su {fasi}{nuove}{orfane}";

        return Ok(ApiResponse<string>.Ok(esito.ToString(), messaggio));
    }

    // ── STEP ─────────────────────────────────────────────────────

    [HttpPost("steps")]
    public IActionResult CreateStep(int projectId, [FromBody] TravelStepSaveRequest req)
    {
        using var c = _db.Open();

        bool exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id",
            new { Id = projectId }) > 0;
        if (!exists) return Ok(ApiResponse<int>.Fail("Commessa non trovata."));

        int sortOrder = (c.ExecuteScalar<int?>(
            "SELECT MAX(sort_order) FROM travel_steps WHERE project_id=@Pid",
            new { Pid = projectId }) ?? 0) + 1;

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO travel_steps (project_id, description, sort_order)
            VALUES (@Pid, @Desc, @Sort);
            SELECT LAST_INSERT_ID()",
            new { Pid = projectId, Desc = (req.Description ?? "").Trim(), Sort = sortOrder });

        AfterWrite(c, projectId);
        return Ok(ApiResponse<int>.Ok(id));
    }

    [HttpPut("steps/{stepId}")]
    public IActionResult UpdateStep(int projectId, int stepId, [FromBody] TravelStepSaveRequest req)
    {
        using var c = _db.Open();

        int rows = c.Execute(@"
            UPDATE travel_steps s
            SET s.description = @Desc, s.row_version = s.row_version + 1
            WHERE s.id=@Id AND s.project_id=@Pid
              AND (@RowVersion IS NULL OR s.row_version = @RowVersion)",
            new { Id = stepId, Pid = projectId, Desc = (req.Description ?? "").Trim(), req.RowVersion });

        if (rows == 0) return Ok(ApiResponse<bool>.Fail(StepConflictMessage(c, projectId, stepId)));

        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("steps/{stepId}")]
    public IActionResult DeleteStep(int projectId, int stepId)
    {
        using var c = _db.Open();

        // I fogli delle calcolatrici hanno un owner polimorfo (la chiave porta l'id della riga):
        // nessuna FK se li porta via, vanno cancellati a mano PRIMA di perdere gli id.
        List<int> rowIds = c.Query<int>(
            "SELECT id FROM travel_step_rows WHERE step_id=@Sid", new { Sid = stepId }).ToList();

        int deleted = c.Execute("DELETE FROM travel_steps WHERE id=@Id AND project_id=@Pid",
            new { Id = stepId, Pid = projectId });
        if (deleted == 0) return Ok(ApiResponse<bool>.Fail("Step non trovato."));

        ProjectCalcSheets.DeleteSheets(c, projectId, rowIds.SelectMany(CalcKeys.ForTravelRow));

        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Nuovo ordine degli step dopo un drag&amp;drop.</summary>
    [HttpPut("steps/reorder")]
    public IActionResult ReorderSteps(int projectId, [FromBody] TravelReorderRequest req)
    {
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute("UPDATE travel_steps SET sort_order=@Sort WHERE id=@Id AND project_id=@Pid",
                new { Sort = ++order, Id = id, Pid = projectId });
        }
        // AfterWrite, non il solo NotifyChanged: il foglio di calcolo del Bilancio ha una riga
        // per step, con la sua etichetta. Riordinando gli step senza risincronizzare, il totale
        // resta giusto (è una somma) ma il DETTAGLIO dietro la voce «Spese Trasferta» mostra le
        // righe nell'ordine vecchio — e chi lo apre per capire un numero trova un elenco che non
        // corrisponde a quello che ha davanti nella pagina Trasferta.
        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── RIGHE-PERSONA ────────────────────────────────────────────

    [HttpPost("steps/{stepId}/rows")]
    public IActionResult CreateRow(int projectId, int stepId)
    {
        using var c = _db.Open();

        bool exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM travel_steps WHERE id=@Id AND project_id=@Pid",
            new { Id = stepId, Pid = projectId }) > 0;
        if (!exists) return Ok(ApiResponse<int>.Fail("Step non trovato."));

        int sortOrder = (c.ExecuteScalar<int?>(
            "SELECT MAX(sort_order) FROM travel_step_rows WHERE step_id=@Sid",
            new { Sid = stepId }) ?? 0) + 1;

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO travel_step_rows (step_id, sort_order) VALUES (@Sid, @Sort);
            SELECT LAST_INSERT_ID()",
            new { Sid = stepId, Sort = sortOrder });

        NotifyChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id));
    }

    /// <summary>
    /// Salva una riga-persona. Il client manda TUTTI i campi editabili insieme, una volta sola,
    /// all'uscita dalla riga: è la regola imparata nel blocco 4, dove il commit per campo faceva
    /// scartare in silenzio il secondo e il terzo salvataggio.
    /// Ore, vitto, indennità e auto NON sono qui: li scrivono le calcolatrici.
    /// </summary>
    [HttpPut("rows/{rowId}")]
    public IActionResult UpdateRow(int projectId, int rowId, [FromBody] TravelRowSaveRequest req)
    {
        using var c = _db.Open();

        // Il nome resta scritto sulla riga anche quando arriva dall'anagrafica: una trasferta
        // vecchia deve restare leggibile se il dipendente non c'è più.
        string personName = (req.PersonName ?? "").Trim();
        if (req.EmployeeId is int empId && personName.Length == 0)
        {
            personName = c.ExecuteScalar<string?>(
                "SELECT CONCAT(first_name,' ',last_name) FROM employees WHERE id=@Id",
                new { Id = empId }) ?? "";
        }

        int rows = c.Execute(@"
            UPDATE travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            SET r.employee_id = @EmployeeId,
                r.person_name = @PersonName,
                r.start_date = @StartDate,
                r.end_date = @EndDate,
                r.exclude_sat = @ExcludeSat,
                r.exclude_sun = @ExcludeSun,
                r.hourly_rate = @HourlyRate,
                r.nights = @Nights,
                r.night_price = @NightPrice,
                r.transport_cost = @TransportCost,
                r.row_version = r.row_version + 1
            WHERE r.id = @Id AND s.project_id = @Pid
              AND (@RowVersion IS NULL OR r.row_version = @RowVersion)",
            new
            {
                Id = rowId,
                Pid = projectId,
                req.EmployeeId,
                PersonName = personName,
                req.StartDate,
                req.EndDate,
                req.ExcludeSat,
                req.ExcludeSun,
                req.HourlyRate,
                req.Nights,
                req.NightPrice,
                req.TransportCost,
                req.RowVersion,
            });

        if (rows == 0) return Ok(ApiResponse<bool>.Fail(RowConflictMessage(c, projectId, rowId)));

        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("rows/{rowId}")]
    public IActionResult DeleteRow(int projectId, int rowId)
    {
        using var c = _db.Open();

        int deleted = c.Execute(@"
            DELETE r FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            WHERE r.id=@Id AND s.project_id=@Pid",
            new { Id = rowId, Pid = projectId });
        if (deleted == 0) return Ok(ApiResponse<bool>.Fail("Riga non trovata."));

        ProjectCalcSheets.DeleteSheets(c, projectId, CalcKeys.ForTravelRow(rowId));

        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("steps/{stepId}/rows/reorder")]
    public IActionResult ReorderRows(int projectId, int stepId, [FromBody] TravelReorderRequest req)
    {
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute(@"
                UPDATE travel_step_rows r
                JOIN travel_steps s ON s.id = r.step_id
                SET r.sort_order=@Sort
                WHERE r.id=@Id AND r.step_id=@Sid AND s.project_id=@Pid",
                new { Sort = ++order, Id = id, Sid = stepId, Pid = projectId });
        }
        // Stessa ragione del riordino degli step: il dettaglio dietro la voce del Bilancio
        // deve restare nell'ordine che si vede qui.
        AfterWrite(c, projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── LE 4 CALCOLATRICI ────────────────────────────────────────

    /// <summary>Dettaglio di una calcolatrice della riga (foglio di calcolo del blocco 5).</summary>
    [HttpGet("rows/{rowId}/calc/{kind}")]
    public IActionResult GetRowCalc(int projectId, int rowId, string kind)
    {
        if (!TravelCalcKinds.IsKnown(kind))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Calcolatrice sconosciuta."));

        using var c = _db.Open();
        if (!RowBelongsToProject(c, projectId, rowId))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Riga non trovata."));

        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(
            ProjectCalcSheets.Load(c, projectId, TravelCalcKinds.SheetKey(kind, rowId))));
    }

    /// <summary>
    /// Conferma di una calcolatrice: salva il dettaglio E scrive il totale nella colonna della
    /// riga, in un colpo solo. Sono due scritture che devono restare d'accordo, quindi le fa il
    /// server: se le facesse il client, una connessione persa lascerebbe totale e dettaglio
    /// disallineati.
    /// </summary>
    [HttpPut("rows/{rowId}/calc/{kind}")]
    public IActionResult SaveRowCalc(
        int projectId, int rowId, string kind, [FromBody] ProjectCalcSheetSaveRequest req)
    {
        if (!TravelCalcKinds.IsKnown(kind))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Calcolatrice sconosciuta."));

        using var c = _db.Open();
        if (!RowBelongsToProject(c, projectId, rowId))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Riga non trovata."));

        (ProjectCalcSheetDto? sheet, string? error) = ProjectCalcSheets.Save(
            c, projectId, TravelCalcKinds.SheetKey(kind, rowId), req, CurrentUserId);

        if (error != null || sheet == null)
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail(error ?? "Salvataggio non riuscito."));

        // Foglio vuoto → colonna a NULL: la cella torna «—», non 0,00 €.
        string column = kind switch
        {
            TravelCalcKinds.Hours => "hours",
            TravelCalcKinds.Meal => "meal_cost",
            TravelCalcKinds.Allowance => "allowance_cost",
            _ => "car_cost",
        };
        c.Execute($@"
            UPDATE travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            SET r.`{column}` = @Total, r.row_version = r.row_version + 1
            WHERE r.id=@Id AND s.project_id=@Pid",
            new { Total = sheet.Total, Id = rowId, Pid = projectId });

        AfterWrite(c, projectId);
        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(sheet));
    }

    /// <summary>
    /// Aggrega costi Trasferta (#67): applica Notti/Prezzo/Vitto/Indennità/Auto/Treno
    /// a tutte le righe selezionate. Una sola sync Bilancio a fine lavoro.
    /// </summary>
    [HttpPut("rows/apply-costs")]
    public IActionResult ApplyCosts(int projectId, [FromBody] TravelApplyCostsRequest req)
    {
        if (req.RowIds == null || req.RowIds.Count == 0)
            return Ok(ApiResponse<int>.Fail("Seleziona almeno una riga di trasferta."));

        HashSet<string> fields = new(
            (req.Fields ?? new List<string>()).Select(f => f.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (fields.Count == 0)
            return Ok(ApiResponse<int>.Fail("Imposta almeno un costo nella tabella Aggrega."));

        using var c = _db.Open();

        // Solo righe della commessa: evita id estranei / di altre commesse.
        List<int> ids = c.Query<int>(@"
            SELECT r.id FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            WHERE s.project_id = @Pid AND r.id IN @Ids",
            new { Pid = projectId, Ids = req.RowIds }).ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<int>.Fail("Nessuna riga valida selezionata."));

        bool setNights = fields.Contains("nights");
        bool setNightPrice = fields.Contains("nightPrice");
        bool setTransport = fields.Contains("transportCost");
        bool setMeal = fields.Contains("mealCost");
        bool setAllowance = fields.Contains("allowanceCost");
        bool setCar = fields.Contains("carCost");

        foreach (int rowId in ids)
        {
            if (setNights || setNightPrice || setTransport)
            {
                c.Execute(@"
                    UPDATE travel_step_rows r
                    JOIN travel_steps s ON s.id = r.step_id
                    SET r.nights = CASE WHEN @SetNights = 1 THEN @Nights ELSE r.nights END,
                        r.night_price = CASE WHEN @SetNightPrice = 1 THEN @NightPrice ELSE r.night_price END,
                        r.transport_cost = CASE WHEN @SetTransport = 1 THEN @TransportCost ELSE r.transport_cost END,
                        r.row_version = r.row_version + 1
                    WHERE r.id = @Id AND s.project_id = @Pid",
                    new
                    {
                        Id = rowId,
                        Pid = projectId,
                        SetNights = setNights ? 1 : 0,
                        SetNightPrice = setNightPrice ? 1 : 0,
                        SetTransport = setTransport ? 1 : 0,
                        req.Nights,
                        req.NightPrice,
                        req.TransportCost,
                    });
            }

            if (setMeal)
                ApplyCalcTotal(c, projectId, rowId, TravelCalcKinds.Meal, req.MealCost);
            if (setAllowance)
                ApplyCalcTotal(c, projectId, rowId, TravelCalcKinds.Allowance, req.AllowanceCost);
            if (setCar)
                ApplyCalcTotal(c, projectId, rowId, TravelCalcKinds.Car, req.CarCost);
        }

        AfterWrite(c, projectId);
        return Ok(ApiResponse<int>.Ok(ids.Count));
    }

    /// <summary>
    /// Scrive il totale di una calcolatrice e allinea il foglio di dettaglio:
    /// una riga sintetica «Aggregazione costi», oppure foglio vuoto se il totale è null.
    /// </summary>
    private void ApplyCalcTotal(
        System.Data.IDbConnection c, int projectId, int rowId, string kind, decimal? total)
    {
        string sheetKey = TravelCalcKinds.SheetKey(kind, rowId);
        List<ProjectCalcRowDto> sheetRows = total == null
            ? new List<ProjectCalcRowDto>()
            : new List<ProjectCalcRowDto>
            {
                new()
                {
                    Description = "Aggregazione costi",
                    Quantity = 1,
                    UnitCost = total,
                    Multiplier = null,
                    Amount = total,
                    MarkupValue = 1,
                },
            };

        (ProjectCalcSheetDto? sheet, string? error) = ProjectCalcSheets.Save(
            c, projectId, sheetKey,
            new ProjectCalcSheetSaveRequest { RowVersion = null, Rows = sheetRows },
            CurrentUserId);
        if (error != null || sheet == null) return;

        string column = kind switch
        {
            TravelCalcKinds.Meal => "meal_cost",
            TravelCalcKinds.Allowance => "allowance_cost",
            _ => "car_cost",
        };
        c.Execute($@"
            UPDATE travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            SET r.`{column}` = @Total, r.row_version = r.row_version + 1
            WHERE r.id=@Id AND s.project_id=@Pid",
            new { Total = sheet.Total, Id = rowId, Pid = projectId });
    }

    /// <summary>
    /// Nominativi proponibili con il loro costo orario di reparto: scegliendo la persona la
    /// tariffa si precompila (D6-B). Il prototipo non ce l'ha — lì la tariffa si sceglie ogni
    /// volta a mano — ma qui il dato esiste già e proporlo evita un errore di distrazione.
    /// </summary>
    [HttpGet("/api/travel/people")]
    public IActionResult People()
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeCostLookup>(@"
            SELECT e.id AS Id, CONCAT(e.first_name,' ',e.last_name) AS FullName,
                   COALESCE(MAX(d.code), '') AS DepartmentCode,
                   COALESCE(MAX(d.hourly_cost), 0) AS HourlyCost,
                   COALESCE(MAX(d.default_markup), 1.450) AS DefaultMarkup
            FROM employees e
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE e.status = 'ACTIVE'
              -- Le wildcard di reparto ([PM] Generico, …) non vanno in trasferta.
              AND e.first_name NOT LIKE '[%'
            GROUP BY e.id, e.first_name, e.last_name").ToList();

        var italian = StringComparer.Create(
            System.Globalization.CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);
        return Ok(ApiResponse<List<EmployeeCostLookup>>.Ok(
            rows.OrderBy(r => r.FullName, italian).ToList()));
    }

    /// <summary>
    /// «Verifica effettuata» (#102): il PM dichiara di aver guardato lo scarico ore di
    /// cantiere arrivato finora. La card smette di essere rossa e la voce di menu si
    /// spegne, finché non arrivano ore nuove. Non cambia nessun dato di trasferta: è una
    /// spunta di lettura, quindi non passa da <see cref="AfterWrite"/>.
    /// </summary>
    [HttpPost("verify")]
    public IActionResult Verify(int projectId)
    {
        using var c = _db.Open();
        ScaricoOre.SegnaVerificato(c, projectId, ScaricoOre.ScopeTrasferta, CurrentUserId);
        NotifyChanged(projectId);
        return Ok(ApiResponse<string>.Ok("ok", "Scarico ore verificato."));
    }

    // ── PAGINA CROSS-COMMESSA ────────────────────────────────────

    [HttpGet("/api/travel/summary")]
    public IActionResult Summary([FromQuery] bool includeClosed = false)
    {
        using var c = _db.Open();
        return Ok(ApiResponse<List<TravelProjectSummaryDto>>.Ok(
            TravelPlanService.Summaries(c, includeClosed)));
    }

    /// <summary>
    /// Numero per il pallino rosso della voce «Trasferta» (#102): persone distinte con ore
    /// di cantiere non ancora verificate, su tutte le commesse. Query minuscola apposta —
    /// gira nella barra di ogni PM una volta al minuto, il riepilogo completo no.
    /// </summary>
    [HttpGet("/api/travel/pending-count")]
    public IActionResult PendingCount()
    {
        using var c = _db.Open();
        return Ok(ApiResponse<int>.Ok(ScaricoOre.PersoneInAttesa(c, ScaricoOre.ScopeTrasferta)));
    }

    // ── UTILITÀ ──────────────────────────────────────────────────

    private static bool RowBelongsToProject(System.Data.IDbConnection c, int projectId, int rowId) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM travel_step_rows r
            JOIN travel_steps s ON s.id = r.step_id
            WHERE r.id=@Id AND s.project_id=@Pid",
            new { Id = rowId, Pid = projectId }) > 0;

    private static string StepConflictMessage(System.Data.IDbConnection c, int projectId, int stepId) =>
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM travel_steps WHERE id=@Id AND project_id=@Pid",
            new { Id = stepId, Pid = projectId }) > 0
            ? "Step modificato da un altro utente: ricarica la trasferta prima di risalvare."
            : "Step non trovato.";

    private static string RowConflictMessage(System.Data.IDbConnection c, int projectId, int rowId) =>
        RowBelongsToProject(c, projectId, rowId)
            ? "Riga modificata da un altro utente: ricarica la trasferta prima di risalvare."
            : "Riga non trovata.";
}
