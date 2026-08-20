using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Segnalazioni su ATEC PM: bug e richieste di miglioramento.
// Dalla #93 ognuno vede SOLO le proprie; la vista completa spetta a chi ha
// `data.bug_reports_all` (ADMIN col jolly, Paolo Zanoni seminato dalla v97) o a chi
// gestisce (`action.manage_bug_reports`): non si può rispondere a segnalazioni che non
// si vedono. Il contenuto lo modifica l'autore (o chi gestisce), e la stessa chiave
// governa lo STATO.
// `nav.bug_reports` è registrata a livello minimo 0, quindi la chiave non toglie nulla a
// nessuno: serve a far valere le concessioni in sola lettura (reparto Contabilità).
// ⚠️ `data.bug_reports_all` NON va messa nell'attributo qui sotto: dentro lo stesso
// [RequireFeature] le chiavi sono in OR (aprirebbe la pagina a chi ha solo la vista),
// come secondo attributo si sommano in AND (la chiuderebbe a chi vede solo le proprie).
// Congelato in VisibilitaSegnalazioniTests.
[ApiController]
[Route("api/bug-reports")]
[Authorize]
[RequireFeature("nav.bug_reports")]
public class BugReportsController : ControllerBase
{
    private readonly BugReportsDbService _bdb;
    private readonly NotificationService _notif;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly IConfiguration _config;

    private readonly FeatureAccessService _access;

    public BugReportsController(
        BugReportsDbService bdb,
        NotificationService notif,
        IHubContext<ProjectHub> hub,
        IConfiguration config,
        FeatureAccessService access)
    {
        _bdb = bdb;
        _notif = notif;
        _hub = hub;
        _config = config;
        _access = access;
    }

    public const string ConflictMessage = "CONFLITTO: segnalazione modificata da un altro utente";

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Chi ha <c>action.manage_bug_reports</c> mette le mani anche sulle segnalazioni altrui.
    /// Si valuta con <c>CanAccessUser</c> (id dalla claim, non dal nome) perché a decidere devono
    /// essere i permessi della PERSONA: <c>CanAccess(ruolo, …)</c> col motore nuovo li ignora.
    /// </summary>
    private bool PuoGestireSegnalazioni =>
        _access.CanAccessUser(CurrentEmployeeId, User.FindFirst(ClaimTypes.Role)?.Value,
                              "action.manage_bug_reports");

    /// <summary>
    /// #93: vista completa dell'elenco. La chiave dedicata è <c>data.bug_reports_all</c>
    /// (ADMIN col jolly, Paolo Zanoni a mano dalla v97), ma chi gestisce vede comunque tutto:
    /// non si cambia stato a una segnalazione che non si vede.
    /// </summary>
    private bool VedeTutte =>
        PuoGestireSegnalazioni
        || _access.CanAccessUser(CurrentEmployeeId, User.FindFirst(ClaimTypes.Role)?.Value,
                                 "data.bug_reports_all");

    /// <summary>
    /// #93: chi non vede tutto legge solo le proprie righe. Statico e pubblico: i test eseguono
    /// QUESTO filtro, non una copia (stesso motivo di <c>WorkRequestsController.RigheDdpFiltro</c>).
    /// Le righe con <c>created_by</c> NULL (autore rimosso) le vede solo chi vede tutto:
    /// il confronto con @Me le scarta da sé.
    /// </summary>
    public static string FiltroVisibilitaSql(bool vedeTutte) =>
        vedeTutte ? "" : " AND b.created_by = @Me";

    /// <summary>
    /// #93: la regola del singolo accesso (oggi usata per gli allegati), isolata per i test:
    /// una segnalazione si apre se è propria o se si vede tutto; senza autore (<c>createdBy</c>
    /// NULL) o senza utenza riconosciuta (<c>me</c> = 0) resta solo la vista completa.
    /// </summary>
    public static bool PuoVedereSegnalazione(bool vedeTutte, int? createdBy, int me) =>
        vedeTutte || (createdBy.HasValue && me > 0 && createdBy.Value == me);

