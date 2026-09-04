using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.RisorseSync;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Hubs;
using MySqlConnector;

namespace ATEC.PM.Server.Controllers;

// API del modulo Gestione Risorse: allocazioni (op/flex/ferie) su dipendenti, con conflitti.
// Lettura: [RequireFeature("nav.risorse")], cioe' chi ha la voce di menu (basta il livello READ:
// i 26 che ce l'hanno in lettura continuano a guardare il planner). Prima era "tutti gli
// autenticati": la voce si poteva negare a qualcuno e lui leggeva lo stesso il planner dall'API
// - il menu spariva, il dato no. Scrittura: gating per chiave sulla persona con
// [RequireFeature("resources.edit")] (seminata a RESP_REPARTO/PM/ADMIN), che copre anche il digest
// manuale; la configurazione del digest ha la sua, [RequireFeature("nav.digest_email")].
// Le modifiche registrano autore+timestamp (audit collaborazione multi-utente)
// e vengono notificate in real-time agli altri client via ResourcePlannerHub.
// Ogni scrittura riuscita sulle allocazioni sveglia anche il motore di sincronizzazione col
// VPS (_sync.Trigger("pm"), PIANO-SYNC-RISORSE.md §4.1): la modifica arriva di là in 1-2 s.
[ApiController]
[Route("api/resource-planner")]
[Authorize]
public class ResourcesController : ControllerBase
{
    private readonly ResourcesDbService _rdb;
    private readonly IHubContext<ResourcePlannerHub> _hub;
    private readonly PlanNotificationService _notify;
    private readonly RisorseSyncService _sync;
    private readonly AllocazioniCampanella _campanella;
    public ResourcesController(ResourcesDbService rdb, IHubContext<ResourcePlannerHub> hub,
        PlanNotificationService notify, RisorseSyncService sync, AllocazioniCampanella campanella)
    {
        _rdb = rdb;
        _hub = hub;
        _notify = notify;
        _sync = sync;
        _campanella = campanella;
    }

    // Id dipendente/utente dal token (claim NameIdentifier), per l'audit "ultima modifica".
    private int CallerId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    // Notifica gli altri client (escluso l'autore, che ha già aggiornato la sua vista in locale).
    // Fire-and-forget: la notifica non deve bloccare né far fallire la risposta HTTP.
    private void NotifyChange(string? conn, string action, params int[] ids)
    {
        IClientProxy target = string.IsNullOrEmpty(conn) ? _hub.Clients.All : _hub.Clients.AllExcept(conn);
        target.SendAsync("AssignmentsChanged", new ResAssignmentChange { Action = action, Ids = ids.ToList() }).SenzaAttesa("AssignmentsChanged");
    }

    // ═══════════════════════════════════════════════════════
    // ALLOCAZIONI
    // ═══════════════════════════════════════════════════════

