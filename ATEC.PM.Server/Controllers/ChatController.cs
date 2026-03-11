using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using System.Security.Claims;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly DbService _db;
    public ChatController(DbService db) => _db = db;

    private int GetCurrentEmployeeId() =>
    int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    private bool IsPmOrAdmin()
    {
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;
        return role == "ADMIN" || role == "PM";
    }

    // ── Lista chat per commessa (filtrata per partecipante, PM/ADMIN vedono tutte) ──
    [HttpGet("project/{projectId}")]
    public IActionResult GetChats(int projectId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();
        bool isPm = IsPmOrAdmin();

        List<ChatListItem> chats;

        if (isPm)
        {
            chats = c.Query<ChatListItem>(@"
                SELECT ch.id, ch.project_id AS ProjectId, ch.title,
                       CONCAT(e.first_name,' ',e.last_name) AS CreatedByName,
                       ch.created_at AS CreatedAt,
                       (SELECT COUNT(*) FROM project_chat_participants p WHERE p.chat_id = ch.id) AS ParticipantCount,
                       (SELECT COUNT(*) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS MessageCount,
                       (SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS LastMessageAt,
                       (SELECT SUBSTRING(m.message, 1, 80) FROM project_chat_messages m WHERE m.chat_id = ch.id ORDER BY m.id DESC LIMIT 1) AS LastMessagePreview,
                       COALESCE((SELECT COUNT(*) FROM project_chat_messages m WHERE m.chat_id = ch.id 
                            AND m.id > COALESCE((SELECT cp2.last_read_message_id FROM project_chat_participants cp2 
                            WHERE cp2.chat_id = ch.id AND cp2.employee_id = @EmpId), 0)), 0) AS UnreadCount
                FROM project_chats ch
                JOIN employees e ON e.id = ch.created_by
                WHERE ch.project_id = @ProjectId
                ORDER BY COALESCE((SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id), ch.created_at) DESC",
                new { ProjectId = projectId, EmpId = empId }).ToList();
        }
        else
        {
            chats = c.Query<ChatListItem>(@"
                SELECT ch.id, ch.project_id AS ProjectId, ch.title,
                       CONCAT(e.first_name,' ',e.last_name) AS CreatedByName,
                       ch.created_at AS CreatedAt,
                       (SELECT COUNT(*) FROM project_chat_participants p WHERE p.chat_id = ch.id) AS ParticipantCount,
                       (SELECT COUNT(*) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS MessageCount,
                       (SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS LastMessageAt,
                       (SELECT SUBSTRING(m.message, 1, 80) FROM project_chat_messages m WHERE m.chat_id = ch.id ORDER BY m.id DESC LIMIT 1) AS LastMessagePreview,
                       COALESCE((SELECT COUNT(*) FROM project_chat_messages m WHERE m.chat_id = ch.id 
                            AND m.id > COALESCE(cp.last_read_message_id, 0)), 0) AS UnreadCount
                FROM project_chats ch
                JOIN employees e ON e.id = ch.created_by
                JOIN project_chat_participants cp ON cp.chat_id = ch.id AND cp.employee_id = @EmpId
                WHERE ch.project_id = @ProjectId
                ORDER BY COALESCE((SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id), ch.created_at) DESC",
                new { ProjectId = projectId, EmpId = empId }).ToList();
        }

        return Ok(ApiResponse<List<ChatListItem>>.Ok(chats));
    }

    // ── Messaggi di una chat ──
    [HttpGet("{chatId}/messages")]
    public IActionResult GetMessages(int chatId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        var messages = c.Query<ChatMessageDto>(@"
            SELECT m.id, m.employee_id AS EmployeeId,
                   CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
                   CONCAT(LEFT(e.first_name,1), LEFT(e.last_name,1)) AS EmployeeInitials,
                   m.message, m.created_at AS CreatedAt,
                   CASE WHEN m.employee_id = @EmpId THEN 1 ELSE 0 END AS IsMine,
                   m.has_attachment AS HasAttachment,
                   COALESCE(m.attachment_name,'') AS AttachmentName,
                   COALESCE(m.attachment_path,'') AS AttachmentPath
            FROM project_chat_messages m
            JOIN employees e ON e.id = m.employee_id
            WHERE m.chat_id = @ChatId
            ORDER BY m.created_at ASC",
            new { ChatId = chatId, EmpId = empId }).ToList();

        return Ok(ApiResponse<List<ChatMessageDto>>.Ok(messages));
    }

    // ── Partecipanti di una chat ──
    [HttpGet("{chatId}/participants")]
    public IActionResult GetParticipants(int chatId)
    {
        using var c = _db.Open();
        var participants = c.Query<ChatParticipantDto>(@"
            SELECT cp.id, cp.employee_id AS EmployeeId,
                   CONCAT(e.first_name,' ',e.last_name) AS EmployeeName
            FROM project_chat_participants cp
            JOIN employees e ON e.id = cp.employee_id
            WHERE cp.chat_id = @ChatId
            ORDER BY e.last_name",
            new { ChatId = chatId }).ToList();

        return Ok(ApiResponse<List<ChatParticipantDto>>.Ok(participants));
    }

    // ── Crea chat ──
    [HttpPost]
    public IActionResult CreateChat([FromBody] ChatCreateRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        int empId = GetCurrentEmployeeId();

        int chatId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chats (project_id, title, created_by)
            VALUES (@ProjectId, @Title, @CreatedBy);
            SELECT LAST_INSERT_ID()",
            new { req.ProjectId, req.Title, CreatedBy = empId }, tx);

        // Aggiungi il creatore come partecipante
        var participantIds = new HashSet<int>(req.ParticipantIds) { empId };

        foreach (int pid in participantIds)
        {
            c.Execute(@"INSERT IGNORE INTO project_chat_participants (chat_id, employee_id)
                VALUES (@ChatId, @EmpId)",
                new { ChatId = chatId, EmpId = pid }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<int>.Ok(chatId, "Chat creata"));
    }

    // ── Invia messaggio ──
    [HttpPost("{chatId}/messages")]
    public IActionResult SendMessage(int chatId, [FromBody] ChatSendMessageRequest req)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(ApiResponse<string>.Fail("Messaggio vuoto."));

        int msgId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chat_messages (chat_id, employee_id, message)
            VALUES (@ChatId, @EmpId, @Message);
            SELECT LAST_INSERT_ID()",
            new { ChatId = chatId, EmpId = empId, req.Message });

        return Ok(ApiResponse<int>.Ok(msgId));
    }

    // ── Elimina messaggio (solo autore o ADMIN) ──
    [HttpDelete("messages/{messageId}")]
    public IActionResult DeleteMessage(int messageId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        var msg = c.QueryFirstOrDefault<dynamic>(
            "SELECT employee_id FROM project_chat_messages WHERE id=@Id",
            new { Id = messageId });

        if (msg == null)
            return NotFound(ApiResponse<string>.Fail("Messaggio non trovato."));

        if ((int)msg.employee_id != empId && !IsPmOrAdmin())
            return Forbid();

        c.Execute("DELETE FROM project_chat_messages WHERE id=@Id", new { Id = messageId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiungi partecipante ──
    [HttpPost("{chatId}/participants")]
    public IActionResult AddParticipant(int chatId, [FromBody] int employeeId)
    {
        using var c = _db.Open();
        c.Execute(@"INSERT IGNORE INTO project_chat_participants (chat_id, employee_id)
            VALUES (@ChatId, @EmpId)",
            new { ChatId = chatId, EmpId = employeeId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Rimuovi partecipante ──
    [HttpDelete("{chatId}/participants/{employeeId}")]
    public IActionResult RemoveParticipant(int chatId, int employeeId)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_chat_participants WHERE chat_id=@ChatId AND employee_id=@EmpId",
            new { ChatId = chatId, EmpId = employeeId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Elimina chat (solo creatore o ADMIN) ──
    [HttpDelete("{chatId}")]
    public IActionResult DeleteChat(int chatId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        var chat = c.QueryFirstOrDefault<dynamic>(
            "SELECT created_by FROM project_chats WHERE id=@Id",
            new { Id = chatId });

        if (chat == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        if ((int)chat.created_by != empId && !IsPmOrAdmin())
            return Forbid();

        c.Execute("DELETE FROM project_chats WHERE id=@Id", new { Id = chatId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Invia messaggio con allegato ──
    [HttpPost("{chatId}/messages/with-attachment")]
    public IActionResult SendMessageWithAttachment(int chatId, [FromBody] ChatAttachmentRequest req)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        // Trova il path della commessa
        var chatInfo = c.QueryFirstOrDefault<dynamic>(@"
            SELECT ch.project_id, p.server_path 
            FROM project_chats ch
            JOIN projects p ON p.id = ch.project_id
            WHERE ch.id = @ChatId", new { ChatId = chatId });

        if (chatInfo == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        string serverPath = (string)(chatInfo.server_path ?? "");
        if (string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        // Salva file su disco
        string chatFolder = Path.Combine(serverPath, "Chat", chatId.ToString());
        Directory.CreateDirectory(chatFolder);

        string safeFileName = Path.GetFileName(req.FileName);
        string filePath = Path.Combine(chatFolder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeFileName}");

        byte[] fileBytes = Convert.FromBase64String(req.FileData);
        System.IO.File.WriteAllBytes(filePath, fileBytes);

        // Salva messaggio con riferimento allegato
        int msgId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chat_messages (chat_id, employee_id, message, has_attachment, attachment_name, attachment_path)
            VALUES (@ChatId, @EmpId, @Message, 1, @AttName, @AttPath);
            SELECT LAST_INSERT_ID()",
            new { ChatId = chatId, EmpId = empId, req.Message, AttName = safeFileName, AttPath = filePath });

        return Ok(ApiResponse<int>.Ok(msgId));
    }

    // ── Segna chat come letta ──
    [HttpPost("{chatId}/mark-read")]
    public IActionResult MarkAsRead(int chatId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        int? lastMsgId = c.ExecuteScalar<int?>(
            "SELECT MAX(id) FROM project_chat_messages WHERE chat_id = @ChatId",
            new { ChatId = chatId });

        if (lastMsgId.HasValue)
        {
            c.Execute(@"UPDATE project_chat_participants 
                SET last_read_message_id = @LastId 
                WHERE chat_id = @ChatId AND employee_id = @EmpId",
                new { LastId = lastMsgId.Value, ChatId = chatId, EmpId = empId });
        }

        return Ok(ApiResponse<bool>.Ok(true));
    }
}