    /// <summary>
    /// Chi può TOGLIERE un allegato: chi gestisce le segnalazioni, oppure chi quel file l'ha
    /// caricato di persona.
    ///
    /// <para><b>Perché non basta «può modificare la segnalazione».</b> Su una segnalazione
    /// possono finire file di due mani diverse: quelli del segnalatore e quelli che chi gestisce
    /// allega rispondendo (la schermata della correzione). Con la sola <c>CheckCanEdit</c> il
    /// segnalatore, che la segnalazione può modificarla perché è sua, poteva cancellare anche le
    /// foto della risposta — cioè la prova del lavoro fatto, per giunta senza che nessuno se ne
    /// accorgesse.</para>
    ///
    /// <para>Allegato senza autore (<c>caricatoDa</c> NULL: caricato da un'utenza poi rimossa)
    /// lo toglie solo chi gestisce. È più severo di prima ed è voluto: di un file di cui non si
    /// sa la provenienza decide chi ha in mano il modulo, non il primo che apre la scheda.</para>
    /// </summary>
    public static bool PuoEliminareAllegato(bool gestisce, int? caricatoDa, int me) =>
        gestisce || (caricatoDa.HasValue && me > 0 && caricatoDa.Value == me);

    private void NotifyChanged(string action, int bugId) =>
        _ = _hub.Clients.Group(ProjectHub.BugReportsGroup)
            .SendAsync("BugReportsChanged", new { action, bugId });