    [HttpGet("assignments")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetAssignments([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        try
        {
            using var c = _rdb.Open();
            string where = "";
            if (from.HasValue && to.HasValue) where = "WHERE a.data_fine >= @From AND a.data_inizio <= @To";
            List<ResAssignmentDto> rows = c.Query<ResAssignmentDto>($@"
                SELECT a.id AS Id, a.employee_id AS EmployeeId,
                       CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                       a.tipo AS Tipo, a.data_inizio AS DataInizio, a.data_fine AS DataFine,
                       a.project_id AS ProjectId, p.code AS ProjectCode, p.title AS ProjectTitle,
                       a.service_id AS ServiceId, s.cod AS ServiceCod,
                       a.other_activity_id AS OtherActivityId, o.descrizione AS OtherActivityDesc,
                       a.descrizione AS Descrizione,
                       a.updated_by AS UpdatedBy,
                       CONCAT_WS(' ', ub.first_name, ub.last_name) AS UpdatedByName,
                       a.updated_at AS UpdatedAt
                FROM res_assignments a
                JOIN employees e ON e.id = a.employee_id
                LEFT JOIN projects p ON p.id = a.project_id
                LEFT JOIN res_services s ON s.id = a.service_id
                LEFT JOIN res_other_activities o ON o.id = a.other_activity_id
                LEFT JOIN employees ub ON ub.id = a.updated_by
                {where}
                ORDER BY e.last_name, e.first_name, a.data_inizio",
                new { From = from, To = to }).ToList();

            MarkConflicts(rows);
            return Ok(ApiResponse<List<ResAssignmentDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ResAssignmentDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("assignments")]
    [RequireFeature("resources.edit")]
    public IActionResult CreateAssignment([FromBody] ResAssignmentCreateRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            if (req.EmployeeIds == null || req.EmployeeIds.Count == 0)
                return Ok(ApiResponse<int>.Fail("Seleziona almeno una risorsa"));
            if (req.DataFine.Date < req.DataInizio.Date)
                return Ok(ApiResponse<int>.Fail("La data fine non può precedere la data inizio"));
            string tipo = NormTipo(req.Tipo);
            // Invariante: le FERIE non hanno commessa/service/altra attività.
            (int? projectId, int? serviceId, int? otherId) = StripAssocIfFerie(tipo, req.ProjectId, req.ServiceId, req.OtherActivityId);

            int caller = CallerId();
            int? autore = caller > 0 ? caller : null;
            var creati = new List<int>();
            var eventi = new List<EventoAllocazione>();
            using var c = _rdb.Open();
            foreach (int empId in req.EmployeeIds.Distinct())
            {
                c.Execute(@"
                    INSERT INTO res_assignments
                        (employee_id, tipo, data_inizio, data_fine, project_id, service_id, other_activity_id, descrizione, updated_by, updated_at)
                    VALUES (@EmployeeId, @Tipo, @DataInizio, @DataFine, @ProjectId, @ServiceId, @OtherActivityId, @Descrizione, @UpdatedBy, NOW())",
                    new { EmployeeId = empId, Tipo = tipo, req.DataInizio, req.DataFine,
                          ProjectId = projectId, ServiceId = serviceId, OtherActivityId = otherId, req.Descrizione,
                          UpdatedBy = autore });
                int id = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
                creati.Add(id);
                eventi.Add(new EventoAllocazione("creata", id,
                    PerCampanella(empId, tipo, req.DataInizio, req.DataFine, projectId, serviceId, otherId, req.Descrizione), null, autore, "pm"));
            }
            NotifyChange(conn, "create", creati.ToArray());
            _sync.Trigger("pm");
            // Campanella (#148): al dipendente assegnato, mai a chi assegna. Non solleva.
            _campanella.Segnala(eventi);
            return Ok(ApiResponse<int>.Ok(creati.Count, $"{creati.Count} allocazioni create"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("assignments/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult UpdateAssignment(int id, [FromBody] ResAssignmentUpdateRequest req, [FromQuery] string? conn = null)
    {
        try
        {
            if (req.EmployeeId <= 0) return Ok(ApiResponse<int>.Fail("Risorsa obbligatoria"));
            if (req.DataFine.Date < req.DataInizio.Date)
                return Ok(ApiResponse<int>.Fail("La data fine non può precedere la data inizio"));
            string tipo = NormTipo(req.Tipo);
            // Invariante: le FERIE non hanno commessa/service/altra attività.
            (int? projectId, int? serviceId, int? otherId) = StripAssocIfFerie(tipo, req.ProjectId, req.ServiceId, req.OtherActivityId);

            using var c = _rdb.Open();

            // Concorrenza ottimistica (rete di sicurezza anche con real-time): se il client invia
            // la versione vista (ExpectedUpdatedAt) e nel frattempo la riga è cambiata → 409, niente lost update.
            if (req.ExpectedUpdatedAt.HasValue)
            {
                DateTime? current = c.ExecuteScalar<DateTime?>(
                    "SELECT updated_at FROM res_assignments WHERE id=@Id", new { Id = id });
                if (current.HasValue && Math.Abs((current.Value - req.ExpectedUpdatedAt.Value).TotalSeconds) > 1)
                    return Conflict(ApiResponse<int>.Fail(
                        "Allocazione modificata da un altro utente nel frattempo. Ricarica e riprova."));
            }

            int caller = CallerId();
            // Com'era prima, per raccontare al dipendente cosa è cambiato (#148).
            AllocazioneCampanella? prima = LeggiPerCampanella(c, id);
            int rows = c.Execute(@"
                UPDATE res_assignments SET
                    employee_id=@EmployeeId, tipo=@Tipo, data_inizio=@DataInizio, data_fine=@DataFine,
                    project_id=@ProjectId, service_id=@ServiceId, other_activity_id=@OtherActivityId, descrizione=@Descrizione,
                    updated_by=@UpdatedBy, updated_at=NOW()
                 WHERE id=@Id",
                new { req.EmployeeId, Tipo = tipo, req.DataInizio, req.DataFine,
                      ProjectId = projectId, ServiceId = serviceId, OtherActivityId = otherId, req.Descrizione,
                      UpdatedBy = caller > 0 ? caller : (int?)null, Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Allocazione non trovata"));
            NotifyChange(conn, "update", id);
            _sync.Trigger("pm");
            // Campanella (#148): solo se è cambiato qualcosa di visibile (un salvataggio identico non avvisa nessuno).
            AllocazioneCampanella dopo = PerCampanella(req.EmployeeId, tipo, req.DataInizio, req.DataFine, projectId, serviceId, otherId, req.Descrizione);
            if (prima != null && prima != dopo)
                _campanella.Segnala(new[] { new EventoAllocazione("modificata", id, dopo, prima, caller > 0 ? caller : null, "pm") });
            return Ok(ApiResponse<int>.Ok(id, "Allocazione aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("assignments/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult DeleteAssignment(int id, [FromQuery] string? conn = null)
    {
        try
        {
            using var c = _rdb.Open();
            int caller = CallerId();
            // Com'era, per la campanella (#148): dopo la DELETE non c'è più niente da leggere.
            AllocazioneCampanella? prima = LeggiPerCampanella(c, id);

            // Prima di perdere la riga: registra chi cancella + lo stato originale, per il digest
            // email (la DELETE la fa sparire da res_assignments, "chi l'ha fatto" andrebbe perso).
            // Traccia e DELETE nella stessa transazione (F1): o entrambe o nessuna.
            using var tx = c.BeginTransaction();
            c.Execute(@"
                INSERT INTO res_notify_pending
                    (assignment_id, made_by, action, orig_employee_id, orig_tipo, orig_data_inizio, orig_data_fine,
                     orig_project_id, orig_service_id, orig_other_activity_id, orig_descrizione)
                SELECT id, @MadeBy, 'delete', employee_id, tipo, data_inizio, data_fine,
                       project_id, service_id, other_activity_id, descrizione
                FROM res_assignments WHERE id=@Id
                ON DUPLICATE KEY UPDATE made_by=VALUES(made_by), touched_at=NOW()",
                new { Id = id, MadeBy = caller }, tx);

            int rows = c.Execute("DELETE FROM res_assignments WHERE id=@Id", new { Id = id }, tx);
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Allocazione non trovata"));
            tx.Commit();
            NotifyChange(conn, "delete", id);
            _sync.Trigger("pm");
            if (prima != null)
                _campanella.Segnala(new[] { new EventoAllocazione("rimossa", id, prima, null, caller > 0 ? caller : null, "pm") });
            return Ok(ApiResponse<bool>.Ok(true, "Allocazione eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // ANAGRAFICA SERVICE (Syncorgest)
    // ═══════════════════════════════════════════════════════

    [HttpGet("services")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetServices()
    {
        try
        {
            using var c = _rdb.Open();
            var rows = c.Query<ResServiceDto>(@"
                SELECT id AS Id, cod AS Cod, cliente AS Cliente, is_active AS IsActive
                FROM res_services WHERE is_active = 1 ORDER BY cod").ToList();
            return Ok(ApiResponse<List<ResServiceDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ResServiceDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("services")]
    [RequireFeature("resources.edit")]
    public IActionResult CreateService([FromBody] ResServiceSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Cod)) return Ok(ApiResponse<int>.Fail("Codice obbligatorio"));
            using var c = _rdb.Open();
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO res_services (cod, cliente, is_active) VALUES (@Cod, @Cliente, 1);
                SELECT LAST_INSERT_ID()", new { Cod = req.Cod.Trim(), req.Cliente });
            return Ok(ApiResponse<int>.Ok(id, "Service creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("services/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult UpdateService(int id, [FromBody] ResServiceSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Cod)) return Ok(ApiResponse<int>.Fail("Codice obbligatorio"));
            using var c = _rdb.Open();
            int rows = c.Execute("UPDATE res_services SET cod=@Cod, cliente=@Cliente WHERE id=@Id",
                new { Cod = req.Cod.Trim(), req.Cliente, Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Service non trovato"));
            return Ok(ApiResponse<int>.Ok(id, "Service aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("services/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult DeleteService(int id)
    {
        try
        {
            using var c = _rdb.Open();
            // Soft-delete: le allocazioni che lo referenziano restano (FK SET NULL non scatta su is_active).
            int rows = c.Execute("UPDATE res_services SET is_active = 0 WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Service non trovato"));
            return Ok(ApiResponse<bool>.Ok(true, "Service eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // ANAGRAFICA ALTRE ATTIVITÀ
    // ═══════════════════════════════════════════════════════

    [HttpGet("others")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetOthers()
    {
        try
        {
            using var c = _rdb.Open();
            var rows = c.Query<ResOtherActivityDto>(@"
                SELECT id AS Id, descrizione AS Descrizione, is_active AS IsActive
                FROM res_other_activities WHERE is_active = 1 ORDER BY descrizione").ToList();
            return Ok(ApiResponse<List<ResOtherActivityDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<ResOtherActivityDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPost("others")]
    [RequireFeature("resources.edit")]
    public IActionResult CreateOther([FromBody] ResOtherActivitySaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Descrizione)) return Ok(ApiResponse<int>.Fail("Descrizione obbligatoria"));
            using var c = _rdb.Open();
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO res_other_activities (descrizione, is_active) VALUES (@Descrizione, 1);
                SELECT LAST_INSERT_ID()", new { Descrizione = req.Descrizione.Trim() });
            return Ok(ApiResponse<int>.Ok(id, "Attività creata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("others/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult UpdateOther(int id, [FromBody] ResOtherActivitySaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Descrizione)) return Ok(ApiResponse<int>.Fail("Descrizione obbligatoria"));
            using var c = _rdb.Open();
            int rows = c.Execute("UPDATE res_other_activities SET descrizione=@Descrizione WHERE id=@Id",
                new { Descrizione = req.Descrizione.Trim(), Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Attività non trovata"));
            return Ok(ApiResponse<int>.Ok(id, "Attività aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("others/{id}")]
    [RequireFeature("resources.edit")]
    public IActionResult DeleteOther(int id)
    {
        try
        {
            using var c = _rdb.Open();
            int rows = c.Execute("UPDATE res_other_activities SET is_active = 0 WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Attività non trovata"));
            return Ok(ApiResponse<bool>.Ok(true, "Attività eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // LOOKUP (risorse + commesse) per le combo del client
    // ═══════════════════════════════════════════════════════

    [HttpGet("lookups/resources")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetResourceLookups()
    {
        try
        {
            using var c = _rdb.Open();
            // Solo persone reali (stessa query di GET /api/employees/real).
            var rows = c.Query<LookupItem>(EmployeeLookupQueries.RealEmployeesSql).ToList();
            return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<LookupItem>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("lookups/projects")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetProjectLookups()
    {
        try
        {
            using var c = _rdb.Open();
            // Solo commesse ATTIVE: seguono la filiera offerta-accettata → conversione-in-commessa,
            // quindi rappresentano già il lavoro reale in corso (bozze/completate/sospese escluse).
            var rows = c.Query<LookupItem>($@"
                SELECT id AS Id, CONCAT_WS(' — ', code, title) AS Name
                FROM projects WHERE status = 'ACTIVE'
                ORDER BY {ProjectSorting.OrderBy("")}").ToList();
            return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<LookupItem>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // DIGEST EMAIL — riepilogo modifiche piano (dipendente/responsabile/PM)
    // ═══════════════════════════════════════════════════════

    // Badge toolbar: conteggio modifiche pendenti dall'ultima istantanea (nessun invio).
    [HttpGet("notify/pending")]
    [RequireFeature("resources.edit")]
    public IActionResult GetNotifyPending()
    {
        try
        {
            // Con la sincronizzazione col VPS attiva (accesa E configurata: la stessa condizione
            // che fa girare il motore) a notificare i dipendenti è il VPS (PIANO-SYNC-RISORSE.md
            // §7, decisione 6): il badge di PM tace e non si calcola niente (la foto la tiene
            // allineata al piano il motore, a ogni giro che scrive). Accesa ma incompleta = motore
            // a riposo, nessuno notifica: il badge torna a contare come a sincronizzazione spenta.
            if (_sync.IsAttiva)
                return Ok(ApiResponse<NotifyPendingDto>.Ok(new NotifyPendingDto { TotalChanges = 0, EmailConfigurata = false, Employees = new() }));
            return Ok(ApiResponse<NotifyPendingDto>.Ok(_notify.ComputePending()));
        }
        catch (Exception ex) { return Ok(ApiResponse<NotifyPendingDto>.Fail($"Errore: {ex.Message}")); }
    }

    // Anteprima completa (nessun invio, nessuna nuova istantanea).
    [HttpGet("digest/preview")]
    [Authorize]
    [RequireFeature("resources.edit")]
    public IActionResult PreviewDigest()
    {
        try { return Ok(ApiResponse<DigestPreviewDto>.Ok(_notify.PreviewDigest())); }
        catch (Exception ex) { return Ok(ApiResponse<DigestPreviewDto>.Fail($"Errore: {ex.Message}")); }
    }

    // Anteprima selettiva per il dialog "Notifica subito" (una riga spuntabile per modifica).
    [HttpGet("digest/preview-selective")]
    [Authorize]
    [RequireFeature("resources.edit")]
    public IActionResult PreviewSelective()
    {
        try { return Ok(ApiResponse<SelectivePreviewDto>.Ok(_notify.BuildSelectivePreview())); }
        catch (Exception ex) { return Ok(ApiResponse<SelectivePreviewDto>.Fail($"Errore: {ex.Message}")); }
    }

    // Esecuzione digest immediata ("Esegui ora"): non tocca digest_last_run, il giro
    // automatico del giorno resta indipendente.
    [HttpPost("digest/run-now")]
    [Authorize]
    [RequireFeature("resources.edit")]
    public IActionResult RunDigestNow()
    {
        try { return Ok(ApiResponse<NotifySendResultDto>.Ok(_notify.SendDigest("manuale"))); }
        catch (Exception ex) { return Ok(ApiResponse<NotifySendResultDto>.Fail($"Errore: {ex.Message}")); }
    }

    // Invio selettivo dal dialog "Notifica subito": solo le variazioni scelte.
    [HttpPost("digest/send-selected")]
    [Authorize]
    [RequireFeature("resources.edit")]
    public IActionResult SendSelected([FromBody] SendSelectedRequest req)
    {
        try
        {
            if (req.AssignmentIds == null || req.AssignmentIds.Count == 0)
                return Ok(ApiResponse<NotifySendResultDto>.Fail("Nessuna modifica selezionata"));
            return Ok(ApiResponse<NotifySendResultDto>.Ok(_notify.SendSelected(req.AssignmentIds, "manuale")));
        }
        catch (Exception ex) { return Ok(ApiResponse<NotifySendResultDto>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("digest/settings")]
    [Authorize]
    [RequireFeature("nav.digest_email")]
    public IActionResult GetDigestSettings()
    {
        try { return Ok(ApiResponse<PlanDigestSettingsDto>.Ok(_notify.GetSettings())); }
        catch (Exception ex) { return Ok(ApiResponse<PlanDigestSettingsDto>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("digest/settings")]
    [Authorize]
    [RequireFeature("nav.digest_email")]
    public IActionResult SaveDigestSettings([FromBody] PlanDigestSettingsDto dto)
    {
        try
        {
            _notify.SaveSettings(dto);
            return Ok(ApiResponse<bool>.Ok(true, "Impostazioni digest salvate"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("digest/status")]
    [Authorize]
    [RequireFeature("nav.digest_email")]
    public IActionResult GetDigestStatus()
    {
        try { return Ok(ApiResponse<DigestStatusDto>.Ok(_notify.GetStatus())); }
        catch (Exception ex) { return Ok(ApiResponse<DigestStatusDto>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // SINCRONIZZAZIONE CON ATEC RISORSE (VPS) — pannello admin
    // Stessa chiave del digest (nav.digest_email): è la pagina dove vive il riquadro.
    // ═══════════════════════════════════════════════════════

    [HttpGet("sync/settings")]
    [RequireFeature("nav.digest_email")]
    public IActionResult GetSyncSettings()
    {
        try { return Ok(ApiResponse<RisorseSyncSettingsDto>.Ok(_sync.GetSettingsDto())); }
        catch (Exception ex) { return Ok(ApiResponse<RisorseSyncSettingsDto>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("sync/settings")]
    [RequireFeature("nav.digest_email")]
    public IActionResult SaveSyncSettings([FromBody] RisorseSyncSettingsDto dto)
    {
        try
        {
            string baseUrl = (dto.BaseUrl ?? "").Trim();
            // La regola sull'indirizzo (URL assoluto, http solo in LAN) sta in un posto solo: la usa anche il «Prova».
            string? erroreIndirizzo = RisorseSyncSettings.ErroreIndirizzo(baseUrl);
            if (erroreIndirizzo != null)
                return Ok(ApiResponse<bool>.Fail(erroreIndirizzo));
            if (dto.Enabled && string.IsNullOrEmpty(baseUrl))
                return Ok(ApiResponse<bool>.Fail("Per accendere la sincronizzazione serve l'indirizzo del VPS"));
            if (dto.Enabled && string.IsNullOrWhiteSpace(dto.Username))
                return Ok(ApiResponse<bool>.Fail("Per accendere la sincronizzazione serve l'utente di servizio"));
            if (dto.Enabled && string.IsNullOrEmpty(dto.Password) && !_sync.GetSettingsDto().HasPassword)
                return Ok(ApiResponse<bool>.Fail("Per accendere la sincronizzazione serve la password dell'utente di servizio"));

            _sync.SaveSettings(dto);
            // Le impostazioni nuove valgono subito: un giro (o lo spegnimento) senza aspettare il timer.
            _sync.Trigger("impostazioni");
            return Ok(ApiResponse<bool>.Ok(true, "Impostazioni sincronizzazione salvate"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // «Prova»: login + stato del VPS con le impostazioni salvate, senza toccare il registro.
    [HttpPost("sync/test")]
    [RequireFeature("nav.digest_email")]
    public async Task<IActionResult> TestSync()
    {
        try
        {
            // Niente RequestAborted: la prova appartiene al servizio, non alla richiesta HTTP.
            SyncStatusDto stato = await _sync.TestAsync();
            return Ok(ApiResponse<SyncStatusDto>.Ok(stato,
                $"VPS raggiunto: {stato.Employees} dipendenti, {stato.Assignments} allocazioni, {stato.Projects} commesse"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<SyncStatusDto>.Fail(RisorseSyncService.MessaggioLeggibile(ex)));
        }
    }

    [HttpGet("sync/status")]
    [RequireFeature("nav.digest_email")]
    public IActionResult GetSyncStatus()
    {
        try { return Ok(ApiResponse<RisorseSyncStatusDto>.Ok(_sync.GetStatus())); }
        catch (Exception ex) { return Ok(ApiResponse<RisorseSyncStatusDto>.Fail($"Errore: {ex.Message}")); }
    }

    // #147: l'avviso nel planner «ATEC Risorse (VPS) non risponde». Chiave della PAGINA
    // (nav.risorse), non del pannello admin: lo deve vedere chiunque abbia il planner davanti.
    [HttpGet("sync/salute")]
    [RequireFeature("nav.risorse")]
    public IActionResult GetSyncSalute()
    {
        try { return Ok(ApiResponse<RisorseSyncSaluteDto>.Ok(_sync.GetSalute())); }
        catch (Exception ex) { return Ok(ApiResponse<RisorseSyncSaluteDto>.Fail($"Errore: {ex.Message}")); }
    }

    // «Esegui ora»: un giro adesso (aspetta quello eventualmente in corso) e ne ritorna l'esito.
    [HttpPost("sync/run-now")]
    [RequireFeature("nav.digest_email")]
    public async Task<IActionResult> RunSyncNow()
    {
        try
        {
            // Niente RequestAborted: il giro appartiene al servizio (token dell'host), chi chiude
            // il browser a metà non lo interrompe.
            RisorseSyncLogEntry esito = await _sync.RunNowAsync("manuale");
            // La riga di registro torna SEMPRE (anche a giro fallito: il pannello la mostra);
            // Success dice com'è andata.
            return Ok(new ApiResponse<RisorseSyncLogEntry>
            {
                Success = esito.Esito == "ok",
                Data = esito,
                Message = esito.Dettaglio ?? (esito.Esito == "ok" ? "Giro completato" : "Giro fallito"),
            });
        }
        catch (Exception ex) { return Ok(ApiResponse<RisorseSyncLogEntry>.Fail(RisorseSyncService.MessaggioLeggibile(ex))); }
    }

    // ═══════════════════════════════════════════════════════
    // Helper
    // ═══════════════════════════════════════════════════════

    private static string NormTipo(string t) => t is "OP" or "FLEX" or "FERIE" ? t : "OP";

    // ── Campanella (#148): la riga nella forma che serve ai testi ──
    private static AllocazioneCampanella PerCampanella(int employeeId, string tipo, DateTime inizio, DateTime fine,
        int? projectId, int? serviceId, int? otherId, string? descrizione) =>
        new(employeeId, tipo, DateOnly.FromDateTime(inizio.Date), DateOnly.FromDateTime(fine.Date),
            projectId, serviceId, otherId, AllocazioniSync.NormalizzaDescrizione(descrizione));

    private static AllocazioneCampanella? LeggiPerCampanella(MySqlConnection c, int id)
    {
        List<(int EmployeeId, string Tipo, DateTime DataInizio, DateTime DataFine, int? ProjectId, int? ServiceId, int? OtherActivityId, string? Descrizione)> righe =
            c.Query<(int, string, DateTime, DateTime, int?, int?, int?, string?)>(@"
                SELECT employee_id, tipo, data_inizio, data_fine, project_id, service_id, other_activity_id, descrizione
                FROM res_assignments WHERE id = @Id", new { Id = id }).ToList();
        if (righe.Count == 0) return null;
        var r = righe[0];
        return PerCampanella(r.EmployeeId, NormTipo(r.Tipo), r.DataInizio, r.DataFine, r.ProjectId, r.ServiceId, r.OtherActivityId, r.Descrizione);
    }

    // Le ferie non si agganciano a commessa/service/altra attività: azzera le associazioni.
    private static (int? ProjectId, int? ServiceId, int? OtherId) StripAssocIfFerie(
        string tipo, int? projectId, int? serviceId, int? otherId) =>
        tipo == "FERIE" ? (null, null, null) : (projectId, serviceId, otherId);

    // Marca le allocazioni in conflitto: per la stessa risorsa, intervalli sovrapposti con
    // combinazione vietata (OP-OP, oppure FERIE contro OP/FLEX). Ammessi: OP-FLEX, FLEX-FLEX, FERIE-FERIE.
    private static void MarkConflicts(List<ResAssignmentDto> rows)
    {
        foreach (var grp in rows.GroupBy(r => r.EmployeeId))
        {
            List<ResAssignmentDto> list = grp.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (Overlap(list[i], list[j]) && Forbidden(list[i].Tipo, list[j].Tipo))
                    {
                        list[i].HasConflict = true;
                        list[j].HasConflict = true;
                    }
        }
    }

    private static bool Overlap(ResAssignmentDto a, ResAssignmentDto b) =>
        a.DataInizio.Date <= b.DataFine.Date && b.DataInizio.Date <= a.DataFine.Date;

    private static bool Forbidden(string t1, string t2)
    {
        // FLEX non va mai in conflitto con nulla. Tutto il resto (OP e FERIE, in qualsiasi
        // combinazione — INCLUSO FERIE+FERIE) confligge se si sovrappone. Regola allineata al
        // programma Gestione Risorse (PlannerLogic.Forbidden) da cui è portato il modulo web.
        if (t1 == "FLEX" || t2 == "FLEX") return false;
        return true;
    }
}
