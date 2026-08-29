using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/hr")]
[Authorize]
[RequireFeature("nav.hr_timbrature", "nav.hr_richieste")]
public class HrController : ControllerBase
{
    private readonly HrAttendanceService _attendance;
    private readonly EcosClient _ecos;
    private readonly FeatureAccessService _access;
    private readonly EmailService _email;

    public HrController(
        HrAttendanceService attendance, EcosClient ecos, FeatureAccessService access, EmailService email)
    {
        _attendance = attendance;
        _ecos = ecos;
        _access = access;
        _email = email;
    }

    private int MeId
    {
        get
        {
            _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id);
            return id;
        }
    }

    private string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    private bool IsAdmin => string.Equals(Role, "ADMIN", StringComparison.OrdinalIgnoreCase);

    private bool CanManageTimbrature => _access.CanWriteUser(MeId, Role, "nav.hr_timbrature");

    private bool CanManageRichieste => _access.CanWriteUser(MeId, Role, "nav.hr_richieste");

    /// <summary>
    /// Il nome che firma il sollecito, come <c>SettingsManager.GetSmtpFrom</c> nell'originale:
    /// il mittente configurato. Vuoto se è un indirizzo email o se manca — in quel caso resta
    /// la sola riga «Ufficio Risorse Umane — ATEC S.r.l.».
    /// </summary>
    private string FirmaMail()
    {
        string nome = _email.ResolveConfig().FromName ?? "";
        return string.IsNullOrWhiteSpace(nome) || nome.Contains('@') ? "" : nome.Trim();
    }

    // ── CARTELLINO / TIMESHEET ────────────────────────────────────────────────

    [HttpGet("timesheet")]
    public IActionResult Timesheet([FromQuery] int year, [FromQuery] int month, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        int targetId = employeeId ?? MeId;
        if (targetId != MeId && !CanManageTimbrature)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Puoi vedere solo il tuo cartellino."));
        }

        return Ok(ApiResponse<HrMonthlyTimesheetDto>.Ok(
            _attendance.GetMonthlyTimesheet(targetId, year, month)));
    }

    /// <summary>
    /// Il cartellino della persona aperta in Excel (voce 7 del port). Stessa guardia di
    /// <see cref="Timesheet"/>: il proprio si esporta sempre, quello altrui richiede la
    /// scrittura su Timbrature.
    /// </summary>
    [HttpGet("timesheet/export")]
    public IActionResult TimesheetExport(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        int targetId = employeeId ?? MeId;
        if (targetId != MeId && !CanManageTimbrature)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Puoi esportare solo il tuo cartellino."));
        }

        HrMonthlyTimesheetDto cartellino = _attendance.GetMonthlyTimesheet(targetId, year, month);
        (byte[] contenuto, string nomeFile) = HrTimesheetExcel.Genera(cartellino);

        return File(contenuto,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nomeFile);
    }

    // ── SOLLECITO DELLA SINGOLA GIORNATA (PIANO-HR-PORT-ORIGINALE.md, voce 1) ─
    //
    // Il pulsante 📧 sulla riga del cartellino, port di btnMailDipendente_Click. Si tocca il
    // cartellino di un'altra persona e si scrive una mail a suo nome: serve la scrittura.

    /// <summary>Il sollecito pronto per una giornata: testo integrale e stato «già chiesto».</summary>
    [HttpGet("day-reminder")]
    public IActionResult DayReminder([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("I solleciti richiedono la scrittura su Timbrature."));

        if (employeeId <= 0) return Ok(ApiResponse<string>.Fail("Dipendente non indicato."));

        HrDayReminderDto sollecito = _attendance.GetDayReminder(employeeId, date, FirmaMail());
        sollecito.SmtpEnabled = _email.Enabled;
        return Ok(ApiResponse<HrDayReminderDto>.Ok(sollecito));
    }

    /// <summary>
    /// Spedisce il sollecito della giornata e lo registra. Con <c>channel = "MAILTO"</c> la
    /// mail l'ha aperta l'utente nel client di posta e qui si registra soltanto: è la stessa
    /// coppia di strade del sollecito mensile, e serve quando l'SMTP non è configurato.
    /// </summary>
    [HttpPost("day-reminder")]
    public IActionResult SendDayReminder([FromBody] HrDayReminderRequest req)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("I solleciti richiedono la scrittura su Timbrature."));

        if (req.EmployeeId <= 0) return Ok(ApiResponse<string>.Fail("Dipendente non indicato."));

        bool mailto = string.Equals(req.Channel, "MAILTO", StringComparison.OrdinalIgnoreCase);

        HrDayReminderDto sollecito = _attendance.GetDayReminder(req.EmployeeId, req.Date, FirmaMail());
        if (!sollecito.CanRemind)
            return Ok(ApiResponse<bool>.Fail("Questa giornata non ha anomalie da segnalare."));
        if (string.IsNullOrWhiteSpace(sollecito.Email))
            return Ok(ApiResponse<bool>.Fail($"Nessuna email configurata per {sollecito.EmployeeName}."));

        if (!mailto)
        {
            if (!_email.Enabled)
                return Ok(ApiResponse<bool>.Fail(
                    "SMTP non configurato: il sollecito si può solo aprire nel client di posta."));

            string htmlBody = System.Net.WebUtility.HtmlEncode(sollecito.Body).Replace("\n", "<br>\n");
            if (!_email.QueueSimpleMail(
                    sollecito.Email, sollecito.EmployeeName, sollecito.Subject, sollecito.Body, htmlBody))
            {
                // 🪤 Nell'originale l'esito dell'invio non veniva controllato: si scriveva nel
                // MailLog e si diceva «Email inviata» anche a SMTP rotto.
                return Ok(ApiResponse<bool>.Fail("Invio non riuscito: la mail non è stata accodata."));
            }
        }

        _attendance.MarkDayReminder(
            req.EmployeeId, req.Date, sollecito.Email, sollecito.Subject, sollecito.Body,
            MeId, mailto ? "MAILTO" : "SMTP");

        return Ok(ApiResponse<bool>.Ok(true, mailto
            ? "Sollecito aperto nel client di posta e registrato."
            : $"Sollecito inviato a {sollecito.Email}."));
    }

    // ── CALENDARIO MENSILE ────────────────────────────────────────────────────
    //
    // Il calendario è l'azienda intera, cartellino per cartellino: con la sola LETTURA si
    // vede il proprio cartellino e basta (§Fase 1 del piano), quindi qui serve la scrittura.

    [HttpGet("calendar")]
    public IActionResult Calendar([FromQuery] int year, [FromQuery] int month, [FromQuery] int? departmentId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Il calendario di tutti richiede la scrittura su Timbrature."));

        return Ok(ApiResponse<HrMonthlyCalendarDto>.Ok(
            _attendance.GetMonthlyCalendar(year, month, departmentId)));
    }

    /// <summary>
    /// Il calendario in Excel, con la stessa forma della pagina (e dell'originale VB):
    /// intestazioni dei giorni, colori delle celle, totali, riquadri bloccati.
    /// </summary>
    [HttpGet("calendar/export")]
    public IActionResult CalendarExport(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] int? departmentId, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("L'export del calendario richiede la scrittura su Timbrature."));

        HrMonthlyCalendarDto calendario = _attendance.GetMonthlyCalendar(year, month, departmentId);
        (byte[] contenuto, string nomeFile) = HrCalendarExcel.Genera(calendario, employeeId);

        return File(contenuto,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nomeFile);
    }

    // ── #132 GIUSTIFICAZIONE DELLE ORE MANCANTI ───────────────────────────────
    //
    // Il clic sulla cella del calendario. Stessa guardia del calendario: si tocca il
    // cartellino di un'altra persona, quindi serve la scrittura su Timbrature.

    [HttpGet("calendar/giustifica")]
    public IActionResult GiustificaInfo([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Giustificare le ore richiede la scrittura su Timbrature."));

        if (employeeId <= 0) return Ok(ApiResponse<string>.Fail("Dipendente non indicato."));

        return Ok(ApiResponse<HrGiustificaInfoDto>.Ok(_attendance.GetGiustificaInfo(employeeId, date)));
    }

    [HttpPost("calendar/giustifica")]
    public IActionResult Giustifica([FromBody] HrGiustificaRequest req)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Giustificare le ore richiede la scrittura su Timbrature."));

        if (req.EmployeeId <= 0) return Ok(ApiResponse<bool>.Fail("Dipendente non indicato."));

        string? errore = _attendance.SaveGiustifica(req, MeId);
        return Ok(errore == null
            ? ApiResponse<bool>.Ok(true, string.IsNullOrWhiteSpace(req.Causale)
                ? "Causale rimossa"
                : "Causale registrata")
            : ApiResponse<bool>.Fail(errore));
    }

    // ── CREDENZIALI ECOS ──────────────────────────────────────────────────────
    //
    // Come il dialogo «Configurazione Credenziali» del programma originale: utente,
    // password e ClientID si mettono da qui invece che a mano nell'appsettings del server.
    // La password è write-only — esce solo il fatto che ci sia.

    [HttpGet("ecos/settings")]
    public IActionResult EcosSettings()
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Le credenziali Ecos richiedono la scrittura su Timbrature."));

        return Ok(ApiResponse<HrEcosSettingsDto>.Ok(_ecos.LeggiCredenziali()));
    }

    [HttpPost("ecos/settings")]
    public IActionResult SaveEcosSettings([FromBody] HrEcosSettingsDto dto)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Le credenziali Ecos richiedono la scrittura su Timbrature."));

        if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.ClientId))
            return Ok(ApiResponse<string>.Fail("Utente e Client ID sono obbligatori."));

        try
        {
            _ecos.SalvaCredenziali(dto);
            return Ok(ApiResponse<HrEcosSettingsDto>.Ok(
                _ecos.LeggiCredenziali(), "Credenziali Ecos salvate."));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<string>.Fail($"Salvataggio non riuscito: {ex.Message}"));
        }
    }

    /// <summary>
    /// Prova le credenziali con una TokenGet e basta: nessun dato viene letto né scritto,
    /// così si può provare senza far partire un import.
    /// </summary>
    [HttpPost("ecos/settings/test")]
    public async Task<IActionResult> TestEcosSettings()
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Le credenziali Ecos richiedono la scrittura su Timbrature."));

        try
        {
            await _ecos.TokenAsync(HttpContext.RequestAborted);
            return Ok(ApiResponse<HrEcosTestResultDto>.Ok(
                new HrEcosTestResultDto { Ok = true, Message = "Collegamento riuscito: Ecos ha risposto col token." }));
        }
        catch (Exception ex)
        {
            // L'errore di Ecos arriva così com'è: «privilegi insufficienti» e «password
            // sbagliata» si distinguono solo leggendolo.
            return Ok(ApiResponse<HrEcosTestResultDto>.Ok(
                new HrEcosTestResultDto { Ok = false, Message = ex.Message }));
        }
    }

    // ── SOLLECITI TIMBRATURE MANCANTI ─────────────────────────────────────────

    /// <summary>
    /// Chi ha giornate col «?» nel mese, col testo del sollecito già pronto: la pagina lo
    /// mostra prima di spedire, perché un sollecito sbagliato lo legge una persona.
    /// </summary>
    [HttpGet("calendar/reminders")]
    public IActionResult Reminders(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] int? departmentId, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("I solleciti richiedono la scrittura su Timbrature."));

        HrRemindersDto solleciti = _attendance.GetReminders(year, month, departmentId, employeeId);
        solleciti.SmtpEnabled = _email.Enabled;
        return Ok(ApiResponse<HrRemindersDto>.Ok(solleciti));
    }

    /// <summary>Invia i solleciti via SMTP e segna le giornate come già chieste.</summary>
    [HttpPost("calendar/reminders")]
    public IActionResult SendReminders(
        [FromQuery] int year, [FromQuery] int month,
        [FromQuery] int? departmentId, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("I solleciti richiedono la scrittura su Timbrature."));

        if (!_email.Enabled)
            return Ok(ApiResponse<HrRemindersResultDto>.Fail(
                "SMTP non configurato: i solleciti si possono solo aprire nel client di posta."));

        HrRemindersDto solleciti = _attendance.GetReminders(year, month, departmentId, employeeId);
        var esito = new HrRemindersResultDto();
        var inviati = new List<(int EmployeeId, List<int> Days, string? Email, string? Subject, string? Body)>();

        foreach (HrReminderTargetDto t in solleciti.Targets)
        {
            if (string.IsNullOrWhiteSpace(t.Email))
            {
                // Senza indirizzo non è un errore da nascondere: è una persona da avvisare
                // in un altro modo, e chi ha premuto il pulsante deve saperlo.
                esito.WithoutEmail.Add($"{t.EmployeeName} ({t.MissingDays.Count} giorni)");
                continue;
            }

            string htmlBody = System.Net.WebUtility.HtmlEncode(t.Body).Replace("\n", "<br>\n");
            if (_email.QueueSimpleMail(t.Email, t.EmployeeName, t.Subject, t.Body, htmlBody))
            {
                esito.Sent++;
                inviati.Add((t.EmployeeId, t.MissingDays, t.Email, t.Subject, t.Body));
            }
            else
            {
                esito.Failed++;
            }
        }

        if (inviati.Count > 0)
            _attendance.MarkReminders(year, month, inviati, MeId, "SMTP");

        esito.Message = esito.Sent == 0 && esito.WithoutEmail.Count == 0 && esito.Failed == 0
            ? "Nessun sollecito da inviare."
            : $"Solleciti inviati: {esito.Sent}"
              + (esito.Failed > 0 ? $", non riusciti: {esito.Failed}" : "")
              + (esito.WithoutEmail.Count > 0 ? $", senza email: {esito.WithoutEmail.Count}" : "");

        return Ok(ApiResponse<HrRemindersResultDto>.Ok(esito, esito.Message));
    }

    /// <summary>
    /// Segna come sollecitate le giornate delle persone aperte nel client di posta: lì la
    /// mail la spedisce l'utente, il server sa solo che gliel'abbiamo messa davanti.
    /// </summary>
    [HttpPost("calendar/reminders/mark")]
    public IActionResult MarkReminders([FromBody] HrMarkRemindersRequest request)
    {
        if (request.Year < 2020 || request.Year > 2100 || request.Month < 1 || request.Month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("I solleciti richiedono la scrittura su Timbrature."));

        if (request.EmployeeIds.Count == 0)
            return Ok(ApiResponse<bool>.Ok(true, "Nessun sollecito da registrare."));

        HrRemindersDto solleciti = _attendance.GetReminders(request.Year, request.Month, null, null);
        var daSegnare = solleciti.Targets
            .Where(t => request.EmployeeIds.Contains(t.EmployeeId))
            // Dal client di posta parte il MailtoBody: nella Cronologia deve finire il testo
            // che la persona ha davanti, non l'altra variante.
            .Select(t => (t.EmployeeId, t.MissingDays, t.Email, (string?)t.Subject, (string?)t.MailtoBody))
            .ToList();

        _attendance.MarkReminders(request.Year, request.Month, daSegnare, MeId, "MAILTO");
        return Ok(ApiResponse<bool>.Ok(true, $"Registrati {daSegnare.Count} solleciti."));
    }

    // ── CRONOLOGIA EMAIL (PIANO-HR-PORT-ORIGINALE.md, voce 6) ─────────────────

    /// <summary>
    /// Le mail di sollecito già mandate, come la <c>MailLogPage</c> dell'originale. Il mese
    /// è quello del <b>giorno di riferimento</b>, non della spedizione.
    /// </summary>
    [HttpGet("reminders/log")]
    public IActionResult ReminderLog(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? employeeId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La cronologia dei solleciti richiede la scrittura su Timbrature."));

        return Ok(ApiResponse<HrReminderLogDto>.Ok(
            _attendance.GetReminderLog(year, month, employeeId)));
    }

    // ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

    [HttpGet("quadratura")]
    public IActionResult Quadratura([FromQuery] int year, [FromQuery] int month, [FromQuery] int? departmentId)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La quadratura di tutti richiede la scrittura su Timbrature."));

        return Ok(ApiResponse<HrQuadraturaMonthDto>.Ok(
            _attendance.GetQuadratura(year, month, departmentId)));
    }

    // ── STATO & IMPORT ────────────────────────────────────────────────────────

    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Lo stato dell'import richiede la scrittura su Timbrature."));
        return Ok(ApiResponse<HrStatusDto>.Ok(_attendance.GetStatus()));
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromQuery] bool full = false)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("L'avvio dell'import richiede la scrittura su Timbrature."));

        HrImportResultDto result = await _attendance.ImportAsync(full, HttpContext.RequestAborted);
        return Ok(result.Success
            ? ApiResponse<HrImportResultDto>.Ok(result, result.Message)
            : ApiResponse<HrImportResultDto>.Fail(result.Message));
    }

    /// <summary>
    /// Risincronizza da Ecos <b>una giornata</b> di un dipendente (voce 2 del port, port di
    /// <c>btnResyncDay_Click</c>). 🪤 Non sposta il cursore dell'import: è un ripescaggio
    /// mirato, non un avanzamento.
    /// </summary>
    [HttpPost("import/day")]
    public async Task<IActionResult> ImportDay([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La risincronizzazione richiede la scrittura su Timbrature."));

        if (employeeId <= 0) return Ok(ApiResponse<string>.Fail("Dipendente non indicato."));
        if (date.Year < 2020 || date.Year > 2100) return Ok(ApiResponse<string>.Fail("Data non valida."));

        HrImportResultDto result = await _attendance.ImportWindowAsync(
            employeeId, date.Date, date.Date, HttpContext.RequestAborted);

        return Ok(result.Success
            ? ApiResponse<HrImportResultDto>.Ok(result, result.Message)
            : ApiResponse<HrImportResultDto>.Fail(result.Message));
    }

    /// <summary>
    /// Riscarica da Ecos <b>un mese scelto</b> (voce 5 del port). È la strada per rifare un
    /// mese: si riscarica invece di cancellare — l'originale aveva anche un «Cancella Mese»,
    /// che qui non esiste apposta (il grezzo è append-only).
    /// </summary>
    [HttpPost("import/month")]
    public async Task<IActionResult> ImportMonth(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int? employeeId)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La sincronizzazione di un mese richiede la scrittura su Timbrature."));

        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return Ok(ApiResponse<string>.Fail("Mese non valido."));

        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        // Sul mese si riscaricano anche le assenze approvate, come faceva il «Aggiorna
        // Report» dell'originale: il calendario mostra ferie e permessi, e rifare il mese
        // senza di loro lascerebbe a video la situazione vecchia.
        HrImportResultDto result = await _attendance.ImportWindowAsync(
            employeeId, primo, ultimo, HttpContext.RequestAborted, conAssenze: true);

        return Ok(result.Success
            ? ApiResponse<HrImportResultDto>.Ok(result, result.Message)
            : ApiResponse<HrImportResultDto>.Fail(result.Message));
    }

    // ── MAPPATURA DIPENDENTI ↔ ECOS ───────────────────────────────────────────

    [HttpGet("mapping")]
    public IActionResult Mapping()
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La mappatura Ecos richiede la scrittura su Timbrature."));
        return Ok(ApiResponse<List<HrMappingRowDto>>.Ok(_attendance.GetEcosMapping()));
    }

    [HttpGet("mapping/badges")]
    public async Task<IActionResult> Badges()
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La mappatura Ecos richiede la scrittura su Timbrature."));

        if (!_ecos.Configured)
            return Ok(ApiResponse<HrBadgesDto>.Ok(new HrBadgesDto { Configured = false }));

        try
        {
            string token = await _ecos.TokenAsync(HttpContext.RequestAborted);
            List<EcosBadge> badges = await _ecos.BadgesAsync(token, HttpContext.RequestAborted);

            // Voce 11 del port: la data dell'ultima lettura riuscita si scrive QUI, nell'unico
            // punto in cui i badge si leggono davvero.
            _attendance.MarkBadgeRead();

            return Ok(ApiResponse<HrBadgesDto>.Ok(new HrBadgesDto
            {
                Configured = true,
                Badges = badges
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(b => new HrBadgeDto { EmplCode = b.EmplCode, Name = b.Name, IsActive = b.IsActive })
                    .ToList(),
            }));
        }
        catch (EcosApiException ex)
        {
            return Ok(ApiResponse<HrBadgesDto>.Fail(ex.Message));
        }
    }

    [HttpPut("mapping/{employeeId:int}")]
    public IActionResult UpdateMapping(int employeeId, [FromBody] HrMappingRequest req)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("La modifica della mappatura richiede la scrittura su Timbrature."));

        string? error = _attendance.UpdateEcosMapping(employeeId, req.EcosEmplCode);
        return Ok(error == null
            ? ApiResponse<bool>.Ok(true, "Mappatura aggiornata")
            : ApiResponse<bool>.Fail(error));
    }

    // ── RETTIFICHE ────────────────────────────────────────────────────────────

    [HttpPost("adjustment")]
    public IActionResult Adjustment([FromBody] HrAdjustmentRequest req)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("L'inserimento di rettifiche richiede la scrittura su Timbrature."));

        string? error = _attendance.AddAdjustment(req, MeId);
        return Ok(error == null
            ? ApiResponse<bool>.Ok(true, "Rettifica registrata")
            : ApiResponse<bool>.Fail(error));
    }

    [HttpDelete("adjustment/{id:long}")]
    public IActionResult DeleteAdjustment(long id)
    {
        if (!CanManageTimbrature)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("L'eliminazione di rettifiche richiede la scrittura su Timbrature."));

        string? error = _attendance.DeleteAdjustment(id, MeId);
        return Ok(error == null
            ? ApiResponse<bool>.Ok(true, "Rettifica eliminata")
            : ApiResponse<bool>.Fail(error));
    }

    // ── RICHIESTE FERIE ED ASSENZE (FASE 2) ───────────────────────────────────

    [HttpGet("absences")]
    public IActionResult GetAbsences(
        [FromQuery] int? employeeId, [FromQuery] int? departmentId,
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? status)
    {
        bool isManagerOrAdmin = CanManageRichieste || IsAdmin;
        List<HrAbsenceDto> list = _attendance.GetAbsences(
            employeeId, departmentId, year, month, status, MeId, isManagerOrAdmin);
        return Ok(ApiResponse<List<HrAbsenceDto>>.Ok(list));
    }

    [HttpPost("absences")]
    public IActionResult CreateAbsence([FromBody] HrCreateAbsenceRequest req)
    {
        bool isManagerOrAdmin = CanManageRichieste || IsAdmin;
        var (id, error) = _attendance.CreateAbsenceRequest(req, MeId, isManagerOrAdmin);
        return Ok(error == null
            ? ApiResponse<int>.Ok(id ?? 0, "Richiesta inserita con successo")
            : ApiResponse<int>.Fail(error));
    }

    [HttpPost("absences/{id:int}/approve")]
    public IActionResult ApproveAbsence(int id, [FromBody] HrApproveAbsenceRequest req)
    {
        bool isManagerOrAdmin = CanManageRichieste || IsAdmin;
        string? error = _attendance.ApproveAbsenceRequest(id, req.Approved, req.RejectionReason, MeId, isManagerOrAdmin);
        return Ok(error == null
            ? ApiResponse<bool>.Ok(true, req.Approved ? "Richiesta approvata" : "Richiesta rifiutata")
            : ApiResponse<bool>.Fail(error));
    }

    [HttpDelete("absences/{id:int}")]
    public IActionResult CancelAbsence(int id)
    {
        string? error = _attendance.CancelAbsenceRequest(id, MeId, IsAdmin);
        return Ok(error == null
            ? ApiResponse<bool>.Ok(true, "Richiesta annullata")
            : ApiResponse<bool>.Fail(error));
    }
}
