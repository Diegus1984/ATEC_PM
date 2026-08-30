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
/// Ciclo RDO Acquisti: crea richiesta offerta su gruppo ATEC, invia email fornitori,
/// registra offerte, sceglie vincitore (applica fornitore+prezzo e avanza stati BOM).
/// </summary>
[ApiController]
[Route("api/purchase-rfqs")]
[Authorize]
[RequireFeature("nav.acquisti_inbox")]
public class PurchaseRfqController : ControllerBase
{
    private readonly DbService _db;
    private readonly EmailService _email;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly NotificationService _notif;
    private readonly DaneaOrderService _daneaOrder;
    private readonly AnagraficheCache _cache;
    private readonly ILogger<PurchaseRfqController> _log;

    public PurchaseRfqController(DbService db, EmailService email, IHubContext<ProjectHub> hub,
        NotificationService notif, DaneaOrderService daneaOrder, AnagraficheCache cache,
        ILogger<PurchaseRfqController> log)
    {
        _db = db;
        _email = email;
        _hub = hub;
        _notif = notif;
        _daneaOrder = daneaOrder;
        _cache = cache;
        _log = log;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>Firma dell'ultima modifica alla riga di distinta (#114), null se il token non ha dipendente.</summary>
    private int? Firma() => CurrentEmployeeId > 0 ? CurrentEmployeeId : null;

    // Real-time best-effort per la lista RDO (gruppo "tutte le commesse" dell'hub).
    private void NotifyRfqChange(int rfqId, string action) =>
        _ = _hub.Clients.Group(ProjectHub.AllGroup).SendAsync("PurchaseRfqChanged", new { RfqId = rfqId, Action = action });

    [HttpGet]
    public IActionResult List([FromQuery] string? status = null)
    {
        using var c = _db.Open();
        var rows = c.Query<PurchaseRfqListItem>(@"
            SELECT r.id, COALESCE(r.atec_code,'') AS AtecCode, COALESCE(r.description,'') AS Description,
                   r.status AS Status, COALESCE(r.notes,'') AS Notes,
                   r.created_by AS CreatedBy,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name),'') AS CreatedByName,
                   r.created_at AS CreatedAt, r.sent_at AS SentAt, r.closed_at AS ClosedAt, r.updated_at AS UpdatedAt,
                   r.danea_order_num AS DaneaOrderNum, r.danea_order_iddoc AS DaneaOrderIdDoc,
                   (SELECT COUNT(*) FROM purchase_rfq_items i WHERE i.rfq_id = r.id) AS ItemCount,
                   (SELECT COALESCE(SUM(b.quantity),0) FROM purchase_rfq_items i
                     JOIN bom_items b ON b.id = i.bom_item_id
                     WHERE i.rfq_id = r.id) AS TotalQuantity,
                   (SELECT COUNT(*) FROM purchase_rfq_offers o WHERE o.rfq_id = r.id) AS OfferCount,
                   -- Vincitore e commessa: chiavi del pannello «Ordini da generare»
                   -- (raggruppamento fornitore+commessa; le RDO sono mono-commessa).
                   (SELECT o.supplier_id FROM purchase_rfq_offers o
                     WHERE o.rfq_id = r.id AND o.is_winner = 1 LIMIT 1) AS WinnerSupplierId,
                   (SELECT s.company_name FROM purchase_rfq_offers o
                     JOIN suppliers s ON s.id = o.supplier_id
                     WHERE o.rfq_id = r.id AND o.is_winner = 1 LIMIT 1) AS WinnerSupplierName,
                   (SELECT o.unit_price FROM purchase_rfq_offers o
                     WHERE o.rfq_id = r.id AND o.is_winner = 1 LIMIT 1) AS WinnerUnitPrice,
                   (SELECT i.project_id FROM purchase_rfq_items i
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectId,
                   (SELECT p.code FROM purchase_rfq_items i
                     JOIN projects p ON p.id = i.project_id
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectCode
            FROM purchase_rfqs r
            LEFT JOIN employees e ON e.id = r.created_by
            WHERE (@Status IS NULL OR @Status = '' OR r.status = @Status)
            ORDER BY r.created_at DESC",
            new { Status = status }).ToList();
        foreach (var row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
        return Ok(ApiResponse<List<PurchaseRfqListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        using var c = _db.Open();
        var detail = LoadDetail(c, id);
        if (detail == null)
            return Ok(ApiResponse<PurchaseRfqDetail>.Fail("RDO non trovata."));
        return Ok(ApiResponse<PurchaseRfqDetail>.Ok(detail));
    }

    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost]
    public IActionResult Create([FromBody] PurchaseRfqCreateRequest req)
    {
        string atec = (req.AtecCode ?? "").Replace(".", "").Trim();
        if (atec.Length == 0)
            return Ok(ApiResponse<List<int>>.Fail("Codice ATEC obbligatorio."));
        if (req.BomItemIds == null || req.BomItemIds.Count == 0)
            return Ok(ApiResponse<List<int>>.Fail("Selezionare almeno una riga distinta."));

        using var c = _db.Open();
        // Il codice letto è quello EFFICACE (snapshot di riga, altrimenti mapping vivo
        // dell'articolo): stesso COALESCE di LoadDetail e LoadFreeRows.
        var bomRows = c.Query<(int Id, int ProjectId, decimal Quantity, string Description, string Atec)>(@"
            SELECT b.id, b.project_id, b.quantity, COALESCE(b.description,''),
                   COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '')
            FROM bom_items b
            LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
            WHERE b.id IN @Ids AND b.ddp_type = 'COMMERCIAL'",
            new { Ids = req.BomItemIds }).ToList();
        if (bomRows.Count == 0)
            return Ok(ApiResponse<List<int>>.Fail(
                "Le righe scelte non sono righe della DDP Commerciale (o non esistono più): " +
                "in gara possono andare solo le righe d'acquisto della DDP Commerciale."));

        // Guardia anti-doppione: una riga distinta può stare in UNA sola RDO viva alla
        // volta (le righe RO restano visibili nei gruppi ATEC → senza questo controllo si
        // potrebbe ri-mandarle in gara e ordinarle due volte). Le righe già occupate si
        // SALTANO (così una nuova riga del gruppo può partire in una nuova RDO senza
        // dover annullare la gara in corso); le RDO annullate liberano le loro righe.
        var busyRows = c.Query<(int BomItemId, int RfqId)>(@"
            SELECT DISTINCT i.bom_item_id, r.id FROM purchase_rfq_items i
            JOIN purchase_rfqs r ON r.id = i.rfq_id
            WHERE i.bom_item_id IN @Ids AND r.status <> 'CANCELLED'",
            new { Ids = req.BomItemIds }).ToList();
        var busyBomIds = busyRows.Select(r => r.BomItemId).ToHashSet();
        bomRows = bomRows.Where(r => !busyBomIds.Contains(r.Id)).ToList();
        if (bomRows.Count == 0)
        {
            string rdoOccupanti = string.Join(", ",
                busyRows.Select(r => r.RfqId).Distinct().OrderBy(x => x).Select(x => $"#{x}"));
            return Ok(ApiResponse<List<int>>.Fail(
                $"Queste righe sono già dentro una gara in corso (RDO {rdoOccupanti}): non serve " +
                "crearne un'altra. Per rifare la gara, annulla prima quella esistente."));
        }

        // Una gara = UN articolo, e la regola deve valere anche QUI, non solo nel client:
        // il raggruppamento per codice lo fa CreateRfqDialog, ma un bundle web vecchio in
        // cache (o una chiamata diretta) manderebbe ancora righe di articoli diversi sotto
        // il codice della prima — la RDO mista nascerebbe, le email chiederebbero offerte
        // sbagliate e l'aggiudicazione la rifiuterebbe comunque a valle. Meglio rifiutare
        // alla nascita, quando le righe sono ancora libere (regola in RdoGuardie, testata).
        var righeFuoriCodice = RdoGuardie.RigheFuoriCodice(
            atec, bomRows.Select(r => (r.Description, (string?)r.Atec)));
        if (righeFuoriCodice.Count > 0)
            return Ok(ApiResponse<List<int>>.Fail(
                $"Una gara = un articolo: {righeFuoriCodice.Count} righe selezionate non hanno il " +
                $"codice ATEC della gara ({CodexListItem.FormatCodice(atec)}): " +
                $"{string.Join(", ", righeFuoriCodice.Take(3))}" +
                (righeFuoriCodice.Count > 3 ? " …" : "") +
                ". Aggiorna la pagina (Ctrl+F5) e riprova: le RDO nascono una per articolo."));

        // Fornitori: quelli richiesti, altrimenti dal mapping catalogo (uguali per ogni RDO).
        var supplierIds = (req.SupplierIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (supplierIds.Count == 0)
        {
            supplierIds = c.Query<int>(@"
                SELECT DISTINCT supplier_id FROM catalog_items
                WHERE is_active = 1 AND atec_code = @Code AND supplier_id IS NOT NULL",
                new { Code = atec }).ToList();
        }

        // Articolo di catalogo per fornitore, letto PRIMA della transazione
        // (MySqlConnector non ammette comandi fuori da una tx pendente).
        var catalogBySupplier = new Dictionary<int, int?>();
        foreach (int sid in supplierIds)
            catalogBySupplier[sid] = c.ExecuteScalar<int?>(@"
                SELECT id FROM catalog_items
                WHERE is_active = 1 AND atec_code = @Code AND supplier_id = @Sid
                ORDER BY id LIMIT 1", new { Code = atec, Sid = sid });

        // REGOLA AZIENDALE: ogni commessa ha le sue RDO (l'ordine Danea che ne nasce è
        // per fornitore+commessa). Un gruppo ATEC che copre N commesse genera N RDO —
        // in transazione unica: o nascono tutte o nessuna (niente doppioni al retry).
        var rfqIds = new List<int>();
        try
        {
            using var tx = c.BeginTransaction();
            foreach (var group in bomRows.GroupBy(r => r.ProjectId).OrderBy(g => g.Key))
            {
                string description = string.IsNullOrWhiteSpace(req.Description)
                    ? group.First().Description
                    : req.Description.Trim();

                int rfqId = c.ExecuteScalar<int>(@"
                    INSERT INTO purchase_rfqs (atec_code, description, status, notes, created_by, updated_at)
                    VALUES (@Code, @Desc, 'DRAFT', @Notes, @By, NOW());
                    SELECT LAST_INSERT_ID()",
                    new { Code = atec, Desc = description, Notes = req.Notes ?? "", By = CurrentEmployeeId }, tx);

                foreach (var row in group)
                {
                    c.Execute(@"
                        INSERT INTO purchase_rfq_items (rfq_id, bom_item_id, project_id, quantity)
                        VALUES (@Rfq, @Bom, @Proj, @Qty)",
                        new { Rfq = rfqId, Bom = row.Id, Proj = row.ProjectId, Qty = row.Quantity }, tx);
                }

                foreach (int sid in supplierIds)
                {
                    c.Execute(@"
                        INSERT IGNORE INTO purchase_rfq_offers (rfq_id, supplier_id, catalog_item_id)
                        VALUES (@Rfq, @Sid, @Cat)",
                        new { Rfq = rfqId, Sid = sid, Cat = catalogBySupplier[sid] }, tx);
                }

                rfqIds.Add(rfqId);
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Creazione RDO fallita (codice ATEC {Atec})", atec);
            return Ok(ApiResponse<List<int>>.Fail(
                "Creazione RDO non riuscita per un errore imprevisto: nessuna RDO è stata creata. " +
                "Riprova; se l'errore si ripete fai una segnalazione."));
        }

        // Notifiche real-time solo a commit riuscito.
        foreach (int rfqId in rfqIds)
            NotifyRfqChange(rfqId, "create");

        return Ok(ApiResponse<List<int>>.Ok(rfqIds, rfqIds.Count == 1
            ? "RDO creata"
            : $"Create {rfqIds.Count} RDO (una per commessa)"));
    }

    /// <summary>
    /// Piano fornitori per le righe distinta selezionate: per ogni fornitore
    /// interpellabile (quello della riga + gli equivalenti trovati col codice ATEC)
    /// gli articoli che può quotare. Righe già in RDO vive sono escluse.
    /// </summary>
    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("offer-plan")]
    public IActionResult OfferPlan([FromBody] PurchaseRfqOfferPlanRequest req)
    {
        var ids = (req.BomItemIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<List<OfferPlanSupplier>>.Fail("Selezionare almeno una riga."));

        using var c = _db.Open();
        var rows = LoadFreeRows(c, ids);
        if (rows.Count == 0)
            return Ok(ApiResponse<List<OfferPlanSupplier>>.Fail(
                "Queste righe sono già dentro una gara (RDO) in corso: non serve crearne un'altra. " +
                "Per rifare la gara, annulla prima quella esistente."));

        var plan = new Dictionary<int, OfferPlanSupplier>();
        foreach (var row in rows)
        {
            // Alternative dal mapping ATEC (stesso codice su articoli di altri fornitori).
            var options = row.Atec.Length == 0
                ? new List<(int SupplierId, int CatalogId, string Code, string Desc, decimal? Cost)>()
                : c.Query<(int SupplierId, int CatalogId, string Code, string Desc, decimal? Cost)>(@"
                    SELECT ci.supplier_id, ci.id, COALESCE(ci.code,''), COALESCE(ci.description,''), ci.unit_cost
                    FROM catalog_items ci
                    WHERE ci.is_active = 1 AND ci.atec_code = @Atec AND ci.supplier_id IS NOT NULL",
                    new { row.Atec }).ToList();

            // Il fornitore della riga è SEMPRE interpellabile (anche senza codice ATEC).
            if (row.SupplierId.HasValue && !options.Any(o => o.SupplierId == row.SupplierId.Value))
                options.Add((row.SupplierId.Value, row.CatalogItemId ?? 0, row.PartNumber, row.Description, null));

            foreach (var opt in options)
            {
                if (!plan.TryGetValue(opt.SupplierId, out var supplier))
                {
                    string anagNome = c.ExecuteScalar<string>(@"
                        SELECT COALESCE(company_name,'')
                        FROM suppliers WHERE id = @Id", new { Id = opt.SupplierId }) ?? "";
                    supplier = new OfferPlanSupplier
                    {
                        SupplierId = opt.SupplierId,
                        SupplierName = anagNome,
                    };
                    plan[opt.SupplierId] = supplier;
                }
                supplier.Items.Add(new OfferPlanItem
                {
                    BomItemId = row.Id,
                    ProjectId = row.ProjectId,
                    ProjectCode = row.ProjectCode,
                    CatalogItemId = opt.CatalogId > 0 ? opt.CatalogId : null,
                    ArticleCode = opt.Code,
                    ArticleDescription = string.IsNullOrEmpty(opt.Desc) ? row.Description : opt.Desc,
                    Quantity = row.Quantity,
                    ListCost = opt.Cost,
                    IsRowSupplier = row.SupplierId == opt.SupplierId,
                });
            }
        }

        var result = plan.Values.OrderBy(s => s.SupplierName).ToList();
        if (result.Count == 0)
            return Ok(ApiResponse<List<OfferPlanSupplier>>.Fail(
                "Nessun fornitore da interpellare per queste righe: assegna un fornitore alla riga " +
                "nella DDP Commerciale, oppure associa il codice ATEC dell'articolo nel Catalogo " +
                "(icona catena), poi riprova."));
        return Ok(ApiResponse<List<OfferPlanSupplier>>.Ok(result));
    }

    /// <summary>
    /// Crea le richieste offerta dai fornitori scelti: RDO automatiche (una per
    /// commessa × codice ATEC, o per riga se senza codice) con le offerte dei
    /// fornitori selezionati. Ritorna le offerte pronte per la mailto.
    /// </summary>
    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("request-offers")]
    public IActionResult RequestOffers([FromBody] PurchaseRfqRequestOffersRequest req)
    {
        var selections = (req.Selections ?? new List<PurchaseRfqRequestOffersSelection>())
            .Where(s => s.BomItemId > 0 && s.SupplierIds != null && s.SupplierIds.Count > 0)
            .ToList();
        if (selections.Count == 0)
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail(
                "Selezionare almeno una riga con un fornitore."));

        using var c = _db.Open();
        var rows = LoadFreeRows(c, selections.Select(s => s.BomItemId).Distinct().ToList());
        if (rows.Count == 0)
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail(
                "Queste righe sono già dentro una gara (RDO) in corso: non serve crearne un'altra. " +
                "Per rifare la gara, annulla prima quella esistente."));
        var suppliersByRow = selections
            .GroupBy(s => s.BomItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(s => s.SupplierIds).Distinct().ToList());

        // Gruppi RDO: commessa × codice ATEC (righe senza codice: una RDO per riga).
        var groups = rows
            .Where(r => suppliersByRow.ContainsKey(r.Id))
            .GroupBy(r => (r.ProjectId, Key: r.Atec.Length > 0 ? r.Atec : $"ROW:{r.Id}"))
            .ToList();
        if (groups.Count == 0)
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail(
                "Le righe scelte non sono più disponibili per una richiesta d'offerta. " +
                "Aggiorna la pagina e riprova."));

        // Articolo di catalogo per (gruppo, fornitore), risolto PRIMA della transazione
        // (MySqlConnector non ammette comandi fuori da una tx pendente).
        // 🪤 La chiave DEVE portare anche il ProjectId: i gruppi sono (commessa × codice) e
        // due commesse con lo stesso codice colliderebbero — innocuo quando l'articolo viene
        // dal mapping (valore identico), sbagliato quando scatta il ripiego sull'articolo
        // della riga, che dipende dal gruppo: l'ultima commessa iterata imporrebbe il suo
        // articolo anche alle RDO delle altre.
        var catalogFor = new Dictionary<(int ProjectId, string GroupKey, int SupplierId), int?>();
        foreach (var g in groups)
        {
            string atec = g.Key.Key.StartsWith("ROW:") ? "" : g.Key.Key;
            var supplierIds = g.SelectMany(r => suppliersByRow[r.Id]).Distinct();
            foreach (int sid in supplierIds)
            {
                int? catalogId = atec.Length > 0
                    ? c.ExecuteScalar<int?>(@"
                        SELECT id FROM catalog_items
                        WHERE is_active = 1 AND atec_code = @Atec AND supplier_id = @Sid
                        ORDER BY id LIMIT 1", new { Atec = atec, Sid = sid })
                    : null;
                // Fornitore della riga senza mapping: si usa l'articolo della riga stessa.
                catalogId ??= g.FirstOrDefault(r => r.SupplierId == sid)?.CatalogItemId;
                catalogFor[(g.Key.ProjectId, g.Key.Key, sid)] = catalogId;
            }
        }

        var createdRfqIds = new List<int>();
        try
        {
            using var tx = c.BeginTransaction();
            foreach (var g in groups)
            {
                string atec = g.Key.Key.StartsWith("ROW:") ? "" : g.Key.Key;
                var first = g.First();
                int rfqId = c.ExecuteScalar<int>(@"
                    INSERT INTO purchase_rfqs (atec_code, description, status, notes, created_by, updated_at)
                    VALUES (@Code, @Desc, 'DRAFT', '', @By, NOW());
                    SELECT LAST_INSERT_ID()",
                    new { Code = atec, Desc = first.Description, By = CurrentEmployeeId }, tx);

                foreach (var row in g)
                    c.Execute(@"INSERT INTO purchase_rfq_items (rfq_id, bom_item_id, project_id, quantity)
                                VALUES (@Rfq, @Bom, @Proj, @Qty)",
                        new { Rfq = rfqId, Bom = row.Id, Proj = row.ProjectId, Qty = row.Quantity }, tx);

                foreach (int sid in g.SelectMany(r => suppliersByRow[r.Id]).Distinct())
                    c.Execute(@"INSERT IGNORE INTO purchase_rfq_offers (rfq_id, supplier_id, catalog_item_id)
                                VALUES (@Rfq, @Sid, @Cat)",
                        new { Rfq = rfqId, Sid = sid, Cat = catalogFor[(g.Key.ProjectId, g.Key.Key, sid)] }, tx);

                createdRfqIds.Add(rfqId);
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Creazione richieste offerta fallita");
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail(
                "Creazione delle richieste non riuscita per un errore imprevisto: nessuna richiesta " +
                "è stata creata. Riprova; se si ripete fai una segnalazione."));
        }

        foreach (int rfqId in createdRfqIds)
            NotifyRfqChange(rfqId, "create");

        // Le offerte appena create, pronte per la mailto (stesso shape di email-candidates).
        var candidates = QueryEmailCandidates(c, createdRfqIds);
        return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Ok(candidates,
            createdRfqIds.Count == 1 ? "Richiesta creata" : $"Create {createdRfqIds.Count} richieste"));
    }

