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
/// Distinta DDP officina della commessa (<c>ddp_officina_items</c>): righe, import di una
/// composizione Codex, grezzi derivati, lavorazioni. Stessa rotta
/// <c>api/projects/{id}/ddp-officina…</c> di prima: spostato da <c>ProjectsController</c> il
/// 04/09/2026, nessun percorso cambiato.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura riguarda UNA commessa (l'id sta nella rotta), quindi il cancello si mette
// una volta sola sulla classe: una commessa in bozza, in stand-by o chiusa si consulta ma non si
// modifica, salvo il permesso di scavalco. E una bozza non si VEDE proprio, letture comprese.
[RequireProjectWritable]
[RequireProjectVisible]
public class ProjectDdpOfficinaController : ProjectsControllerBase
{
    private readonly ILogger<ProjectDdpOfficinaController> _logger;
    public ProjectDdpOfficinaController(
        DbService db,
        NotificationService notif,
        ILogger<ProjectDdpOfficinaController> logger,
        IHubContext<ProjectHub> hub,
        FeatureAccessService access,
        AnagraficheCache cache) : base(db, hub, notif, access, cache)
    {
        _logger = logger;
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
                   o.created_at AS CreatedAt, o.updated_at AS UpdatedAt,
                   -- #142: l'ordine Danea del GREZZO (riga di DDP Commerciale generata dalla
                   -- derivazione #135) — chi costruisce vede da qui se il materiale è ordinato.
                   COALESCE(drv.gz_codice, '') AS GrezzoCodice,
                   COALESCE(gz.danea_ref, '') AS GrezzoDaneaRef,
                   gz.danea_order_iddoc AS GrezzoDaneaOrderIdDoc,
                   -- Grezzo «scoperto» visto dall'officina (01/09/2026): il 201 non ha
                   -- NESSUN articolo Danea — la catena ambra in griglia apre l'associazione
                   -- al volo, senza passare dalla DDP Commerciale.
                   (drv.gz_id IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM catalog_items ca
                                    WHERE ca.is_active = 1
                                      AND ca.codex_item_id = drv.gz_id)) AS GrezzoNeedsMapping
            FROM ddp_officina_items o
            LEFT JOIN employees e ON e.id = o.created_by
            -- Stessa catena del ricalcolo (GrezziDerivazione): 101 → riferimento 201 → riga
            -- grezzo della commessa (bom_items.raw_codex_code = codice del 201 senza punti).
            LEFT JOIN (
                SELECT src.codice AS src_codice, rif.id AS gz_id,
                       REPLACE(rif.codice, '.', '') AS gz_codice
                FROM codex_items src
                JOIN codex_item_references r ON r.source_codex_id = src.id AND r.ref_type = '201'
                JOIN codex_items rif ON rif.id = r.ref_codex_id
            ) drv ON drv.src_codice = REPLACE(REPLACE(COALESCE(o.part_number,''), '.', ''), ' ', '')
            LEFT JOIN bom_items gz
              ON gz.project_id = o.project_id AND gz.raw_codex_code = drv.gz_codice
            WHERE o.project_id = @Id
            ORDER BY o.id", new { Id = id }).ToList();
            foreach (OfficinaItemListItem row in rows)
            {
                if (row.PartNumber.Length > 0)
                    row.PartNumber = CodexListItem.FormatCodice(row.PartNumber);
                if (row.GrezzoCodice.Length > 0)
                    row.GrezzoCodice = CodexListItem.FormatCodice(row.GrezzoCodice);
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
}
