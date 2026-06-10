using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

// Gestore DDP (sezione PM): riepilogo delle DDP Commerciali aggregato per commessa.
[ApiController]
[Route("api/ddp-manager")]
[Authorize]
public class DdpManagerController : ControllerBase
{
    private readonly DbService _db;
    public DdpManagerController(DbService db) => _db = db;

    // Stati "consegnato/gestito" di default (fallback se l'aggregazione A2 non è configurata).
    private static readonly string[] DefaultDelivered = { "CON", "COS", "DISP", "ASS", "MOD" };

    // Set "Materiale Consegnato" = appartenenze dell'aggregazione A2 (configurabile da "Aggregazioni DDP").
    private static string[] LoadDelivered(System.Data.IDbConnection c)
    {
        string[] keys = c.Query<string>(@"
            SELECT s.status_key FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            WHERE a.code = 'A2'").ToArray();
        return keys.Length > 0 ? keys : DefaultDelivered;
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try
        {
            using var c = _db.Open();
            string[] delivered = LoadDelivered(c);

            List<DdpProjectSummary> summaries = c.Query<DdpProjectSummary>($@"
                SELECT b.project_id AS ProjectId, p.code AS Code,
                       COALESCE(cu.company_name, '') AS CustomerName,
                       COUNT(*) AS TotalRows,
                       COALESCE(SUM(b.quantity * b.unit_cost), 0) AS TotalValue,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN 1 ELSE 0 END) AS DatedCount,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                AND b.date_needed < CURDATE() THEN 1 ELSE 0 END) AS OverdueCount,
                       MIN(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryStart,
                       MAX(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryEnd,
                       MAX(b.created_at) AS LastInsertedAt
                FROM bom_items b
                JOIN projects p ON p.id = b.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE b.ddp_type = 'COMMERCIAL'
                GROUP BY b.project_id, p.code, cu.company_name
                ORDER BY p.code DESC", new { Delivered = delivered }).ToList();

            return Ok(ApiResponse<List<DdpProjectSummary>>.Ok(summaries));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpProjectSummary>>.Fail($"Errore: {ex.Message}"));
        }
    }

    // Sintesi di una singola commessa: KPI + ripartizione per stato.
    [HttpGet("{projectId:int}")]
    public IActionResult GetDetail(int projectId)
    {
        try
        {
            using var c = _db.Open();
            string[] delivered = LoadDelivered(c);

            DdpProjectDetail? head = c.QueryFirstOrDefault<DdpProjectDetail>($@"
                SELECT b.project_id AS ProjectId, p.code AS Code,
                       COALESCE(cu.company_name, '') AS CustomerName,
                       COUNT(*) AS TotalRows,
                       COALESCE(SUM(b.quantity * b.unit_cost), 0) AS TotalValue,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN 1 ELSE 0 END) AS DatedCount,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                AND b.date_needed < CURDATE() THEN 1 ELSE 0 END) AS OverdueCount,
                       MIN(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryStart,
                       MAX(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryEnd
                FROM bom_items b
                JOIN projects p ON p.id = b.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE b.ddp_type = 'COMMERCIAL' AND b.project_id = @pid
                GROUP BY b.project_id, p.code, cu.company_name", new { pid = projectId, Delivered = delivered });

            if (head == null)
                return Ok(ApiResponse<DdpProjectDetail>.Fail("Nessuna DDP commerciale per questa commessa"));

            head.StatusCounts = c.Query<DdpStatusCount>(@"
                SELECT item_status AS StatusKey, COUNT(*) AS Count
                FROM bom_items
                WHERE ddp_type = 'COMMERCIAL' AND project_id = @pid
                GROUP BY item_status
                ORDER BY Count DESC", new { pid = projectId }).ToList();

            return Ok(ApiResponse<DdpProjectDetail>.Ok(head));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DdpProjectDetail>.Fail($"Errore: {ex.Message}"));
        }
    }
}