    private sealed record FreeRow(int Id, int ProjectId, string ProjectCode, decimal Quantity,
        string PartNumber, string Description, int? CatalogItemId, int? SupplierId, string Atec);

    /// <summary>Righe distinta commerciali acquistabili e NON già in RDO vive.</summary>
    private static List<FreeRow> LoadFreeRows(System.Data.IDbConnection c, List<int> bomItemIds)
    {
        return c.Query<FreeRow>(@"
            SELECT b.id AS Id, b.project_id AS ProjectId, COALESCE(p.code,'') AS ProjectCode,
                   b.quantity AS Quantity, COALESCE(b.part_number,'') AS PartNumber,
                   COALESCE(b.description,'') AS Description,
                   b.catalog_item_id AS CatalogItemId, b.supplier_id AS SupplierId,
                   COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS Atec
            FROM bom_items b
            JOIN projects p ON p.id = b.project_id
            LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
            WHERE b.id IN @Ids AND b.ddp_type = 'COMMERCIAL'
              AND NOT EXISTS(SELECT 1 FROM purchase_rfq_items pri
                             JOIN purchase_rfqs pr ON pr.id = pri.rfq_id
                             WHERE pri.bom_item_id = b.id AND pr.status <> 'CANCELLED')",
            new { Ids = bomItemIds }).ToList();
    }

