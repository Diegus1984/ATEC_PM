using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using System.Security.Claims;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly DbService _db;
    private readonly NotificationService _notif;
    public NotificationsController(DbService db, NotificationService notif) { _db = db; _notif = notif; }

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>GET /api/notifications?unreadOnly=false&limit=50</summary>
    [HttpGet]
    public IActionResult GetAll([FromQuery] bool unreadOnly = false, [FromQuery] int limit = 50)
    {
        int empId = GetCurrentEmployeeId();
        if (empId == 0) return Unauthorized();

        using var c = _db.Open();
        string where = unreadOnly ? "AND nr.is_read = FALSE" : "";
        var rows = c.Query<NotificationListItem>($@"
            SELECT nr.id, nr.notification_id AS NotificationId,
                   n.notification_type AS NotificationType, n.severity AS Severity,
                   n.title, n.message,
                   n.reference_type AS ReferenceType, n.reference_id AS ReferenceId,
                   n.project_id AS ProjectId,
                   COALESCE(p.code, '') AS ProjectCode,
                   COALESCE(CONCAT(emp.first_name, ' ', emp.last_name), 'Sistema') AS CreatedByName,
                   COALESCE(CONCAT(b.part_number, ' - ', b.description), '') AS ReferenceLabel,
                   nr.is_read AS IsRead, nr.read_at AS ReadAt,
                   n.created_at AS CreatedAt
            FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN projects p ON p.id = n.project_id
            LEFT JOIN employees emp ON emp.id = n.created_by
            LEFT JOIN bom_items b ON n.reference_type = 'BOM' AND b.id = n.reference_id
            WHERE nr.employee_id = @EmpId {where}
            ORDER BY n.created_at DESC
            LIMIT @Limit", new { EmpId = empId, Limit = limit }).ToList();

        return Ok(ApiResponse<List<NotificationListItem>>.Ok(rows));
    }

    /// <summary>GET /api/notifications/badge</summary>
    [HttpGet("badge")]
    public IActionResult GetBadge()
    {
        int empId = GetCurrentEmployeeId();
        if (empId == 0) return Unauthorized();

        using var c = _db.Open();
        int count = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM notification_recipients WHERE employee_id = @EmpId AND is_read = FALSE",
            new { EmpId = empId });

        return Ok(ApiResponse<NotificationBadge>.Ok(new NotificationBadge { UnreadCount = count }));
    }

    /// <summary>PUT /api/notifications/{id}/read</summary>
    [HttpPut("{id}/read")]
    public IActionResult MarkRead(int id)
    {
        int empId = GetCurrentEmployeeId();
        using var c = _db.Open();
        c.Execute(
            "UPDATE notification_recipients SET is_read = TRUE, read_at = NOW() WHERE id = @Id AND employee_id = @EmpId",
            new { Id = id, EmpId = empId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>PUT /api/notifications/read-all</summary>
    [HttpPut("read-all")]
    public IActionResult MarkAllRead()
    {
        int empId = GetCurrentEmployeeId();
        using var c = _db.Open();
        c.Execute(
            "UPDATE notification_recipients SET is_read = TRUE, read_at = NOW() WHERE employee_id = @EmpId AND is_read = FALSE",
            new { EmpId = empId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>POST /api/notifications/check-pending — Genera notifiche mancanti al login</summary>
    [HttpPost("check-pending")]
    public IActionResult CheckPending()
    {
        int empId = GetCurrentEmployeeId();
        if (empId == 0) return Unauthorized();

        using var c = _db.Open();
        int count = 0;

        // Commesse ACTIVE dove il dipendente è assegnato ma non ha ricevuto notifica PROJECT_STATUS
        var missingActiveProjects = c.Query<dynamic>(@"
            SELECT DISTINCT p.id AS ProjectId, p.code AS ProjectCode
            FROM projects p
            JOIN project_phases pp ON pp.project_id = p.id
            JOIN phase_assignments pa ON pa.project_phase_id = pp.id
            WHERE p.status = 'ACTIVE'
              AND pa.employee_id = @EmpId
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  JOIN notification_recipients nr ON nr.notification_id = n.id
                  WHERE n.notification_type = 'PROJECT_STATUS'
                    AND n.project_id = p.id
                    AND nr.employee_id = @EmpId
              )", new { EmpId = empId }).ToList();

        foreach (var proj in missingActiveProjects)
        {
            _notif.Create("PROJECT_STATUS", "INFO",
                $"Commessa {(string)proj.ProjectCode} - ATTIVA",
                $"La commessa {(string)proj.ProjectCode} e' ora attiva. Le attivita' assegnate sono operative.",
                "PROJECT", (int)proj.ProjectId, (int)proj.ProjectId, null,
                new[] { empId });
            count++;
        }

        // Assegnazioni a fasi su commesse ACTIVE senza notifica PHASE_ASSIGNED
        var missingAssignments = c.Query<dynamic>(@"
            SELECT pa.id AS AssignmentId, pp.id AS PhaseId, p.id AS ProjectId, p.code AS ProjectCode,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS PhaseName
            FROM phase_assignments pa
            JOIN project_phases pp ON pp.id = pa.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            WHERE pa.employee_id = @EmpId
              AND p.status = 'ACTIVE'
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  JOIN notification_recipients nr ON nr.notification_id = n.id
                  WHERE n.notification_type = 'PHASE_ASSIGNED'
                    AND n.reference_type = 'PHASE'
                    AND n.reference_id = pp.id
                    AND nr.employee_id = @EmpId
              )", new { EmpId = empId }).ToList();

        foreach (var a in missingAssignments)
        {
            _notif.Create("PHASE_ASSIGNED", "INFO",
                $"Nuova assegnazione - {(string)a.ProjectCode}",
                $"Sei stato assegnato alla fase: {(string)a.PhaseName}",
                "PHASE", (int)a.PhaseId, (int)a.ProjectId, null,
                new[] { empId });
            count++;
        }

        return Ok(ApiResponse<int>.Ok(count, $"{count} notifiche pendenti generate"));
    }

    /// <summary>DELETE /api/notifications/{id}</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        int empId = GetCurrentEmployeeId();
        using var c = _db.Open();
        c.Execute(
            "DELETE FROM notification_recipients WHERE id = @Id AND employee_id = @EmpId",
            new { Id = id, EmpId = empId });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
