using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Feedback Acquisti / Feedback Magazzino (sezione PM): due viste aggregate su TUTTE le commesse,
// port del prototipo Gestione_DDP_New_V26.html. Gli stati tracciati sono le aggregazioni
// A6 (Feedback Acquisti) e A7 (Feedback Magazzino), configurabili da "Aggregazioni DDP".
[ApiController]
[Route("api/ddp-feedback")]
[Authorize]
// Pagine feedback: sotto-pagine del Gestore DDP, stessa chiave.
[RequireFeature("nav.gestore_ddp")]
// #88: anche qui si scrive sempre su UNA commessa (note, righe nascoste, righe spente:
// sono dati suoi, non preferenze personali), quindi vale lo stesso cancello delle altre
// scritture di commessa — in stand-by o chiusa si guarda e basta.
[RequireProjectWritable]
public class DdpFeedbackController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    private readonly ProjectWriteGuard _guard;

    public DdpFeedbackController(DbService db, IHubContext<ProjectHub> hub, ProjectWriteGuard guard)
    {
        _db = db;
        _hub = hub;
        _guard = guard;
    }

    private void NotifyDdpChange(int projectId, string ddpType)
    {
        var payload = new DdpChange { ProjectId = projectId, Action = "feedback", ItemId = 0, DdpType = ddpType };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
            _hub.Clients.Group(group).SendAsync("DdpChanged", payload).SenzaAttesa("DdpChanged");
    }

    // Stati dell'aggregazione (A6/A7), ordinati come in "Aggregazioni DDP" (sort_order di ddp_statuses).
    private static string[] LoadAggregationStates(System.Data.IDbConnection c, string code, string[] fallback)
    {
        string[] keys = c.Query<string>(@"
            SELECT s.status_key FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            JOIN ddp_statuses st ON st.status_key = s.status_key
            WHERE a.code = @Code
            ORDER BY st.sort_order", new { Code = code }).ToArray();
        return keys.Length > 0 ? keys : fallback;
    }

    // Universo delle DDP esistenti (commessa + tipo), come in Gestore DDP.
    // #88: per-utente, non piu static — le DDP di una commessa in bozza si vedono solo con la chiave.
    private List<(int ProjectId, string Code, string Title, string CustomerName, string DdpType)> LoadDdpUniverse(System.Data.IDbConnection c)
    {
        string filtroBozze = _guard.FiltroBozzeSql(User);
        return c.Query<(int, string, string, string, string)>($@"
            SELECT DISTINCT b.project_id, p.code, COALESCE(p.title, ''), COALESCE(cu.company_name, ''), 'COMMERCIAL'
            FROM bom_items b
            JOIN projects p ON p.id = b.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE b.ddp_type = 'COMMERCIAL'{filtroBozze}
            UNION
            SELECT DISTINCT o.project_id, p.code, COALESCE(p.title, ''), COALESCE(cu.company_name, ''), 'OFFICINA'
            FROM ddp_officina_items o
            JOIN projects p ON p.id = o.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE 1=1{filtroBozze}
            ORDER BY 2 DESC, 4").ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // FEEDBACK ACQUISTI
    // ══════════════════════════════════════════════════════════════

    [HttpGet("acquisti")]
    public IActionResult GetAcquisti()
    {
        try
        {
            using var c = _db.Open();
            string[] states = LoadAggregationStates(c, "A6", new[] { "VER", "CHEK", "DO", "RO", "PAR" });
            var universe = LoadDdpUniverse(c);

            var counts = c.Query<(int ProjectId, string DdpType, string StatusKey, int Count)>(@"
                SELECT b.project_id AS ProjectId, 'COMMERCIAL' AS DdpType, b.item_status AS StatusKey, COUNT(*) AS Count
                FROM bom_items b
                WHERE b.ddp_type = 'COMMERCIAL' AND b.item_status IN @States
                GROUP BY b.project_id, b.item_status
                UNION ALL
                SELECT o.project_id, 'OFFICINA', o.item_status, COUNT(*)
                FROM ddp_officina_items o
                WHERE o.item_status IN @States
                GROUP BY o.project_id, o.item_status", new { States = states })
                .ToDictionary(r => (r.ProjectId, r.DdpType, r.StatusKey), r => r.Count);

            var overrides = c.Query<(int ProjectId, string DdpType, string StatusKey, string Note, bool Hidden)>(
                "SELECT project_id AS ProjectId, ddp_type AS DdpType, status_key AS StatusKey, note AS Note, hidden AS Hidden FROM ddp_feedback_acquisti")
                .ToDictionary(r => (r.ProjectId, r.DdpType, r.StatusKey), r => (r.Note, r.Hidden));

            var groups = universe.Select(u =>
            {
                var group = new DdpFeedbackAcquistiGroup
                {
                    ProjectId = u.ProjectId,
                    Code = u.Code,
                    Title = u.Title,
                    CustomerName = u.CustomerName,
                    DdpType = u.DdpType,
                };
                foreach (string state in states)
                {
                    var key = (u.ProjectId, u.DdpType, state);
                    counts.TryGetValue(key, out int count);
                    overrides.TryGetValue(key, out var ov);
                    group.Rows.Add(new DdpFeedbackAcquistiRow
                    {
                        StatusKey = state,
                        Count = count,
                        Note = ov.Note ?? "",
                        Hidden = ov.Hidden,
                    });
                }
                return group;
            }).ToList();

            return Ok(ApiResponse<List<DdpFeedbackAcquistiGroup>>.Ok(groups));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpFeedbackAcquistiGroup>>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("acquisti/{projectId:int}/{ddpType}/{statusKey}/note")]
    public IActionResult SetAcquistiNote(int projectId, string ddpType, string statusKey, [FromBody] DdpFeedbackAcquistiSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            c.Execute(@"
                INSERT INTO ddp_feedback_acquisti (project_id, ddp_type, status_key, note, hidden)
                VALUES (@ProjectId, @DdpType, @StatusKey, @Note, 0)
                ON DUPLICATE KEY UPDATE note = @Note",
                new { ProjectId = projectId, DdpType = ddpType.ToUpperInvariant(), StatusKey = statusKey, Note = req.Note ?? "" });

            NotifyDdpChange(projectId, ddpType);
            return Ok(ApiResponse<bool>.Ok(true, "Salvato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("acquisti/{projectId:int}/{ddpType}/{statusKey}/hidden")]
    public IActionResult SetAcquistiHidden(int projectId, string ddpType, string statusKey, [FromBody] DdpFeedbackAcquistiSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            c.Execute(@"
                INSERT INTO ddp_feedback_acquisti (project_id, ddp_type, status_key, note, hidden)
                VALUES (@ProjectId, @DdpType, @StatusKey, '', @Hidden)
                ON DUPLICATE KEY UPDATE hidden = @Hidden",
                new { ProjectId = projectId, DdpType = ddpType.ToUpperInvariant(), StatusKey = statusKey, Hidden = req.Hidden ?? false });

            NotifyDdpChange(projectId, ddpType);
            return Ok(ApiResponse<bool>.Ok(true, "Salvato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("acquisti/{projectId:int}/{ddpType}/reset")]
    public IActionResult ResetAcquistiHidden(int projectId, string ddpType)
    {
        try
        {
            using var c = _db.Open();
            c.Execute(
                "UPDATE ddp_feedback_acquisti SET hidden = 0 WHERE project_id = @ProjectId AND ddp_type = @DdpType",
                new { ProjectId = projectId, DdpType = ddpType.ToUpperInvariant() });

            NotifyDdpChange(projectId, ddpType);
            return Ok(ApiResponse<bool>.Ok(true, "Righe ripristinate"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ══════════════════════════════════════════════════════════════
    // FEEDBACK MAGAZZINO
    // ══════════════════════════════════════════════════════════════

    [HttpGet("magazzino")]
    public IActionResult GetMagazzino()
    {
        try
        {
            using var c = _db.Open();
            string[] states = LoadAggregationStates(c, "A7", new[] { "DISP", "PAR" });
            var universe = LoadDdpUniverse(c);

            var rows = c.Query<DdpFeedbackMagazzinoRow2>(@"
                SELECT b.project_id AS ProjectId, 'COMMERCIAL' AS DdpType, b.id AS ItemId,
                       b.requested_by AS RequestedBy,
                       b.created_by AS CreatedById,
                       COALESCE(CONCAT(eb.first_name, ' ', eb.last_name), '') AS CreatedByName,
                       b.created_at AS CreatedAt,
                       b.description AS Description, b.quantity AS Quantity,
                       b.unit AS Unit, '' AS Material, '' AS Treatment,
                       COALESCE(su.company_name, '') AS SupplierName, b.manufacturer AS Manufacturer,
                       b.item_status AS ItemStatus, b.danea_ref AS DaneaRef, b.destination AS Destination,
                       b.destination_spec AS DestinationSpec, b.notes AS Notes
                FROM bom_items b
                LEFT JOIN suppliers su ON su.id = b.supplier_id
                LEFT JOIN employees eb ON eb.id = b.created_by
                WHERE b.ddp_type = 'COMMERCIAL' AND b.item_status IN @States
                UNION ALL
                SELECT o.project_id, 'OFFICINA', o.id,
                       o.requested_by,
                       o.created_by AS CreatedById,
                       COALESCE(CONCAT(eo.first_name, ' ', eo.last_name), '') AS CreatedByName,
                       o.created_at AS CreatedAt,
                       o.description, o.quantity,
                       '' , o.material, o.treatment,
                       o.supplier_name, '',
                       o.item_status, o.danea_ref, o.destination, o.destination_spec, o.notes
                FROM ddp_officina_items o
                LEFT JOIN employees eo ON eo.id = o.created_by
                WHERE o.item_status IN @States
                ORDER BY ItemId", new { States = states }).ToList();

            var hidden = c.Query<(int ProjectId, string DdpType, int ItemId)>(
                "SELECT project_id AS ProjectId, ddp_type AS DdpType, item_id AS ItemId FROM ddp_feedback_magazzino_hidden")
                .Select(h => (h.ProjectId, h.DdpType, h.ItemId))
                .ToHashSet();

            var groups = universe.Select(u => new DdpFeedbackMagazzinoGroup
            {
                ProjectId = u.ProjectId,
                Code = u.Code,
                Title = u.Title,
                CustomerName = u.CustomerName,
                DdpType = u.DdpType,
                Rows = rows
                    .Where(r => r.ProjectId == u.ProjectId && r.DdpType == u.DdpType)
                    .Select(r => new DdpFeedbackMagazzinoRow
                    {
                        ItemId = r.ItemId,
                        RequestedBy = r.RequestedBy,
                        CreatedById = r.CreatedById,
                        CreatedByName = r.CreatedByName,
                        CreatedAt = r.CreatedAt,
                        Description = r.Description,
                        Quantity = r.Quantity,
                        Unit = r.Unit,
                        Material = r.Material,
                        Treatment = r.Treatment,
                        SupplierName = r.SupplierName,
                        Manufacturer = r.Manufacturer,
                        ItemStatus = r.ItemStatus,
                        DaneaRef = r.DaneaRef,
                        Destination = r.Destination,
                        DestinationSpec = r.DestinationSpec,
                        Notes = r.Notes,
                        Hidden = hidden.Contains((u.ProjectId, u.DdpType, r.ItemId)),
                    })
                    .ToList(),
            })
            // Solo DDP che hanno almeno una riga negli stati di magazzino tracciati.
            .Where(g => g.Rows.Count > 0)
            .ToList();

            return Ok(ApiResponse<List<DdpFeedbackMagazzinoGroup>>.Ok(groups));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpFeedbackMagazzinoGroup>>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("magazzino/{projectId:int}/{ddpType}/{itemId:int}/hidden")]
    public IActionResult SetMagazzinoHidden(int projectId, string ddpType, int itemId, [FromBody] DdpFeedbackAcquistiSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            string type = ddpType.ToUpperInvariant();
            if (req.Hidden == true)
            {
                c.Execute(@"
                    INSERT IGNORE INTO ddp_feedback_magazzino_hidden (project_id, ddp_type, item_id)
                    VALUES (@ProjectId, @DdpType, @ItemId)",
                    new { ProjectId = projectId, DdpType = type, ItemId = itemId });
            }
            else
            {
                c.Execute(
                    "DELETE FROM ddp_feedback_magazzino_hidden WHERE project_id = @ProjectId AND ddp_type = @DdpType AND item_id = @ItemId",
                    new { ProjectId = projectId, DdpType = type, ItemId = itemId });
            }

            NotifyDdpChange(projectId, ddpType);
            return Ok(ApiResponse<bool>.Ok(true, "Salvato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPost("magazzino/{projectId:int}/{ddpType}/reset")]
    public IActionResult ResetMagazzinoHidden(int projectId, string ddpType)
    {
        try
        {
            using var c = _db.Open();
            c.Execute(
                "DELETE FROM ddp_feedback_magazzino_hidden WHERE project_id = @ProjectId AND ddp_type = @DdpType",
                new { ProjectId = projectId, DdpType = ddpType.ToUpperInvariant() });

            NotifyDdpChange(projectId, ddpType);
            return Ok(ApiResponse<bool>.Ok(true, "Righe ripristinate"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    // Shape intermedio per la UNION Dapper (ProjectId/DdpType servono per il raggruppamento in C#,
    // non fanno parte del DTO pubblico DdpFeedbackMagazzinoRow).
    private class DdpFeedbackMagazzinoRow2
    {
        public int ProjectId { get; set; }
        public string DdpType { get; set; } = "";
        public int ItemId { get; set; }
        public string RequestedBy { get; set; } = "";
        public int? CreatedById { get; set; }
        public string CreatedByName { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public string Material { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string ItemStatus { get; set; } = "";
        public string DaneaRef { get; set; } = "";
        public string Destination { get; set; } = "";
        public string DestinationSpec { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