    // Cartella degli allegati: FUORI da wwwroot e da /uploads/cms (serviti in anonimo) e
    // soprattutto fuori dalla cartella del programma, che l'aggiornamento sostituisce in
    // blocco. Vedi UploadPaths. Il download passa dall'endpoint autenticato qui sotto.
    private string AttachmentsDir
    {
        get
        {
            string root = UploadPaths.Bugs(_config);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private const string BugSelect = @"
        SELECT b.id AS Id, b.kind AS Kind, b.title AS Title, b.description AS Description,
               b.area AS Area, b.severity AS Severity, b.status AS Status,
               COALESCE(b.admin_note, '') AS AdminNote,
               b.context AS Context,
               b.fixed_in_build AS FixedInBuild,
               b.archived_at AS ArchivedAt,
               b.created_by AS CreatedById,
               COALESCE(CONCAT(e.first_name, ' ', e.last_name), 'Utente rimosso') AS CreatedByName,
               b.created_at AS CreatedAt, b.updated_at AS UpdatedAt, b.resolved_at AS ResolvedAt,
               b.row_version AS RowVersion
        FROM bug_reports b
        LEFT JOIN employees e ON e.id = b.created_by";

    // ═══════════════════════════════════════════════════════
    // LETTURA
    // ═══════════════════════════════════════════════════════

    /// <summary>Elenco: completo per chi vede tutto, solo le proprie per gli altri (#93). Più recenti in cima.</summary>
    [HttpGet]
    public IActionResult GetAll([FromQuery] bool archived = false)
    {
        try
        {
            using var c = _bdb.Open();
            int me = CurrentEmployeeId;
            string archiveFilter = archived ? " AND b.archived_at IS NOT NULL" : " AND b.archived_at IS NULL";
            List<BugReportDto> rows = c.Query<BugReportDto>(
                BugSelect + " WHERE 1=1" + archiveFilter + FiltroVisibilitaSql(VedeTutte)
                          + " ORDER BY b.created_at DESC, b.id DESC",
                new { Me = me }).ToList();

            foreach (BugReportDto row in rows)
                row.IsMine = row.CreatedById.HasValue && row.CreatedById.Value == me;

            // Allegati in una query sola, ristretta alle sole segnalazioni visibili.
            if (rows.Count > 0)
            {
                List<BugAttachmentDto> attachments = c.Query<BugAttachmentDto>(@"
                    SELECT a.id AS Id, a.bug_id AS BugId, a.file_name AS FileName,
                           a.content_type AS ContentType, a.size_bytes AS SizeBytes,
                           a.is_reply AS IsReply,
                           a.created_by AS CreatedById,
                           COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS CreatedByName,
                           a.created_at AS CreatedAt
                    FROM bug_report_attachments a
                    LEFT JOIN employees e ON e.id = a.created_by
                    WHERE a.bug_id IN @Ids
                    ORDER BY a.id", new { Ids = rows.Select(r => r.Id).ToList() }).ToList();
                foreach (BugAttachmentDto a in attachments)
                    a.IsImage = a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var byBug = attachments.GroupBy(a => a.BugId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (BugReportDto row in rows)
                    if (byBug.TryGetValue(row.Id, out List<BugAttachmentDto>? list))
                        row.Attachments = list;
            }

            return Ok(ApiResponse<List<BugReportDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<BugReportDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("counts")]
    public IActionResult GetCounts()
    {
        try
        {
            using var c = _bdb.Open();
            // `All` è parola riservata in MySQL: va sempre fra backtick.
            // Stesso filtro dell'elenco (#93): così anche il badge del menu conta solo il visibile.
            var dto = c.QuerySingle<BugReportCountsDto>(@"
                SELECT COALESCE(SUM(b.archived_at IS NULL), 0) AS `All`,
                       COALESCE(SUM(b.archived_at IS NULL AND b.created_by = @Me), 0) AS Mine,
                       COALESCE(SUM(b.archived_at IS NULL AND b.status = 'OPEN'), 0) AS Open,
                       COALESCE(SUM(b.archived_at IS NULL AND b.status = 'IN_PROGRESS'), 0) AS InProgress,
                       COALESCE(SUM(b.archived_at IS NULL AND b.status = 'RESOLVED'), 0) AS Resolved,
                       COALESCE(SUM(b.archived_at IS NULL AND b.status = 'REJECTED'), 0) AS Rejected,
                       COALESCE(SUM(b.archived_at IS NOT NULL), 0) AS Archived
                FROM bug_reports b
                WHERE 1=1" + FiltroVisibilitaSql(VedeTutte), new { Me = CurrentEmployeeId });
            return Ok(ApiResponse<BugReportCountsDto>.Ok(dto));
        }
        catch (Exception ex) { return Ok(ApiResponse<BugReportCountsDto>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // SCRITTURA
    // ═══════════════════════════════════════════════════════

    [HttpPost]
    public IActionResult Create([FromBody] BugReportSaveRequest req)
    {
        try
        {
            string title = (req.Title ?? "").Trim();
            if (title.Length == 0) return Ok(ApiResponse<int>.Fail("Il titolo è obbligatorio"));

            using var c = _bdb.Open();
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO bug_reports (kind, title, description, area, severity, status, context, created_by)
                VALUES (@Kind, @Title, @Description, @Area, @Severity, 'OPEN', @Context, @CreatedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    Kind = NormalizeKind(req.Kind),
                    Title = Truncate(title, 300),
                    Description = (req.Description ?? "").Trim(),
                    Area = Truncate((req.Area ?? "").Trim(), 200),
                    Severity = NormalizeSeverity(req.Severity),
                    Context = (req.Context ?? "").Trim(),
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });

            NotifyAdmins(c, id, NormalizeKind(req.Kind), title, NormalizeSeverity(req.Severity));
            NotifyChanged("create", id);
            return Ok(ApiResponse<int>.Ok(id, "Segnalazione inviata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] BugReportSaveRequest req)
    {
        try
        {
            string title = (req.Title ?? "").Trim();
            if (title.Length == 0) return Ok(ApiResponse<int>.Fail("Il titolo è obbligatorio"));

            using var c = _bdb.Open();
            string? error = CheckCanEdit(c, id);
            if (error != null) return Ok(ApiResponse<int>.Fail(error));

            int rows = c.Execute(@"
                UPDATE bug_reports SET
                    kind=@Kind, title=@Title, description=@Description, area=@Area,
                    severity=@Severity, updated_at=CURRENT_TIMESTAMP,
                    row_version = row_version + 1
                 WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
                new
                {
                    Kind = NormalizeKind(req.Kind),
                    Title = Truncate(title, 300),
                    Description = (req.Description ?? "").Trim(),
                    Area = Truncate((req.Area ?? "").Trim(), 200),
                    Severity = NormalizeSeverity(req.Severity),
                    Id = id,
                    req.RowVersion
                });
            if (rows == 0) return Ok(ApiResponse<int>.Fail(ConflictMessage));

            NotifyChanged("update", id);
            return Ok(ApiResponse<int>.Ok(id, "Segnalazione aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Cambio di stato + nota di risposta: serve <c>action.manage_bug_reports</c>.</summary>
    // Secondo attributo, non una chiave in più dentro quello di classe: due [RequireFeature]
    // distinti si sommano in AND (dentro il primo sarebbero in OR e aprirebbero il cancello).
    [HttpPut("{id}/status")]
    [Authorize]
    [RequireFeature("action.manage_bug_reports")]
    public IActionResult UpdateStatus(int id, [FromBody] BugReportStatusRequest req)
    {
        try
        {
            using var c = _bdb.Open();
            string status = NormalizeStatus(req.Status);

            // Stato PRIMA del salvataggio: distingue «passa a risolto» da «era già risolto e si
            // corregge la nota». Da questa differenza dipendono due cose che non vanno ripetute:
            // la campanella al segnalatore e la build di risoluzione.
            string statoPrecedente = c.ExecuteScalar<string>(
                "SELECT status FROM bug_reports WHERE id=@Id", new { Id = id }) ?? "";
            bool diventaRisolta = status == "RESOLVED" && statoPrecedente != "RESOLVED";
            // Build sconosciuta (sviluppo senza version.json) = NULL, non stringa vuota: la CASE
            // qui sotto scrive solo quando c'è un valore vero da scrivere.
            string? buildToSet = diventaRisolta ? GetCurrentServerBuild() : null;
            if (string.IsNullOrWhiteSpace(buildToSet)) buildToSet = null;

            int rows = c.Execute(@"
                UPDATE bug_reports SET
                    status=@Status,
                    admin_note=@AdminNote,
                    resolved_at = CASE
                        WHEN @Status IN ('RESOLVED','REJECTED') THEN COALESCE(resolved_at, CURRENT_TIMESTAMP)
                        ELSE NULL END,
                    -- Alla NUOVA risoluzione si riscrive la build, non si tiene la prima: una
                    -- segnalazione riaperta e richiusa è stata corretta adesso, e dire al
                    -- segnalatore di verificare in una build dove il difetto c'era ancora è
                    -- peggio che non dirgli niente. Fuori dalla transizione il campo non si tocca.
                    fixed_in_build = CASE
                        WHEN @FixedInBuild IS NOT NULL THEN @FixedInBuild
                        ELSE fixed_in_build END,
                    updated_at=CURRENT_TIMESTAMP,
                    row_version = row_version + 1
                 WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
                new
                {
                    Status = status,
                    AdminNote = (req.AdminNote ?? "").Trim(),
                    FixedInBuild = buildToSet,
                    Id = id,
                    req.RowVersion
                });
            if (rows == 0)
            {
                int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM bug_reports WHERE id=@Id", new { Id = id });
                return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Segnalazione non trovata"));
            }

            // Solo alla transizione: `Create` delle notifiche non deduplica, quindi notificare a
            // ogni salvataggio con stato RESOLVED significherebbe una campanella nuova ogni volta
            // che si ritocca la risposta di una segnalazione già chiusa.
            if (diventaRisolta)
            {
                NotifyAuthorResolved(c, id, buildToSet ?? "");
            }

            NotifyChanged("status", id);
            return Ok(ApiResponse<int>.Ok(id, "Stato aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("{id}/archive")]
    [Authorize]
    [RequireFeature("action.manage_bug_reports")]
    public IActionResult Archive(int id)
    {
        try
        {
            using var c = _bdb.Open();
            // L'archivio è la fine del percorso, non un modo per far sparire il lavoro arretrato:
            // una segnalazione ancora aperta archiviata sparirebbe dall'elenco del segnalatore
            // senza che nessuno le abbia risposto. Il vincolo vale QUI, non solo nel menu della
            // pagina: il pulsante nascosto non è un permesso.
            string stato = c.ExecuteScalar<string>(
                "SELECT status FROM bug_reports WHERE id=@Id", new { Id = id }) ?? "";
            if (stato.Length == 0) return Ok(ApiResponse<bool>.Fail("Segnalazione non trovata"));
            if (stato != "RESOLVED" && stato != "REJECTED")
                return Ok(ApiResponse<bool>.Fail(
                    "Si archiviano solo le segnalazioni risolte o rifiutate: questa è ancora aperta"));

            int rows = c.Execute(@"
                UPDATE bug_reports SET
                    archived_at = CURRENT_TIMESTAMP,
                    updated_at = CURRENT_TIMESTAMP,
                    row_version = row_version + 1
                WHERE id = @Id AND archived_at IS NULL", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Segnalazione non trovata o già archiviata"));

            NotifyChanged("archive", id);
            return Ok(ApiResponse<bool>.Ok(true, "Segnalazione archiviata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("{id}/unarchive")]
    [Authorize]
    [RequireFeature("action.manage_bug_reports")]
    public IActionResult Unarchive(int id)
    {
        try
        {
            using var c = _bdb.Open();
            int rows = c.Execute(@"
                UPDATE bug_reports SET
                    archived_at = NULL,
                    updated_at = CURRENT_TIMESTAMP,
                    row_version = row_version + 1
                WHERE id = @Id AND archived_at IS NOT NULL", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Segnalazione non trovata o non archiviata"));

            NotifyChanged("unarchive", id);
            return Ok(ApiResponse<bool>.Ok(true, "Segnalazione ripristinata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _bdb.Open();
            string? error = CheckCanEdit(c, id);
            if (error != null) return Ok(ApiResponse<bool>.Fail(error));

            // I file su disco vanno rimossi prima: la CASCADE cancella solo le righe.
            List<string> stored = c.Query<string>(
                "SELECT stored_name FROM bug_report_attachments WHERE bug_id=@Id", new { Id = id }).ToList();

            int rows = c.Execute("DELETE FROM bug_reports WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Segnalazione non trovata"));

            foreach (string name in stored) TryDeleteFile(name);

            NotifyChanged("delete", id);
            return Ok(ApiResponse<bool>.Ok(true, "Segnalazione eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // ALLEGATI (screenshot e file)
    // ═══════════════════════════════════════════════════════

    [HttpPost("{id}/attachments")]
    [RequestSizeLimit(25_000_000)] // 25 MB per file
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file, [FromQuery] bool isReply = false)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Ok(ApiResponse<int>.Fail("Nessun file ricevuto"));

            using var c = _bdb.Open();
            string? error = CheckCanEdit(c, id);
            if (error != null) return Ok(ApiResponse<int>.Fail(error));

            bool replyFlag = isReply && PuoGestireSegnalazioni;

            // Nome su disco indipendente da quello originale: niente path traversal, niente
            // collisioni. Il nome che vede l'utente resta in tabella.
            string original = Path.GetFileName(file.FileName ?? "allegato");
            string ext = Path.GetExtension(original);
            string storedName = $"bug{id}_{(replyFlag ? "rep_" : "")}{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
            string fullPath = Path.Combine(AttachmentsDir, storedName);

            await using (var stream = new FileStream(fullPath, FileMode.CreateNew))
                await file.CopyToAsync(stream);

            int attachmentId = c.ExecuteScalar<int>(@"
                INSERT INTO bug_report_attachments
                    (bug_id, file_name, stored_name, content_type, size_bytes, is_reply, created_by)
                VALUES (@BugId, @FileName, @StoredName, @ContentType, @Size, @IsReply, @CreatedBy);
                SELECT LAST_INSERT_ID()",
                new
                {
                    BugId = id,
                    FileName = Truncate(original, 300),
                    StoredName = storedName,
                    ContentType = Truncate(file.ContentType ?? "", 150),
                    Size = file.Length,
                    IsReply = replyFlag ? 1 : 0,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });

            NotifyChanged("attachment", id);
            return Ok(ApiResponse<int>.Ok(attachmentId, "Allegato caricato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Download/anteprima dell'allegato: passa da qui (autenticato), non da wwwroot.</summary>
    [HttpGet("attachments/{attachmentId}")]
    public IActionResult DownloadAttachment(int attachmentId)
    {
        using var c = _bdb.Open();
        AttachmentRow? row = c.QuerySingleOrDefault<AttachmentRow>(@"
            SELECT a.bug_id AS BugId, a.file_name AS FileName, a.stored_name AS StoredName,
                   a.content_type AS ContentType, a.is_reply AS IsReply, b.created_by AS CreatedBy
            FROM bug_report_attachments a
            JOIN bug_reports b ON b.id = a.bug_id
            WHERE a.id=@Id", new { Id = attachmentId });
        if (row == null) return NotFound();

        // #93: l'elenco è filtrato, ma l'id di un allegato si può indovinare. Stesso 404 di un
        // allegato inesistente: a chi non vede la segnalazione non si conferma che esiste.
        if (!PuoVedereSegnalazione(VedeTutte, row.CreatedBy, CurrentEmployeeId)) return NotFound();

        string fullPath = Path.Combine(AttachmentsDir, row.StoredName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        string contentType = string.IsNullOrWhiteSpace(row.ContentType)
            ? "application/octet-stream"
            : row.ContentType;
        return PhysicalFile(fullPath, contentType, row.FileName);
    }

    [HttpDelete("attachments/{attachmentId}")]
    public IActionResult DeleteAttachment(int attachmentId)
    {
        try
        {
            using var c = _bdb.Open();
            AttachmentRow? row = c.QuerySingleOrDefault<AttachmentRow>(@"
                SELECT bug_id AS BugId, file_name AS FileName, stored_name AS StoredName,
                       content_type AS ContentType, is_reply AS IsReply, created_by AS UploadedBy
                FROM bug_report_attachments WHERE id=@Id", new { Id = attachmentId });
            if (row == null) return Ok(ApiResponse<bool>.Fail("Allegato non trovato"));

            // Primo cancello: la segnalazione dev'essere sua (o si dev'essere gestori). Tiene
            // anche la #93 — a chi non la vede si risponde «non trovata», non «non tua».
            string? error = CheckCanEdit(c, row.BugId);
            if (error != null) return Ok(ApiResponse<bool>.Fail(error));

            // Gli allegati di risposta li toglie solo chi gestisce le segnalazioni
            if (row.IsReply && !PuoGestireSegnalazioni)
            {
                return Ok(ApiResponse<bool>.Fail(
                    "Questo allegato fa parte della risposta e può essere rimosso solo da chi gestisce le segnalazioni"));
            }

            // Secondo cancello: il file lo toglie chi l'ha messo. Vedi PuoEliminareAllegato
            if (!PuoEliminareAllegato(PuoGestireSegnalazioni, row.UploadedBy, CurrentEmployeeId))
                return Ok(ApiResponse<bool>.Fail(
                    "Questo allegato l'ha caricato un'altra persona: può toglierlo solo chi gestisce le segnalazioni"));

            c.Execute("DELETE FROM bug_report_attachments WHERE id=@Id", new { Id = attachmentId });
            TryDeleteFile(row.StoredName);

            NotifyChanged("attachment", row.BugId);
            return Ok(ApiResponse<bool>.Ok(true, "Allegato eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ── helper ─────────────────────────────────────────────

    // Classe, non ValueTuple: Dapper mappa per nome di proprietà.
    private sealed class AttachmentRow
    {
        public int BugId { get; set; }
        public string FileName { get; set; } = "";
        public string StoredName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public bool IsReply { get; set; }
        /// <summary>Autore della segnalazione (#93). Lo riempie solo la query del download.</summary>
        public int? CreatedBy { get; set; }
        /// <summary>Chi ha caricato QUESTO file. Lo riempie solo la query della cancellazione.</summary>
        public int? UploadedBy { get; set; }
    }

    private void NotifyAuthorResolved(MySqlConnection c, int bugId, string build)
    {
        try
        {
            var bug = c.QuerySingleOrDefault<(int? CreatedBy, string Title)>(
                "SELECT created_by AS CreatedBy, title AS Title FROM bug_reports WHERE id=@Id",
                new { Id = bugId });

            if (bug.CreatedBy.HasValue && bug.CreatedBy.Value > 0 && bug.CreatedBy.Value != CurrentEmployeeId)
            {
                string buildInfo = string.IsNullOrWhiteSpace(build) ? "" : $" nella build {build}";
                _notif.Create(
                    "BUG_RESOLVED",
                    "INFO",
                    $"Segnalazione risolta: {bug.Title}",
                    $"Risolto{buildInfo}: aggiorna e verifica.",
                    "BUG_REPORT", bugId, null,
                    CurrentEmployeeId > 0 ? CurrentEmployeeId : null,
                    new List<int> { bug.CreatedBy.Value });
            }
        }
        catch
        {
            // Notifica best-effort
        }
    }

    private string GetCurrentServerBuild()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "version.json");
            if (!System.IO.File.Exists(path))
            {
                string fallback = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "version.json");
                if (System.IO.File.Exists(fallback)) path = fallback;
            }
            if (System.IO.File.Exists(path))
            {
                string json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("build", out var prop))
                {
                    string? val = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
        }
        catch { }
        // Niente version.json (sviluppo, o cartella servita altrove): si lascia vuoto. Scrivere
        // qui la data di adesso inventerebbe una build mai esistita, e il segnalatore andrebbe a
        // cercare una versione che non troverà mai — meglio nessun dato di uno falso.
        return "";
    }

    /// <summary>null = può modificare; altrimenti il messaggio di errore da restituire.</summary>
    private string? CheckCanEdit(MySqlConnection c, int bugId)
    {
        int? author = c.ExecuteScalar<int?>(
            "SELECT COALESCE(created_by, 0) FROM bug_reports WHERE id=@Id", new { Id = bugId });
        if (author == null) return "Segnalazione non trovata";
        if (PuoGestireSegnalazioni) return null;
        if (CurrentEmployeeId > 0 && author.Value == CurrentEmployeeId) return null;
        // #93: a chi la segnalazione non è nemmeno VISIBILE si risponde come se non
        // esistesse — il messaggio differenziato confermerebbe l'esistenza dell'id
        // (stesso criterio del download allegati). `author` 0 = created_by NULL.
        if (!PuoVedereSegnalazione(VedeTutte, author.Value == 0 ? null : author, CurrentEmployeeId))
            return "Segnalazione non trovata";
        return "Puoi modificare solo le segnalazioni che hai aperto tu";
    }

    // Notifica agli ADMIN: la segnalazione arriva sulla campanella senza dover
    // aprire la sezione. Best-effort, un errore qui non deve far fallire la POST.
    private void NotifyAdmins(MySqlConnection c, int bugId, string kind, string title, string severity)
    {
        try
        {
            List<int> admins = c.Query<int>(@"
                SELECT id FROM employees
                WHERE user_role = 'ADMIN' AND status = 'ACTIVE' AND id <> @Me",
                new { Me = CurrentEmployeeId }).ToList();
            if (admins.Count == 0) return;

            string author = c.ExecuteScalar<string>(
                "SELECT COALESCE(CONCAT(first_name,' ',last_name), '') FROM employees WHERE id=@Id",
                new { Id = CurrentEmployeeId }) ?? "";
            string what = kind == "IMPROVEMENT" ? "Nuova richiesta di miglioramento" : "Nuova segnalazione bug";

            _notif.Create(
                "BUG_REPORT",
                severity == "HIGH" ? "ALARM" : "INFO",
                $"{what}: {title}",
                string.IsNullOrWhiteSpace(author) ? title : $"{title} — segnalata da {author}",
                "BUG_REPORT", bugId, null,
                CurrentEmployeeId > 0 ? CurrentEmployeeId : null,
                admins);
        }
        catch
        {
            // La segnalazione è già salvata: la notifica mancata non la deve annullare.
        }
    }

    private void TryDeleteFile(string storedName)
    {
        try
        {
            string fullPath = Path.Combine(AttachmentsDir, storedName);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }
        catch
        {
            // File già rimosso o in uso: la riga a DB è comunque sparita.
        }
    }

    private static string NormalizeKind(string? s) =>
        (s ?? "").ToUpperInvariant() == "IMPROVEMENT" ? "IMPROVEMENT" : "BUG";

    private static string NormalizeSeverity(string? s) =>
        (s ?? "").ToUpperInvariant() switch
        {
            "LOW" => "LOW",
            "HIGH" => "HIGH",
            _ => "MEDIUM"
        };

    private static string NormalizeStatus(string? s) =>
        (s ?? "").ToUpperInvariant() switch
        {
            "IN_PROGRESS" => "IN_PROGRESS",
            "RESOLVED" => "RESOLVED",
            "REJECTED" => "REJECTED",
            _ => "OPEN"
        };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
