namespace ATEC.PM.Shared.DTOs;

public class NotificationListItem
{
    public int Id { get; set; }
    public int NotificationId { get; set; }
    public string NotificationType { get; set; } = "";
    public string Severity { get; set; } = "INFO";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string ReferenceType { get; set; } = "";
    public int ReferenceId { get; set; }
    public string ReferenceLabel { get; set; } = "";
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationBadge
{
    public int UnreadCount { get; set; }
}
