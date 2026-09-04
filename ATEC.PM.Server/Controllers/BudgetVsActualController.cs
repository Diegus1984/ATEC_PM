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
[Route("api/projects/{projectId}/budget-vs-actual")]
[Authorize]
// Dati economici (budget/actual): visibili solo da livello PM in su (feature data.budget).
[RequireFeature("data.budget")]
// #88: ogni scrittura di questo controller riguarda UNA commessa (l'id sta nella rotta),
// quindi il cancello si mette una volta sola sulla classe: una commessa in bozza, in
// stand-by o chiusa si consulta ma non si modifica, salvo il permesso di scavalco.
[RequireProjectWritable]
// #88: e una bozza non si VEDE proprio, letture comprese — 404 come se non esistesse.
[RequireProjectVisible]
public class BudgetVsActualController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    private readonly AnagraficheCache _cache;
    public BudgetVsActualController(DbService db, IHubContext<ProjectHub> hub, AnagraficheCache cache)
    {
        _db = db;
        _hub = hub;
        _cache = cache;
    }

    private int CurrentUserId =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Ambiente condiviso: chi ha aperto il Bilancio della commessa e chi guarda la pagina
    /// /bilancio cross-commessa ricaricano appena qualcuno tocca ordine o vendita.
    /// </summary>
    private void NotifyBudgetChanged(int projectId)
    {
        object payload = new { projectId };
        _hub.Clients.Group(ProjectHub.ProjectGroup(projectId)).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
        _hub.Clients.Group(ProjectHub.ProjectsGroup).SendAsync("BudgetChanged", payload).SenzaAttesa("BudgetChanged");
    }

    [HttpGet]
    public IActionResult Get(int projectId)
    {
        using var c = _db.Open();

        // NB: la commessa NON ha una colonna linked_quote_id. Il legame con
        // l'offerta è inverso: quotes.project_id → la commessa (una commessa è
        // "convertita" se esiste una quote che la referenzia). Stessa logica di
        // ProjectsController.GetById.
        var proj = c.QueryFirstOrDefault<dynamic>(
            @"SELECT revenue, sale_total, actual_travel_cost, budget_total, budget_hours_total,
                     COALESCE((SELECT q.id FROM quotes q WHERE q.project_id = p.id LIMIT 1), 0) AS linked_quote_id
              FROM projects p WHERE p.id=@pid",
            new { pid = projectId });

        // ── PREVENTIVO: sezioni + risorse ──────────────────────────
        var sections = c.Query<dynamic>(@"
            SELECT s.id, s.name, s.section_type, s.group_name, s.sort_order, s.template_id,
                   COALESCE(csg.bg_color, '#6B7280') AS group_color,
                   COALESCE(csg.sort_order, 99) AS group_sort
            FROM project_cost_sections s
            LEFT JOIN cost_section_templates cst ON cst.id = s.template_id
            LEFT JOIN cost_section_groups csg ON csg.id = cst.group_id
            WHERE s.project_id = @pid AND s.is_enabled = 1
            ORDER BY s.sort_order", new { pid = projectId }).ToList();

        var resources = c.Query<dynamic>(@"
            SELECT r.id AS ResourceId,
                   r.employee_id AS EmployeeId,
                   r.section_id AS SectionId,
                   r.resource_name AS ResourceName,
                   r.work_days AS WorkDays,
                   r.hours_per_day AS HoursPerDay,
                   (r.work_days * r.hours_per_day) AS TotalHours,
                   r.hourly_cost AS HourlyCost,
                   (r.work_days * r.hours_per_day * r.hourly_cost) AS TotalCost,
                   r.markup_value AS MarkupValue,
                   (r.work_days * r.hours_per_day * r.hourly_cost * r.markup_value) AS TotalSale,
                   r.num_trips AS NumTrips,
                   r.km_per_trip AS KmPerTrip,
                   r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood,
                   r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays,
                   r.daily_allowance AS DailyAllowance
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.project_id = @pid AND s.is_enabled = 1
            ORDER BY r.sort_order", new { pid = projectId }).ToList();

        var resourcesBySection = resources.GroupBy(r => (int)r.SectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── TRASFERTA A RIGHE (segnalazione #33) ───────────────────
        // Somma per sezione delle righe di project_cost_travel_rows. Le colonne restano
        // separate perché il K si applica a tutte tranne l'indennità.
        // NB: la formula di riga vive in ProjectCostTravelRowDto (MarkableCost / TravelCost);
        // qui è riscritta in SQL per non caricare in memoria tutte le righe di una commessa —
        // se cambia una delle due, l'altra va cambiata insieme.
        var travelRowsBySection = c.Query<TravelRowSectionTotals>(@"
            SELECT t.section_id                                       AS SectionId,
                   SUM(COALESCE(t.nights,0) * COALESCE(t.night_price,0)) AS LodgingCost,
                   SUM(COALESCE(t.meal_cost,0))                       AS MealCost,
                   SUM(COALESCE(t.car_cost,0))                        AS CarCost,
                   SUM(COALESCE(t.transport_cost,0))                  AS TransportCost,
                   SUM(COALESCE(t.allowance_cost,0))                  AS AllowanceCost
            FROM project_cost_travel_rows t
            JOIN project_cost_sections s ON s.id = t.section_id
            WHERE s.project_id = @pid AND s.is_enabled = 1
            GROUP BY t.section_id", new { pid = projectId })
            .ToDictionary(t => t.SectionId);

        // ── ASSEGNATE: ore da phase_assignments aggregate per cost_section ──
        // v74: la sezione è lo snapshot sulla riga della fase di commessa, senza ripiego sul
        // template — che con le fasi multi-sezione ne indicherebbe una sola, cioè una a caso.
        var assigned = c.Query<dynamic>(@"
            SELECT pcs.id AS CostSectionId,
                   SUM(pa.planned_hours) AS Hours
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            JOIN project_cost_sections pcs
                 ON pcs.template_id = pp.cost_section_template_id
                AND pcs.project_id = pp.project_id
            WHERE pp.project_id = @pid AND pcs.is_enabled = 1
            GROUP BY pcs.id", new { pid = projectId }).ToList();

        var assignedBySection = assigned.ToDictionary(a => (int)a.CostSectionId);

        // ── CONSUNTIVO: usa view v_timesheet_with_section ────────────
        var actuals = c.Query<dynamic>(@"
            SELECT pcs.id AS CostSectionId,
                   vt.employee_id AS EmployeeId,
                   vt.employee_name AS EmployeeName,
                   vt.phase_name AS PhaseName,
                   vt.entry_type AS EntryType,
                   vt.work_date AS WorkDate,
                   vt.hours AS Hours,
                   vt.hourly_cost AS HourlyCost
            FROM v_timesheet_with_section vt
            JOIN project_cost_sections pcs
                 ON pcs.project_id = vt.project_id
                AND pcs.template_id = vt.cost_section_template_id
            WHERE vt.project_id = @pid
              AND vt.cost_section_template_id IS NOT NULL
              AND pcs.is_enabled = 1
              -- Extra Lavoro (#39): le ore che il PM ha spostato non pesano sui costi della
              -- commessa, a meno che le abbia rimesse dentro dalla pagina Extra Lavoro.
              AND vt.counts_in_project = 1
            ORDER BY vt.employee_name, vt.work_date, vt.phase_name",
            new { pid = projectId }).ToList();

        // Ore orfane: TUTTE quelle che non trovano una sezione abilitata nella commessa.
        // Prima qui c'era solo `cost_section_template_id IS NULL`, e le ore su una fase la
        // cui sezione NON è fra quelle della commessa (preventivo mai inizializzato, sezione
        // aggiunta all'anagrafica dopo, sezione disabilitata) sparivano dal consuntivo in
        // silenzio — il totale ore della Dashboard le contava, il Bilancio no, e nessuno a
        // video diceva perché. Il LEFT JOIN è il complemento esatto della query qui sopra:
        // ogni ora imputata finisce O in una sezione O fra le orfane, mai nel nulla.
        // SectionName dice QUALE sezione manca, così chi legge sa cosa aggiungere.
        var orphanActuals = c.Query<dynamic>(@"
            SELECT COALESCE(cst.name, '') AS SectionName,
                   vt.employee_id AS EmployeeId,
                   vt.employee_name AS EmployeeName,
                   vt.phase_name AS PhaseName,
                   vt.entry_type AS EntryType,
                   vt.work_date AS WorkDate,
                   vt.hours AS Hours,
                   vt.hourly_cost AS HourlyCost
            FROM v_timesheet_with_section vt
            LEFT JOIN project_cost_sections pcs
                 ON pcs.project_id = vt.project_id
                AND pcs.template_id = vt.cost_section_template_id
                AND pcs.is_enabled = 1
            LEFT JOIN cost_section_templates cst ON cst.id = vt.cost_section_template_id
            WHERE vt.project_id = @pid
              AND pcs.id IS NULL
              AND vt.counts_in_project = 1   -- Extra Lavoro (#39), come sopra
            ORDER BY vt.employee_name, vt.work_date, vt.phase_name",
            new { pid = projectId }).ToList();

        var actualsBySection = actuals.GroupBy(a => (int)a.CostSectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── BUILD RESULT ───────────────────────────────────────────
        var grouped = sections.GroupBy(s => (string)s.group_name)
            .OrderBy(g => g.Min(s => (int)s.group_sort));

        var result = new BudgetVsActualData 
        { 
            ProjectId = projectId,
            LinkedQuoteId = (int)(proj?.linked_quote_id ?? 0)
        };

        foreach (var grp in grouped)
        {
            var groupDto = new BvaGroupDto
            {
                GroupName = grp.Key,
                Color = (string)grp.First().group_color,
                SortOrder = (int)grp.First().group_sort
            };

            foreach (var sec in grp.OrderBy(s => (int)s.sort_order))
            {
                int secId = (int)sec.id;
                var sectionDto = new BvaSectionDto
                {
                    SectionId = secId,
                    SectionName = (string)sec.name,
                    SectionType = (string)sec.section_type,
                    TemplateId = (int?)sec.template_id
                };

                // Preventivo
                if (resourcesBySection.TryGetValue(secId, out var resList))
                {
                    foreach (var r in resList)
                    {
                        sectionDto.BudgetResources.Add(new BvaBudgetResourceDto
                        {
                            ResourceId = (int)r.ResourceId,
                            EmployeeId = (int?)r.EmployeeId,
                            ResourceName = (string)r.ResourceName,
                            WorkDays = (decimal)r.WorkDays,
                            HoursPerDay = (decimal)r.HoursPerDay,
                            TotalHours = (decimal)r.TotalHours,
                            HourlyCost = (decimal)r.HourlyCost,
                            TotalCost = (decimal)r.TotalCost,
                            MarkupValue = (decimal)r.MarkupValue,
                            TotalSale = (decimal)r.TotalSale,
                            NumTrips = (int)r.NumTrips,
                            KmPerTrip = (decimal)r.KmPerTrip,
                            CostPerKm = (decimal)r.CostPerKm,
                            DailyFood = (decimal)r.DailyFood,
                            DailyHotel = (decimal)r.DailyHotel,
                            AllowanceDays = (int)r.AllowanceDays,
                            DailyAllowance = (decimal)r.DailyAllowance
                        });
                    }
                    sectionDto.BudgetHours = sectionDto.BudgetResources.Sum(r => r.TotalHours);
                    sectionDto.BudgetCost = sectionDto.BudgetResources.Sum(r => r.TotalCost);
                    sectionDto.BudgetSale = sectionDto.BudgetResources.Sum(r => r.TotalSale);
                    // Trasferta «legacy»: i 7 campi digitati sulla risorsa pianificata.
                    // Valgono solo finché la sezione non ha righe di trasferta (vedi sotto).
                    sectionDto.BudgetTravelCost = sectionDto.BudgetResources.Sum(r => r.TravelCost);
                    sectionDto.BudgetAccommodationCost = sectionDto.BudgetResources.Sum(r => r.AccommodationCost);
                    sectionDto.BudgetAllowanceCost = sectionDto.BudgetResources.Sum(r => r.AllowanceCost);
                }

                // Regola anti-doppio-conteggio (#33): i 7 campi legacy non si cancellano dal
                // database, quindi la sorgente dev'essere UNA SOLA. Stessa forma già collaudata
                // sul lato consuntivo (foglio di calcolo ?? importo digitato a mano): se la
                // sezione ha almeno una riga si usano SOLO le righe.
                if (travelRowsBySection.TryGetValue(secId, out var travelRows))
                {
                    sectionDto.BudgetTravelFromRows = true;
                    sectionDto.BudgetAccommodationCost = travelRows.LodgingCost + travelRows.MealCost;
                    sectionDto.BudgetTravelCost = travelRows.CarCost + travelRows.TransportCost;
                    sectionDto.BudgetAllowanceCost = travelRows.AllowanceCost;
                }

                // Assegnate
                if (assignedBySection.TryGetValue(secId, out var asg))
                {
                    sectionDto.AssignedHours = (decimal)asg.Hours;
                    // Costo assegnato = ore assegnate × costo orario medio della sezione
                    decimal avgCost = sectionDto.BudgetHours > 0
                        ? sectionDto.BudgetCost / sectionDto.BudgetHours : 0;
                    sectionDto.AssignedCost = sectionDto.AssignedHours * avgCost;
                }

                // Consuntivo
                if (actualsBySection.TryGetValue(secId, out var actList))
                    BuildActualEmployees(sectionDto, actList);

                groupDto.Sections.Add(sectionDto);
            }

            groupDto.BudgetHours = groupDto.Sections.Sum(s => s.BudgetHours);
            groupDto.BudgetCost = groupDto.Sections.Sum(s => s.BudgetCost);
            groupDto.AssignedHours = groupDto.Sections.Sum(s => s.AssignedHours);
            groupDto.AssignedCost = groupDto.Sections.Sum(s => s.AssignedCost);
            groupDto.ActualHours = groupDto.Sections.Sum(s => s.ActualHours);
            groupDto.ActualCost = groupDto.Sections.Sum(s => s.ActualCost);
            result.Groups.Add(groupDto);
        }

        // Gruppo orfano: una riga per ogni sezione mancante, col nome della sezione, così
        // si vede subito COSA aggiungere alla commessa perché quelle ore trovino casa.
        // Le fasi davvero senza sezione restano sotto la voce storica.
        if (orphanActuals.Any())
        {
            var orphanGroup = new BvaGroupDto { GroupName = "NON ASSEGNATO", SortOrder = 999 };
            foreach (var perSezione in orphanActuals
                         .GroupBy(a => (string)a.SectionName)
                         .OrderBy(g => g.Key))
            {
                var orphanSection = new BvaSectionDto
                {
                    SectionName = perSezione.Key == ""
                        ? "Fasi senza sezione costo"
                        : $"{perSezione.Key} — sezione non presente in questa commessa",
                };
                BuildActualEmployees(orphanSection, perSezione);
                orphanGroup.Sections.Add(orphanSection);
            }
            orphanGroup.ActualHours = orphanGroup.Sections.Sum(s => s.ActualHours);
            orphanGroup.ActualCost = orphanGroup.Sections.Sum(s => s.ActualCost);
            result.Groups.Add(orphanGroup);
        }

        result.TotalBudgetHours = result.Groups.Sum(g => g.BudgetHours);
        result.TotalBudgetCost = result.Groups.Sum(g => g.BudgetCost);
        result.TotalAssignedHours = result.Groups.Sum(g => g.AssignedHours);
        result.TotalAssignedCost = result.Groups.Sum(g => g.AssignedCost);
        result.TotalActualHours = result.Groups.Sum(g => g.ActualHours);
        result.TotalActualCost = result.Groups.Sum(g => g.ActualCost);

        // ── MATERIALI ─────────────────────────────────────────────
        var matSections = c.Query<dynamic>(@"
            SELECT id, id AS SectionId, name, markup_value, commission_markup
            FROM project_material_sections
            WHERE project_id = @pid AND is_enabled = 1
            ORDER BY sort_order", new { pid = projectId }).ToList();

        var matItemsRaw = c.Query<dynamic>(@"
            SELECT pmi.section_id AS SectionId, pmi.id AS Id, pmi.parent_item_id AS ParentItemId,
                   pmi.description AS Description, pmi.quantity AS Quantity,
                   pmi.unit_cost AS UnitCost, pmi.markup_value AS MarkupValue,
                   pmi.item_type AS ItemType
            FROM project_material_items pmi
            JOIN project_material_sections pms ON pms.id = pmi.section_id
            WHERE pms.project_id = @pid AND pms.is_enabled = 1
              AND (pmi.quantity > 0 OR pmi.unit_cost > 0)
            ORDER BY pmi.sort_order", new { pid = projectId }).ToList();

        var matItemsBySection = matItemsRaw.GroupBy(i => (int)i.SectionId)
            .ToDictionary(g => g.Key, g => g.Select(i => new BvaMaterialItemDto
            {
                Id = (int)i.Id,
                ParentItemId = (int?)i.ParentItemId,
                Description = (string)i.Description,
                Quantity = (decimal)i.Quantity,
                UnitCost = i.UnitCost ?? 0m,
                MarkupValue = (decimal)i.MarkupValue,
                ItemType = (string)i.ItemType
            }).ToList());

        foreach (var ms in matSections)
        {
            int msId = (int)ms.id;
            var secDto = new BvaMaterialSectionDto
            {
                SectionId = msId,
                SectionName = (string)ms.name,
                MarkupValue = (decimal)ms.markup_value,
                CommissionMarkup = (decimal)ms.commission_markup
            };
            if (matItemsBySection.TryGetValue(msId, out var items))
            {
                secDto.Items = items;
                secDto.TotalNetCost = items.Sum(i => i.NetCost);
                secDto.TotalSaleCost = items.Sum(i => i.SaleCost);
            }
            result.MaterialSections.Add(secDto);
        }

        result.TotalMaterialNetCost = result.MaterialSections.Sum(s => s.TotalNetCost);
        result.TotalMaterialSaleCost = result.MaterialSections.Sum(s => s.TotalSaleCost);

        // ── LAVORAZIONI OFFICINE A PREVENTIVO ────────────────────
        // È l'unica delle 4 voci del Riepilogo Costi senza una struttura che la alimenti
        // (le altre tre vengono da risorse pianificate, materiali e trasferta): la compila
        // la finestra di calcolo a righe del blocco 5.
        result.WorkshopBudgetSheet = ProjectCalcSheets.Load(c, projectId, CalcKeys.WorkshopBudget);
        decimal? budgetWorkshopDisplay = result.WorkshopBudgetSheet.Total;
        decimal budgetWorkshopCost = budgetWorkshopDisplay ?? 0;
        decimal budgetWorkshopSale = result.WorkshopBudgetSheet.SaleTotal ?? 0;

        // ── SCHEDA PREZZI ─────────────────────────────────────────
        var pricing = c.QueryFirstOrDefault<dynamic>(
            @"SELECT contingency_pct, negotiation_margin_pct, final_price_override
              FROM project_pricing WHERE project_id=@pid",
            new { pid = projectId });

        // ── TRASFERTA A PREVENTIVO: a COSTO SECCO (decisione D34-K, 06/08/2026) ──
        // La #33 aveva introdotto un K di ricarico sulle spese (alloggio, vitto, auto,
        // treno/aereo) lasciando fuori l'indennità. La sera stessa Paolo l'ha ritirato:
        // «ELIMINIAMO LA LOGICA DI RICARICO K per le trasferte. Restano solo importi di costo
        // Netto senza ricarichi e questo rende il tutto coerente.»
        // Rimozione a costo zero: in produzione nessuna commessa e nessuna offerta aveva mai
        // usato un K ≠ 1,000, quindi non si è mosso un solo importo.
        // `project_pricing.travel_markup` NON è stata cancellata: resta a 1,000 come colonna
        // dormiente, così riaccendere il ricarico è una riga e il Commerciale, che la legge
        // ancora nel suo DTO, non si rompe.
        // Effetto collaterale voluto: sparisce l'asimmetria preventivo/consuntivo della D33-A.
        // I due lati della voce «Spese Trasferta / indennità» sono ora **omogenei**, entrambi
        // a costo secco, e i due hint che dichiaravano l'asimmetria non servono più.
        decimal budgetTravelMarkable = result.Groups.Sum(g => g.Sections.Sum(s => s.BudgetTravelMarkableCost));
        decimal budgetTravelAllowance = result.Groups.Sum(g => g.Sections.Sum(s => s.BudgetAllowanceCost));
        decimal budgetTravelTotal = budgetTravelMarkable + budgetTravelAllowance;

        // «Compilata» = almeno una sezione ha righe di trasferta. Serve alla regola del «—»:
        // un calcolo a righe che somma zero è un dato (0,00 €), l'assenza di righe è un vuoto.
        bool budgetTravelFromRows = result.Groups.Any(g => g.Sections.Any(s => s.BudgetTravelFromRows));

        // ── I DUE TOTALI DI PREVENTIVO DELLA COMMESSA (segnalazione #34) ──────────────
        // Paolo li vuole entrambi in fondo all'Ordine Commessa, sullo stesso asse:
        //   TOTALE COSTI            = somma della colonna NETTI di tutte le sezioni + trasferta
        //   TOTALE COSTI DI VENDITA = somma della colonna VENDITA di tutte le sezioni + trasferta
        // Il secondo esisteva già, ma nascosto dentro `BvaPricingDto.NetCost` e con un nome che
        // diceva il contrario di quello che è (di qui la rinomina della #36 nella Scheda Prezzi).
        // Si calcolano FUORI dal ramo `pricing != null`: una commessa senza scheda prezzi ha
        // comunque i suoi due totali, e il Margine di Sicurezza deve poterli usare lo stesso.
        // La trasferta entra in tutti e due con lo stesso numero, perché dopo la D34-K non c'è
        // più un lato "vendita" della trasferta distinto dal costo.
        decimal resourceSale = result.Groups.Sum(g => g.Sections.Sum(s => s.BudgetSale));
        decimal totalBudgetSaleCost = resourceSale + result.TotalMaterialSaleCost
                                      + budgetWorkshopSale + budgetTravelTotal;
        // NB: il gemello «TOTALE COSTI» (colonna Netti) NON si calcola qui. Vale per
        // definizione `budgetCost`, che più sotto ha una regola in più: se la commessa non ha
        // sezioni di preventivo ripiega su `projects.budget_total`, il budget digitato a mano.
        // Ricalcolarlo qui senza quel ripiego darebbe 0 nel footer dell'Ordine e il valore vero
        // nel riquadro del Conto Economico — due «Totale Costi» diversi nella stessa pagina,
        // che è esattamente ciò che la #34 doveva togliere di mezzo.

        if (pricing != null)
        {
            // Il «costo netto» storico della Scheda Prezzi È il Totale Costi di Vendita: stesso
            // numero, un nome solo (#34 + #36). Da qui partono Contingency, Offerta e Finale.
            decimal netCost = totalBudgetSaleCost;

            decimal contPct = (decimal)pricing.contingency_pct;
            decimal contAmount = netCost * contPct;
            decimal offerPrice = netCost + contAmount;

            decimal negPct = (decimal)pricing.negotiation_margin_pct;
            decimal negAmount = offerPrice * negPct;
            decimal finalPrice = offerPrice + negAmount;

            result.Pricing = new BvaPricingDto
            {
                NetCost = netCost,
                ContingencyPct = contPct,
                ContingencyAmount = contAmount,
                OfferPrice = offerPrice,
                NegotiationPct = negPct,
                NegotiationAmount = negAmount,
                FinalPrice = finalPrice
            };
        }

        // ── RIEPILOGO ECONOMICO ──────────────────────────────────

        // Consuntivo acquisti: esclusi solo gli stati «esclusi da totale» (aggregazione A9)
        // — i consegnati restano (sono un costo reale). Stessa regola della dashboard commessa.
        // Dal 04/08/2026 le due metà sono SEPARATE: «Materiali commerciali» (bom_items) e
        // «Lavorazioni Officine» (ddp_officina_items, a sua volta divisa interne/esterne).
        // È una ripartizione a somma invariata: actualTotalCost non cambia di un centesimo.
        string[] ddpExcluded = DdpAggregationSet.Load(c, "A9", _cache);
        decimal actualMaterialCost = ProjectEconomics.GetCommercialMaterialCost(c, projectId, ddpExcluded);
        ProjectEconomics.WorkshopCost workshop = ProjectEconomics.GetWorkshopCost(c, projectId, ddpExcluded);

        // Tecnici attivi e fasi
        int activeTechs = c.ExecuteScalar<int>(@"
            SELECT COUNT(DISTINCT pa.employee_id)
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            WHERE pp.project_id = @pid", new { pid = projectId });

        var phaseCounts = c.QueryFirstOrDefault<dynamic>(@"
            -- Le fasi spente (#51) stanno fuori dall'elenco e dal Timesheet: contarle
            -- al denominatore dell'avanzamento vorrebbe dire chiedere di completare
            -- fasi che nessuno vede più.
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN status='COMPLETED' THEN 1 ELSE 0 END) AS Completed
            FROM project_phases WHERE is_off = 0 AND project_id = @pid", new { pid = projectId });

        // ── ORDINE COMMESSA ──────────────────────────────────────
        // Alla prima apertura la commessa riceve la sua riga d'ordine, seeddata dal revenue
        // esistente: il Totale Ordine non cambia, cambia solo dove è scritto.
        ProjectEconomics.EnsureOrderLines(c, projectId);
        result.OrderLines = ProjectEconomics.GetOrderLines(c, projectId);

        // Totale Ordine = somma delle righe (≡ projects.revenue, che il server tiene allineato
        // ad ogni scrittura). `displayOrderTotal` è NULL se nessuna riga ha un importo: serve a
        // scrivere «—» invece di 0,00 €, mentre i calcoli usano lo 0.
        decimal? displayOrderTotal = ProjectEconomics.DisplayOrderTotal(result.OrderLines);
        decimal orderPrice = displayOrderTotal ?? 0;

        // ── TOTALE COSTI DI VENDITA: CALCOLATO, non più digitato (segnalazione #34) ──
        // Fino al 06/08/2026 era `projects.sale_total`, un importo scritto a mano nel footer
        // dell'Ordine Commessa. Paolo ha deciso che lo calcola il programma.
        // ⚠ CONSEGUENZA ACCETTATA CONSAPEVOLMENTE: dove il valore digitato non coincideva con
        // il calcolato, il Margine di Sicurezza **è cambiato il giorno del deploy**. La colonna
        // `sale_total` NON viene cancellata — resta come traccia di quello che era stato scritto
        // a mano, e la si può rileggere se qualcuno contesta un numero storico.
        // «—» solo se non c'è NIENTE di preventivato: un preventivo che somma zero è un dato.
        bool hasBudgetData = result.Groups.Any() || budgetWorkshopSale != 0 || result.MaterialSections.Any();
        decimal? saleTotal = hasBudgetData ? totalBudgetSaleCost : null;
        // Se la commessa ha una trasferta compilata (blocco 6) il costo lo dice il suo foglio
        // di calcolo; altrimenti resta il valore digitato a mano, come per tutte le commesse
        // di prima. Stessa regola in SQL in /bilancio e nella dashboard commessa.
        //
        // Dal 06/08/2026 (D34-K) i due lati della voce «Spese Trasferta / indennità» sono
        // OMOGENEI: costo secco tutti e due. L'asimmetria della D33-A — preventivo ricaricato,
        // consuntivo no — è stata rimossa insieme al K, ed è la ragione per cui il confronto
        // preventivo/consuntivo di questa voce ora si legge senza note a piè di pagina.
        // Il costo del personale in trasferta resta comunque FUORI dal Bilancio (D6-C,
        // riconfermata da Paolo il 06/08): quelle ore arrivano dal timesheet come «Risorse
        // Atec», e portarle anche di qui le conterebbe due volte.
        decimal? travelSheetTotal = ProjectCalcSheets
            .Load(c, projectId, CalcKeys.TravelActual).Total;
        decimal actualTravelCost = travelSheetTotal ?? (decimal)(proj?.actual_travel_cost ?? 0m);

        // ── MARGINE DI SICUREZZA = Ordine − Totale Costi di Vendita (segnalazione #34) ──
        // Si chiamava «Delta Ordine». Paolo: «Rinomina Delta ordine con Margine di Sicurezza e
        // manteniamo Contingency per la restante gestione attuale.» È quindi il nome definitivo
        // dell'importo, e resta una grandezza DIVERSA dalla Contingency della Scheda Prezzi
        // (che è una % di imprevisti sul costo di vendita): la finestra di spiegazione lo dice.
        // Come nel prototipo si calcola appena UNO dei due esiste, NULL solo se mancano entrambi.
        decimal? orderDelta = (displayOrderTotal == null && saleTotal == null)
            ? null
            : (displayOrderTotal ?? 0) - (saleTotal ?? 0);

        // Se non ci sono gruppi (no preventivo), usiamo i totali manuali della commessa.
        // Le lavorazioni si sommano in ENTRAMBI i rami: la finestra di calcolo si compila
        // anche su una commessa senza preventivo strutturato.
        // La trasferta entra qui con lo STESSO valore ricaricato della voce «spese» del
        // Riepilogo Costi: il «Totale Costi» lo somma il client voce per voce, la Redditività
        // arriva da qui — se i due numeri divergessero le due righe non quadrerebbero a video.
        bool hasQuoteSections = result.Groups.Any();
        decimal computedBudgetCost = result.TotalBudgetCost + result.TotalMaterialNetCost + budgetTravelTotal;
        decimal budgetCost = (hasQuoteSections ? computedBudgetCost : (decimal)(proj?.budget_total ?? 0m))
                             + budgetWorkshopCost;
        decimal budgetResourceCost = hasQuoteSections ? result.TotalBudgetCost : (decimal)(proj?.budget_total ?? 0m);

        decimal actualTotalCost = result.TotalActualCost + actualMaterialCost + workshop.Total + actualTravelCost;
        int totalPhases = (int)(phaseCounts?.Total ?? 0);
        int completedPhases = (int)(phaseCounts?.Completed ?? 0);

        // Redditività su ENTRAMBI i lati, in € e in %, sempre rispetto al Totale Ordine
        // (mai al Totale Vendita, che entra solo nel Delta Ordine). Il lato preventivo
        // risponde alla domanda «il preventivo era già in perdita?».
        decimal budgetProfit = orderPrice - budgetCost;
        decimal actualProfit = orderPrice - actualTotalCost;

        // ── PREZZO OFFERTA FINALE: derivato, salvo override manuale (#35) ─────────────
        // Paolo: «Solo valore da mostrare». L'override NON si propaga a valle — Offerta,
        // Contingency e Margine restano quelli calcolati dalla Scheda Prezzi. Sostituisce
        // soltanto il numero del riquadro, e il riquadro lo dichiara.
        decimal? finalPriceOverride = (decimal?)pricing?.final_price_override;

        result.Economic = new BvaEconomicSummary
        {
            FinalOfferPrice = finalPriceOverride ?? result.Pricing?.FinalPrice ?? 0,
            FinalOfferPriceIsManual = finalPriceOverride != null,
            OrderPrice = orderPrice,
            SaleTotal = saleTotal,
            OrderDelta = orderDelta,
            // «TOTALE COSTI» del footer Ordine ≡ «Totale Costi» del Conto Economico: stesso
            // campo, così non possono divergere (vedi il commento sopra a totalBudgetSaleCost).
            TotalBudgetNetCost = budgetCost,
            TotalBudgetSaleCost = totalBudgetSaleCost,
            TotalNetCost = result.Pricing?.NetCost ?? 0,
            ContingencyAmount = result.Pricing?.ContingencyAmount ?? 0,
            BudgetCost = budgetCost,
            BudgetResourceHours = hasQuoteSections ? result.TotalBudgetHours : (decimal)(proj?.budget_hours_total ?? 0m),
            BudgetResourceCost = budgetResourceCost,
            BudgetMaterialCost = result.TotalMaterialNetCost,
            // Il COSTO, non la vendita (D4): il 45% è un ricarico commerciale e non deve
            // gonfiare la voce di costo del Riepilogo.
            BudgetWorkshopCost = budgetWorkshopCost,
            BudgetWorkshopInternalCost =
                ProjectCalcSheets.GroupTotal(result.WorkshopBudgetSheet, CalcGroups.Internal) ?? 0,
            BudgetWorkshopExternalCost =
                ProjectCalcSheets.GroupTotal(result.WorkshopBudgetSheet, CalcGroups.External) ?? 0,
            BudgetWorkshopSale = budgetWorkshopSale,
            // Dopo la D34-K la trasferta non è più un'eccezione: costo secco come le altre voci.
            // I due addendi restano separati perché il client scriva sotto la voce da cosa è
            // composta («spese + indennità»), non più perché uno dei due fosse ricaricato.
            BudgetTravelCost = budgetTravelTotal,
            BudgetTravelMarkableCost = budgetTravelMarkable,
            BudgetAllowanceCost = budgetTravelAllowance,
            ActualResourceHours = result.TotalActualHours,
            ActualResourceCost = result.TotalActualCost,
            ActualMaterialCost = actualMaterialCost,
            ActualWorkshopCost = workshop.Total,
            ActualWorkshopInternalCost = workshop.Internal,
            ActualWorkshopExternalCost = workshop.External,
            ActualWorkshopUnclassifiedCost = workshop.Unclassified,
            ActualTravelCost = actualTravelCost,
            ActualTravelFromPlan = travelSheetTotal != null,
            ActualTotalCost = actualTotalCost,
            BudgetProfitability = budgetProfit,
            BudgetProfitabilityPct = orderPrice > 0 ? Math.Round(budgetProfit / orderPrice * 100, 2) : 0,
            Profitability = actualProfit,
            ProfitabilityPct = orderPrice > 0 ? Math.Round(actualProfit / orderPrice * 100, 2) : 0,
            ActiveTechnicians = activeTechs,
            TotalPhases = totalPhases,
            CompletedPhases = completedPhases,
            ProgressPct = totalPhases > 0 ? (int)Math.Round((decimal)completedPhases / totalPhases * 100) : 0
        };

        // ── RIEPILOGO COSTI voce-per-voce ────────────────────────
        // Le 4 voci e le loro etichette sono quelle del prototipo V32, nello stesso ordine.
        // NULL = voce non valorizzata su quel lato → «—» a video (≠ 0,00 €).
        static decimal? NullIfZero(decimal value) => value == 0 ? null : value;

        // ── LAVORAZIONI OFFICINE IN DUE VOCI (segnalazione #54, 08/08/2026) ──
        // «Esterne» = comprate fuori, «Atec» = costruite in casa. La ripartizione esisteva già
        // nei numeri (interne/esterne di preventivo e di consuntivo), mancava a video: il
        // Riepilogo mostrava un totale unico e il PM non sapeva quanto stava spendendo dentro.
        // A somma invariata: le due voci più l'eventuale terza («non classificate») ridanno
        // esattamente il totale di prima, che è ciò che il footer e la redditività usano.
        // Preventivo: null se il foglio di calcolo non esiste ancora (a video «—»), altrimenti
        // il totale della sezione, anche se è zero — zero compilato è un dato.
        decimal? budgetWorkshopExternalDisplay = budgetWorkshopDisplay == null
            ? null
            : ProjectCalcSheets.GroupTotal(result.WorkshopBudgetSheet, CalcGroups.External) ?? 0;
        decimal? budgetWorkshopInternalDisplay = budgetWorkshopDisplay == null
            ? null
            : ProjectCalcSheets.GroupTotal(result.WorkshopBudgetSheet, CalcGroups.Internal) ?? 0;
        // Righe del foglio senza sezione: non dovrebbero esistere (la finestra le crea sempre
        // dentro una delle due), ma se ci sono il loro importo NON deve sparire dal totale.
        decimal budgetWorkshopUnclassified =
            (budgetWorkshopDisplay ?? 0)
            - (budgetWorkshopExternalDisplay ?? 0)
            - (budgetWorkshopInternalDisplay ?? 0);

        // I sottotitoli sono NOTE, mai numeri: gli importi li formatta il client con euro()
        // e le ore con il suo formattatore, altrimenti la cultura del server decide i separatori.
        result.CostLines = new List<BvaCostLineDto>
        {
            new()
            {
                Key = "risorse",
                Label = "Risorse Atec",
                Budget = NullIfZero(budgetResourceCost),
                Actual = NullIfZero(result.TotalActualCost),
                ActualHint = "dal timesheet reale",
            },
            new()
            {
                Key = "materiali",
                Label = "Materiali commerciali",
                Budget = NullIfZero(result.TotalMaterialNetCost),
                Actual = NullIfZero(actualMaterialCost),
                ActualHint = "dalla distinta DDP commerciale",
            },
            new()
            {
                Key = "lavorazioni_esterne",
                Label = "Lavorazione Officine Esterne",
                // NON NullIfZero: qui «—» vuol dire «nessuna riga di calcolo», mentre un
                // calcolo che somma zero è un dato compilato. Se lo dice il foglio, 0,00 €.
                Budget = budgetWorkshopExternalDisplay,
                Actual = NullIfZero(workshop.External),
                BudgetHint = budgetWorkshopDisplay == null
                    ? "da compilare con il calcolo a righe"
                    : "dal calcolo a righe, sezione Esterne",
                ActualHint = "dalla DDP officina, righe di tipo Esterna",
            },
            new()
            {
                Key = "lavorazioni_atec",
                Label = "Lavorazione Officine Atec",
                Budget = budgetWorkshopInternalDisplay,
                Actual = NullIfZero(workshop.Internal),
                BudgetHint = budgetWorkshopDisplay == null
                    ? "da compilare con il calcolo a righe"
                    : "dal calcolo a righe, sezione Interne",
                ActualHint = "dalla DDP officina, righe di tipo Interna",
            },
            new()
            {
                Key = "spese",
                Label = "Spese Trasferta / indennità",
                // Con la trasferta a righe «—» vuol dire «nessuna riga»: una tabella che somma
                // zero è un dato compilato, come per le lavorazioni. Senza righe si torna ai 7
                // campi legacy, dove zero vuol dire «non compilato» e resta NullIfZero.
                Budget = budgetTravelFromRows ? budgetTravelTotal : NullIfZero(budgetTravelTotal),
                // L'unica voce del Riepilogo con un valore ricaricato: scritto a video, o si
                // legge come un errore di calcolo.
                // Dopo la D34-K i due lati sono omogenei (costo secco entrambi): gli hint
                // dicono da DOVE viene il numero, non più che uno dei due è ricaricato.
                BudgetHint = budgetTravelFromRows
                    ? "dalle righe di trasferta del preventivo"
                    : "dai campi trasferta delle risorse",
                // Con la trasferta compilata «—» vuol dire «nessuno step»: un piano che somma
                // zero è un dato, non un vuoto. Senza, resta la regola di prima.
                Actual = travelSheetTotal ?? NullIfZero(actualTravelCost),
                ActualHint = travelSheetTotal != null
                    ? "dalla Gestione Trasferta"
                    : "importo digitato a mano",
            },
        };

        // Terza voce officine, SOLO se c'è qualcosa da metterci: righe di cui non si sa la
        // natura (nessun tipo congelato, nessuna lavorazione collegata e uno stato che non lo
        // dice). Attribuirle a una delle due sarebbe inventare; nasconderle farebbe sparire un
        // costo vero dal Riepilogo, quindi hanno una riga loro con scritto cosa sono.
        if (workshop.Unclassified != 0 || budgetWorkshopUnclassified != 0)
        {
            result.CostLines.Insert(4, new BvaCostLineDto
            {
                Key = "lavorazioni_nc",
                Label = "Lavorazioni Officine non classificate",
                Budget = NullIfZero(budgetWorkshopUnclassified),
                Actual = NullIfZero(workshop.Unclassified),
                BudgetHint = "righe del calcolo senza sezione",
                ActualHint = "righe DDP officina senza tipo Interna/Esterna",
            });
        }

        return Ok(ApiResponse<BudgetVsActualData>.Ok(result));
    }

    [HttpPatch("actual-travel-cost")]
    public IActionResult UpdateActualTravelCost(int projectId, [FromBody] decimal value)
    {
        using var c = _db.Open();
        c.Execute("UPDATE projects SET actual_travel_cost=@Val WHERE id=@Id",
            new { Val = value, Id = projectId });
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// <b>DISMESSO dalla segnalazione #34 (06/08/2026).</b> Il «Totale Costi di Vendita» non si
    /// digita più: lo calcola il server sommando la colonna Vendita di tutte le sezioni più la
    /// trasferta di preventivo. La rotta resta e risponde con un errore parlante invece di
    /// sparire, perché un client vecchio in cache che continuasse a chiamarla scriverebbe in
    /// silenzio un campo che nessuno legge più — e l'utente vedrebbe il suo numero sparire al
    /// primo ricalcolo senza capire perché.
    /// La colonna <c>projects.sale_total</c> non è stata cancellata: conserva ciò che era stato
    /// digitato a mano prima del cambio, se qualcuno contesta un valore storico.
    /// </summary>
    [HttpPatch("sale-total")]
    public IActionResult UpdateSaleTotal(int projectId, [FromBody] decimal? value)
    {
        return Ok(ApiResponse<bool>.Fail(
            "Il «Totale Costi di Vendita» ora lo calcola il programma dalle sezioni di costo del "
            + "preventivo e non si imposta più a mano. Ricarica la pagina per vedere il valore aggiornato."));
    }

    /// <summary>
    /// «Prezzo offerta finale» imputato a mano (segnalazione #35). Body: decimal grezzo, oppure
    /// <c>null</c> per tornare al valore calcolato dalla Scheda Prezzi.
    /// <para>
    /// È <b>solo il valore da mostrare</b>: non ricalcola Offerta, Contingency né Margine.
    /// La riga di <c>project_pricing</c> viene creata se non c'è, altrimenti su una commessa
    /// senza scheda prezzi il salvataggio non scriverebbe niente e l'utente vedrebbe il suo
    /// numero sparire al primo ricarico.
    /// </para>
    /// </summary>
    [HttpPatch("final-price-override")]
    public IActionResult UpdateFinalPriceOverride(int projectId, [FromBody] decimal? value)
    {
        if (value is < 0)
            return Ok(ApiResponse<bool>.Fail("Il prezzo non può essere negativo."));

        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<bool>.Fail("Commessa non trovata."));

        c.Execute(@"
            INSERT INTO project_pricing (project_id, final_price_override)
            VALUES (@Id, @Val)
            ON DUPLICATE KEY UPDATE final_price_override = @Val",
            new { Id = projectId, Val = value });

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ═══════════════════════════════════════════════════════════════
    // FOGLI DI CALCOLO A RIGHE — il dettaglio dietro il totale di una voce
    // La finestra lavora su una copia locale e conferma tutto in una PUT sola: niente
    // commit per campo, quindi nessuna delle due perdite di dati viste nel blocco 4
    // (refetch che cancella quello che si sta scrivendo, salvataggi che si scartano).
    // ═══════════════════════════════════════════════════════════════

    [HttpGet("calc/{calcKey}")]
    public IActionResult GetCalcSheet(int projectId, string calcKey)
    {
        if (!CalcKeys.IsKnown(calcKey))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Foglio di calcolo sconosciuto."));

        using var c = _db.Open();
        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(
            ProjectCalcSheets.Load(c, projectId, calcKey)));
    }

    /// <summary>
    /// Conferma della finestra: riscrive l'intero foglio e restituisce quello salvato.
    /// Le righe vuote le scarta il server, non solo la UI.
    /// </summary>
    [HttpPut("calc/{calcKey}")]
    public IActionResult SaveCalcSheet(int projectId, string calcKey, [FromBody] ProjectCalcSheetSaveRequest req)
    {
        if (!CalcKeys.IsKnown(calcKey))
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail("Foglio di calcolo sconosciuto."));

        using var c = _db.Open();
        (ProjectCalcSheetDto? sheet, string? error) =
            ProjectCalcSheets.Save(c, projectId, calcKey, req, CurrentUserId > 0 ? CurrentUserId : null);

        if (error != null || sheet == null)
            return Ok(ApiResponse<ProjectCalcSheetDto>.Fail(error ?? "Salvataggio non riuscito."));

        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<ProjectCalcSheetDto>.Ok(sheet));
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDINE COMMESSA — righe Ordine / Posizione / Importo
    // Ogni scrittura riallinea projects.revenue alla somma delle righe: SAL, dashboard e
    // cash flow continuano a leggere revenue senza sapere che l'ordine è diventato multi-riga.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Aggiunge una riga in coda, oppure subito sotto <c>AfterLineId</c>.</summary>
    [HttpPost("order-lines")]
    public IActionResult CreateOrderLine(int projectId, [FromBody] ProjectOrderLineSaveRequest req)
    {
        using var c = _db.Open();

        bool exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id",
            new { Id = projectId }) > 0;
        if (!exists) return Ok(ApiResponse<int>.Fail("Commessa non trovata."));

        int sortOrder;
        if (req.AfterLineId is int afterId)
        {
            int? afterSort = c.ExecuteScalar<int?>(
                "SELECT sort_order FROM project_order_lines WHERE id=@Id AND project_id=@Pid",
                new { Id = afterId, Pid = projectId });
            if (afterSort == null)
                return Ok(ApiResponse<int>.Fail("Riga di riferimento non trovata."));

            // Fa spazio: tutte le righe successive scalano di uno.
            c.Execute(@"UPDATE project_order_lines SET sort_order = sort_order + 1
                        WHERE project_id=@Pid AND sort_order > @After",
                new { Pid = projectId, After = afterSort.Value });
            sortOrder = afterSort.Value + 1;
        }
        else
        {
            sortOrder = (c.ExecuteScalar<int?>(
                "SELECT MAX(sort_order) FROM project_order_lines WHERE project_id=@Pid",
                new { Pid = projectId }) ?? 0) + 1;
        }

        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO project_order_lines (project_id, order_ref, order_position, amount, sort_order)
            VALUES (@Pid, @OrderRef, @OrderPosition, @Amount, @SortOrder);
            SELECT LAST_INSERT_ID()",
            new
            {
                Pid = projectId,
                OrderRef = (req.OrderRef ?? "").Trim(),
                OrderPosition = NormalizePosition(req.OrderPosition),
                req.Amount,
                SortOrder = sortOrder,
            });

        ProjectEconomics.SyncRevenueFromOrderLines(c, projectId);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<int>.Ok(newId));
    }

    /// <summary>Modifica una riga ordine (concorrenza ottimistica su row_version).</summary>
    [HttpPut("order-lines/{lineId}")]
    public IActionResult UpdateOrderLine(int projectId, int lineId, [FromBody] ProjectOrderLineSaveRequest req)
    {
        using var c = _db.Open();

        int rows = c.Execute(@"
            UPDATE project_order_lines SET
                order_ref = @OrderRef,
                order_position = @OrderPosition,
                amount = @Amount,
                row_version = row_version + 1
            WHERE id=@Id AND project_id=@Pid
              AND (@RowVersion IS NULL OR row_version = @RowVersion)",
            new
            {
                Id = lineId,
                Pid = projectId,
                OrderRef = (req.OrderRef ?? "").Trim(),
                OrderPosition = NormalizePosition(req.OrderPosition),
                req.Amount,
                req.RowVersion,
            });

        if (rows == 0)
        {
            bool stillThere = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM project_order_lines WHERE id=@Id AND project_id=@Pid",
                new { Id = lineId, Pid = projectId }) > 0;
            return Ok(ApiResponse<bool>.Fail(stillThere
                ? "Riga modificata da un altro utente: ricarica il Bilancio prima di risalvare."
                : "Riga ordine non trovata."));
        }

        ProjectEconomics.SyncRevenueFromOrderLines(c, projectId);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Elimina una riga ordine. Resta sempre almeno una riga: cancellata l'ultima, ne
    /// ricompare una vuota (stesso comportamento del prototipo V32).
    /// </summary>
    [HttpDelete("order-lines/{lineId}")]
    public IActionResult DeleteOrderLine(int projectId, int lineId)
    {
        using var c = _db.Open();

        int rows = c.Execute("DELETE FROM project_order_lines WHERE id=@Id AND project_id=@Pid",
            new { Id = lineId, Pid = projectId });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Riga ordine non trovata."));

        int left = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_order_lines WHERE project_id=@Pid", new { Pid = projectId });
        if (left == 0)
            c.Execute(@"INSERT INTO project_order_lines (project_id, order_ref, order_position, amount, sort_order)
                        VALUES (@Pid, '', '', NULL, 1)", new { Pid = projectId });

        ProjectEconomics.SyncRevenueFromOrderLines(c, projectId);
        NotifyBudgetChanged(projectId);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Totali per sezione delle righe di trasferta a preventivo (project_cost_travel_rows).
    /// Le colonne restano separate perché il K si applica a tutte tranne l'indennità.
    /// </summary>
    private sealed class TravelRowSectionTotals
    {
        public int SectionId { get; set; }
        /// <summary>Notti × Prezzo, sommato sulle righe della sezione.</summary>
        public decimal LodgingCost { get; set; }
        public decimal MealCost { get; set; }
        public decimal CarCost { get; set; }
        public decimal TransportCost { get; set; }
        /// <summary>L'unica colonna che NON prende il K (D33-A).</summary>
        public decimal AllowanceCost { get; set; }
    }

    /// <summary>Posizione: solo cifre, massimo 5 (come nel prototipo).</summary>
    private static string NormalizePosition(string? raw) =>
        new string((raw ?? "").Where(char.IsDigit).Take(5).ToArray());

    /// <summary>
    /// Raggruppa le entry per dipendente e popola ActualEmployees della sezione
    /// </summary>
    private static void BuildActualEmployees(BvaSectionDto sectionDto, IEnumerable<dynamic> entries)
    {
        var byEmployee = entries.GroupBy(a => (int)a.EmployeeId);
        foreach (var empGroup in byEmployee)
        {
            var empDto = new BvaActualEmployeeDto
            {
                EmployeeName = (string)empGroup.First().EmployeeName
            };
            foreach (var a in empGroup)
            {
                decimal hours = (decimal)a.Hours;
                decimal hCost = (decimal)a.HourlyCost;
                empDto.Details.Add(new BvaActualDetailDto
                {
                    WorkDate = (DateTime)a.WorkDate,
                    PhaseName = (string)a.PhaseName,
                    EntryType = (string)a.EntryType,
                    Hours = hours,
                    HourlyCost = hCost,
                    TotalCost = hours * hCost
                });
            }
            empDto.TotalHours = empDto.Details.Sum(d => d.Hours);
            empDto.TotalCost = empDto.Details.Sum(d => d.TotalCost);
            sectionDto.ActualEmployees.Add(empDto);
        }
        sectionDto.ActualHours = sectionDto.ActualEmployees.Sum(e => e.TotalHours);
        sectionDto.ActualCost = sectionDto.ActualEmployees.Sum(e => e.TotalCost);
    }
}