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
    public NotificationsController(DbService db) => _db = db;

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
                   COALESCE(b.part_number, '') AS ReferenceLabel,
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
