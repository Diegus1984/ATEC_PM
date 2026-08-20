
namespace ATEC.PM.Shared.DTOs;

public class ChatListItem
{
    public int Id { get; set; }
    /// <summary>Commessa (o «altra attività») di riferimento. <b>null = chat senza commessa</b>.</summary>
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectTitle { get; set; } = "";
    public string Title { get; set; } = "";
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ParticipantCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string LastMessagePreview { get; set; } = "";
    public int UnreadCount { get; set; }
    public bool IsParticipant { get; set; }
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
    public int? ReplyToMessageId { get; set; }
    public string ReplyToPreview { get; set; } = "";
    public string ReplyToEmployeeName { get; set; } = "";
    public int ReadByCount { get; set; }
    public int OtherParticipantCount { get; set; }
}

public class ChatMessagesPage
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public bool HasMore { get; set; }
}

public class ChatInboxBadge
{
    public int UnreadCount { get; set; }
}

public class ChatCreateRequest
{
    /// <summary>Commessa o «altra attività» scelta nella tendina. null/0 = voce «— Nessuna —».</summary>
    public int? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public List<int> ParticipantIds { get; set; } = new();
}

public class ChatSendMessageRequest
{
    public int ChatId { get; set; }
    public string Message { get; set; } = "";
    public int? ReplyToMessageId { get; set; }
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
    public int? ReplyToMessageId { get; set; }
}

/** Notifica real-time (SignalR `ChatChanged`) su una chat di commessa. */
public class ChatChange
{
    /// <summary>null per le chat senza commessa: arrivano solo al gruppo inbox globale.</summary>
    public int? ProjectId { get; set; }
    public int ChatId { get; set; }
    public string Action { get; set; } = "";
}

/// <summary>
/// Avviso real-time (SignalR <c>ChatMessageReceived</c>) mandato SOLO ai partecipanti della chat,
/// uno per messaggio: è quello che fa comparire il riquadro discreto in basso (#78).
/// <para>Non passa dal gruppo della commessa né dall'inbox globale — quelli li ascolta anche chi
/// nella conversazione non c'è, e l'anteprima del messaggio è roba di chi la chat ce l'ha.</para>
/// </summary>
public class ChatMessageAlert
{
    public int ChatId { get; set; }
    public int? ProjectId { get; set; }
    public string ChatTitle { get; set; } = "";
    public string ContextLabel { get; set; } = "";
    public int SenderId { get; set; }
    public string SenderName { get; set; } = "";
    public string Preview { get; set; } = "";
}

/** Notifica real-time (SignalR `ChatTyping`) — qualcuno sta scrivendo. */
public class ChatTyping
{
    public int ProjectId { get; set; }
    public int ChatId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
}