    /// <summary>Offerte non ancora contattate delle RDO indicate (shape email-candidates).</summary>
    private static List<PurchaseRfqEmailCandidate> QueryEmailCandidates(
        System.Data.IDbConnection c, List<int> rfqIds)
    {
        var rows = c.Query<PurchaseRfqEmailCandidate>(@"
            SELECT o.id AS OfferId, o.rfq_id AS RfqId,
                   COALESCE(r.atec_code,'') AS AtecCode,
                   COALESCE(r.description,'') AS RfqDescription,
                   o.supplier_id AS SupplierId,
                   COALESCE(s.company_name,'') AS SupplierName,
                   COALESCE(s.email,'') AS SupplierEmail,
                   COALESCE(ci.code,'') AS CatalogCode,
                   COALESCE(ci.description,'') AS CatalogDescription,
                   (SELECT COALESCE(SUM(b.quantity),0) FROM purchase_rfq_items i
                     JOIN bom_items b ON b.id = i.bom_item_id
                     WHERE i.rfq_id = r.id) AS Quantity,
                   (SELECT i.project_id FROM purchase_rfq_items i
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectId,
                   (SELECT p.code FROM purchase_rfq_items i
                     JOIN projects p ON p.id = i.project_id
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectCode
            FROM purchase_rfq_offers o
            JOIN purchase_rfqs r ON r.id = o.rfq_id
            JOIN suppliers s ON s.id = o.supplier_id
            LEFT JOIN catalog_items ci ON ci.id = o.catalog_item_id
            WHERE o.rfq_id IN @Ids AND o.email_sent_at IS NULL
            ORDER BY s.company_name, r.id", new { Ids = rfqIds }).ToList();
        foreach (var row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
        return rows;
    }

    /// <summary>
    /// Offerte in attesa di richiesta email: RDO aperte (DRAFT/SENT) senza ordine
    /// generato, fornitori non ancora contattati. Il client le raggruppa per fornitore
    /// e compone la mailto (destinatario dall'anagrafica, articoli Danea nel corpo).
    /// </summary>
    [HttpGet("email-candidates")]
    public IActionResult EmailCandidates()
    {
        using var c = _db.Open();
        var rows = c.Query<PurchaseRfqEmailCandidate>(@"
            SELECT o.id AS OfferId, o.rfq_id AS RfqId,
                   COALESCE(r.atec_code,'') AS AtecCode,
                   COALESCE(r.description,'') AS RfqDescription,
                   o.supplier_id AS SupplierId,
                   COALESCE(s.company_name,'') AS SupplierName,
                   COALESCE(s.email,'') AS SupplierEmail,
                   COALESCE(ci.code,'') AS CatalogCode,
                   COALESCE(ci.description,'') AS CatalogDescription,
                   (SELECT COALESCE(SUM(b.quantity),0) FROM purchase_rfq_items i
                     JOIN bom_items b ON b.id = i.bom_item_id
                     WHERE i.rfq_id = r.id) AS Quantity,
                   (SELECT i.project_id FROM purchase_rfq_items i
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectId,
                   (SELECT p.code FROM purchase_rfq_items i
                     JOIN projects p ON p.id = i.project_id
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectCode
            FROM purchase_rfq_offers o
            JOIN purchase_rfqs r ON r.id = o.rfq_id
            JOIN suppliers s ON s.id = o.supplier_id
            LEFT JOIN catalog_items ci ON ci.id = o.catalog_item_id
            WHERE r.status IN ('DRAFT','SENT')
              AND r.danea_order_iddoc IS NULL
              AND o.email_sent_at IS NULL
            ORDER BY s.company_name, r.id").ToList();
        foreach (var row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
        return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Ok(rows));
    }

    /// <summary>
    /// Registra l'invio via mailto: timestamp sulle offerte contattate e RDO in DRAFT
    /// avanzate a SENT. Nessuna email parte dal server (la compone il client mail
    /// dell'utente); qui resta la traccia di CHI è stato contattato e quando.
    /// </summary>
    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("mark-emailed")]
    public IActionResult MarkEmailed([FromBody] PurchaseRfqMarkEmailedRequest req)
    {
        var ids = (req.OfferIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<bool>.Fail("Nessuna offerta indicata."));

        using var c = _db.Open();
        c.Execute(@"UPDATE purchase_rfq_offers SET email_sent_at = NOW()
                    WHERE id IN @Ids AND email_sent_at IS NULL", new { Ids = ids });
        var rfqIds = c.Query<int>(
            "SELECT DISTINCT rfq_id FROM purchase_rfq_offers WHERE id IN @Ids", new { Ids = ids }).ToList();
        if (rfqIds.Count > 0)
            c.Execute(@"UPDATE purchase_rfqs SET status = 'SENT', sent_at = COALESCE(sent_at, NOW()), updated_at = NOW()
                        WHERE id IN @Ids AND status = 'DRAFT'", new { Ids = rfqIds });
        foreach (int rfqId in rfqIds)
            NotifyRfqChange(rfqId, "send");
        return Ok(ApiResponse<bool>.Ok(true, "Richiesta offerta registrata"));
    }

    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("{id}/send")]
    public IActionResult Send(int id)
    {
        using var c = _db.Open();
        var detail = LoadDetail(c, id);
        if (detail == null)
            return Ok(ApiResponse<bool>.Fail("RDO non trovata."));
        if (detail.Status is "CLOSED" or "CANCELLED")
            return Ok(ApiResponse<bool>.Fail("RDO chiusa: non si possono inviare email."));
        if (detail.Offers.Count == 0)
            return Ok(ApiResponse<bool>.Fail("Nessun fornitore in RDO."));

        int sent = 0;
        // Fornitori senza email in anagrafica: prima venivano saltati in silenzio e la RDO
        // risultava comunque «inviata» — i nomi si raccolgono per dirlo chiaro nell'esito.
        var senzaEmail = new List<string>();
        foreach (var offer in detail.Offers)
        {
            if (string.IsNullOrWhiteSpace(offer.SupplierEmail))
            {
                senzaEmail.Add(offer.SupplierName);
                continue;
            }

            string lines = string.Join("\n", detail.Items.Select(i =>
                $"- {i.ProjectCode}: {i.Description} × {i.Quantity:0.###}"));
            string subject = $"Richiesta offerta {detail.AtecCode} — {detail.Description}";
            string body =
                $"Gentile {offer.SupplierName},\n\n" +
                $"vi chiediamo un'offerta per il codice ATEC {detail.AtecCode} ({detail.Description}).\n\n" +
                $"Fabbisogno:\n{lines}\n\n" +
                (string.IsNullOrWhiteSpace(detail.Notes) ? "" : $"Note: {detail.Notes}\n\n") +
                "Cordiali saluti,\nATEC PM";

            // Corpo HTML = testo con a-capo resi (altrimenti i client mail collassano le righe).
            string htmlBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>\n");

            if (_email.QueueSimpleMail(offer.SupplierEmail, offer.SupplierName, subject, body, htmlBody))
            {
                c.Execute("UPDATE purchase_rfq_offers SET email_sent_at = NOW() WHERE id = @Id", new { Id = offer.Id });
                sent++;
            }
        }

        c.Execute(@"UPDATE purchase_rfqs SET status = 'SENT', sent_at = COALESCE(sent_at, NOW()), updated_at = NOW() WHERE id = @Id",
            new { Id = id });
        NotifyRfqChange(id, "send");

        if (sent == 0 && !_email.Enabled)
            return Ok(ApiResponse<bool>.Ok(true,
                "L'invio automatico delle email non è attivo su questo server: la richiesta è stata " +
                "registrata come inviata, ma i fornitori NON hanno ricevuto nulla."));
        string esito = $"Email accodate per l'invio: {sent} su {detail.Offers.Count} fornitori.";
        if (senzaEmail.Count > 0)
            esito += senzaEmail.Count == 1
                ? $" Attenzione: 1 fornitore senza indirizzo email in anagrafica NON è stato " +
                  $"contattato: {senzaEmail[0]}. Aggiungi l'email nell'anagrafica fornitori e rimanda."
                : $" Attenzione: {senzaEmail.Count} fornitori senza indirizzo email in anagrafica NON " +
                  $"sono stati contattati: {string.Join(", ", senzaEmail)}. " +
                  "Aggiungi l'email nell'anagrafica fornitori e rimanda.";
        return Ok(ApiResponse<bool>.Ok(true, esito));
    }

    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPut("{id}/offers/{offerId}")]
    public IActionResult SaveOffer(int id, int offerId, [FromBody] PurchaseRfqOfferSaveRequest req)
    {
        using var c = _db.Open();
        // 🪤 Il blocco del prezzo all'aggiudicazione deve stare QUI, non solo nel disabled
        // del client: un dialogo aperto su cache stantia (l'hub invalida la lista, non il
        // dettaglio) resterebbe modificabile dopo che un collega ha aggiudicato. Correggere
        // l'offerta vincente a RDO chiusa aggiorna solo purchase_rfq_offers, mentre
        // bom_items.unit_cost lo scrive unicamente SelectWinner: l'ordine Danea rileggerebbe
        // il prezzo nuovo lasciando distinta e Bilancio su quello vecchio, in silenzio.
        string? statoRdo = c.ExecuteScalar<string?>(
            "SELECT status FROM purchase_rfqs WHERE id = @Id", new { Id = id });
        if (statoRdo == null)
            return Ok(ApiResponse<bool>.Fail("RDO non trovata."));
        if (statoRdo is "CLOSED" or "CANCELLED")
            return Ok(ApiResponse<bool>.Fail(
                "RDO chiusa o annullata: le offerte non si modificano più. Un'aggiudicazione " +
                "sbagliata si ripara annullando e rifacendo la gara, non correggendo il prezzo qui."));
        // Un'offerta a 0 o in negativo non esiste: meglio rifiutarla all'ingresso che
        // scoprirla all'aggiudicazione (per togliere un prezzo si svuota il campo → null).
        if (req.UnitPrice is <= 0)
            return Ok(ApiResponse<bool>.Fail(
                "Il prezzo dell'offerta deve essere maggiore di zero (per toglierlo, svuota il campo)."));
        int n = c.Execute(@"
            UPDATE purchase_rfq_offers SET
                catalog_item_id = @CatalogItemId,
                unit_price = @UnitPrice,
                valid_until = @ValidUntil,
                notes = @Notes
            WHERE id = @OfferId AND rfq_id = @RfqId",
            new
            {
                OfferId = offerId,
                RfqId = id,
                req.CatalogItemId,
                req.UnitPrice,
                req.ValidUntil,
                Notes = req.Notes ?? "",
            });
        if (n == 0)
            return Ok(ApiResponse<bool>.Fail("Offerta non trovata."));
        c.Execute("UPDATE purchase_rfqs SET updated_at = NOW() WHERE id = @Id", new { Id = id });
        NotifyRfqChange(id, "offer");
        return Ok(ApiResponse<bool>.Ok(true, "Offerta aggiornata"));
    }

    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("{id}/select-winner")]
    public IActionResult SelectWinner(int id, [FromBody] PurchaseRfqSelectWinnerRequest req)
    {
        using var c = _db.Open();
        var detail = LoadDetail(c, id);
        if (detail == null)
            return Ok(ApiResponse<bool>.Fail("RDO non trovata."));
        // Anche le ANNULLATE: prima si rifiutavano solo le CLOSED, e una RDO annullata
        // restava aggiudicabile — con le righe magari già ripartite in un'altra gara.
        if (detail.Status is "CLOSED" or "CANCELLED")
            return Ok(ApiResponse<bool>.Fail("RDO chiusa o annullata: non è più possibile scegliere il vincitore."));

        var offer = detail.Offers.FirstOrDefault(o => o.Id == req.OfferId);
        if (offer == null)
            return Ok(ApiResponse<bool>.Fail("Offerta non trovata."));
        // 🪤 Qui c'era un ripiego: senza prezzo sull'offerta si prendeva il primo costo utile
        // delle righe di distinta e lo si scriveva come prezzo dell'offerta vincente. Con più
        // fornitori in gara era il modo di aggiudicare a un prezzo che NESSUNO aveva offerto —
        // per giunta scritto a DB come se il fornitore l'avesse proposto. Il prezzo di un'offerta
        // lo mette solo chi l'ha ricevuta. E un prezzo a 0 o negativo non si aggiudica:
        // finirebbe dritto in bom_items.unit_cost, nel Bilancio e nell'ordine Danea.
        string? errorePrezzo = RdoGuardie.PrezzoNonAggiudicabile(offer.UnitPrice);
        if (errorePrezzo != null)
            return Ok(ApiResponse<bool>.Fail(errorePrezzo));

        // 🪤 GARA MISTA — si rifiuta PRIMA di scrivere qualsiasi cosa.
        //
        // Aggiudicare riscrive su ogni riga codice, descrizione, UM, produttore, articolo Danea
        // e codice ATEC del vincitore: se in gara sono finite righe di pezzi diversi (succedeva
        // col «Richiedi RDO» della testata, che raggruppava tutto sotto il codice della prima
        // riga), le righe dell'altro pezzo diventavano quel pezzo e nella distinta non restava
        // traccia di cos'erano.
        //
        // Si guarda se le RIGHE non vanno d'accordo TRA LORO, non se sono d'accordo con la
        // testata: il codice della testata è congelato alla creazione, mentre quello delle righe
        // può arrivare dopo (è normale che il buyer mappi l'articolo mentre aspetta le offerte,
        // e succede anche dalla Inbox Acquisti). Confrontarli farebbe saltare gare sanissime.
        //
        // E si RIFIUTA invece di saltare le righe: saltarle lascerebbe la RDO chiusa con dentro
        // righe non aggiudicate, che a quel punto non si possono né annullare né rimettere in
        // gara (LoadFreeRows e la guardia anti-doppione le vedono occupate) — e l'ordine Danea
        // conterebbe comunque la loro quantità. Meglio fermarsi: la RDO resta aperta e
        // annullabile, e le righe restano libere.
        // Il «senza codice» conta come gruppo a sé, e PIÙ righe tutte senza codice sono mista
        // per definizione: due righe non mappate sono articoli che nessuno può giurare siano
        // lo stesso, e riscriverle sopra l'identità del vincitore è proprio il danno da
        // evitare. La regola (copia unica, testata) sta in RdoGuardie.
        var codiciInGara = RdoGuardie.CodiciInGara(detail.Items.Select(i => i.AtecCode));
        string? erroreGaraMista = RdoGuardie.GaraMista(codiciInGara, detail.Items.Count);
        if (erroreGaraMista != null)
            return Ok(ApiResponse<bool>.Fail(erroreGaraMista));

        string targetStatus = string.IsNullOrWhiteSpace(req.TargetStatus) ? "RO" : req.TargetStatus.Trim().ToUpperInvariant();

        // Snapshot catalogo dell'offerta vincente. ATTENZIONE: l'oggetto della RDO
        // (detail.Description) è il TITOLO DELLA GARA, non la descrizione dell'articolo:
        // non deve mai finire nella riga di distinta, altrimenti in Inbox Acquisti tutte le
        // righe della stessa RDO diventano «Richiesta offerta — Commessa X» e non si sa più
        // cosa si è comprato. Senza articolo di catalogo la riga tiene la sua identità.
        string partNumber = offer.CatalogCode ?? "";
        string manufacturer = "";
        string unit = "PZ";
        string description = "";
        bool hasCatalog = false;
        // 🪤 Il codice ATEC da stampare sulle righe si prende dall'ARTICOLO del vincitore, non
        // dalla testata della RDO. Quello di testata è congelato alla creazione e può essere
        // vecchio o addirittura «GENERICO»: scrivendolo si mette uno SNAPSHOT sulla riga, e lo
        // snapshot vince sul COALESCE usato in tutte le letture — il mapping giusto sparirebbe
        // da Inbox, griglia DDP e gruppi gara, in silenzio e senza una schermata che lo rimetta
        // a posto. Senza articolo di catalogo non si scrive niente e la riga resta com'è.
        string atec = "";
        if (offer.CatalogItemId.HasValue)
        {
            var cat = c.QueryFirstOrDefault<(string Code, string Desc, string Unit, string Manufacturer, string AtecCode)>(@"
                SELECT COALESCE(code,''), COALESCE(description,''), COALESCE(unit,'PZ'),
                       COALESCE(manufacturer,''), COALESCE(atec_code,'')
                FROM catalog_items WHERE id = @Id", new { Id = offer.CatalogItemId.Value });
            if (cat != default)
            {
                hasCatalog = true;
                partNumber = cat.Code;
                description = cat.Desc;
                unit = string.IsNullOrEmpty(cat.Unit) ? unit : cat.Unit;
                manufacturer = cat.Manufacturer;
                atec = cat.AtecCode.Replace(".", "").Trim();
                // Articolo del vincitore senza mapping: si congela sulla riga il codice sotto
                // cui la gara è corsa (la guardia qui sopra garantisce che sia uno solo).
                // Senza questo ripiego la riga finirebbe a puntare a un articolo non mappato e
                // il suo codice EFFICACE diventerebbe vuoto: il mapping sparirebbe in silenzio,
                // che è esattamente ciò che si voleva evitare smettendo di usare la testata.
                if (atec.Length == 0 && codiciInGara.Count == 1)
                    atec = codiciInGara[0];
            }
        }


        // Avanzamenti calcolati PRIMA della transazione (Validate legge la matrice dalla
        // stessa connessione: MySqlConnector non ammette comandi fuori da una tx pendente).
        var rowPlans = new List<(int BomItemId, int ProjectId, string ProjectCode, string? OldStatus, string ApplyStatus)>();
        foreach (var item in detail.Items)
        {
            string? oldStatus = c.ExecuteScalar<string?>(
                "SELECT item_status FROM bom_items WHERE id = @Id", new { Id = item.BomItemId });
            string? transitionError = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, oldStatus, targetStatus, _cache);
            string applyStatus = transitionError == null ? targetStatus : (oldStatus ?? "DO");
            rowPlans.Add((item.BomItemId, item.ProjectId, item.ProjectCode, oldStatus, applyStatus));
        }

        // Per la campanella: righe avanzate di stato, raggruppate per commessa (1 notifica a commessa).
        var advanced = new Dictionary<int, (string ProjectCode, int Count, string FromStatus, int FirstItemId)>();
        foreach (var plan in rowPlans)
        {
            if (plan.ApplyStatus == plan.OldStatus) continue;
            advanced[plan.ProjectId] = advanced.TryGetValue(plan.ProjectId, out var cur)
                ? (cur.ProjectCode, cur.Count + 1, cur.FromStatus, cur.FirstItemId)
                : (plan.ProjectCode, 1, plan.OldStatus ?? "", plan.BomItemId);
        }

        // 🪤 TUTTO in UNA transazione: righe, vincitore e chiusura della RDO. Prima ogni
        // scrittura era sciolta: un errore a metà lasciava le prime righe riscritte con
        // l'identità del vincitore e la RDO ancora aperta (o viceversa righe intatte e RDO
        // chiusa) — uno stato senza uscita che nessuna schermata sapeva riparare.
        try
        {
            using var tx = c.BeginTransaction();

            // Ricontrollo DENTRO la transazione: le guardie qui sopra hanno letto uno
            // snapshot, e fra quella lettura e questo punto un collega può aver cambiato
            // il prezzo o l'articolo dell'offerta (SaveOffer passa: la RDO è ancora
            // aperta). Aggiudicare col dato stantio scriverebbe su bom_items un prezzo
            // diverso da quello registrato sull'offerta — la divergenza silenziosa che
            // tutto questo giro vuole impedire. In caso di sorpresa si rifiuta e basta.
            var offerNow = c.QueryFirstOrDefault<(decimal? UnitPrice, int? CatalogItemId)>(@"
                SELECT unit_price, catalog_item_id FROM purchase_rfq_offers
                WHERE id = @OfferId AND rfq_id = @Id",
                new { OfferId = req.OfferId, Id = id }, tx);
            if (offerNow.UnitPrice != offer.UnitPrice || offerNow.CatalogItemId != offer.CatalogItemId)
                throw new InvalidOperationException(
                    "l'offerta è stata modificata da un altro utente mentre si aggiudicava: ricontrolla e riprova");

            foreach (var plan in rowPlans)
            {
                // Prezzo aggiudicato, fornitore e stato si applicano sempre; l'identità della riga
                // (codice, descrizione, UM, produttore, articolo) solo se l'offerta porta davvero
                // un articolo di catalogo — e mai con valori vuoti.
                c.Execute(@"
                    UPDATE bom_items SET
                        catalog_item_id = IF(@HasCatalog, @CatId, catalog_item_id),
                        part_number = IF(LENGTH(@Part) > 0, @Part, part_number),
                        description = IF(LENGTH(@Desc) > 0, @Desc, description),
                        unit = IF(@HasCatalog, @Unit, unit),
                        unit_cost = @Price,
                        supplier_id = @SuppId,
                        manufacturer = IF(LENGTH(@Mfr) > 0, @Mfr, manufacturer),
                        atec_code = IF(LENGTH(@Atec) > 0, @Atec, atec_code),
                        item_status = @Status,
                        updated_at = NOW(),
                        -- Firma dell'ultima modifica (#114): l'aggiudicazione cambia la riga di
                        -- distinta, e la card della Dashboard deve sapere chi è stato.
                        updated_by = @UpdatedBy
                    WHERE id = @BomId AND project_id = @ProjId",
                    new
                    {
                        UpdatedBy = Firma(),
                        HasCatalog = hasCatalog,
                        CatId = offer.CatalogItemId,
                        Part = partNumber,
                        Desc = description,
                        Unit = unit,
                        Price = offer.UnitPrice!.Value,
                        SuppId = offer.SupplierId,
                        Mfr = manufacturer,
                        Atec = atec,
                        Status = plan.ApplyStatus,
                        BomId = plan.BomItemId,
                        ProjId = plan.ProjectId,
                    }, tx);

                // `log:` obbligatorio qui: Registra inghiotte i propri errori (per non far
                // saltare l'aggiudicazione per una riga di storia), e senza logger una
                // cronistoria persa non lascerebbe traccia da nessuna parte.
                DdpItemEvents.Registra(c, DdpItemEvents.Commerciale, plan.BomItemId, plan.ProjectId,
                    plan.OldStatus, plan.ApplyStatus, User, note: "aggiudicazione offerta", tx: tx, log: _log);
            }

            c.Execute("UPDATE purchase_rfq_offers SET is_winner = 0 WHERE rfq_id = @Id", new { Id = id }, tx);
            c.Execute("UPDATE purchase_rfq_offers SET is_winner = 1 WHERE id = @OfferId AND rfq_id = @Id",
                new { OfferId = req.OfferId, Id = id }, tx);
            // Chiusura CONDIZIONATA allo stato: se nel frattempo un collega ha annullato
            // la RDO (o l'ha aggiudicata lui), riscriverla CLOSED la resusciterebbe con
            // le righe magari già ripartite in un'altra gara. Zero righe toccate = tutto
            // il lavoro di questa transazione va buttato, e il throw lo butta.
            int chiuse = c.Execute(@"
                UPDATE purchase_rfqs SET status = 'CLOSED', closed_at = NOW(), updated_at = NOW()
                WHERE id = @Id AND status NOT IN ('CLOSED','CANCELLED')",
                new { Id = id }, tx);
            if (chiuse == 0)
                throw new InvalidOperationException(
                    "la RDO è stata chiusa o annullata da un altro utente nel frattempo");
            tx.Commit();
        }
        catch (InvalidOperationException ex)
        {
            // Rifiuti VOLUTI dei throw qui sopra (offerta modificata / RDO chiusa da un
            // collega): il testo è già pensato per l'utente e spiega cosa è successo.
            return Ok(ApiResponse<bool>.Fail(
                $"Aggiudicazione non riuscita: {ex.Message} (nessuna modifica applicata, la RDO resta aperta)."));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Aggiudicazione RDO {RfqId} fallita", id);
            return Ok(ApiResponse<bool>.Fail(
                "Aggiudicazione non riuscita per un errore imprevisto: nessuna modifica salvata. " +
                "Riprova; se si ripete fai una segnalazione."));
        }

        // Notifiche real-time solo a commit riuscito.
        foreach (var plan in rowPlans)
        {
            var payload = new DdpChange
            {
                ProjectId = plan.ProjectId,
                Action = "update",
                ItemId = plan.BomItemId,
                DdpType = "COMMERCIAL",
            };
            _ = _hub.Clients.Group($"project-{plan.ProjectId}").SendAsync("DdpChanged", payload);
            _ = _hub.Clients.Group(ProjectHub.AllGroup).SendAsync("DdpChanged", payload);
        }
        NotifyRfqChange(id, "winner");

        // Campanella come per il cambio stato da PUT (una notifica per commessa toccata, solo ACTIVE).
        foreach (var (projectId, info) in advanced)
        {
            try
            {
                string projStatus = c.ExecuteScalar<string?>(
                    "SELECT status FROM projects WHERE id = @Id", new { Id = projectId }) ?? "";
                if (projStatus != "ACTIVE") continue;

                int currentEmpId = CurrentEmployeeId;
                List<int> recipients = _notif.GetProjectPmIds(projectId);
                recipients.AddRange(_notif.GetAcqEmployeeIds());
                recipients.Remove(currentEmpId);
                if (recipients.Count == 0) continue;

                _notif.Create("DDP_STATUS_CHANGED", "INFO",
                    $"Cambio stato DDP — {info.ProjectCode}",
                    $"RDO {detail.AtecCode} ({offer.SupplierName}): {info.Count} righe da " +
                    $"{DdpStatusMap.ToLabel(info.FromStatus)} a {DdpStatusMap.ToLabel(targetStatus)}",
                    "BOM", info.FirstItemId, projectId, currentEmpId, recipients);
            }
            catch { /* non bloccare il vincitore per errore notifica */ }
        }

        return Ok(ApiResponse<bool>.Ok(true, detail.Items.Count == 1
            ? "Vincitore applicato a 1 riga"
            : $"Vincitore applicato a {detail.Items.Count} righe"));
    }

    // Strada B (22/07/2026): scrive l'ordine fornitore DIRETTAMENTE nel Firebird di
    // Danea (Atec_PM) via DaneaOrderService — testata+righe+IVA+movimento InArrivo.
    // Una riga d'ordine per RDO: articolo Danea del vincitore, qtà = somma fabbisogni,
    // prezzo = offerta vincente. REGOLA AZIENDALE: 1 ordine = 1 fornitore + 1 commessa.
    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("{id}/create-danea-order")]
    public IActionResult CreateDaneaOrder(int id, [FromBody] PurchaseRfqCreateOrderRequest req)
    {
        using var c = _db.Open();
        var (order, error) = CreateDaneaOrderForRfqs(c, new List<int> { id }, req.ExpectedDate);
        if (error != null)
            return Ok(ApiResponse<PurchaseRfqDetail>.Fail(error));
        var updated = LoadDetail(c, id);
        return Ok(ApiResponse<PurchaseRfqDetail>.Ok(updated!,
            $"Ordine fornitore n. {order!.Num} creato in Danea (Atec_PM)"));
    }

    // Accorpamento multi-RDO: più RDO chiuse dello STESSO fornitore vincitore e della
    // STESSA commessa in un unico ordine Danea multi-riga (una riga per RDO).
    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("create-danea-order-multi")]
    public IActionResult CreateDaneaOrderMulti([FromBody] PurchaseRfqCreateOrderMultiRequest req)
    {
        var ids = (req.RfqIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<int>.Fail("Selezionare almeno una RDO."));

        using var c = _db.Open();
        var (order, error) = CreateDaneaOrderForRfqs(c, ids, req.ExpectedDate);
        if (error != null)
            return Ok(ApiResponse<int>.Fail(error));
        return Ok(ApiResponse<int>.Ok(order!.Num,
            $"Ordine fornitore n. {order.Num} creato in Danea ({ids.Count} RDO, {ids.Count} righe)"));
    }

    /// <summary>
    /// Cuore della generazione ordine (singola o multi-RDO): valida le RDO (vincitore
    /// unico con prezzo e articolo, STESSA commessa), le prenota con claim atomico,
    /// scrive l'ordine in Firebird (una riga per RDO) e registra tutto in MySQL in
    /// transazione unica. Ritorna l'ordine creato oppure il messaggio d'errore.
    /// </summary>
    private (DaneaOrderService.OrderResult? Order, string? Error) CreateDaneaOrderForRfqs(
        System.Data.IDbConnection c, List<int> rfqIds, DateTime? expectedDate)
    {
        // ── Carico e valido ogni RDO; una riga d'ordine per RDO ──
        var details = new List<PurchaseRfqDetail>();
        var winners = new List<PurchaseRfqOfferDto>();
        var lines = new List<DaneaOrderService.OrderLine>();
        foreach (int id in rfqIds)
        {
            var detail = LoadDetail(c, id);
            if (detail == null)
                return (null, $"RDO #{id} non trovata.");
            if (detail.Status == "CANCELLED")
                return (null, $"RDO #{id} è annullata: non se ne genera l'ordine.");
            if (detail.DaneaOrderNum.HasValue)
                return (null, $"RDO #{id}: ordine già generato (n. {detail.DaneaOrderNum}), non si duplica.");
            var winner = detail.Offers.FirstOrDefault(o => o.IsWinner);
            if (winner == null)
                return (null, $"RDO #{id}: scegliere prima il vincitore.");
            // Stessa regola dell'aggiudicazione (copia unica in RdoGuardie): le RDO
            // aggiudicate PRIMA della guardia possono avere un vincitore storico a 0 —
            // il vecchio ripiego sul costo di riga poteva scriverlo — e l'ordine a 0 €
            // in Firebird è irreversibile.
            string? errPrezzo = RdoGuardie.PrezzoNonAggiudicabile(winner.UnitPrice);
            if (errPrezzo != null)
                return (null, $"RDO #{id}: {errPrezzo}");
            // Articolo Danea del vincitore: dal suo articolo di catalogo (codice Danea).
            string? articleCode = winner.CatalogItemId.HasValue
                ? c.ExecuteScalar<string?>("SELECT code FROM catalog_items WHERE id = @Id",
                    new { Id = winner.CatalogItemId.Value })
                : null;
            if (string.IsNullOrWhiteSpace(articleCode))
                return (null, $"RDO #{id}: l'offerta vincente non indica quale articolo Danea ordinare. " +
                    "Apri il dettaglio della RDO, collega l'articolo del fornitore all'offerta vincente " +
                    "(«Collega articolo»), poi rigenera l'ordine.");
            decimal totalQty = detail.Items.Sum(i => i.Quantity);
            if (totalQty <= 0)
                return (null, $"RDO #{id}: le righe di distinta collegate hanno quantità zero. " +
                    "Correggi le quantità nella DDP Commerciale della commessa e rigenera l'ordine.");

            details.Add(detail);
            winners.Add(winner);
            // Il `!` è garantito dalla guardia PrezzoNonAggiudicabile qui sopra.
            lines.Add(new DaneaOrderService.OrderLine(articleCode.Trim(), totalQty, winner.UnitPrice!.Value));
        }

        // ── Vincoli di accorpamento ──
        if (winners.Select(w => w.SupplierId).Distinct().Count() > 1)
            return (null, "Le RDO selezionate hanno fornitori vincitori diversi: un ordine per fornitore.");
        if (details.SelectMany(d => d.Items).Select(i => i.ProjectId).Distinct().Count() > 1)
            return (null, "Le RDO selezionate appartengono a commesse diverse: un ordine per commessa.");

        var supplier = c.QueryFirstOrDefault<(string Vat, string Name)>(@"
            SELECT COALESCE(vat_number,''), COALESCE(company_name,'')
            FROM suppliers WHERE id = @Id", new { Id = winners[0].SupplierId });

        // CLAIM atomico anti-doppio ordine su TUTTE le RDO (sentinella 0): l'ordine in
        // Danea è irreversibile, quindi si procede solo se NESSUNA è già prenotata/evasa.
        // Riga per riga, tracciando cosa si è prenotato DAVVERO: in caso di fallimento si
        // rilasciano solo i propri claim (un IN cieco libererebbe il claim di un altro
        // utente a metà generazione → doppio ordine).
        var claimedIds = new List<int>();
        foreach (int id in rfqIds)
        {
            // `status <> 'CANCELLED'` DENTRO il claim: il rifiuto delle annullate qui sopra
            // legge uno snapshot, e un Annulla concorrente può infilarsi prima del claim —
            // che poi prenoterebbe la RDO annullata, l'ordine partirebbe e il bookkeeping
            // la resusciterebbe a CLOSED. Col controllo nel claim l'annullo vince; DOPO il
            // claim non può più succedere (Cancel rifiuta le RDO con iddoc valorizzato).
            if (c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = 0, updated_at = NOW()
                            WHERE id = @Id AND danea_order_iddoc IS NULL AND status <> 'CANCELLED'",
                    new { Id = id }) == 1)
                claimedIds.Add(id);
        }
        if (claimedIds.Count != rfqIds.Count)
        {
            if (claimedIds.Count > 0)
                c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = NULL, updated_at = NOW()
                            WHERE id IN @Ids AND danea_order_iddoc = 0", new { Ids = claimedIds });
            return (null, "Generazione ordine già eseguita o in corso da un altro utente, o RDO appena annullata: aggiorna la pagina.");
        }

        string note = $"RDO {string.Join(", ", details.Select(d => $"#{d.Id}"))} — " +
                      $"{string.Join(", ", details.Select(d => d.AtecCode).Distinct())} (generato da ATEC PM)";
        DaneaOrderService.OrderResult order;
        try
        {
            order = _daneaOrder.CreateSupplierOrder(supplier.Vat, supplier.Name, lines, expectedDate, note);
        }
        catch (InvalidOperationException ex)
        {
            // Rifiuti VOLUTI di DaneaOrderService (fornitore, articolo o aliquota IVA mancanti
            // in Atec_PM): il testo è già in italiano e dice cosa sistemare — passa all'utente.
            // Nessuna scrittura avvenuta in Danea: si rilascia il claim e si può riprovare.
            c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = NULL, updated_at = NOW()
                        WHERE id IN @Ids AND danea_order_iddoc = 0", new { Ids = rfqIds });
            return (null, $"Ordine NON creato in Danea (nessuna scrittura effettuata): {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Creazione ordine Danea fallita (RDO {RfqIds})", string.Join(",", rfqIds));
            // Nessuna scrittura avvenuta in Danea: si rilascia il claim e si può riprovare.
            c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = NULL, updated_at = NOW()
                        WHERE id IN @Ids AND danea_order_iddoc = 0", new { Ids = rfqIds });
            return (null, "Ordine NON creato in Danea (nessuna scrittura effettuata): puoi riprovare " +
                "subito. Se l'errore si ripete, fai una segnalazione.");
        }

        // Avanzamenti calcolati PRIMA della transazione (Validate legge la matrice dalla
        // stessa connessione: MySqlConnector non ammette comandi fuori da una tx pendente).
        var rowUpdates = new List<(int BomItemId, int ProjectId, bool Advance)>();
        foreach (var item in details.SelectMany(d => d.Items))
        {
            string? oldStatus = c.ExecuteScalar<string?>(
                "SELECT item_status FROM bom_items WHERE id = @Id", new { Id = item.BomItemId });
            // Riga GIÀ in IO: niente avanzamento e soprattutto niente secondo evento in
            // cronistoria (Validate accetta i passaggi su se stessi, quindi da solo non
            // basta a distinguere il no-op — Registra li scarta, ma qui l'evento
            // dell'ordine si scrive a mano).
            bool advance = !string.Equals((oldStatus ?? "").Trim(), "IO", StringComparison.OrdinalIgnoreCase)
                && DdpTransitionService.Validate(
                    c, DdpTransitionService.TypeCommercial, oldStatus, "IO", _cache) == null;
            rowUpdates.Add((item.BomItemId, item.ProjectId, advance));
        }

        // Bookkeeping MySQL in transazione unica: o TUTTE le RDO e le righe registrano
        // l'ordine, o niente (il claim resta e impedisce comunque un secondo ordine).
        // Riferimento su TUTTE le righe (l'ordine esiste comunque): Rif. Danea = numero
        // ordine + IDDoc per il popup + data ordine + Data Prev. = consegna prevista;
        // l'avanzamento a IO resta soggetto alla matrice stati.
        try
        {
            using var tx = c.BeginTransaction();
            c.Execute(@"UPDATE purchase_rfqs SET status = 'CLOSED', danea_order_iddoc = @IdDoc, danea_order_num = @Num, updated_at = NOW()
                        WHERE id IN @Ids", new { order.IdDoc, order.Num, Ids = rfqIds }, tx);
            foreach (var (bomItemId, _, advance) in rowUpdates)
            {
                // date_needed («Data Prev.» in griglia) = consegna prevista dell'ordine:
                // se indicata sostituisce la data desiderata (fa fede la promessa fornitore).
                // Lo stato avanza a 'IO' SOLO se la matrice stati lo ammette (`advance`,
                // calcolato prima della transazione): prima era forzato e scavalcava la
                // matrice — l'unico posto del software dove le transizioni configurate in
                // Conf. DDP non contavano niente. Il riferimento all'ordine si scrive
                // comunque: l'ordine esiste, qualunque sia lo stato della riga.
                c.Execute(@"UPDATE bom_items
                            SET danea_ref = @Ref, danea_order_iddoc = @IdDoc,
                                date_ordered = CURDATE(),
                                date_needed = COALESCE(@Expected, date_needed),
                                item_status = IF(@Advance, 'IO', item_status),
                                updated_at = NOW(), updated_by = @UpdatedBy
                            WHERE id = @Id",
                    new { Ref = order.Num.ToString(), order.IdDoc, Expected = expectedDate, Advance = advance, Id = bomItemId, UpdatedBy = Firma() }, tx);

                // Cronistoria: «ordinato il» nasce qui, con il numero dell'ordine Danea —
                // ma solo se lo stato è davvero passato a IO, o la storia mentirebbe.
                if (advance)
                {
                    int projectIdRiga = c.ExecuteScalar<int>(
                        "SELECT project_id FROM bom_items WHERE id = @Id", new { Id = bomItemId }, tx);
                    c.Execute(@"
                        INSERT INTO ddp_item_events
                            (item_type, item_id, project_id, from_status, to_status,
                             changed_at, changed_by_name, origin, note)
                        VALUES ('COMMERCIAL', @ItemId, @ProjectId, NULL, 'IO', NOW(), @Utente, 'SISTEMA', @Note)",
                        new
                        {
                            ItemId = bomItemId,
                            ProjectId = projectIdRiga,
                            Utente = User.Identity?.Name ?? "",
                            Note = $"ordine Danea n. {order.Num}"
                        }, tx);
                }
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            // L'ordine in Danea ESISTE ma la registrazione in ATEC PM è fallita: il claim
            // (sentinella 0) resta a bloccare i doppioni; serve un intervento manuale.
            _log.LogError(ex, "Ordine Danea n. {Num} creato ma registrazione ATEC PM fallita (RDO {RfqIds})",
                order.Num, string.Join(",", rfqIds));
            return (order, $"Ordine n. {order.Num} CREATO in Danea, ma la registrazione in ATEC PM " +
                "non è riuscita: NON rigenerare l'ordine. Fai una segnalazione.");
        }

        // Notifiche real-time solo a commit riuscito.
        foreach (var (bomItemId, projectId, _) in rowUpdates)
        {
            var payload = new DdpChange
            {
                ProjectId = projectId,
                Action = "update",
                ItemId = bomItemId,
                DdpType = "COMMERCIAL",
            };
            _ = _hub.Clients.Group($"project-{projectId}").SendAsync("DdpChanged", payload);
            _ = _hub.Clients.Group(ProjectHub.AllGroup).SendAsync("DdpChanged", payload);
        }
        foreach (int id in rfqIds)
            NotifyRfqChange(id, "order");

        return (order, null);
    }

    [ScritturaNonDiCommessa("Le RDO stanno sul codice ATEC e raggruppano righe di commesse diverse: non appartengono a una commessa sola")]
    [HttpPost("{id}/cancel")]
    public IActionResult Cancel(int id)
    {
        using var c = _db.Open();
        // Annullabile TUTTO tranne ciò che ha (o sta generando) un ordine Danea: l'ordine
        // è irreversibile, la RDO che l'ha prodotto deve restare a fargli da pezza
        // d'appoggio. Prima si rifiutavano tutte le CLOSED, e un'aggiudicazione sbagliata
        // murava le righe: la RDO chiusa non si annullava e le righe non rientravano in
        // nessuna gara (la guardia anti-doppione le vede occupate finché la RDO non è
        // CANCELLED). Ora la gara chiusa per sbaglio si annulla e le righe tornano libere;
        // i valori scritti dall'aggiudicazione (prezzo, fornitore, identità) restano sulla
        // distinta e la prossima aggiudicazione li riscrive. Il claim della generazione
        // ordine (sentinella danea_order_iddoc = 0) conta come ordine in corso.
        // SELECT solo DIAGNOSTICA: il rifiuto lo decide comunque l'UPDATE qui sotto
        // (identico a prima); questa lettura serve unicamente a dire all'utente PERCHÉ.
        var stato = c.QueryFirstOrDefault<(string? Status, int? IdDoc, int? Num)>(@"
            SELECT status, danea_order_iddoc, danea_order_num
            FROM purchase_rfqs WHERE id = @Id", new { Id = id });
        int n = c.Execute(@"
            UPDATE purchase_rfqs SET status = 'CANCELLED', updated_at = NOW()
            WHERE id = @Id AND status <> 'CANCELLED' AND danea_order_iddoc IS NULL",
            new { Id = id });
        if (n == 0)
        {
            if (stato.Status == null)
                return Ok(ApiResponse<bool>.Fail("RDO non trovata."));
            if (stato.Status == "CANCELLED")
                return Ok(ApiResponse<bool>.Fail("RDO già annullata: le righe sono già libere."));
            if (stato.IdDoc.HasValue)
                return Ok(ApiResponse<bool>.Fail(stato.Num.HasValue
                    ? $"Questa RDO ha già generato l'ordine fornitore n. {stato.Num} in Danea: " +
                      "un ordine emesso non si annulla da qui."
                    // Sentinella 0 senza numero: generazione in corso ADESSO, oppure claim
                    // rimasto appeso da una generazione interrotta a metà — dal solo DB non
                    // si distingue, e il messaggio non deve promettere che si sblocca da sé.
                    : "Su questa RDO risulta una generazione dell'ordine Danea in corso (o " +
                      "interrotta a metà): non si può annullare. Se tra qualche minuto non " +
                      "si sblocca, fai una segnalazione."));
            // Corsa fra la SELECT e l'UPDATE (annullo o ordine concorrente): esito generico.
            return Ok(ApiResponse<bool>.Fail(
                "RDO non annullabile in questo momento: aggiorna la pagina e riprova."));
        }
        NotifyRfqChange(id, "cancel");
        return Ok(ApiResponse<bool>.Ok(true, "RDO annullata: le righe tornano disponibili per una nuova gara"));
    }

    private static PurchaseRfqDetail? LoadDetail(System.Data.IDbConnection c, int id)
    {
        var head = c.QueryFirstOrDefault<PurchaseRfqDetail>(@"
            SELECT r.id, COALESCE(r.atec_code,'') AS AtecCode, COALESCE(r.description,'') AS Description,
                   r.status AS Status, COALESCE(r.notes,'') AS Notes,
                   r.created_by AS CreatedBy,
                   COALESCE(CONCAT(e.first_name,' ',e.last_name),'') AS CreatedByName,
                   r.created_at AS CreatedAt, r.sent_at AS SentAt, r.closed_at AS ClosedAt, r.updated_at AS UpdatedAt,
                   r.danea_order_num AS DaneaOrderNum, r.danea_order_iddoc AS DaneaOrderIdDoc,
                   (SELECT COUNT(*) FROM purchase_rfq_items i WHERE i.rfq_id = r.id) AS ItemCount,
                   (SELECT COALESCE(SUM(b.quantity),0) FROM purchase_rfq_items i
                     JOIN bom_items b ON b.id = i.bom_item_id
                     WHERE i.rfq_id = r.id) AS TotalQuantity,
                   (SELECT COUNT(*) FROM purchase_rfq_offers o WHERE o.rfq_id = r.id) AS OfferCount,
                   -- Commessa (le RDO sono mono-commessa): senza, il dettaglio usciva con
                   -- projectCode VUOTO e l'oggetto delle email ai fornitori diceva
                   -- «Richiesta Offerta — Commessa » e basta.
                   (SELECT i.project_id FROM purchase_rfq_items i
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectId,
                   (SELECT p.code FROM purchase_rfq_items i
                     JOIN projects p ON p.id = i.project_id
                     WHERE i.rfq_id = r.id ORDER BY i.id LIMIT 1) AS ProjectCode
            FROM purchase_rfqs r
            LEFT JOIN employees e ON e.id = r.created_by
            WHERE r.id = @Id", new { Id = id });
        if (head == null) return null;

        if (head.AtecCode.Length > 0)
            head.AtecCode = CodexListItem.FormatCodice(head.AtecCode);

        head.Items = c.Query<PurchaseRfqItemDto>($@"
            SELECT i.id, i.rfq_id AS RfqId, i.bom_item_id AS BomItemId, i.project_id AS ProjectId,
                   -- Quantità ATTUALE della riga di distinta, non lo snapshot di
                   -- purchase_rfq_items: il fabbisogno può cambiare mentre la gara corre, e
                   -- l'ordine Danea (che somma queste righe) deve ordinare quello che serve
                   -- OGGI — con lo snapshot si ordinava la quantità di quando la RDO è nata.
                   COALESCE(p.code,'') AS ProjectCode, b.quantity AS Quantity,
                   COALESCE(b.part_number,'') AS PartNumber,
                   COALESCE(b.description,'') AS Description,
                   COALESCE(b.item_status,'') AS ItemStatus,
                   COALESCE(b.unit_cost, 0) AS UnitCost,
                   b.date_needed AS DateNeeded,
                   COALESCE(b.danea_ref,'') AS DaneaRef,
                   b.danea_order_iddoc AS DaneaOrderIdDoc,
                   -- Codice ATEC EFFICACE, non il solo snapshot di riga: `assign-from-bom`
                   -- scrive lo snapshot solo sulla riga di partenza e lascia che sia il
                   -- COALESCE in lettura a coprire le altre (è quello che fanno la Inbox
                   -- Acquisti e LoadFreeRows). Leggendo qui il solo `b.atec_code` la maggior
                   -- parte delle righe risulterebbe senza codice pur mostrandone uno a video,
                   -- e la guardia sulle gare miste diventerebbe un colpo a vuoto.
                   COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS AtecCode
            FROM purchase_rfq_items i
            JOIN projects p ON p.id = i.project_id
            JOIN bom_items b ON b.id = i.bom_item_id
            LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
            WHERE i.rfq_id = @Id
            ORDER BY {ProjectSorting.OrderBy("p")}, i.id", new { Id = id }).ToList();

        // Codice puntato come ovunque nel software (testata qui sopra, Inbox Acquisti,
        // Catalogo, griglia DDP): mostrarlo grezzo solo qui farebbe sembrare due codici
        // diversi lo stesso codice, per giunta a due righe di distanza nella stessa finestra.
        // La guardia sulle gare miste normalizza togliendo i punti, quindi non ne risente.
        foreach (var riga in head.Items)
            if (riga.AtecCode.Length > 0)
                riga.AtecCode = CodexListItem.FormatCodice(riga.AtecCode);

        head.Offers = c.Query<PurchaseRfqOfferDto>(@"
            SELECT o.id, o.rfq_id AS RfqId, o.supplier_id AS SupplierId,
                   COALESCE(s.company_name,'') AS SupplierName,
                   COALESCE(s.email,'') AS SupplierEmail,
                   o.catalog_item_id AS CatalogItemId,
                   COALESCE(ci.code,'') AS CatalogCode,
                   o.unit_price AS UnitPrice, o.valid_until AS ValidUntil,
                   COALESCE(o.notes,'') AS Notes, o.email_sent_at AS EmailSentAt,
                   o.is_winner AS IsWinner
            FROM purchase_rfq_offers o
            JOIN suppliers s ON s.id = o.supplier_id
            LEFT JOIN catalog_items ci ON ci.id = o.catalog_item_id
            WHERE o.rfq_id = @Id
            ORDER BY s.company_name", new { Id = id }).ToList();

        return head;
    }
}
