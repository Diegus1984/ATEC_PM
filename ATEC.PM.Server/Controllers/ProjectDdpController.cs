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
/// Distinta DDP commerciale della commessa (<c>bom_items</c>): righe, fornitore grezzo,
/// stati e cronistoria. Stessa rotta <c>api/projects/{id}/ddp…</c> di prima: spostato da
/// <c>ProjectsController</c> il 04/09/2026, nessun percorso cambiato.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
// #88: ogni scrittura riguarda UNA commessa (l'id sta nella rotta), quindi il cancello si mette
// una volta sola sulla classe: una commessa in bozza, in stand-by o chiusa si consulta ma non si
// modifica, salvo il permesso di scavalco. E una bozza non si VEDE proprio, letture comprese.
[RequireProjectWritable]
[RequireProjectVisible]
public class ProjectDdpController : ProjectsControllerBase
{
    private readonly ILogger<ProjectDdpController> _logger;
    public ProjectDdpController(
        DbService db,
        NotificationService notif,
        ILogger<ProjectDdpController> logger,
        IHubContext<ProjectHub> hub,
        FeatureAccessService access,
        AnagraficheCache cache) : base(db, hub, notif, access, cache)
    {
        _logger = logger;
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
            var rows = c.Query<BomItemListItem>($@"
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
                   -- #142: grezzo «scoperto» → la riga si ferma finché il 201 non è associato.
                   {GrezziDerivazione.SqlGrezzoScoperto("b")} AS RawNeedsMapping,
                   -- Codice ATEC «scoperto» (01/09/2026): la riga si inserisce e si
                   -- associa, ma non cambia stato finché il codice non ha un articolo.
                   {GrezziDerivazione.SqlAtecScoperto("COALESCE(NULLIF(b.atec_code,''), ci.atec_code)")} AS AtecNeedsMapping,
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

    // #142: applica la SCELTA del fornitore a una riga grezzo (nata dalla derivazione #135).
    // La logica vera sta in GrezziDerivazione.ApplicaFornitore (testata coi test del
    // ricalcolo): qui solo permessi, esiti HTTP e notifica real-time.
    [RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox", "project.ddp_officina")]
    [HttpPost("{id}/ddp/raw-supplier")]
    public IActionResult SetRawSupplier(int id, [FromBody] RawSupplierRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _db.Open();
            int? firma = GetCurrentEmployeeId() > 0 ? GetCurrentEmployeeId() : null;
            GrezziDerivazione.EsitoFornitore esito =
                GrezziDerivazione.ApplicaFornitore(c, id, req.RawCodexCode, req.CatalogItemId, firma);
            if (!esito.Ok)
            {
                return esito.RigaId == 0 && esito.Errore != null && esito.Errore.Contains("non trovata")
                    ? NotFound(ApiResponse<bool>.Fail(esito.Errore))
                    : BadRequest(ApiResponse<bool>.Fail(esito.Errore ?? "Fornitore non applicato."));
            }

            NotifyDdpChange(id, conn, "update", esito.RigaId);
            return Ok(ApiResponse<bool>.Ok(true, "Fornitore applicato al grezzo"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(ex.Message));
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

            // #142: una riga «scoperta» non avanza di stato — prima si associa il
            // commerciale vero, «altrimenti risulta ordinato» un articolo che Danea non
            // conosce (regola di Diego, 01/09/2026). Vale per il grezzo da derivazione E
            // per qualunque riga col codice ATEC senza articoli. Le altre modifiche
            // (quantità, note, date) restano libere: fermo l'oggetto, non chi ci lavora.
            if (!string.Equals(oldStatus ?? "", req.ItemStatus ?? "", StringComparison.OrdinalIgnoreCase))
            {
                const string exprAtec =
                    "COALESCE(NULLIF(b.atec_code,''), (SELECT ci2.atec_code FROM catalog_items ci2 WHERE ci2.id = b.catalog_item_id))";
                var scoperta = c.QueryFirstOrDefault<(bool Grezzo, bool Atec, string? AtecCode)>($@"
                    SELECT {GrezziDerivazione.SqlGrezzoScoperto("b")} AS Grezzo,
                           {GrezziDerivazione.SqlAtecScoperto(exprAtec)} AS Atec,
                           {exprAtec} AS AtecCode
                    FROM bom_items b
                    WHERE b.id = @ItemId AND b.project_id = @Id",
                    new { ItemId = itemId, Id = id });
                if (scoperta.Grezzo)
                    return BadRequest(ApiResponse<DateTime?>.Fail(
                        "Grezzo da associare: il codice 201 di derivazione non è ancora associato " +
                        "a nessun articolo commerciale. Associa l'articolo (Codex → Articoli Danea) e riprova."));
                if (scoperta.Atec)
                    return BadRequest(ApiResponse<DateTime?>.Fail(
                        $"Codice da associare: il codice ATEC {CodexListItem.FormatCodice(scoperta.AtecCode ?? "")} " +
                        "non è associato a nessun articolo commerciale. Associa l'articolo " +
                        "(icona catena sulla riga) e riprova."));
            }

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

    /// <summary>Quantità editabile sulla DDP commerciale: VER (ingresso) o DO.</summary>
    private static bool IsCommercialQtyEditable(string? status) =>
        string.Equals(status, "VER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DO", StringComparison.OrdinalIgnoreCase);
}
