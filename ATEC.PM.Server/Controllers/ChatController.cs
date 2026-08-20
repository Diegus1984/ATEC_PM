using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using System.Data;
using System.Security.Claims;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private const int MessagePageSizeDefault = 50;
    private const int MessagePageSizeMax = 100;

    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly FeatureAccessService _access;
    private readonly NotificationService _notifications;
    private readonly IConfiguration _config;

    public ChatController(
        DbService db,
        IHubContext<ProjectHub> hub,
        FeatureAccessService access,
        NotificationService notifications,
        IConfiguration config)
    {
        _db = db;
        _hub = hub;
        _access = access;
        _notifications = notifications;
        _config = config;
    }

    private void NotifyChatChange(int? projectId, int chatId, string action)
    {
        var payload = new ChatChange
        {
            ProjectId = projectId,
            ChatId = chatId,
            Action = action,
        };
        if (projectId.HasValue)
            _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId.Value)).SendAsync("ChatChanged", payload);
        _ = _hub.Clients.Group(ProjectHub.ChatInboxGroup).SendAsync("ChatChanged", payload);
    }

    /// <summary>Commessa della chat (null = chat senza commessa, o chat inesistente).</summary>
    private static int? ProjectOfChat(IDbConnection c, int chatId) =>
        c.ExecuteScalar<int?>("SELECT project_id FROM project_chats WHERE id = @ChatId",
            new { ChatId = chatId });

    private int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    private string CurrentEmployeeName() =>
        User.FindFirst(ClaimTypes.Name)?.Value ?? "";

    /// <summary>
    /// Chi modera le chat: le vede tutte anche senza esserne partecipante, e può eliminare
    /// messaggi e chat creati da altri. È una chiave sulla persona
    /// («Modera le chat di commessa»), non più il livello del ruolo.
    /// Basta l'accesso in lettura: la moderazione è un perimetro di visibilità, e le due
    /// eliminazioni che la usano hanno già la loro chiave di sezione a monte.
    /// </summary>
    private bool CanModerateChat() =>
        _access.CanAccessUser(GetCurrentEmployeeId(), User.FindFirst(ClaimTypes.Role)?.Value, "action.moderate_chat");

    /// <summary>
    /// Chat esistente e (partecipante oppure moderatore). Stesso criterio di
    /// <see cref="DownloadAttachment"/>: chi non modera resta nella propria conversazione.
    /// </summary>
    private IActionResult? EnsureChatAccess(IDbConnection c, int chatId)
    {
        int? found = c.ExecuteScalar<int?>(
            "SELECT id FROM project_chats WHERE id = @ChatId",
            new { ChatId = chatId });
        if (found == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        if (CanModerateChat())
            return null;

        int empId = GetCurrentEmployeeId();
        int access = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_chat_participants WHERE chat_id=@ChatId AND employee_id=@EmpId",
            new { ChatId = chatId, EmpId = empId });
        if (access == 0)
            return Forbid();

        return null;
    }

    private const string ChatListColumns = @"
        ch.id, ch.project_id AS ProjectId, COALESCE(pr.code,'') AS ProjectCode,
        COALESCE(pr.title,'') AS ProjectTitle, ch.title,
        ch.created_by AS CreatedById,
        CONCAT(e.first_name,' ',e.last_name) AS CreatedByName,
        ch.created_at AS CreatedAt,
        (SELECT COUNT(*) FROM project_chat_participants p WHERE p.chat_id = ch.id) AS ParticipantCount,
        (SELECT COUNT(*) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS MessageCount,
        (SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id) AS LastMessageAt,
        (SELECT SUBSTRING(m.message, 1, 80) FROM project_chat_messages m WHERE m.chat_id = ch.id ORDER BY m.id DESC LIMIT 1) AS LastMessagePreview,
        -- I non letti valgono solo se sei dentro la chat. Chi modera la vede comunque in
        -- lista commessa (#78), ma senza partecipazione non c'è un «ultimo letto» tuo: il
        -- COALESCE a 0 faceva contare TUTTI i messaggi come non letti — pallini arancioni
        -- in distinta e zero nel menu principale (che somma solo le chat in cui sei).
        CASE WHEN EXISTS(
                SELECT 1 FROM project_chat_participants me
                WHERE me.chat_id = ch.id AND me.employee_id = @EmpId)
             THEN COALESCE((SELECT COUNT(*) FROM project_chat_messages m
                           WHERE m.chat_id = ch.id
                             AND m.id > COALESCE((
                                 SELECT cp2.last_read_message_id
                                 FROM project_chat_participants cp2
                                 WHERE cp2.chat_id = ch.id AND cp2.employee_id = @EmpId
                             ), 0)), 0)
             ELSE 0 END AS UnreadCount,
        EXISTS(SELECT 1 FROM project_chat_participants p2 WHERE p2.chat_id = ch.id AND p2.employee_id = @EmpId) AS IsParticipant";

    private const string MessageColumns = @"
        m.id, m.employee_id AS EmployeeId,
        CONCAT(e.first_name,' ',e.last_name) AS EmployeeName,
        CONCAT(LEFT(e.first_name,1), LEFT(e.last_name,1)) AS EmployeeInitials,
        m.message, m.created_at AS CreatedAt,
        CASE WHEN m.employee_id = @EmpId THEN 1 ELSE 0 END AS IsMine,
        m.has_attachment AS HasAttachment,
        COALESCE(m.attachment_name,'') AS AttachmentName,
        '' AS AttachmentPath,
        m.reply_to_message_id AS ReplyToMessageId,
        COALESCE((SELECT SUBSTRING(r.message, 1, 80) FROM project_chat_messages r WHERE r.id = m.reply_to_message_id), '') AS ReplyToPreview,
        COALESCE((SELECT CONCAT(re.first_name,' ',re.last_name)
                  FROM project_chat_messages r JOIN employees re ON re.id = r.employee_id
                  WHERE r.id = m.reply_to_message_id), '') AS ReplyToEmployeeName,
        (SELECT COUNT(*) FROM project_chat_participants p
          WHERE p.chat_id = m.chat_id AND p.employee_id <> m.employee_id
            AND COALESCE(p.last_read_message_id, 0) >= m.id) AS ReadByCount,
        (SELECT COUNT(*) FROM project_chat_participants p
          WHERE p.chat_id = m.chat_id AND p.employee_id <> m.employee_id) AS OtherParticipantCount";

    // ── Inbox globale (solo chat in cui sei partecipante) ──
    //
    // `limit` serve alla pagina «Chat» del menu principale, che le vuole tutte: la campanella
    // in testata si accontenta delle ultime 80. La JOIN su projects è LEFT perché dalla #78
    // una chat può non avere commessa.
    [RequireFeature("project.chat", AccessOnly = true)]
    [HttpGet("inbox")]
    public IActionResult GetInbox([FromQuery] int limit = 80)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();
        int take = Math.Clamp(limit, 1, 500);
        var chats = c.Query<ChatListItem>($@"
            SELECT {ChatListColumns}
            FROM project_chats ch
            JOIN employees e ON e.id = ch.created_by
            LEFT JOIN projects pr ON pr.id = ch.project_id
            JOIN project_chat_participants me ON me.chat_id = ch.id AND me.employee_id = @EmpId
            ORDER BY COALESCE((SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id), ch.created_at) DESC
            LIMIT @Take",
            new { EmpId = empId, Take = take }).ToList();
        return Ok(ApiResponse<List<ChatListItem>>.Ok(chats));
    }

    [RequireFeature("project.chat", AccessOnly = true)]
    [HttpGet("inbox/badge")]
    public IActionResult GetInboxBadge()
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();
        int unread = c.ExecuteScalar<int>(@"
            SELECT COALESCE(SUM((
                SELECT COUNT(*) FROM project_chat_messages m
                WHERE m.chat_id = cp.chat_id
                  AND m.id > COALESCE(cp.last_read_message_id, 0)
            )), 0)
            FROM project_chat_participants cp
            WHERE cp.employee_id = @EmpId",
            new { EmpId = empId });
        return Ok(ApiResponse<ChatInboxBadge>.Ok(new ChatInboxBadge { UnreadCount = unread }));
    }

    // ── Lista chat per commessa (filtrata per partecipante, chi modera le vede tutte) ──
    [RequireFeature("project.chat")]
    [RequireProjectVisible]
    [HttpGet("project/{projectId}")]
    public IActionResult GetChats(int projectId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();
        bool canModerate = CanModerateChat();

        var chats = c.Query<ChatListItem>($@"
            SELECT {ChatListColumns}
            FROM project_chats ch
            JOIN employees e ON e.id = ch.created_by
            LEFT JOIN projects pr ON pr.id = ch.project_id
            WHERE ch.project_id = @ProjectId
              AND (@CanModerate = 1 OR EXISTS(
                    SELECT 1 FROM project_chat_participants cp
                    WHERE cp.chat_id = ch.id AND cp.employee_id = @EmpId))
            ORDER BY COALESCE((SELECT MAX(m.created_at) FROM project_chat_messages m WHERE m.chat_id = ch.id), ch.created_at) DESC",
            new { ProjectId = projectId, EmpId = empId, CanModerate = canModerate ? 1 : 0 }).ToList();

        return Ok(ApiResponse<List<ChatListItem>>.Ok(chats));
    }

    // ── Messaggi di una chat (pagina da fondo, cursor beforeId) ──
    [RequireFeature("project.chat")]
    [HttpGet("{chatId}/messages")]
    public IActionResult GetMessages(int chatId, [FromQuery] int beforeId = 0, [FromQuery] int limit = MessagePageSizeDefault)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        int empId = GetCurrentEmployeeId();
        int take = Math.Clamp(limit, 1, MessagePageSizeMax);

        var rows = c.Query<ChatMessageDto>($@"
            SELECT {MessageColumns}
            FROM project_chat_messages m
            JOIN employees e ON e.id = m.employee_id
            WHERE m.chat_id = @ChatId
              AND (@BeforeId = 0 OR m.id < @BeforeId)
            ORDER BY m.id DESC
            LIMIT @Take",
            new { ChatId = chatId, EmpId = empId, BeforeId = beforeId, Take = take + 1 }).ToList();

        bool hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        rows.Reverse();

        return Ok(ApiResponse<ChatMessagesPage>.Ok(new ChatMessagesPage
        {
            Messages = rows,
            HasMore = hasMore,
        }));
    }

    // ── Partecipanti di una chat ──
    [RequireFeature("project.chat")]
    [HttpGet("{chatId}/participants")]
    public IActionResult GetParticipants(int chatId)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

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

    // ── Crea chat (titolo opzionale: 1:1 prende i cognomi) ──
    //
    // La commessa è FACOLTATIVA (#78): la tendina «Commessa o attività» ha come prima voce
    // «— Nessuna —», e quella scelta arriva qui come ProjectId nullo.
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost]
    public IActionResult CreateChat([FromBody] ChatCreateRequest req)
    {
        using var c = _db.Open();

        int? projectId = req.ProjectId is > 0 ? req.ProjectId : null;
        if (projectId.HasValue)
        {
            int? project = c.ExecuteScalar<int?>(
                "SELECT id FROM projects WHERE id = @Id", new { Id = projectId.Value });
            if (project == null)
                return NotFound(ApiResponse<string>.Fail("Commessa non trovata."));
        }

        using var tx = c.BeginTransaction();

        int empId = GetCurrentEmployeeId();
        var participantIds = new HashSet<int>(req.ParticipantIds) { empId };
        string title = ResolveTitle(c, req.Title, participantIds, tx);

        int chatId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chats (project_id, title, created_by)
            VALUES (@ProjectId, @Title, @CreatedBy);
            SELECT LAST_INSERT_ID()",
            new { ProjectId = projectId, Title = title, CreatedBy = empId }, tx);

        foreach (int pid in participantIds)
        {
            c.Execute(@"INSERT IGNORE INTO project_chat_participants (chat_id, employee_id)
                VALUES (@ChatId, @EmpId)",
                new { ChatId = chatId, EmpId = pid }, tx);
        }

        tx.Commit();
        NotifyChatChange(projectId, chatId, "create");
        return Ok(ApiResponse<int>.Ok(chatId, "Chat creata"));
    }

    // ── Invia messaggio ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost("{chatId}/messages")]
    public IActionResult SendMessage(int chatId, [FromBody] ChatSendMessageRequest req)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        int empId = GetCurrentEmployeeId();

        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(ApiResponse<string>.Fail("Messaggio vuoto."));

        int? projectId = ProjectOfChat(c, chatId);

        int? replyTo = ResolveReplyTo(c, chatId, req.ReplyToMessageId);

        int msgId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chat_messages (chat_id, employee_id, message, reply_to_message_id)
            VALUES (@ChatId, @EmpId, @Message, @ReplyTo);
            SELECT LAST_INSERT_ID()",
            new { ChatId = chatId, EmpId = empId, req.Message, ReplyTo = replyTo });

        // Una menzione tira dentro chi non c'era: è la regola della #78 («con @ deve apparire la
        // lista dei colleghi, in modo da coinvolgere un collega non inserito prima»).
        if (AggiungiMenzionatiAllaChat(c, chatId, req.Message) > 0)
            NotifyChatChange(projectId, chatId, "participants");

        NotifyChatChange(projectId, chatId, "message");
        NotifyChatParticipants(chatId, projectId, empId, req.Message);
        NotifyChatMessageAlert(c, chatId, projectId, empId, req.Message);

        return Ok(ApiResponse<int>.Ok(msgId));
    }

    // ── Elimina messaggio (solo autore o ADMIN) ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpDelete("messages/{messageId}")]
    public IActionResult DeleteMessage(int messageId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        var msg = c.QueryFirstOrDefault<dynamic>(
            "SELECT employee_id, chat_id AS ChatId FROM project_chat_messages WHERE id=@Id",
            new { Id = messageId });

        if (msg == null)
            return NotFound(ApiResponse<string>.Fail("Messaggio non trovato."));

        int chatIdForAccess = (int)msg.ChatId;
        if (EnsureChatAccess(c, chatIdForAccess) is { } denied)
            return denied;

        if ((int)msg.employee_id != empId && !CanModerateChat())
            return Forbid();

        int chatId = (int)msg.ChatId;
        int? projectId = ProjectOfChat(c, chatId);

        c.Execute("UPDATE project_chat_messages SET reply_to_message_id = NULL WHERE reply_to_message_id=@Id",
            new { Id = messageId });
        c.Execute("DELETE FROM project_chat_messages WHERE id=@Id", new { Id = messageId });

        NotifyChatChange(projectId, chatId, "delete_message");

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Aggiungi partecipante ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost("{chatId}/participants")]
    public IActionResult AddParticipant(int chatId, [FromBody] int employeeId)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        c.Execute(@"INSERT IGNORE INTO project_chat_participants (chat_id, employee_id)
            VALUES (@ChatId, @EmpId)",
            new { ChatId = chatId, EmpId = employeeId });

        NotifyChatChange(ProjectOfChat(c, chatId), chatId, "participants");

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Rimuovi partecipante ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpDelete("{chatId}/participants/{employeeId}")]
    public IActionResult RemoveParticipant(int chatId, int employeeId)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        c.Execute("DELETE FROM project_chat_participants WHERE chat_id=@ChatId AND employee_id=@EmpId",
            new { ChatId = chatId, EmpId = employeeId });

        NotifyChatChange(ProjectOfChat(c, chatId), chatId, "participants");

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Esci dalla chat (togli te stesso; la conversazione resta per gli altri) ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost("{chatId}/leave")]
    public IActionResult LeaveChat(int chatId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        // La commessa può essere nulla (chat senza commessa): l'esistenza della chat si chiede
        // alla riga, non al suo project_id — che altrimenti farebbe rispondere «non trovata».
        int? found = c.ExecuteScalar<int?>(
            "SELECT id FROM project_chats WHERE id = @ChatId", new { ChatId = chatId });
        if (found == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        int? projectId = ProjectOfChat(c, chatId);

        int removed = c.Execute(
            "DELETE FROM project_chat_participants WHERE chat_id=@ChatId AND employee_id=@EmpId",
            new { ChatId = chatId, EmpId = empId });
        if (removed == 0)
            return BadRequest(ApiResponse<string>.Fail("Non sei partecipante di questa chat."));

        NotifyChatChange(projectId, chatId, "leave");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Elimina chat (solo creatore o ADMIN) ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpDelete("{chatId}")]
    public IActionResult DeleteChat(int chatId)
    {
        using var c = _db.Open();
        int empId = GetCurrentEmployeeId();

        var chat = c.QueryFirstOrDefault<dynamic>(
            "SELECT created_by, project_id AS ProjectId FROM project_chats WHERE id=@Id",
            new { Id = chatId });

        if (chat == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        if ((int)chat.created_by != empId && !CanModerateChat())
            return Forbid();

        int? projectId = (int?)chat.ProjectId;
        c.Execute("DELETE FROM project_chats WHERE id=@Id", new { Id = chatId });
        NotifyChatChange(projectId, chatId, "delete_chat");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Invia messaggio con allegato ──
    [RequireFeature("project.chat")]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost("{chatId}/messages/with-attachment")]
    public IActionResult SendMessageWithAttachment(int chatId, [FromBody] ChatAttachmentRequest req)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        int empId = GetCurrentEmployeeId();

        var chatInfo = c.QueryFirstOrDefault<dynamic>(@"
            SELECT ch.project_id, p.server_path
            FROM project_chats ch
            LEFT JOIN projects p ON p.id = ch.project_id
            WHERE ch.id = @ChatId", new { ChatId = chatId });

        if (chatInfo == null)
            return NotFound(ApiResponse<string>.Fail("Chat non trovata."));

        int? attachProjectId = (int?)chatInfo.project_id;
        string serverPath = (string)(chatInfo.server_path ?? "");
        if (attachProjectId.HasValue && string.IsNullOrEmpty(serverPath))
            return BadRequest(ApiResponse<string>.Fail("Cartella commessa non creata."));

        // Chat senza commessa: nessuna cartella di commessa dove appoggiarsi, si usa la radice
        // degli upload (che sopravvive agli aggiornamenti ed è dentro il backup completo).
        string chatFolder = attachProjectId.HasValue
            ? Path.Combine(serverPath, "Chat", chatId.ToString())
            : Path.Combine(UploadPaths.Chat(_config), chatId.ToString());
        Directory.CreateDirectory(chatFolder);

        string safeFileName = Path.GetFileName(req.FileName);
        string filePath = Path.Combine(chatFolder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeFileName}");

        byte[] fileBytes = Convert.FromBase64String(req.FileData);
        const int maxBytes = 20 * 1024 * 1024;
        if (fileBytes.Length > maxBytes)
            return BadRequest(ApiResponse<string>.Fail("File troppo grande (max 20 MB)."));

        System.IO.File.WriteAllBytes(filePath, fileBytes);

        int? replyTo = ResolveReplyTo(c, chatId, req.ReplyToMessageId);
        string caption = string.IsNullOrWhiteSpace(req.Message) ? $"📎 {safeFileName}" : req.Message;

        int msgId = c.ExecuteScalar<int>(@"
            INSERT INTO project_chat_messages (chat_id, employee_id, message, has_attachment, attachment_name, attachment_path, reply_to_message_id)
            VALUES (@ChatId, @EmpId, @Message, 1, @AttName, @AttPath, @ReplyTo);
            SELECT LAST_INSERT_ID()",
            new { ChatId = chatId, EmpId = empId, Message = caption, AttName = safeFileName, AttPath = filePath, ReplyTo = replyTo });

        if (AggiungiMenzionatiAllaChat(c, chatId, caption) > 0)
            NotifyChatChange(attachProjectId, chatId, "participants");

        NotifyChatChange(attachProjectId, chatId, "message");
        NotifyChatParticipants(chatId, attachProjectId, empId, caption);
        NotifyChatMessageAlert(c, chatId, attachProjectId, empId, caption);

        return Ok(ApiResponse<int>.Ok(msgId));
    }

    // ── Segna chat come letta ──
    [RequireFeature("project.chat", AccessOnly = true)]
    [ScritturaNonDiCommessa("La chat e comunicazione fra persone, non un dato della commessa: zittirla proprio quando una commessa va in stand-by o si chiude toglierebbe il posto in cui ci si dice perche")]
    [HttpPost("{chatId}/mark-read")]
    public IActionResult MarkAsRead(int chatId)
    {
        using var c = _db.Open();
        if (EnsureChatAccess(c, chatId) is { } denied)
            return denied;

        int empId = GetCurrentEmployeeId();

        int? lastMsgId = c.ExecuteScalar<int?>(
            "SELECT MAX(id) FROM project_chat_messages WHERE chat_id = @ChatId",
            new { ChatId = chatId });

        // 🪤 Il segnale parte SOLO se il segnaposto si è davvero mosso. Prima si scriveva
        // sempre e si notificava sempre, e siccome il client rileggeva i messaggi a ogni
        // notifica — e la rilettura faceva ripartire il «letto» — chi teneva una chat aperta
        // restava in un giro di aggiornamenti continui: è il «tutto sembra rallentato» della #78.
        int letto = 0;
        if (lastMsgId.HasValue)
        {
            letto = c.Execute(@"UPDATE project_chat_participants
                SET last_read_message_id = @LastId
                WHERE chat_id = @ChatId AND employee_id = @EmpId
                  AND COALESCE(last_read_message_id, 0) < @LastId",
                new { LastId = lastMsgId.Value, ChatId = chatId, EmpId = empId });
        }

        c.Execute(@"
            UPDATE notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            SET nr.is_read = 1, nr.read_at = NOW()
            WHERE nr.employee_id = @EmpId AND nr.is_read = 0
              AND n.reference_type = 'CHAT' AND n.reference_id = @ChatId",
            new { EmpId = empId, ChatId = chatId });

        if (letto > 0)
            NotifyChatChange(ProjectOfChat(c, chatId), chatId, "read");

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Download allegato messaggio (web: path assoluto su server non esponibile al client) ──
    [RequireFeature("project.chat")]
    [HttpGet("messages/{messageId}/attachment")]
    public IActionResult DownloadAttachment(int messageId)
    {
        using var c = _db.Open();

        var msg = c.QueryFirstOrDefault<dynamic>(@"
            SELECT m.chat_id AS ChatId, m.has_attachment AS HasAttachment,
                   COALESCE(m.attachment_path,'') AS AttachmentPath,
                   COALESCE(m.attachment_name,'') AS AttachmentName,
                   ch.project_id AS ProjectId, p.server_path AS ServerPath
            FROM project_chat_messages m
            JOIN project_chats ch ON ch.id = m.chat_id
            LEFT JOIN projects p ON p.id = ch.project_id
            WHERE m.id = @Id",
            new { Id = messageId });

        if (msg == null)
            return NotFound(ApiResponse<string>.Fail("Messaggio non trovato."));

        if (EnsureChatAccess(c, (int)msg.ChatId) is { } denied)
            return denied;

        if (!(bool)msg.HasAttachment || string.IsNullOrEmpty((string)msg.AttachmentPath))
            return NotFound(ApiResponse<string>.Fail("Nessun allegato."));

        string attachmentPath = (string)msg.AttachmentPath;
        string serverPath = (string)(msg.ServerPath ?? "");
        // Radice ammessa: la cartella della commessa, oppure quella degli allegati delle chat
        // senza commessa. Fuori di lì il file non si serve, qualunque cosa dica la colonna.
        string normalizedRoot = string.IsNullOrEmpty(serverPath)
            ? Path.GetFullPath(UploadPaths.Chat(_config))
            : Path.GetFullPath(serverPath);

        string normalizedFile = Path.GetFullPath(attachmentPath);
        if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<string>.Fail("Path allegato non valido."));

        if (!System.IO.File.Exists(attachmentPath))
            return NotFound(ApiResponse<string>.Fail("File non trovato."));

        string fileName = (string)msg.AttachmentName;
        if (string.IsNullOrEmpty(fileName))
            fileName = Path.GetFileName(attachmentPath);

        string ext = Path.GetExtension(fileName).ToLower();
        string contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(attachmentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, fileName);
    }

    private static int? ResolveReplyTo(IDbConnection c, int chatId, int? replyToId)
    {
        if (replyToId is not > 0)
            return null;
        return c.ExecuteScalar<int?>(
            "SELECT id FROM project_chat_messages WHERE id=@Id AND chat_id=@ChatId",
            new { Id = replyToId.Value, ChatId = chatId });
    }

    private static string ResolveTitle(
        IDbConnection c, string title, HashSet<int> participantIds, IDbTransaction tx)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        var names = c.Query<string>(@"
            SELECT CONCAT(last_name, ' ', LEFT(first_name,1), '.')
            FROM employees WHERE id IN @Ids
            ORDER BY last_name, first_name",
            new { Ids = participantIds.ToList() }, tx).ToList();
        if (names.Count == 0)
            return "Chat";
        string joined = string.Join(" / ", names);
        return joined.Length <= 200 ? joined : joined[..197] + "…";
    }

    /// <summary>
    /// Menzioni «@Nome Cognome» rivolte a colleghi che nella chat non ci sono ancora: li
    /// aggiunge come partecipanti. È la richiesta della #78 — la tendina del «@» elenca tutti,
    /// proprio per tirare dentro chi non era stato messo alla creazione.
    /// </summary>
    /// <returns>Quanti ne ha aggiunti.</returns>
    private static int AggiungiMenzionatiAllaChat(IDbConnection c, int chatId, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !message.Contains('@'))
            return 0;

        // Solo chi NON è già dentro: la lista è corta (i dipendenti attivi con utenza), e il
        // confronto si fa in memoria perché il nome è composto da due colonne.
        var estranei = c.Query<(int Id, string Name)>(@"
            SELECT e.id AS Id, CONCAT(e.first_name,' ',e.last_name) AS Name
            FROM employees e
            WHERE e.status = 'ACTIVE'
              AND NOT EXISTS (SELECT 1 FROM project_chat_participants p
                              WHERE p.chat_id = @ChatId AND p.employee_id = e.id)",
            new { ChatId = chatId }).ToList();

        int aggiunti = 0;
        foreach (var persona in estranei)
        {
            if (string.IsNullOrWhiteSpace(persona.Name)) continue;
            if (!message.Contains("@" + persona.Name, StringComparison.OrdinalIgnoreCase)) continue;

            aggiunti += c.Execute(@"INSERT IGNORE INTO project_chat_participants (chat_id, employee_id)
                VALUES (@ChatId, @EmpId)",
                new { ChatId = chatId, EmpId = persona.Id });
        }

        return aggiunti;
    }

    /// <summary>
    /// L'avviso «hai un messaggio», uno per messaggio, mandato ai soli partecipanti (mittente
    /// escluso) sul loro gruppo personale: è quello che fa comparire il riquadro discreto.
    /// Separato dalle notifiche in campanella, che invece si diradano apposta.
    /// </summary>
    private void NotifyChatMessageAlert(IDbConnection c, int chatId, int? projectId, int senderId, string message)
    {
        var destinatari = c.Query<int>(@"
            SELECT cp.employee_id
            FROM project_chat_participants cp
            JOIN employees e ON e.id = cp.employee_id
            WHERE cp.chat_id = @ChatId AND cp.employee_id <> @Me AND e.status = 'ACTIVE'",
            new { ChatId = chatId, Me = senderId }).ToList();
        if (destinatari.Count == 0)
            return;

        var info = c.QueryFirstOrDefault<dynamic>(@"
            SELECT ch.title AS Title, COALESCE(pr.code,'') AS Code, COALESCE(pr.title,'') AS ProjectTitle
            FROM project_chats ch
            LEFT JOIN projects pr ON pr.id = ch.project_id
            WHERE ch.id = @ChatId", new { ChatId = chatId });

        string code = (string)(info?.Code ?? "");
        string projectTitle = (string)(info?.ProjectTitle ?? "");
        string contesto = string.IsNullOrEmpty(code)
            ? ""
            : (string.IsNullOrEmpty(projectTitle) ? code : $"{code} · {projectTitle}");

        var alert = new ChatMessageAlert
        {
            ChatId = chatId,
            ProjectId = projectId,
            ChatTitle = (string)(info?.Title ?? "Chat"),
            ContextLabel = contesto,
            SenderId = senderId,
            SenderName = CurrentEmployeeName(),
            Preview = message.Length > 120 ? message[..120] + "…" : message,
        };

        foreach (int empId in destinatari)
            _ = _hub.Clients.Group(ProjectHub.UserGroup(empId)).SendAsync("ChatMessageReceived", alert);
    }

    /// <summary>
    /// Notifica i partecipanti: @mention sempre; gli altri solo se non hanno già
    /// una CHAT_MESSAGE non letta su questa chat (evita di intasare la campanella).
    /// </summary>
    private void NotifyChatParticipants(int chatId, int? projectId, int senderId, string message)
    {
        using var c = _db.Open();
        var parts = c.Query<(int Id, string Name)>(@"
            SELECT e.id AS Id, CONCAT(e.first_name,' ',e.last_name) AS Name
            FROM project_chat_participants cp
            JOIN employees e ON e.id = cp.employee_id
            WHERE cp.chat_id = @ChatId AND e.status = 'ACTIVE'",
            new { ChatId = chatId }).ToList();

        var others = parts.Where(p => p.Id != senderId).ToList();
        if (others.Count == 0)
            return;

        var mentioned = others
            .Where(p => message.Contains("@" + p.Name, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .ToHashSet();

        string preview = message.Length > 80 ? message[..80] + "…" : message;
        string chatTitle = c.ExecuteScalar<string>(
            "SELECT title FROM project_chats WHERE id=@Id", new { Id = chatId }) ?? "Chat";
        string senderName = CurrentEmployeeName();

        if (mentioned.Count > 0)
        {
            _notifications.Create(
                "CHAT_MENTION", "INFO",
                $"{senderName} ti ha menzionato",
                preview,
                "CHAT", chatId, projectId, senderId, mentioned);
        }

        var rest = others.Select(p => p.Id).Where(id => !mentioned.Contains(id)).ToList();
        if (rest.Count == 0)
            return;

        var alreadyUnread = c.Query<int>(@"
            SELECT nr.employee_id
            FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            WHERE n.notification_type = 'CHAT_MESSAGE'
              AND n.reference_type = 'CHAT'
              AND n.reference_id = @ChatId
              AND nr.is_read = 0
              AND nr.employee_id IN @Ids",
            new { ChatId = chatId, Ids = rest }).ToHashSet();

        var toNotify = rest.Where(id => !alreadyUnread.Contains(id)).ToList();
        if (toNotify.Count == 0)
            return;

        _notifications.Create(
            "CHAT_MESSAGE", "INFO",
            $"{senderName} · {chatTitle}",
            preview,
            "CHAT", chatId, projectId, senderId, toNotify);
    }
}
