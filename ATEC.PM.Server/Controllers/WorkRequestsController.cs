using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/work-requests")]
[Authorize]
public class WorkRequestsController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ILogger<WorkRequestsController> _logger;

    public WorkRequestsController(DbService db, IHubContext<ProjectHub> hub, ILogger<WorkRequestsController> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public const string ConflictMessage = "CONFLITTO: lavorazione modificata da un altro utente";

    // Notifica realtime best-effort: sempre al gruppo globale (Pannello Lavorazioni) e
    // anche al gruppo della commessa (griglia nel dettaglio commessa).
    private void NotifyChanged(string action, int? projectId = null)
    {
        _ = _hub.Clients.Group(ProjectHub.WorkRequestsGroup)
            .SendAsync("WorkRequestsChanged", new { action, projectId });
        if (projectId.HasValue)
            _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId.Value))
                .SendAsync("WorkRequestsChanged", new { action, projectId });
    }

    // Ritorno di stato verso la DDP Officina: lavorazione consegnata → riga collegata a COS.
    // Best-effort (non blocca la risposta) + evento DdpChanged per Gestore DDP e distinta.
    private void SyncOfficinaBuilt(System.Data.IDbConnection c, int workRequestId)
    {
        try
        {
            (int ProjectId, int OfficinaItemId)? changed = WorkRequestDdpSync.MarkOfficinaBuilt(c, workRequestId);
            if (changed == null) return;
            var payload = new DdpChange
            {
                ProjectId = changed.Value.ProjectId,
                Action = "update",
                ItemId = changed.Value.OfficinaItemId,
                DdpType = "OFFICINA",
            };
            foreach (string group in new[] { ProjectHub.ProjectGroup(changed.Value.ProjectId), ProjectHub.AllGroup })
                _ = _hub.Clients.Group(group).SendAsync("DdpChanged", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sync COS su DDP Officina fallito per lavorazione {Id}", workRequestId);
        }
    }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // ── Ottiene le lavorazioni di una specifica commessa ────────────────────────
    [HttpGet("project/{projectId}")]
    public IActionResult GetByProject(int projectId)
    {
        try
        {
            using var c = _db.Open();
            // NB: la tabella projects ha `title`, non `name`.
            // Solo WR manuali (senza link DDP) oppure collegate a un 101 (particolare a disegno).
            const string workshopFilter = @"
                AND (
                    wr.ddp_officina_item_id IS NULL
                    OR EXISTS (
                        SELECT 1 FROM ddp_officina_items o
                        WHERE o.id = wr.ddp_officina_item_id
                          AND REPLACE(REPLACE(COALESCE(o.part_number, ''), '.', ''), ' ', '') LIKE '101%'
                    )
                )";
            string sql = @"
                SELECT wr.*, p.title AS ProjectName, p.code AS ProjectCode
                FROM project_work_requests wr
                JOIN projects p ON p.id = wr.project_id";
            if (projectId > 0)
            {
                sql += " WHERE wr.project_id = @ProjectId" + workshopFilter;
            }
            else
            {
                sql += " WHERE 1=1" + workshopFilter;
            }
            sql += " ORDER BY wr.created_at DESC";

            var rows = c.Query<dynamic>(sql, new { ProjectId = projectId }).ToList();

            var dtos = rows.Select(MapRowToDto).ToList();
            return Ok(ApiResponse<List<WorkRequestDto>>.Ok(dtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching work requests for project {ProjectId}", projectId);
            return StatusCode(500, ApiResponse<List<WorkRequestDto>>.Fail("Internal server error"));
        }
    }

    // ── Ottiene le lavorazioni non completate filtrate per priorità e ambito ──
    [HttpGet("priorities")]
    public IActionResult GetByPriorities([FromQuery] string? scopes, [FromQuery] string? levels)
    {
        try
        {
            using var c = _db.Open();
            
            // Active (uncompleted) work requests: solo manuali o collegate a codici 101.
            string sql = @"
                SELECT wr.*, p.title AS ProjectName, p.code AS ProjectCode
                FROM project_work_requests wr
                JOIN projects p ON p.id = wr.project_id
                WHERE wr.is_delivered = 0 AND wr.is_staging = 0
                  AND (
                    wr.ddp_officina_item_id IS NULL
                    OR EXISTS (
                        SELECT 1 FROM ddp_officina_items o
                        WHERE o.id = wr.ddp_officina_item_id
                          AND REPLACE(REPLACE(COALESCE(o.part_number, ''), '.', ''), ' ', '') LIKE '101%'
                    )
                  )";

            var parameters = new DynamicParameters();

            // Filter by scopes (e.g. 'ext', 'int' or both)
            if (!string.IsNullOrEmpty(scopes))
            {
                var scopeList = scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (scopeList.Length > 0 && scopeList.Length < 2)
                {
                    var scope = scopeList[0];
                    if (scope == "ext")
                    {
                        sql += " AND wr.type = 'External'";
                    }
                    else if (scope == "int")
                    {
                        sql += " AND wr.type = 'Internal'";
                    }
                }
            }

            // Filtra per priorità (es. '0,1,2')
            if (!string.IsNullOrEmpty(levels))
            {
                var levelList = levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(l => int.TryParse(l, out int val) ? (int?)val : null)
                                      .Where(l => l.HasValue)
                                      .Select(l => l!.Value)
                                      .ToList();
                if (levelList.Count > 0)
                {
                    sql += " AND wr.priority IN @Levels";
                    parameters.Add("Levels", levelList);
                }
            }

            sql += " ORDER BY wr.priority ASC, wr.request_date ASC";

            var rows = c.Query<dynamic>(sql, parameters).ToList();
            var dtos = rows.Select(MapRowToDto).ToList();
            return Ok(ApiResponse<List<WorkRequestDto>>.Ok(dtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching work requests by priorities");
            return StatusCode(500, ApiResponse<List<WorkRequestDto>>.Fail("Internal server error"));
        }
    }

    // ── Crea una lavorazione ──────────────────────────────────────────
    [HttpPost]
    public IActionResult Create([FromBody] WorkRequestSaveRequest req)
    {
        if (req.ProjectId <= 0)
            return BadRequest(ApiResponse<string>.Fail("Invalid Project ID"));

        try
        {
            using var c = _db.Open();
            string rfqsJson = JsonSerializer.Serialize(req.Rfqs ?? new List<RfqDto>());

            int newId = c.ExecuteScalar<int>(@"
                INSERT INTO project_work_requests (
                    project_id, request_date, description, type, priority, availability_date, notes,
                    is_ultra_critical, is_delivered, delivered_at, is_staging, rfqs,
                    po_supplier, po_number, po_date,
                    has_treatment, treatment_date, treatment_notes, is_treatment_confirmed, treatment_confirmed_at,
                    created_at
                ) VALUES (
                    @ProjectId, @RequestDate, @Description, @Type, @Priority, @AvailabilityDate, @Notes,
                    @IsUltraCritical, @IsDelivered, @DeliveredAt, @IsStaging, @Rfqs,
                    @PoSupplier, @PoNumber, @PoDate,
                    @HasTreatment, @TreatmentDate, @TreatmentNotes, @IsTreatmentConfirmed, @TreatmentConfirmedAt,
                    @CreatedAt
                );
                SELECT LAST_INSERT_ID()", new
            {
                req.ProjectId,
                RequestDate = string.IsNullOrEmpty(req.RequestDate) ? null : req.RequestDate,
                req.Description,
                req.Type,
                // Default: priorità più bassa (P2); P0 = critica.
                Priority = req.Priority ?? 2,
                AvailabilityDate = string.IsNullOrEmpty(req.AvailabilityDate) ? null : req.AvailabilityDate,
                req.Notes,
                IsUltraCritical = req.IsUltraCritical ? 1 : 0,
                IsDelivered = req.IsDelivered ? 1 : 0,
                req.DeliveredAt,
                IsStaging = req.IsStaging ? 1 : 0,
                Rfqs = rfqsJson,
                req.PoSupplier,
                req.PoNumber,
                PoDate = string.IsNullOrEmpty(req.PoDate) ? null : req.PoDate,
                HasTreatment = req.HasTreatment ? 1 : 0,
                TreatmentDate = string.IsNullOrEmpty(req.TreatmentDate) ? null : req.TreatmentDate,
                req.TreatmentNotes,
                IsTreatmentConfirmed = req.IsTreatmentConfirmed ? 1 : 0,
                req.TreatmentConfirmedAt,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            NotifyChanged("create", req.ProjectId);
            return Ok(ApiResponse<int>.Ok(newId, "Work request created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work request for project {ProjectId}", req.ProjectId);
            return StatusCode(500, ApiResponse<int>.Fail("Internal server error"));
        }
    }

    // ── Aggiorna una lavorazione ──────────────────────────────────────────
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] WorkRequestSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            
            // Verifica esistenza dell'entità
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_work_requests WHERE id = @Id", new { Id = id });
            if (exists == 0)
                return NotFound(ApiResponse<bool>.Fail("Work request not found"));

            string rfqsJson = JsonSerializer.Serialize(req.Rfqs ?? new List<RfqDto>());

            // Concurrency guard opzionale (pattern Check list): se il client manda RowVersion
            // e nel frattempo la riga è cambiata, nessuna riga viene aggiornata → CONFLITTO.
            int rows = c.Execute(@"
                UPDATE project_work_requests SET
                    request_date = @RequestDate,
                    description = @Description,
                    type = @Type,
                    priority = @Priority,
                    availability_date = @AvailabilityDate,
                    notes = @Notes,
                    is_ultra_critical = @IsUltraCritical,
                    is_delivered = @IsDelivered,
                    delivered_at = @DeliveredAt,
                    is_staging = @IsStaging,
                    rfqs = @Rfqs,
                    po_supplier = @PoSupplier,
                    po_number = @PoNumber,
                    po_date = @PoDate,
                    has_treatment = @HasTreatment,
                    treatment_date = @TreatmentDate,
                    treatment_notes = @TreatmentNotes,
                    is_treatment_confirmed = @IsTreatmentConfirmed,
                    treatment_confirmed_at = @TreatmentConfirmedAt,
                    row_version = row_version + 1
                WHERE id = @Id AND (@RowVersion IS NULL OR row_version = @RowVersion)", new
            {
                Id = id,
                req.RowVersion,
                RequestDate = string.IsNullOrEmpty(req.RequestDate) ? null : req.RequestDate,
                req.Description,
                req.Type,
                req.Priority,
                AvailabilityDate = string.IsNullOrEmpty(req.AvailabilityDate) ? null : req.AvailabilityDate,
                req.Notes,
                IsUltraCritical = req.IsUltraCritical ? 1 : 0,
                IsDelivered = req.IsDelivered ? 1 : 0,
                req.DeliveredAt,
                IsStaging = req.IsStaging ? 1 : 0,
                Rfqs = rfqsJson,
                req.PoSupplier,
                req.PoNumber,
                PoDate = string.IsNullOrEmpty(req.PoDate) ? null : req.PoDate,
                HasTreatment = req.HasTreatment ? 1 : 0,
                TreatmentDate = string.IsNullOrEmpty(req.TreatmentDate) ? null : req.TreatmentDate,
                req.TreatmentNotes,
                IsTreatmentConfirmed = req.IsTreatmentConfirmed ? 1 : 0,
                req.TreatmentConfirmedAt
            });

            // Esistenza già verificata sopra: 0 righe aggiornate = row_version cambiata nel frattempo
            if (rows == 0)
                return Ok(ApiResponse<bool>.Fail(ConflictMessage));

            // Consegna impostata dal salvataggio completo → riga DDP Officina a COS.
            if (req.IsDelivered)
                SyncOfficinaBuilt(c, id);

            NotifyChanged("update", req.ProjectId > 0 ? req.ProjectId : null);
            return Ok(ApiResponse<bool>.Ok(true, "Work request updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating work request {Id}", id);
            return StatusCode(500, ApiResponse<bool>.Fail("Internal server error"));
        }
    }

    // ── Aggiorna un singolo campo inline ──────────────────────────────────────
    [HttpPatch("{id}/field")]
    public IActionResult UpdateField(int id, [FromBody] FieldUpdateRequest req)
    {
        try
        {
            var allowed = new HashSet<string> {
                "request_date", "description", "type",
                "priority", "is_delivered", "delivered_at", "is_staging",
                "is_ultra_critical", "notes", "availability_date",
                "po_supplier", "po_number", "po_date",
                "has_treatment", "treatment_date", "treatment_notes",
                "is_treatment_confirmed", "treatment_confirmed_at"
            };

            // Normalizza i valori booleani per il database (es. convertendo "true" a "1")
            string? val = req.Value;
            if (val == "true" || val == "True") val = "1";
            else if (val == "false" || val == "False") val = "0";

            string? error = _db.UpdateField("project_work_requests", id, req.Field, val, allowed);
            if (error != null)
                return BadRequest(ApiResponse<string>.Fail(error));

            // Vincoli di logica di business: se consegnato o con trattamenti confermati, azzera la criticità
            if (req.Field == "is_delivered" && val == "1")
            {
                _db.UpdateField("project_work_requests", id, "is_ultra_critical", "0", allowed);
                _db.UpdateField("project_work_requests", id, "delivered_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), allowed);
            }
            else if (req.Field == "is_delivered" && val == "0")
            {
                _db.UpdateField("project_work_requests", id, "delivered_at", null, allowed);
            }

            if (req.Field == "is_treatment_confirmed" && val == "1")
            {
                _db.UpdateField("project_work_requests", id, "is_ultra_critical", "0", allowed);
                _db.UpdateField("project_work_requests", id, "treatment_confirmed_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), allowed);
            }
            else if (req.Field == "is_treatment_confirmed" && val == "0")
            {
                _db.UpdateField("project_work_requests", id, "treatment_confirmed_at", null, allowed);
            }

            // Incrementa il concurrency token e notifica i client in ascolto
            using (var c = _db.Open())
            {
                c.Execute("UPDATE project_work_requests SET row_version = row_version + 1 WHERE id = @Id", new { Id = id });
                int? projectId = c.ExecuteScalar<int?>(
                    "SELECT project_id FROM project_work_requests WHERE id = @Id", new { Id = id });
                // Consegna spuntata → la riga DDP Officina collegata passa a COS (Costruito).
                if (req.Field == "is_delivered" && val == "1")
                    SyncOfficinaBuilt(c, id);
                NotifyChanged("update", projectId);
            }

            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error patching field {Field} of work request {Id}", req.Field, id);
            return StatusCode(500, ApiResponse<bool>.Fail("Internal server error"));
        }
    }

    // ── Elimina una lavorazione ──────────────────────────────────────────
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _db.Open();
            // Legge la commessa PRIMA della delete, serve per il broadcast al gruppo project-{id}
            int? projectId = c.ExecuteScalar<int?>(
                "SELECT project_id FROM project_work_requests WHERE id = @Id", new { Id = id });
            int deleted = c.Execute("DELETE FROM project_work_requests WHERE id = @Id", new { Id = id });
            if (deleted == 0)
                return NotFound(ApiResponse<bool>.Fail("Work request not found"));

            NotifyChanged("delete", projectId);
            return Ok(ApiResponse<bool>.Ok(true, "Work request deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting work request {Id}", id);
            return StatusCode(500, ApiResponse<bool>.Fail("Internal server error"));
        }
    }

    // ── Mappa la riga raw del database su WorkRequestDto ─────────────────────
    private WorkRequestDto MapRowToDto(dynamic row)
    {
        var dto = new WorkRequestDto
        {
            Id = Convert.ToInt32(row.id),
            ProjectId = Convert.ToInt32(row.project_id),
            ProjectName = row.ProjectName ?? "",
            ProjectCode = row.ProjectCode ?? "",
            RequestDate = row.request_date == null ? "" : ((DateTime)row.request_date).ToString("yyyy-MM-dd"),
            Description = row.description ?? "",
            Type = row.type ?? "",
            Priority = row.priority == null ? null : (int?)Convert.ToInt32(row.priority),
            AvailabilityDate = row.availability_date == null ? "" : ((DateTime)row.availability_date).ToString("yyyy-MM-dd"),
            Notes = row.notes ?? "",
            IsUltraCritical = Convert.ToBoolean(row.is_ultra_critical),
            IsDelivered = Convert.ToBoolean(row.is_delivered),
            DeliveredAt = row.delivered_at == null ? null : (long?)Convert.ToInt64(row.delivered_at),
            IsStaging = Convert.ToBoolean(row.is_staging),
            PoSupplier = row.po_supplier ?? "",
            PoNumber = row.po_number ?? "",
            PoDate = row.po_date == null ? "" : ((DateTime)row.po_date).ToString("yyyy-MM-dd"),
            HasTreatment = Convert.ToBoolean(row.has_treatment),
            TreatmentDate = row.treatment_date == null ? "" : ((DateTime)row.treatment_date).ToString("yyyy-MM-dd"),
            TreatmentNotes = row.treatment_notes ?? "",
            IsTreatmentConfirmed = Convert.ToBoolean(row.is_treatment_confirmed),
            TreatmentConfirmedAt = row.treatment_confirmed_at == null ? null : (long?)Convert.ToInt64(row.treatment_confirmed_at),
            RowVersion = Convert.ToInt32(row.row_version),
            CreatedAt = Convert.ToInt64(row.created_at),
            DdpOfficinaItemId = row.ddp_officina_item_id == null ? null : (int?)Convert.ToInt32(row.ddp_officina_item_id)
        };

        try
        {
            string rfqsStr = row.rfqs;
            if (!string.IsNullOrEmpty(rfqsStr))
            {
                dto.Rfqs = JsonSerializer.Deserialize<List<RfqDto>>(rfqsStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RfqDto>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize RFQs for work request {Id}", dto.Id);
        }

        return dto;
    }
}
