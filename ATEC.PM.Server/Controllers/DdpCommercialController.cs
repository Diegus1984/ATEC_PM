using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Inbox DDP commerciale cross-commessa (Acquisti).
/// Scritture restano su ProjectsController (/api/projects/{id}/ddp/...).
/// </summary>
/// <remarks>
/// La chiave è quella della PAGINA che lo usa (Inbox Acquisti), non quella della sezione
/// «DDP Commerciali» della commessa: questo controller espone una sola GET cross-commessa e
/// il suo unico chiamante è <c>/acquisti</c> — la sezione di commessa non lo tocca mai.
/// Stessa chiave già usata da <c>DaneaOrdersController</c>, l'altro controller di quella pagina.
/// (segnalazione #63, Fase 1)
/// </remarks>
[ApiController]
[Route("api/ddp-commercial")]
[Authorize]
[RequireFeature("nav.acquisti_inbox")]
public class DdpCommercialController : ControllerBase
{
    private readonly DbService _db;
    private readonly ProjectWriteGuard _guard;

    public DdpCommercialController(DbService db, ProjectWriteGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    /// <summary>
    /// Elenco flat di tutte le righe commerciali (commesse non cancellate).
    /// Filtri vista (VER/CHEK/RO/DO, aggregazione ATEC) lato client.
    /// </summary>
    [HttpGet("inbox")]
    public IActionResult GetInbox([FromQuery] int? projectId = null)
    {
        try
        {
            using var c = _db.Open();
            var rows = c.Query<AcquistiInboxItem>($@"
                SELECT b.id AS Id, b.project_id AS ProjectId,
                       b.catalog_item_id AS CatalogItemId,
                       COALESCE(b.part_number,'') AS PartNumber,
                       COALESCE(b.description,'') AS Description,
                       COALESCE(b.unit,'') AS Unit,
                       b.quantity AS Quantity,
                       b.unit_cost AS UnitCost,
                       b.supplier_id AS SupplierId,
                       COALESCE(s.company_name,'') AS SupplierName,
                       COALESCE(b.manufacturer,'') AS Manufacturer,
                       -- «Cod. prod. forn.» dell'articolo Danea agganciato (01/09/2026).
                       COALESCE(ci.supplier_code,'') AS SupplierCode,
                       COALESCE(b.item_status,'VER') AS ItemStatus,
                       COALESCE(b.requested_by,'') AS RequestedBy,
                       COALESCE(b.danea_ref,'') AS DaneaRef,
                       b.danea_order_iddoc AS DaneaOrderIdDoc,
                       -- Riga già dentro una RDO viva: nel pannello «Richiedi le offerte»
                       -- resta VISIBILE ma non selezionabile, col link alla sua RDO
                       -- (niente deve sparire in silenzio).
                       EXISTS(SELECT 1 FROM purchase_rfq_items pri
                              JOIN purchase_rfqs pr ON pr.id = pri.rfq_id
                              WHERE pri.bom_item_id = b.id
                                AND pr.status <> 'CANCELLED') AS InActiveRfq,
                       (SELECT pri.rfq_id FROM purchase_rfq_items pri
                        JOIN purchase_rfqs pr ON pr.id = pri.rfq_id
                        WHERE pri.bom_item_id = b.id AND pr.status <> 'CANCELLED'
                        ORDER BY pri.rfq_id DESC LIMIT 1) AS ActiveRfqId,
                       (SELECT pr.status FROM purchase_rfq_items pri
                        JOIN purchase_rfqs pr ON pr.id = pri.rfq_id
                        WHERE pri.bom_item_id = b.id AND pr.status <> 'CANCELLED'
                        ORDER BY pri.rfq_id DESC LIMIT 1) AS ActiveRfqStatus,
                       -- Oggetto della RDO: è il titolo della GARA, non la descrizione
                       -- dell'articolo. Va mostrato a parte (icona + tooltip), mai al posto
                       -- della descrizione della riga.
                       (SELECT COALESCE(pr.description,'') FROM purchase_rfq_items pri
                        JOIN purchase_rfqs pr ON pr.id = pri.rfq_id
                        WHERE pri.bom_item_id = b.id AND pr.status <> 'CANCELLED'
                        ORDER BY pri.rfq_id DESC LIMIT 1) AS ActiveRfqSubject,
                       b.date_needed AS DateNeeded,
                       COALESCE(b.destination,'') AS Destination,
                       COALESCE(b.destination_spec,'') AS DestinationSpec,
                       COALESCE(b.notes,'') AS Notes,
                       b.ddp_type AS DdpType,
                       -- Codice ATEC effettivo: snapshot di riga, altrimenti mapping vivo
                       -- dell'articolo Danea (catalog_items.atec_code): appena l'associazione
                       -- esiste, TUTTE le righe di quell'articolo la mostrano senza backfill.
                       COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS AtecCode,
                       -- #142 anche qui (01/09/2026): il grezzo «scoperto» e i codici senza
                       -- articoli si vedono PRIMA di provare la gara, non solo nel rifiuto.
                       COALESCE(b.raw_codex_code,'') AS RawCodexCode,
                       COALESCE(b.raw_sources,'') AS RawSources,
                       {GrezziDerivazione.SqlGrezzoScoperto("b")} AS RawNeedsMapping,
                       {GrezziDerivazione.SqlAtecScoperto("COALESCE(NULLIF(b.atec_code,''), ci.atec_code)")} AS AtecNeedsMapping,
                       b.created_by AS CreatedById,
                       COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS CreatedByName,
                       b.created_at AS CreatedAt, b.updated_at AS UpdatedAt,
                       COALESCE(p.code,'') AS ProjectCode,
                       COALESCE(p.title,'') AS ProjectTitle,
                       COALESCE(cu.company_name,'') AS CustomerName,
                       CASE
                         WHEN b.date_needed IS NULL THEN NULL
                         ELSE DATEDIFF(CURDATE(), b.date_needed)
                       END AS DaysLate
                FROM bom_items b
                JOIN projects p ON p.id = b.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                LEFT JOIN suppliers s ON s.id = b.supplier_id
                LEFT JOIN catalog_items ci ON ci.id = b.catalog_item_id
                LEFT JOIN employees e ON e.id = b.created_by
                WHERE b.ddp_type = 'COMMERCIAL'
                  AND COALESCE(p.status,'') <> 'CANCELLED'{_guard.FiltroBozzeSql(User)}
                  AND (@ProjectId IS NULL OR b.project_id = @ProjectId)
                  -- #119: l'intestazione di un gruppo (5xx) NON è una riga da comprare — è
                  -- l'etichetta che raggruppa i componenti, che qui ci sono già uno per uno,
                  -- e ha costo 0. Lasciarla passare metterebbe ad Acquisti una voce da
                  -- ordinare che non corrisponde a nessun articolo. Stesso principio della
                  -- dedup dei costi (ProjectEconomics.CommercialeParentDedup).
                  AND b.{ProjectEconomics.CommercialeParentDedup}
                ORDER BY
                  CASE WHEN b.date_needed IS NULL THEN 1 ELSE 0 END,
                  b.date_needed ASC,
                  p.code ASC,
                  b.id ASC",
                new { ProjectId = projectId }).ToList();

            foreach (AcquistiInboxItem row in rows)
            {
                if (row.AtecCode.Length > 0)
                    row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
                if (row.RawCodexCode.Length > 0)
                    row.RawCodexCode = CodexListItem.FormatCodice(row.RawCodexCode);
            }

            return Ok(ApiResponse<List<AcquistiInboxItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<AcquistiInboxItem>>.Fail(ex.Message));
        }
    }
}
