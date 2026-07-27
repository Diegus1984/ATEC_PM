using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Inbox DDP Officina cross-commessa per il Responsabile Officina.
/// Scritture restano su ProjectsController (/api/projects/{id}/ddp-officina/...).
/// </summary>
[ApiController]
[Route("api/ddp-officina")]
[Authorize]
public class DdpOfficinaController : ControllerBase
{
    private readonly DbService _db;
    public DdpOfficinaController(DbService db) => _db = db;

    /// <summary>
    /// Elenco flat di tutte le righe officina (commesse non cancellate).
    /// Filtri vista lato client; qui solo opzionale projectId.
    /// Ordine: ultra-critico → priorità WR → scadenza / ritardo → codice commessa.
    /// </summary>
    [HttpGet("inbox")]
    public IActionResult GetInbox([FromQuery] int? projectId = null)
    {
        try
        {
            using var c = _db.Open();
            var rows = c.Query<OfficinaInboxItem>(@"
                SELECT o.id AS Id, o.project_id AS ProjectId,
                       COALESCE(o.part_number,'') AS PartNumber,
                       COALESCE(o.description,'') AS Description,
                       o.quantity AS Quantity,
                       o.quantity_produced AS QuantityProduced,
                       o.unit_cost AS UnitCost,
                       COALESCE(o.material,'') AS Material,
                       COALESCE(o.treatment,'') AS Treatment,
                       o.supplier_id AS SupplierId,
                       COALESCE(o.supplier_name,'') AS SupplierName,
                       COALESCE(o.item_status,'DO') AS ItemStatus,
                       COALESCE(o.requested_by,'') AS RequestedBy,
                       COALESCE(o.danea_ref,'') AS DaneaRef,
                       o.date_needed AS DateNeeded, o.order_date AS OrderDate,
                       COALESCE(o.destination,'') AS Destination,
                       COALESCE(o.destination_spec,'') AS DestinationSpec,
                       COALESCE(o.notes,'') AS Notes,
                       o.parent_officina_item_id AS ParentOfficinaItemId,
                       o.composition_qty AS CompositionQty,
                       o.created_at AS CreatedAt, o.updated_at AS UpdatedAt,
                       COALESCE(p.code,'') AS ProjectCode,
                       COALESCE(p.title,'') AS ProjectTitle,
                       COALESCE(cu.company_name,'') AS CustomerName,
                       wr.id AS WorkRequestId,
                       wr.priority AS WrPriority,
                       COALESCE(wr.is_ultra_critical, 0) AS IsUltraCritical,
                       CASE
                         WHEN o.date_needed IS NULL THEN NULL
                         ELSE DATEDIFF(CURDATE(), o.date_needed)
                       END AS DaysLate
                FROM ddp_officina_items o
                JOIN projects p ON p.id = o.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                LEFT JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
                WHERE COALESCE(p.status,'') <> 'CANCELLED'
                  AND (@ProjectId IS NULL OR o.project_id = @ProjectId)
                ORDER BY
                  COALESCE(wr.is_ultra_critical, 0) DESC,
                  CASE WHEN wr.priority IS NULL THEN 1 ELSE 0 END,
                  wr.priority ASC,
                  CASE WHEN o.date_needed IS NULL THEN 1 ELSE 0 END,
                  o.date_needed ASC,
                  p.code ASC,
                  o.id ASC",
                new { ProjectId = projectId }).ToList();

            return Ok(ApiResponse<List<OfficinaInboxItem>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<OfficinaInboxItem>>.Fail(ex.Message));
        }
    }
}
