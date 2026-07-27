using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Inbox DDP commerciale cross-commessa (Acquisti).
/// Scritture restano su ProjectsController (/api/projects/{id}/ddp/...).
/// </summary>
[ApiController]
[Route("api/ddp-commercial")]
[Authorize]
public class DdpCommercialController : ControllerBase
{
    private readonly DbService _db;
    public DdpCommercialController(DbService db) => _db = db;

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
            var rows = c.Query<AcquistiInboxItem>(@"
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
                       b.date_needed AS DateNeeded,
                       COALESCE(b.destination,'') AS Destination,
                       COALESCE(b.destination_spec,'') AS DestinationSpec,
                       COALESCE(b.notes,'') AS Notes,
                       b.ddp_type AS DdpType,
                       -- Codice ATEC effettivo: snapshot di riga, altrimenti mapping vivo
                       -- dell'articolo Danea (catalog_items.atec_code): appena l'associazione
                       -- esiste, TUTTE le righe di quell'articolo la mostrano senza backfill.
                       COALESCE(NULLIF(b.atec_code,''), ci.atec_code, '') AS AtecCode,
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
                WHERE b.ddp_type = 'COMMERCIAL'
                  AND COALESCE(p.status,'') <> 'CANCELLED'
                  AND (@ProjectId IS NULL OR b.project_id = @ProjectId)
                ORDER BY
                  CASE WHEN b.date_needed IS NULL THEN 1 ELSE 0 END,
                  b.date_needed ASC,
                  p.code ASC,
                  b.id ASC",
                new { ProjectId = projectId }).ToList();

            foreach (AcquistiInboxItem row in rows)
                if (row.AtecCode.Length > 0)
                    row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);

            return Ok(ApiResponse<List<AcquistiInboxItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<AcquistiInboxItem>>.Fail(ex.Message));
        }
    }
}
