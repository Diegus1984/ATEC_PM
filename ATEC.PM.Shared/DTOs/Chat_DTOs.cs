
namespace ATEC.PM.Shared.DTOs;

public class ChatListItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ParticipantCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string LastMessagePreview { get; set; } = "";

    public int UnreadCount { get; set; }
}

public class ChatMessageDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string EmployeeInitials { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsMine { get; set; }
    public bool HasAttachment { get; set; }
    public string AttachmentName { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
}

public class ChatCreateRequest
{
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public List<int> ParticipantIds { get; set; } = new();
}

public class ChatSendMessageRequest
{
    public int ChatId { get; set; }
    public string Message { get; set; } = "";
}

public class ChatParticipantDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
}

public class ChatAttachmentRequest
{
    public int ChatId { get; set; }
    public string Message { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileData { get; set; } = "";
}
