using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects/{projectId}/costing")]
[Authorize]
// Dati economici (costi/margini commessa): visibili solo da livello PM in su (feature data.costs).
[RequireFeature("data.costs")]
// #88: ogni scrittura di questo controller riguarda UNA commessa (l'id sta nella rotta),
// quindi il cancello si mette una volta sola sulla classe: una commessa in bozza, in
// stand-by o chiusa si consulta ma non si modifica, salvo il permesso di scavalco.
[RequireProjectWritable]
// #88: e una bozza non si VEDE proprio, letture comprese — 404 come se non esistesse.
[RequireProjectVisible]
public class ProjectCostingController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public ProjectCostingController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? CurrentUserId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) && id > 0 ? id : null;

    /// <summary>
    /// Ambiente condiviso: la trasferta di preventivo entra nel Riepilogo Costi, quindi chi ha
    /// il Bilancio aperto (o la pagina /bilancio) deve vedere il numero muoversi da solo.
    /// Stesso evento di <c>BudgetVsActualController.NotifyBudgetChanged</c>.
    /// </summary>
    private void NotifyBudgetChanged(int projectId)
    {
        object payload = new { projectId };
        _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
        _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
    }

    [HttpPost("init")]
    public IActionResult Initialize(int projectId)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        try
        {
            int exists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_cost_sections WHERE project_id=@projectId",
                new { projectId }, tx);
            if (exists > 0)
                return BadRequest(ApiResponse<string>.Fail("Configurazione già inizializzata"));

            var templates = c.Query<dynamic>(@"
                SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
                FROM cost_section_templates t
                JOIN cost_section_groups g ON g.id = t.group_id
                WHERE t.is_default_project=1 AND t.is_active=1
                ORDER BY t.sort_order", transaction: tx).ToList();

            foreach (var tmpl in templates)
            {
                int newSectionId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @section_type, @group_name, @sort_order, 1);
                    SELECT LAST_INSERT_ID();",
                    new { projectId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, tmpl.sort_order }, tx);

                c.Execute(@"
                    INSERT INTO project_cost_section_departments (project_cost_section_id, department_id)
                    SELECT @newSectionId, department_id
                    FROM cost_section_template_departments
                    WHERE section_template_id = @templateId",
                    new { newSectionId, templateId = (int)tmpl.id }, tx);
            }


            // 3. Copia categorie materiali
            var categories = c.Query<dynamic>(@"
                SELECT id, name, default_markup, default_commission_markup, sort_order
                FROM material_categories
                WHERE is_active=1 ORDER BY sort_order", transaction: tx).ToList();

            foreach (var cat in categories)
            {
                c.Execute(@"
                    INSERT INTO project_material_sections (project_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                    VALUES (@projectId, @id, @name, @default_markup, @default_commission_markup, @sort_order, 1)",
                    new { projectId, cat.id, cat.name, cat.default_markup, cat.default_commission_markup, cat.sort_order }, tx);
            }

            // 4. Scheda prezzi default
            c.Execute("INSERT INTO project_pricing (project_id) VALUES (@projectId)", new { projectId }, tx);

            tx.Commit();
            NotifyBudgetChanged(projectId);
            return Ok(ApiResponse<string>.Ok("", "Configurazione inizializzata"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return StatusCode(500, ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("sections/{sectionId}/departments")]
    public IActionResult SetSectionDepartments(int projectId, int sectionId, [FromBody] SectionDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM project_cost_section_departments WHERE project_cost_section_id=@sectionId",
            new { sectionId }, tx);

        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO project_cost_section_departments (project_cost_section_id, department_id) VALUES (@sectionId, @deptId)",
                new { sectionId, deptId }, tx);
        }

        tx.Commit();
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [HttpGet("available-templates")]
    public IActionResult GetAvailableTemplates(int projectId)
    {
        using var c = _db.Open();
        (List<CostSectionGroupDto> groups, List<CostSectionTemplateDto> templates) =
            CostingDataService.GetAvailableTemplates(c, CostingDataService.CostingScope.Project, projectId);
        return Ok(ApiResponse<object>.Ok(new { Groups = groups, Templates = templates }));
    }

    [HttpGet]
    public IActionResult GetAll(int projectId)
    {
        using var c = _db.Open();
        ProjectCostingData data = CostingDataService.LoadProject(c, projectId);
        return Ok(ApiResponse<ProjectCostingData>.Ok(data));
    }

    // ══════════════════════════════════════════════════════════════
    // DIPENDENTI PER SEZIONE (filtrati per reparti associati)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Risorse proponibili per una sezione di preventivo, filtrate sui reparti della sezione.
    ///
    /// Tre correzioni del 04/08/2026 (punto 6 del blocco 5), tutte visibili nella tendina:
    ///  1. non solo i wildcard reparto: il filtro <c>first_name LIKE '[%'</c> nascondeva le
    ///     persone vere, che il preventivo può benissimo nominare (e il prototipo V32 propone
    ///     proprio l'anagrafica del personale). I wildcard restano in cima, così il default
    ///     «generico» non si perde in mezzo ai nomi;
    ///  2. via i nomi già usati nella sezione — con <paramref name="excludeResourceId"/> per
    ///     la modifica, altrimenti la riga aperta non troverebbe più la propria risorsa;
    ///  3. ordinamento italiano (accenti e maiuscole come li ordina una persona), che l'ORDER BY
    ///     SQL non garantisce: dipende dalla collation della colonna.
    /// </summary>
    [HttpGet("sections/{sectionId}/employees")]
    public IActionResult GetEmployeesForSection(
        int projectId, int sectionId, [FromQuery] int? excludeResourceId = null)
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeCostLookup>(@"
            SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS FullName,
                   MAX(d.code) AS DepartmentCode, MAX(d.hourly_cost) AS HourlyCost,
                   MAX(d.default_markup) AS DefaultMarkup
            FROM employees e
            JOIN employee_departments ed ON ed.employee_id = e.id
            JOIN departments d ON d.id = ed.department_id
            JOIN project_cost_section_departments psd ON psd.department_id = d.id
            WHERE psd.project_cost_section_id = @sectionId
              AND e.status <> 'TERMINATED'
              AND NOT EXISTS (
                  SELECT 1 FROM project_cost_resources r
                  WHERE r.section_id = @sectionId
                    AND r.employee_id = e.id
                    AND (@excludeResourceId IS NULL OR r.id <> @excludeResourceId)
              )
            GROUP BY e.id, e.first_name, e.last_name",
            new { sectionId, excludeResourceId }).ToList();

        // Wildcard reparto ([PM] Generico, …) in testa, poi le persone; dentro ai due blocchi
        // ordine alfabetico italiano.
        var italian = StringComparer.Create(
            System.Globalization.CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);
        var ordered = rows
            .OrderByDescending(r => r.FullName.StartsWith('['))
            .ThenBy(r => r.FullName, italian)
            .ToList();

        return Ok(ApiResponse<List<EmployeeCostLookup>>.Ok(ordered));
    }

    // ══════════════════════════════════════════════════════════════
    // SEZIONI COSTO
    // ══════════════════════════════════════════════════════════════

    [HttpPost("sections")]
    public IActionResult AddSection(int projectId, [FromBody] ProjectCostSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_cost_sections (project_id, template_id, name, section_type, group_name, sort_order, is_enabled)
            VALUES (@ProjectId, @TemplateId, @Name, @SectionType, @GroupName, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { ProjectId = projectId, req.TemplateId, req.Name, req.SectionType, req.GroupName, req.SortOrder, req.IsEnabled });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id, "Sezione aggiunta"));
    }

    [HttpPatch("sections/{id}/field")]
    public IActionResult UpdateSectionField(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "is_enabled", "sort_order" };
        string? error = _db.UpdateField("project_cost_sections", id, req.Field, req.Value, allowed,
            "project_id=@projectId", new { projectId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("sections/{id}")]
    public IActionResult DeleteSection(int projectId, int id)
    {
        using var c = _db.Open();

        // I fogli delle calcolatrici della trasferta hanno owner polimorfo (la chiave porta l'id
        // della riga): nessuna FK se li porta via quando la CASCADE cancella le righe, vanno
        // cancellati a mano PRIMA di perdere gli id. Stessa precauzione di TravelController.
        List<int> travelRowIds = c.Query<int>(@"
            SELECT t.id FROM project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            WHERE t.section_id=@id AND s.project_id=@projectId", new { id, projectId }).ToList();

        // La risposta resta un Ok anche se la sezione non c'era più (comportamento storico:
        // cancellare due volte non è un errore da mostrare a video).
        int deleted = c.Execute(
            "DELETE FROM project_cost_sections WHERE id=@id AND project_id=@projectId", new { id, projectId });

        if (deleted > 0)
            ProjectCalcSheets.DeleteSheets(c, projectId,
                travelRowIds.SelectMany(ProjectCostTravelCalcKinds.ForRow));

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    // ══════════════════════════════════════════════════════════════
    // RISORSE
    // ══════════════════════════════════════════════════════════════

    [HttpPost("resources")]
    public IActionResult AddResource(int projectId, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day, hourly_cost, markup_value,
                num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance, sort_order)
            VALUES (@SectionId, @EmployeeId, @ResourceName, @WorkDays, @HoursPerDay, @HourlyCost, @MarkupValue,
                @NumTrips, @KmPerTrip, @CostPerKm, @DailyFood, @DailyHotel, @AllowanceDays, @DailyAllowance, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id, "Risorsa aggiunta"));
    }

    [HttpPut("resources/{id}")]
    public IActionResult UpdateResource(int projectId, int id, [FromBody] ProjectCostResourceSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE project_cost_resources SET employee_id=@EmployeeId, resource_name=@ResourceName,
                work_days=@WorkDays, hours_per_day=@HoursPerDay, hourly_cost=@HourlyCost, markup_value=@MarkupValue,
                num_trips=@NumTrips, km_per_trip=@KmPerTrip, cost_per_km=@CostPerKm,
                daily_food=@DailyFood, daily_hotel=@DailyHotel,
                allowance_days=@AllowanceDays, daily_allowance=@DailyAllowance, sort_order=@SortOrder
            WHERE id=@Id", req);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Risorsa aggiornata"));
    }

    [HttpDelete("resources/{id}")]
    public IActionResult DeleteResource(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_cost_resources WHERE id=@id", new { id });
        // Le righe di trasferta agganciate NON si cancellano: la FK è ON DELETE SET NULL, il
        // nominativo resta scritto sulla riga e la spesa non sparisce insieme alla risorsa.
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Risorsa eliminata"));
    }

    // ══════════════════════════════════════════════════════════════
    // TRASFERTA A RIGHE DELLA SEZIONE (segnalazione #33)
    // Prende il posto dei 7 campi digitati a mano nel dialogo risorsa. Il dettaglio di vitto,
    // indennità e auto sta nei fogli di calcolo del blocco 5, con la chiave che porta l'id
    // della riga: owner polimorfo, nessuna FK, quindi i fogli si cancellano a mano.
    // ══════════════════════════════════════════════════════════════

    private const string TravelRowColumns = @"
        t.id AS Id, t.section_id AS SectionId, t.resource_id AS ResourceId,
        t.person_name AS PersonName, t.nights AS Nights, t.night_price AS NightPrice,
        t.meal_cost AS MealCost, t.allowance_cost AS AllowanceCost, t.car_cost AS CarCost,
        t.transport_cost AS TransportCost, t.sort_order AS SortOrder, t.row_version AS RowVersion";

    /// <summary>Righe di trasferta di una sezione di costo, in ordine di griglia.</summary>
    [HttpGet("sections/{sectionId}/travel-rows")]
    public IActionResult GetTravelRows(int projectId, int sectionId)
    {
        using var c = _db.Open();
        if (!SectionBelongsToProject(c, projectId, sectionId))
            return Ok(ApiResponse<List<ProjectCostTravelRowDto>>.Fail("Sezione non trovata."));

        List<ProjectCostTravelRowDto> rows = c.Query<ProjectCostTravelRowDto>($@"
            SELECT {TravelRowColumns}
            FROM project_cost_travel_rows t
            WHERE t.section_id = @Sid
            ORDER BY t.sort_order, t.id",
            new { Sid = sectionId }).ToList();

        return Ok(ApiResponse<List<ProjectCostTravelRowDto>>.Ok(rows));
    }

    /// <summary>
    /// Riga nuova in coda. Il corpo è facoltativo: la griglia aggiunge una riga vuota e la
    /// compila dopo, con un solo salvataggio all'uscita dalla riga.
    /// </summary>
    [HttpPost("sections/{sectionId}/travel-rows")]
    public IActionResult CreateTravelRow(
        int projectId, int sectionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ProjectCostTravelRowSaveRequest? req)
    {
        using var c = _db.Open();
        if (!SectionBelongsToProject(c, projectId, sectionId))
            return Ok(ApiResponse<int>.Fail("Sezione non trovata."));

        int sortOrder = req is { SortOrder: > 0 }
            ? req.SortOrder
            : (c.ExecuteScalar<int?>(
                "SELECT MAX(sort_order) FROM project_cost_travel_rows WHERE section_id=@Sid",
                new { Sid = sectionId }) ?? 0) + 1;

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO project_cost_travel_rows
                (section_id, resource_id, person_name, nights, night_price, transport_cost, sort_order)
            VALUES (@Sid, @Rid, @Name, @Nights, @NightPrice, @Transport, @Sort);
            SELECT LAST_INSERT_ID()",
            new
            {
                Sid = sectionId,
                Rid = req?.ResourceId,
                Name = ResolvePersonName(c, sectionId, req?.ResourceId, req?.PersonName),
                Nights = req?.Nights,
                NightPrice = req?.NightPrice,
                Transport = req?.TransportCost,
                Sort = sortOrder,
            });

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id));
    }

    /// <summary>
    /// Salva una riga. Il client manda TUTTI i campi editabili insieme, una volta sola,
    /// all'uscita dalla riga (regola del blocco 4: il commit per campo faceva scartare in
    /// silenzio il secondo e il terzo salvataggio).
    /// Vitto, indennità e auto NON sono qui: li scrivono le calcolatrici, insieme al dettaglio.
    /// </summary>
    [HttpPut("travel-rows/{rowId}")]
    public IActionResult UpdateTravelRow(
        int projectId, int rowId, [FromBody] ProjectCostTravelRowSaveRequest req)
    {
        using var c = _db.Open();

        int? sectionId = c.ExecuteScalar<int?>(@"
            SELECT t.section_id FROM project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            WHERE t.id=@Id AND s.project_id=@Pid",
            new { Id = rowId, Pid = projectId });
        if (sectionId == null) return Ok(ApiResponse<bool>.Fail("Riga non trovata."));

        int rows = c.Execute(@"
            UPDATE project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            SET t.resource_id = @ResourceId,
                t.person_name = @PersonName,
                t.nights = @Nights,
                t.night_price = @NightPrice,
                t.transport_cost = @TransportCost,
                -- SortOrder a 0 = «non lo sto spostando»: un salvataggio di riga non deve
                -- rimescolare la griglia solo perché il client non ha rimandato l'ordine.
                t.sort_order = IF(@SortOrder > 0, @SortOrder, t.sort_order),
                t.row_version = t.row_version + 1
            WHERE t.id = @Id AND s.project_id = @Pid
              AND (@RowVersion IS NULL OR t.row_version = @RowVersion)",
            new
            {
                Id = rowId,
                Pid = projectId,
                req.ResourceId,
                PersonName = ResolvePersonName(c, sectionId.Value, req.ResourceId, req.PersonName),
                req.Nights,
                req.NightPrice,
                req.TransportCost,
                req.SortOrder,
                req.RowVersion,
            });

        if (rows == 0)
            return Ok(ApiResponse<bool>.Fail(
                "Riga modificata da un altro utente: ricarica il preventivo prima di risalvare."));

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("travel-rows/{rowId}")]
    public IActionResult DeleteTravelRow(int projectId, int rowId)
    {
        using var c = _db.Open();

        int deleted = c.Execute(@"
            DELETE t FROM project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            WHERE t.id=@Id AND s.project_id=@Pid",
            new { Id = rowId, Pid = projectId });
        if (deleted == 0) return Ok(ApiResponse<bool>.Fail("Riga non trovata."));

        // Niente FK sui fogli: se non li cancelliamo qui restano orfani per sempre.
        ProjectCalcSheets.DeleteSheets(c, projectId, ProjectCostTravelCalcKinds.ForRow(rowId));

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── LE 3 CALCOLATRICI DELLA RIGA ─────────────────────────────

    /// <summary>Dettaglio di una calcolatrice della riga (vitto | indennita | auto).</summary>
    [HttpGet("travel-rows/{rowId}/calc/{kind}")]
    public IActionResult GetTravelRowCalc(int projectId, int rowId, string kind)
    {
        if (!ProjectCostTravelCalcKinds.IsKnown(kind))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Calcolatrice sconosciuta."));

        using var c = _db.Open();
        if (!TravelRowBelongsToProject(c, projectId, rowId))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Riga non trovata."));

        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(
            ProjectCalcSheets.Load(c, projectId, ProjectCostTravelCalcKinds.SheetKey(kind, rowId))));
    }

    /// <summary>
    /// Conferma di una calcolatrice: salva il dettaglio E scrive il totale nella colonna della
    /// riga, in un colpo solo. Sono due scritture che devono restare d'accordo, quindi le fa il
    /// server: se le facesse il client, una connessione persa lascerebbe totale e dettaglio
    /// disallineati.
    /// </summary>
    [HttpPut("travel-rows/{rowId}/calc/{kind}")]
    public IActionResult SaveTravelRowCalc(
        int projectId, int rowId, string kind, [FromBody] ProjectCalcSheetSaveRequest req)
    {
        if (!ProjectCostTravelCalcKinds.IsKnown(kind))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Calcolatrice sconosciuta."));

        using var c = _db.Open();
        if (!TravelRowBelongsToProject(c, projectId, rowId))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Riga non trovata."));

        (ProjectCalcSheetDto? sheet, string? error) = ProjectCalcSheets.Save(
            c, projectId, ProjectCostTravelCalcKinds.SheetKey(kind, rowId), req, CurrentUserId);

        if (error != null || sheet == null)
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail(error ?? "Salvataggio non riuscito."));

        // Foglio vuoto → colonna a NULL: la cella torna «—», non 0,00 €.
        string column = ProjectCostTravelCalcKinds.Column(kind);
        c.Execute($@"
            UPDATE project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            SET t.`{column}` = @Total, t.row_version = t.row_version + 1
            WHERE t.id=@Id AND s.project_id=@Pid",
            new { Total = sheet.Total, Id = rowId, Pid = projectId });

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(sheet));
    }

    // ── UTILITÀ TRASFERTA ────────────────────────────────────────

    private static bool SectionBelongsToProject(System.Data.IDbConnection c, int projectId, int sectionId) =>
        c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_cost_sections WHERE id=@Sid AND project_id=@Pid",
            new { Sid = sectionId, Pid = projectId }) > 0;

    private static bool TravelRowBelongsToProject(System.Data.IDbConnection c, int projectId, int rowId) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            WHERE t.id=@Id AND s.project_id=@Pid",
            new { Id = rowId, Pid = projectId }) > 0;

    /// <summary>
    /// Il nome resta scritto sulla riga anche quando arriva dalle risorse pianificate: se la
    /// risorsa viene cancellata (FK ON DELETE SET NULL) la riga deve restare leggibile.
    /// </summary>
    private static string ResolvePersonName(
        System.Data.IDbConnection c, int sectionId, int? resourceId, string? personName)
    {
        string name = (personName ?? "").Trim();
        if (resourceId is int rid && name.Length == 0)
        {
            name = c.ExecuteScalar<string?>(
                "SELECT resource_name FROM project_cost_resources WHERE id=@Id AND section_id=@Sid",
                new { Id = rid, Sid = sectionId }) ?? "";
        }
        return name;
    }

    // ══════════════════════════════════════════════════════════════
    // MATERIALI
    // ══════════════════════════════════════════════════════════════

    [HttpPost("material-sections")]
    public IActionResult AddMaterialSection(int projectId, [FromBody] ProjectMaterialSectionDto req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_material_sections (project_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
            VALUES (@ProjectId, @CategoryId, @Name, @MarkupValue, @CommissionMarkup, @SortOrder, @IsEnabled);
            SELECT LAST_INSERT_ID();",
            new { ProjectId = projectId, req.CategoryId, req.Name, req.MarkupValue, req.CommissionMarkup, req.SortOrder, req.IsEnabled });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id, "Sezione materiale aggiunta"));
    }

    [HttpPatch("material-sections/{id}/field")]
    public IActionResult UpdateMaterialSectionField(int projectId, int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "markup_value", "commission_markup", "is_enabled", "sort_order" };
        string? error = _db.UpdateField("project_material_sections", id, req.Field, req.Value, allowed,
            "project_id=@projectId", new { projectId });
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("material-sections/{id}")]
    public IActionResult DeleteMaterialSection(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_material_sections WHERE id=@id AND project_id=@projectId", new { id, projectId });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Sezione eliminata"));
    }

    [HttpPost("material-items")]
    public IActionResult AddMaterialItem(int projectId, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(@"
            INSERT INTO project_material_items (section_id, parent_item_id, description, quantity, unit_cost, markup_value, item_type, sort_order)
            VALUES (@SectionId, @ParentItemId, @Description, @Quantity, @UnitCost, @MarkupValue, @ItemType, @SortOrder);
            SELECT LAST_INSERT_ID();", req);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(id, "Materiale aggiunto"));
    }

    [HttpPut("material-items/{id}")]
    public IActionResult UpdateMaterialItem(int projectId, int id, [FromBody] ProjectMaterialItemSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"
            UPDATE project_material_items SET parent_item_id=@ParentItemId, description=@Description,
                quantity=@Quantity, unit_cost=@UnitCost, markup_value=@MarkupValue,
                item_type=@ItemType, sort_order=@SortOrder
            WHERE id=@Id", req);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Materiale aggiornato"));
    }

    [HttpDelete("material-items/{id}")]
    public IActionResult DeleteMaterialItem(int projectId, int id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_material_items WHERE id=@id", new { id });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Materiale eliminato"));
    }

    /// <summary>Autocomplete descrizioni materiali già usate in tutti i progetti.</summary>
    [HttpGet("material-items/suggestions")]
    public IActionResult GetMaterialSuggestions(int projectId, [FromQuery] string q = "")
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(ApiResponse<List<string>>.Ok(new()));

        using var c = _db.Open();
        List<string> suggestions = c.Query<string>(@"
            SELECT DISTINCT description
            FROM project_material_items
            WHERE description LIKE CONCAT('%', @Query, '%')
              AND description != ''
            ORDER BY description
            LIMIT 15",
            new { Query = q.Trim() }).ToList();

        return Ok(ApiResponse<List<string>>.Ok(suggestions));
    }

    // ══════════════════════════════════════════════════════════════
    // PRICING
    // ══════════════════════════════════════════════════════════════

    [HttpPut("pricing")]
    public IActionResult UpdatePricing(int projectId, [FromBody] ProjectPricingDto req)
    {
        // I ricarichi sono MOLTIPLICATORI (1,450 = +45%), non percentuali. Senza questo
        // controllo un K a 0 arrivato da una chiamata fuori dalla pagina azzererebbe in
        // silenzio tutte le spese di trasferta a preventivo: stessa banda di ProjectCalcSheets.
        if (!IsValidMarkup(req.TravelMarkup))
            return Ok(ApiResponse<string>.Fail("K trasferta fuori intervallo: ammesso da 1,000 a 9,999 (1,450 = +45%)"));
        if (!IsValidMarkup(req.AllowanceMarkup))
            return Ok(ApiResponse<string>.Fail("K indennità fuori intervallo: ammesso da 1,000 a 9,999 (1,450 = +45%)"));

        using var c = _db.Open();
        c.Execute(@"
            UPDATE project_pricing SET
                contingency_pct=@ContingencyPct,
                negotiation_margin_pct=@NegotiationMarginPct,
                travel_markup=@TravelMarkup, allowance_markup=@AllowanceMarkup
            WHERE project_id=@projectId",
            new
            {
                req.ContingencyPct,
                req.NegotiationMarginPct,
                req.TravelMarkup,
                req.AllowanceMarkup,
                projectId
            });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Prezzi aggiornati"));
    }

    /// <summary>Moltiplicatore di ricarico ammesso: da 1,000 a 9,999 (stessa banda dei fogli di calcolo).</summary>
    private static bool IsValidMarkup(decimal value) => value >= 1.000m && value <= 9.999m;

    [HttpPut("sections/{id}/distribution")]
    public IActionResult UpdateSectionDistribution(int projectId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE project_cost_sections
            SET contingency_pct=@ContPct, margin_pct=@MargPct,
                contingency_pinned=@ContPin, margin_pinned=@MargPin,
                is_shadowed=@Shadowed
            WHERE id=@Id AND project_id=@projectId",
            new
            {
                ContPct = req.ContingencyPct,
                MargPct = req.MarginPct,
                ContPin = req.ContingencyPinned,
                MargPin = req.MarginPinned,
                Shadowed = req.IsShadowed,
                Id = id,
                projectId
            });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Distribuzione aggiornata"));
    }

    [HttpPut("material-items/{id}/distribution")]
    public IActionResult UpdateMaterialItemDistribution(int projectId, int id, [FromBody] SectionDistributionDto req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE project_material_items
            SET contingency_pct=@ContPct, margin_pct=@MargPct,
                contingency_pinned=@ContPin, margin_pinned=@MargPin,
                is_shadowed=@Shadowed
            WHERE id=@Id",
            new
            {
                ContPct = req.ContingencyPct,
                MargPct = req.MarginPct,
                ContPin = req.ContingencyPinned,
                MargPin = req.MarginPinned,
                Shadowed = req.IsShadowed,
                Id = id
            });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<string>.Ok("", "Distribuzione materiale aggiornata"));
    }
}
