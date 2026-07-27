using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Ciclo RDO Acquisti: crea richiesta offerta su gruppo ATEC, invia email fornitori,
/// registra offerte, sceglie vincitore (applica fornitore+prezzo e avanza stati BOM).
/// </summary>
[ApiController]
[Route("api/purchase-rfqs")]
[Authorize(Roles = "ADMIN,PM,RESP_REPARTO")]
public class PurchaseRfqController : ControllerBase
{
    private readonly DbService _db;
    private readonly EmailService _email;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly NotificationService _notif;
    private readonly DaneaOrderService _daneaOrder;

    public PurchaseRfqController(DbService db, EmailService email, IHubContext<ProjectHub> hub,
        NotificationService notif, DaneaOrderService daneaOrder)
    {
        _db = db;
        _email = email;
        _hub = hub;
        _notif = notif;
        _daneaOrder = daneaOrder;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

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
                   (SELECT COALESCE(SUM(i.quantity),0) FROM purchase_rfq_items i WHERE i.rfq_id = r.id) AS TotalQuantity,
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

    [HttpPost]
    public IActionResult Create([FromBody] PurchaseRfqCreateRequest req)
    {
        string atec = (req.AtecCode ?? "").Replace(".", "").Trim();
        if (atec.Length == 0)
            return Ok(ApiResponse<List<int>>.Fail("Codice ATEC obbligatorio."));
        if (req.BomItemIds == null || req.BomItemIds.Count == 0)
            return Ok(ApiResponse<List<int>>.Fail("Selezionare almeno una riga distinta."));

        using var c = _db.Open();
        var bomRows = c.Query<(int Id, int ProjectId, decimal Quantity, string Description)>(@"
            SELECT id, project_id, quantity, COALESCE(description,'')
            FROM bom_items
            WHERE id IN @Ids AND ddp_type = 'COMMERCIAL'",
            new { Ids = req.BomItemIds }).ToList();
        if (bomRows.Count == 0)
            return Ok(ApiResponse<List<int>>.Fail("Nessuna riga BOM valida."));

        // Guardia anti-doppione: una riga distinta può stare in UNA sola RDO viva alla
        // volta (le righe RO restano visibili nei gruppi ATEC → senza questo controllo si
        // potrebbe ri-mandarle in gara e ordinarle due volte). Le righe già occupate si
        // SALTANO (così una nuova riga del gruppo può partire in una nuova RDO senza
        // dover annullare la gara in corso); le RDO annullate liberano le loro righe.
        var busyBomIds = c.Query<int>(@"
            SELECT DISTINCT i.bom_item_id FROM purchase_rfq_items i
            JOIN purchase_rfqs r ON r.id = i.rfq_id
            WHERE i.bom_item_id IN @Ids AND r.status <> 'CANCELLED'",
            new { Ids = req.BomItemIds }).ToHashSet();
        bomRows = bomRows.Where(r => !busyBomIds.Contains(r.Id)).ToList();
        if (bomRows.Count == 0)
            return Ok(ApiResponse<List<int>>.Fail(
                "Tutte le righe selezionate sono già in RDO non annullate: nessuna nuova RDO da creare."));

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
            return Ok(ApiResponse<List<int>>.Fail(
                $"Creazione RDO non riuscita: {ex.Message} (nessuna RDO creata)."));
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
                "Le righe selezionate sono già in gara (RDO non annullate)."));

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
                    var anag = c.QueryFirstOrDefault<(string Name, string Email)>(@"
                        SELECT COALESCE(company_name,''), COALESCE(email,'')
                        FROM suppliers WHERE id = @Id", new { Id = opt.SupplierId });
                    supplier = new OfferPlanSupplier
                    {
                        SupplierId = opt.SupplierId,
                        SupplierName = anag.Name,
                        SupplierEmail = anag.Email,
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
                "Nessun fornitore trovato: le righe non hanno né fornitore né codice ATEC mappato."));
        return Ok(ApiResponse<List<OfferPlanSupplier>>.Ok(result));
    }

    /// <summary>
    /// Crea le richieste offerta dai fornitori scelti: RDO automatiche (una per
    /// commessa × codice ATEC, o per riga se senza codice) con le offerte dei
    /// fornitori selezionati. Ritorna le offerte pronte per la mailto.
    /// </summary>
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
                "Le righe selezionate sono già in gara (RDO non annullate)."));
        var suppliersByRow = selections
            .GroupBy(s => s.BomItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(s => s.SupplierIds).Distinct().ToList());

        // Gruppi RDO: commessa × codice ATEC (righe senza codice: una RDO per riga).
        var groups = rows
            .Where(r => suppliersByRow.ContainsKey(r.Id))
            .GroupBy(r => (r.ProjectId, Key: r.Atec.Length > 0 ? r.Atec : $"ROW:{r.Id}"))
            .ToList();
        if (groups.Count == 0)
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail("Nessuna riga valida."));

        // Articolo di catalogo per (gruppo, fornitore), risolto PRIMA della transazione
        // (MySqlConnector non ammette comandi fuori da una tx pendente).
        var catalogFor = new Dictionary<(string GroupKey, int SupplierId), int?>();
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
                catalogFor[(g.Key.Key, sid)] = catalogId;
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
                        new { Rfq = rfqId, Sid = sid, Cat = catalogFor[(g.Key.Key, sid)] }, tx);

                createdRfqIds.Add(rfqId);
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<PurchaseRfqEmailCandidate>>.Fail(
                $"Creazione richieste non riuscita: {ex.Message} (nessuna richiesta creata)."));
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
                   (SELECT COALESCE(SUM(i.quantity),0) FROM purchase_rfq_items i
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
                   (SELECT COALESCE(SUM(i.quantity),0) FROM purchase_rfq_items i
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
        foreach (var offer in detail.Offers)
        {
            if (string.IsNullOrWhiteSpace(offer.SupplierEmail)) continue;

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
            return Ok(ApiResponse<bool>.Ok(true, "RDO marcata SENT (SMTP disabilitato: nessuna email inviata)."));
        return Ok(ApiResponse<bool>.Ok(true, $"Email accodate: {sent}/{detail.Offers.Count}"));
    }

    [HttpPut("{id}/offers/{offerId}")]
    public IActionResult SaveOffer(int id, int offerId, [FromBody] PurchaseRfqOfferSaveRequest req)
    {
        using var c = _db.Open();
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

    [HttpPost("{id}/select-winner")]
    public IActionResult SelectWinner(int id, [FromBody] PurchaseRfqSelectWinnerRequest req)
    {
        using var c = _db.Open();
        var detail = LoadDetail(c, id);
        if (detail == null)
            return Ok(ApiResponse<bool>.Fail("RDO non trovata."));
        if (detail.Status == "CLOSED")
            return Ok(ApiResponse<bool>.Fail("RDO già chiusa."));

        var offer = detail.Offers.FirstOrDefault(o => o.Id == req.OfferId);
        if (offer == null)
            return Ok(ApiResponse<bool>.Fail("Offerta non trovata."));
        if (!offer.UnitPrice.HasValue)
        {
            var itemCost = detail.Items.FirstOrDefault(i => i.UnitCost.HasValue && i.UnitCost.Value > 0)?.UnitCost;
            if (itemCost.HasValue && itemCost.Value > 0)
            {
                offer.UnitPrice = itemCost.Value;
                c.Execute("UPDATE purchase_rfq_offers SET unit_price = @Price WHERE id = @OfferId",
                    new { Price = offer.UnitPrice.Value, OfferId = offer.Id });
            }
            else
            {
                return Ok(ApiResponse<bool>.Fail("Registrare il prezzo dell'offerta prima di scegliere il vincitore."));
            }
        }

        string targetStatus = string.IsNullOrWhiteSpace(req.TargetStatus) ? "RO" : req.TargetStatus.Trim().ToUpperInvariant();

        // Snapshot catalogo se presente.
        string partNumber = offer.CatalogCode;
        string manufacturer = "";
        string unit = "PZ";
        string description = detail.Description;
        if (offer.CatalogItemId.HasValue)
        {
            var cat = c.QueryFirstOrDefault<(string Code, string Desc, string Unit, string Manufacturer)>(@"
                SELECT COALESCE(code,''), COALESCE(description,''), COALESCE(unit,'PZ'), COALESCE(manufacturer,'')
                FROM catalog_items WHERE id = @Id", new { Id = offer.CatalogItemId.Value });
            if (cat != default)
            {
                partNumber = cat.Code;
                description = string.IsNullOrEmpty(cat.Desc) ? description : cat.Desc;
                unit = string.IsNullOrEmpty(cat.Unit) ? unit : cat.Unit;
                manufacturer = cat.Manufacturer;
            }
        }

        string atec = detail.AtecCode.Replace(".", "");

        // Per la campanella: righe avanzate di stato, raggruppate per commessa (1 notifica a commessa).
        var advanced = new Dictionary<int, (string ProjectCode, int Count, string FromStatus, int FirstItemId)>();

        foreach (var item in detail.Items)
        {
            string? oldStatus = c.ExecuteScalar<string?>(
                "SELECT item_status FROM bom_items WHERE id = @Id", new { Id = item.BomItemId });
            string? transitionError = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeCommercial, oldStatus, targetStatus);
            string applyStatus = transitionError == null ? targetStatus : (oldStatus ?? "DO");

            if (applyStatus != oldStatus)
            {
                advanced[item.ProjectId] = advanced.TryGetValue(item.ProjectId, out var cur)
                    ? (cur.ProjectCode, cur.Count + 1, cur.FromStatus, cur.FirstItemId)
                    : (item.ProjectCode, 1, oldStatus ?? "", item.BomItemId);
            }

            c.Execute(@"
                UPDATE bom_items SET
                    catalog_item_id = @CatId,
                    part_number = @Part,
                    description = @Desc,
                    unit = @Unit,
                    unit_cost = @Price,
                    supplier_id = @SuppId,
                    manufacturer = @Mfr,
                    atec_code = @Atec,
                    item_status = @Status,
                    updated_at = NOW()
                WHERE id = @BomId AND project_id = @ProjId",
                new
                {
                    CatId = offer.CatalogItemId,
                    Part = partNumber,
                    Desc = description,
                    Unit = unit,
                    Price = offer.UnitPrice.Value,
                    SuppId = offer.SupplierId,
                    Mfr = manufacturer,
                    Atec = atec,
                    Status = applyStatus,
                    BomId = item.BomItemId,
                    ProjId = item.ProjectId,
                });

            var payload = new DdpChange
            {
                ProjectId = item.ProjectId,
                Action = "update",
                ItemId = item.BomItemId,
                DdpType = "COMMERCIAL",
            };
            _ = _hub.Clients.Group($"project-{item.ProjectId}").SendAsync("DdpChanged", payload);
            _ = _hub.Clients.Group(ProjectHub.AllGroup).SendAsync("DdpChanged", payload);
        }

        c.Execute("UPDATE purchase_rfq_offers SET is_winner = 0 WHERE rfq_id = @Id", new { Id = id });
        c.Execute("UPDATE purchase_rfq_offers SET is_winner = 1 WHERE id = @OfferId AND rfq_id = @Id",
            new { OfferId = req.OfferId, Id = id });
        c.Execute(@"UPDATE purchase_rfqs SET status = 'CLOSED', closed_at = NOW(), updated_at = NOW() WHERE id = @Id",
            new { Id = id });
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

        return Ok(ApiResponse<bool>.Ok(true, $"Vincitore applicato a {detail.Items.Count} righe"));
    }

    // Strada B (22/07/2026): scrive l'ordine fornitore DIRETTAMENTE nel Firebird di
    // Danea (Atec_PM) via DaneaOrderService — testata+righe+IVA+movimento InArrivo.
    // Una riga d'ordine per RDO: articolo Danea del vincitore, qtà = somma fabbisogni,
    // prezzo = offerta vincente. REGOLA AZIENDALE: 1 ordine = 1 fornitore + 1 commessa.
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
            if (detail.DaneaOrderNum.HasValue)
                return (null, $"RDO #{id}: ordine già generato (n. {detail.DaneaOrderNum}), non si duplica.");
            var winner = detail.Offers.FirstOrDefault(o => o.IsWinner);
            if (winner == null || !winner.UnitPrice.HasValue)
                return (null, $"RDO #{id}: scegliere prima il vincitore (con prezzo).");
            // Articolo Danea del vincitore: dal suo articolo di catalogo (codice Danea).
            string? articleCode = winner.CatalogItemId.HasValue
                ? c.ExecuteScalar<string?>("SELECT code FROM catalog_items WHERE id = @Id",
                    new { Id = winner.CatalogItemId.Value })
                : null;
            if (string.IsNullOrWhiteSpace(articleCode))
                return (null, $"RDO #{id}: l'offerta vincente non ha un articolo Danea associato.");
            decimal totalQty = detail.Items.Sum(i => i.Quantity);
            if (totalQty <= 0)
                return (null, $"RDO #{id}: quantità totale nulla.");

            details.Add(detail);
            winners.Add(winner);
            lines.Add(new DaneaOrderService.OrderLine(articleCode.Trim(), totalQty, winner.UnitPrice.Value));
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
            if (c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = 0, updated_at = NOW()
                            WHERE id = @Id AND danea_order_iddoc IS NULL", new { Id = id }) == 1)
                claimedIds.Add(id);
        }
        if (claimedIds.Count != rfqIds.Count)
        {
            if (claimedIds.Count > 0)
                c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = NULL, updated_at = NOW()
                            WHERE id IN @Ids AND danea_order_iddoc = 0", new { Ids = claimedIds });
            return (null, "Generazione ordine già eseguita o in corso da un altro utente: aggiorna la pagina.");
        }

        string note = $"RDO {string.Join(", ", details.Select(d => $"#{d.Id}"))} — " +
                      $"{string.Join(", ", details.Select(d => d.AtecCode).Distinct())} (generato da ATEC PM)";
        DaneaOrderService.OrderResult order;
        try
        {
            order = _daneaOrder.CreateSupplierOrder(supplier.Vat, supplier.Name, lines, expectedDate, note);
        }
        catch (Exception ex)
        {
            // Nessuna scrittura avvenuta in Danea: si rilascia il claim e si può riprovare.
            c.Execute(@"UPDATE purchase_rfqs SET danea_order_iddoc = NULL, updated_at = NOW()
                        WHERE id IN @Ids AND danea_order_iddoc = 0", new { Ids = rfqIds });
            return (null, $"Ordine NON creato: {ex.Message}");
        }

        // Avanzamenti calcolati PRIMA della transazione (Validate legge la matrice dalla
        // stessa connessione: MySqlConnector non ammette comandi fuori da una tx pendente).
        var rowUpdates = new List<(int BomItemId, int ProjectId, bool Advance)>();
        foreach (var item in details.SelectMany(d => d.Items))
        {
            string? oldStatus = c.ExecuteScalar<string?>(
                "SELECT item_status FROM bom_items WHERE id = @Id", new { Id = item.BomItemId });
            bool advance = DdpTransitionService.Validate(
                c, DdpTransitionService.TypeCommercial, oldStatus, "IO") == null;
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
                // Al momento della creazione dell'ODA, lo stato della riga viene sempre impostato a 'IO' (In Ordine).
                c.Execute(@"UPDATE bom_items
                            SET danea_ref = @Ref, danea_order_iddoc = @IdDoc,
                                date_ordered = CURDATE(),
                                date_needed = COALESCE(@Expected, date_needed),
                                item_status = 'IO',
                                updated_at = NOW()
                            WHERE id = @Id",
                    new { Ref = order.Num.ToString(), order.IdDoc, Expected = expectedDate, Id = bomItemId }, tx);
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            // L'ordine in Danea ESISTE ma la registrazione in ATEC PM è fallita: il claim
            // (sentinella 0) resta a bloccare i doppioni; serve un intervento manuale.
            return (order, $"Ordine n. {order.Num} CREATO in Danea, ma registrazione in ATEC PM non riuscita: {ex.Message}. " +
                "Non rigenerare l'ordine: segnala il problema.");
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

    [HttpPost("{id}/cancel")]
    public IActionResult Cancel(int id)
    {
        using var c = _db.Open();
        int n = c.Execute(@"
            UPDATE purchase_rfqs SET status = 'CANCELLED', updated_at = NOW()
            WHERE id = @Id AND status <> 'CLOSED'", new { Id = id });
        if (n == 0)
            return Ok(ApiResponse<bool>.Fail("RDO non trovata o già chiusa."));
        NotifyRfqChange(id, "cancel");
        return Ok(ApiResponse<bool>.Ok(true, "RDO annullata"));
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
                   (SELECT COALESCE(SUM(i.quantity),0) FROM purchase_rfq_items i WHERE i.rfq_id = r.id) AS TotalQuantity,
                   (SELECT COUNT(*) FROM purchase_rfq_offers o WHERE o.rfq_id = r.id) AS OfferCount
            FROM purchase_rfqs r
            LEFT JOIN employees e ON e.id = r.created_by
            WHERE r.id = @Id", new { Id = id });
        if (head == null) return null;

        if (head.AtecCode.Length > 0)
            head.AtecCode = CodexListItem.FormatCodice(head.AtecCode);

        head.Items = c.Query<PurchaseRfqItemDto>(@"
            SELECT i.id, i.rfq_id AS RfqId, i.bom_item_id AS BomItemId, i.project_id AS ProjectId,
                   COALESCE(p.code,'') AS ProjectCode, i.quantity AS Quantity,
                   COALESCE(b.part_number,'') AS PartNumber,
                   COALESCE(b.description,'') AS Description,
                   COALESCE(b.item_status,'') AS ItemStatus,
                   COALESCE(b.unit_cost, 0) AS UnitCost,
                   b.date_needed AS DateNeeded,
                   COALESCE(b.danea_ref,'') AS DaneaRef,
                   b.danea_order_iddoc AS DaneaOrderIdDoc
            FROM purchase_rfq_items i
            JOIN projects p ON p.id = i.project_id
            JOIN bom_items b ON b.id = i.bom_item_id
            WHERE i.rfq_id = @Id
            ORDER BY p.code, i.id", new { Id = id }).ToList();

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
