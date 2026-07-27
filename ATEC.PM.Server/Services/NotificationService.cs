using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>Stati DDP considerati "materiale consegnato" (aggregazione A2) — matrice stati v7
/// (DISP assorbe CON/COS/SPED; il vecchio MOD è confluito in RAM, fuori dal set consegnato).</summary>
internal static class DdpDeliveredStatuses
{
    internal static readonly string[] Keys = { "DISP", "ASS" };
}

/// <summary>
/// Servizio centralizzato per creare notifiche.
/// Usato dal controller DDP (trigger su cambio stato) e dal BackgroundService (scadenze).
/// </summary>
public class NotificationService
{
    private readonly DbService _db;

    public NotificationService(DbService db) => _db = db;

    /// <summary>
    /// Crea una notifica e la invia a uno o più destinatari.
    /// </summary>
    public void Create(string type, string severity, string title, string message,
        string refType, int refId, int? projectId, int? createdBy, IEnumerable<int> recipientIds)
    {
        using var c = _db.Open();
        int notifId = c.ExecuteScalar<int>(@"
            INSERT INTO notifications (notification_type, severity, title, message, reference_type, reference_id, project_id, created_by)
            VALUES (@Type, @Severity, @Title, @Message, @RefType, @RefId, @ProjectId, @CreatedBy);
            SELECT LAST_INSERT_ID()",
            new
            {
                Type = type,
                Severity = severity,
                Title = title,
                Message = message,
                RefType = refType,
                RefId = refId,
                ProjectId = projectId,
                CreatedBy = createdBy
            });

        foreach (int empId in recipientIds.Distinct())
        {
            c.Execute(
                "INSERT INTO notification_recipients (notification_id, employee_id) VALUES (@NotifId, @EmpId)",
                new { NotifId = notifId, EmpId = empId });
        }
    }

    /// <summary>
    /// Trova i PM della commessa (employee con user_role PM o ADMIN che è pm_id del progetto).
    /// </summary>
    public List<int> GetProjectPmIds(int projectId)
    {
        using var c = _db.Open();
        return c.Query<int>(@"
            SELECT DISTINCT e.id FROM employees e
            WHERE e.status = 'ACTIVE' AND (
                e.id = (SELECT pm_id FROM projects WHERE id = @Pid)
                OR e.user_role IN ('ADMIN', 'PM')
            )", new { Pid = projectId }).ToList();
    }

    /// <summary>
    /// Tutti i dipendenti assegnati a fasi della commessa + PM.
    /// </summary>
    public List<int> GetProjectEmployeeIds(int projectId)
    {
        using var c = _db.Open();
        return c.Query<int>(@"
            SELECT DISTINCT emp_id FROM (
                SELECT pa.employee_id AS emp_id
                FROM phase_assignments pa
                JOIN project_phases pp ON pp.id = pa.project_phase_id
                WHERE pp.project_id = @Pid
                UNION
                SELECT pm_id AS emp_id FROM projects WHERE id = @Pid AND pm_id IS NOT NULL
            ) sub
            JOIN employees e ON e.id = sub.emp_id
            WHERE e.status = 'ACTIVE'", new { Pid = projectId }).ToList();
    }

    /// <summary>
    /// Trova dipendenti del reparto ACQ (acquisti).
    /// </summary>
    public List<int> GetAcqEmployeeIds()
    {
        using var c = _db.Open();
        return c.Query<int>(@"
            SELECT DISTINCT ed.employee_id FROM employee_departments ed
            JOIN departments d ON d.id = ed.department_id
            JOIN employees e ON e.id = ed.employee_id
            WHERE d.code = 'ACQ' AND e.status = 'ACTIVE'").ToList();
    }

    /// <summary>ADMIN attivi — destinatari di direzione / fallback quando manca un referente specifico.</summary>
    public List<int> GetAdminIds()
    {
        using var c = _db.Open();
        return c.Query<int>(
            "SELECT id FROM employees WHERE user_role = 'ADMIN' AND status = 'ACTIVE'").ToList();
    }

    /// <summary>
    /// Rimuove istantaneamente le notifiche relative a entità che sono state risolte/chiuse/pagate/consegnate.
    /// </summary>
    public int CleanResolvedNotifications()
    {
        using var c = _db.Open();
        int totalDeleted = 0;

        // 0. Promemoria superati: il giro giornaliero crea una notifica NUOVA per la stessa
        // scadenza ancora aperta ("scaduta da 2 g" ieri, "da 3 g" oggi) → per ogni
        // (dipendente, tipo, riferimento) si tiene solo la più recente
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            JOIN (
                SELECT nr2.employee_id, n2.notification_type, n2.reference_type, n2.reference_id,
                       MAX(n2.id) AS keep_id
                FROM notification_recipients nr2
                JOIN notifications n2 ON n2.id = nr2.notification_id
                WHERE n2.reference_id <> 0
                GROUP BY nr2.employee_id, n2.notification_type, n2.reference_type, n2.reference_id
                HAVING COUNT(*) > 1
            ) dup ON dup.employee_id = nr.employee_id
                 AND dup.notification_type = n.notification_type
                 AND dup.reference_type = n.reference_type
                 AND dup.reference_id = n.reference_id
            WHERE n.id < dup.keep_id");

        // 1. DDP Articoli consegnati, annullati, o non più scaduti
        string[] excludedDdp = DdpAggregationSet.Load(c, "A9");
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            JOIN bom_items b ON b.id = n.reference_id
            WHERE n.notification_type = 'DDP_OVERDUE'
              AND n.reference_type = 'BOM'
              AND (
                  b.item_status IN @Delivered
                  OR b.item_status IN @Excluded
                  OR b.date_needed IS NULL
                  OR b.date_needed >= CURDATE()
              )", new { Delivered = DdpDeliveredStatuses.Keys, Excluded = excludedDdp });

        // 2. Checklist chiuse, rimosse o senza scadenza
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN checklist_items i ON i.id = n.reference_id
            WHERE n.notification_type = 'CHECKLIST_DUE' AND n.reference_type = 'CHECKLIST'
              AND (i.id IS NULL OR i.status = 'CLOSED' OR i.due_date IS NULL)");

        // 3. Azioni MoM chiuse, rimosse o senza scadenza
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN mom_action_items a ON a.id = n.reference_id
            WHERE n.notification_type = 'MOM_DUE' AND n.reference_type = 'MOM_ACTION'
              AND (a.id IS NULL OR a.status = 'CLOSED' OR a.data_check IS NULL)");

        // 4. Commesse non più attive, completate, o senza scadenza
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN projects p ON p.id = n.reference_id
            WHERE n.notification_type = 'PROJECT_DUE' AND n.reference_type = 'PROJECT'
              AND (p.id IS NULL OR p.status <> 'ACTIVE' OR p.end_date_planned IS NULL OR p.end_date_actual IS NOT NULL)");

        // 5. SAL emessi, rimossi o senza scadenza
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN sal_rows sr ON sr.id = n.reference_id
            WHERE n.notification_type = 'SAL_DUE' AND n.reference_type = 'SAL_ROW'
              AND (sr.id IS NULL OR sr.data_fatt IS NULL OR sr.stato = 'emessa')");

        // 6. Incassi SAL pagati, rimossi o senza scadenza/gg saldo
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN sal_rows sr ON sr.id = n.reference_id
            WHERE n.notification_type = 'SAL_INCASSO_DUE' AND n.reference_type = 'SAL_ROW'
              AND (sr.id IS NULL OR sr.pagamento = 'Pagata' OR sr.data_fatt IS NULL OR sr.gg_saldo IS NULL)");

        // 7. Trattamenti confermati, rimossi o senza scadenza
        totalDeleted += c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN project_work_requests wr ON wr.id = n.reference_id
            WHERE n.notification_type = 'WORKREQUEST_TREATMENT_DUE' AND n.reference_type = 'WORK_REQUEST'
              AND (wr.id IS NULL OR wr.has_treatment = 0 OR wr.is_treatment_confirmed = 1 OR wr.treatment_date IS NULL)");

        // Rimuove notifiche orfane
        c.Execute("DELETE FROM notifications WHERE id NOT IN (SELECT DISTINCT notification_id FROM notification_recipients)");

        return totalDeleted;
    }
}

/// <summary>
/// BackgroundService che ogni mattina:
/// 1. Controlla articoli DDP scaduti (date_needed &lt; oggi, non in A2 consegnato né ANN)
/// 2. Pulisce notifiche vecchie (retention 5gg lette, 30gg non lette)
/// </summary>
public class NotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<NotificationBackgroundService> _log;
    private readonly int _retentionReadDays;
    private readonly int _retentionUnreadDays;
    private readonly int _warningDays;
    private readonly TimeSpan _interval;

    public NotificationBackgroundService(IServiceProvider sp, IConfiguration config, ILogger<NotificationBackgroundService> log)
    {
        _sp = sp;
        _log = log;
        _retentionReadDays = int.TryParse(config["Notifications:RetentionReadDays"], out int r) ? r : 5;
        _retentionUnreadDays = int.TryParse(config["Notifications:RetentionUnreadDays"], out int u) ? u : 30;
        // Giorni di anticipo per l'avviso "in scadenza" (WARNING) prima della scadenza.
        _warningDays = int.TryParse(config["Notifications:DeadlineWarningDays"], out int w) ? w : 3;
        int hours = int.TryParse(config["Notifications:IntervalHours"], out int h) ? h : 6;
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(10000, ct);

        _log.LogInformation("[Notifications] Job attivo — intervallo {Hours}h", _interval.TotalHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckOverdueDdp();
                await CheckTimesheetAnomalies();
                await CheckChecklistDeadlines();
                await CheckMoMDeadlines();
                await CheckProjectDeadlines();
                await CheckSalDeadlines();
                await CheckSalIncassoDeadlines();
                await CheckTreatmentDeadlines();
                await CleanupRetention();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Notifications] Errore nel job giornaliero");
            }

            await Task.Delay(_interval, ct);
        }
    }

    private async Task CheckOverdueDdp()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notifService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        using var c = db.Open();

        // Stati «esclusi da totale/conteggi» (aggregazione A9): un articolo in questi stati
        // (annullato/sospeso/rimesso/sostituito) non è "in ritardo" e non va notificato.
        string[] excluded = DdpAggregationSet.Load(c, "A9");

        // Rimuove notifiche "in ritardo" ormai risolte (consegnato, escluso da A9, o data non scaduta).
        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            JOIN bom_items b ON b.id = n.reference_id
            WHERE n.notification_type = 'DDP_OVERDUE'
              AND n.reference_type = 'BOM'
              AND (
                  b.item_status IN @Delivered
                  OR b.item_status IN @Excluded
                  OR b.date_needed IS NULL
                  OR b.date_needed >= CURDATE()
              )",
            new { Delivered = DdpDeliveredStatuses.Keys, Excluded = excluded });

        // Articoli scaduti non ancora notificati oggi (stessa logica KPI Gestore DDP).
        var overdue = c.Query<dynamic>(@"
            SELECT b.id, b.project_id, b.part_number, b.description, b.date_needed,
                   COALESCE(p.code, '') AS project_code
            FROM bom_items b
            JOIN projects p ON p.id = b.project_id
            WHERE b.date_needed < CURDATE()
              AND b.item_status NOT IN @Delivered
              AND COALESCE(b.item_status,'') NOT IN @Excluded
              AND b.date_needed IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'DDP_OVERDUE'
                    AND n.reference_type = 'BOM'
                    AND n.reference_id = b.id
                    AND DATE(n.created_at) = CURDATE()
              )", new { Delivered = DdpDeliveredStatuses.Keys, Excluded = excluded }).ToList();

        foreach (var item in overdue)
        {
            int projectId = (int)item.project_id;
            List<int> recipients = notifService.GetProjectPmIds(projectId);
            recipients.AddRange(notifService.GetAcqEmployeeIds());

            if (recipients.Count == 0) continue;

            string title = $"Articolo in ritardo — {item.project_code}";
            string msg = $"{item.part_number} - {item.description} — previsto {((DateTime)item.date_needed):dd/MM/yyyy}";

            notifService.Create(
                "DDP_OVERDUE", "ALARM", title, msg,
                "BOM", (int)item.id, projectId, null, recipients);
        }

        if (overdue.Count > 0)
            _log.LogInformation($"[Notifications] {overdue.Count} articoli scaduti notificati.");

        await Task.CompletedTask;
    }

    private async Task CheckTimesheetAnomalies()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notifService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        using var c = db.Open();
        int count = 0;

        // ── ANOMALIA 1: Ore giornaliere > 10h (ultimi 2 giorni) ─────────
        var excessiveHours = c.Query<dynamic>(@"
            SELECT te.employee_id, te.work_date, SUM(te.hours) AS total_hours,
                   CONCAT(e.first_name, ' ', e.last_name) AS employee_name
            FROM timesheet_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.work_date >= DATE_SUB(CURDATE(), INTERVAL 2 DAY)
              AND p.status = 'ACTIVE'
            GROUP BY te.employee_id, te.work_date
            HAVING SUM(te.hours) > 10
            AND NOT EXISTS (
                SELECT 1 FROM notifications n
                WHERE n.notification_type = 'TIMESHEET_ANOMALY'
                  AND n.reference_type = 'EMPLOYEE'
                  AND n.reference_id = te.employee_id
                  AND DATE(n.created_at) = te.work_date
            )").ToList();

        foreach (var row in excessiveHours)
        {
            int empId = (int)row.employee_id;
            DateTime workDate = (DateTime)row.work_date;
            decimal totalHours = (decimal)row.total_hours;
            string empName = (string)row.employee_name;

            // Destinatari: PM delle commesse dove ha lavorato quel giorno + ADMIN
            List<int> recipients = c.Query<int>(@"
                SELECT DISTINCT p.pm_id
                FROM timesheet_entries te
                JOIN project_phases pp ON pp.id = te.project_phase_id
                JOIN projects p ON p.id = pp.project_id
                WHERE te.employee_id = @EmpId AND te.work_date = @WorkDate AND p.pm_id IS NOT NULL",
                new { EmpId = empId, WorkDate = workDate }).ToList();

            // Aggiungi ADMIN
            List<int> admins = c.Query<int>(
                "SELECT id FROM employees WHERE user_role = 'ADMIN' AND status = 'ACTIVE'").ToList();
            recipients.AddRange(admins);
            recipients = recipients.Distinct().ToList();

            if (recipients.Count == 0) continue;

            notifService.Create(
                "TIMESHEET_ANOMALY", "WARNING",
                $"Ore anomale — {empName}",
                $"{totalHours:N1}h registrate il {workDate:dd/MM/yyyy}",
                "EMPLOYEE", empId, null, null, recipients);
            count++;
        }

        // ── ANOMALIA 2: Fase sfora budget > 150% ────────────────────────
        var overBudgetPhases = c.Query<dynamic>(@"
            SELECT pp.id AS phase_id, pp.project_id, pp.budget_hours,
                   COALESCE(NULLIF(pp.custom_name,''), pt.name) AS phase_name,
                   p.code AS project_code,
                   SUM(te.hours) AS hours_worked,
                   ROUND(SUM(te.hours) / pp.budget_hours * 100, 0) AS pct
            FROM project_phases pp
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            JOIN projects p ON p.id = pp.project_id
            JOIN timesheet_entries te ON te.project_phase_id = pp.id
            WHERE pp.budget_hours > 0
              AND p.status = 'ACTIVE'
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'TIMESHEET_ANOMALY'
                    AND n.reference_type = 'PHASE'
                    AND n.reference_id = pp.id
              )
            GROUP BY pp.id
            HAVING (SUM(te.hours) / pp.budget_hours) > 1.5").ToList();

        foreach (var row in overBudgetPhases)
        {
            int phaseId = (int)row.phase_id;
            int projectId = (int)row.project_id;
            decimal budgetHours = (decimal)row.budget_hours;
            decimal hoursWorked = (decimal)row.hours_worked;
            long pct = (long)row.pct;
            string phaseName = (string)row.phase_name;
            string projectCode = (string)row.project_code;

            List<int> recipients = notifService.GetProjectPmIds(projectId);
            if (recipients.Count == 0) continue;

            notifService.Create(
                "TIMESHEET_ANOMALY", "ALARM",
                $"Fase sfora budget — {projectCode}",
                $"{phaseName}: {hoursWorked:N1}h su {budgetHours:N1}h budget ({pct}%)",
                "PHASE", phaseId, projectId, null, recipients);
            count++;
        }

        if (count > 0)
            _log.LogInformation("[Notifications] {Count} anomalie timesheet notificate.", count);

        await Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════
    // NOTIFICHE DI SCADENZA — attività (check list), azioni MoM, commesse
    // ───────────────────────────────────────────────────────────────
    // Regola comune (frequenza = promemoria giornaliero):
    //   giorni = DATEDIFF(scadenza, oggi)
    //     0 ≤ giorni ≤ N  → WARNING "in scadenza"
    //     giorni < 0       → ALARM   "scaduta"
    //   Dedup: al massimo un WARNING e un ALARM per entità AL GIORNO
    //   (severità + DATE(created_at)=CURDATE()), così ri-avvisa ogni giorno finché non gestita.
    //   Prima di creare, una pulizia rimuove le notifiche pendenti di entità ormai risolte
    //   (chiuse/eliminate/consegnate/senza data) o la cui severità non è più coerente
    //   (es. una WARNING quando l'entità è ora scaduta): al giro dopo nasce l'ALARM corretto.
    // ═══════════════════════════════════════════════════════════════

    private static string DueText(int days) =>
        days < 0 ? $"scaduta da {-days} g" : days == 0 ? "scade oggi" : $"scade tra {days} g";

    private static string Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s!.Length <= max ? s! : s!.Substring(0, max - 1) + "…");

    /// <summary>Attività check list (checklist_items.due_date) in scadenza o scadute, non CLOSED.</summary>
    private async Task CheckChecklistDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        // Pulizia: rimuove le notifiche non più pertinenti (item risolto/eliminato o severità superata).
        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN checklist_items i ON i.id = n.reference_id
            WHERE n.notification_type = 'CHECKLIST_DUE' AND n.reference_type = 'CHECKLIST'
              AND (
                  i.id IS NULL OR i.status = 'CLOSED' OR i.due_date IS NULL
                  OR (n.severity = 'WARNING' AND NOT (DATEDIFF(i.due_date, CURDATE()) BETWEEN 0 AND @Warn))
                  OR (n.severity = 'ALARM'   AND NOT (DATEDIFF(i.due_date, CURDATE()) < 0))
              )", new { Warn = _warningDays });

        var rows = c.Query<dynamic>(@"
            SELECT i.id, i.project_id, i.description, i.due_date, i.created_by,
                   DATEDIFF(i.due_date, CURDATE()) AS days,
                   CASE WHEN DATEDIFF(i.due_date, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END AS sev,
                   COALESCE(p.code, '') AS project_code
            FROM checklist_items i
            LEFT JOIN projects p ON p.id = i.project_id
            WHERE i.due_date IS NOT NULL
              AND i.status <> 'CLOSED'
              AND DATEDIFF(i.due_date, CURDATE()) <= @Warn
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'CHECKLIST_DUE' AND n.reference_type = 'CHECKLIST'
                    AND n.reference_id = i.id
                    AND n.severity = CASE WHEN DATEDIFF(i.due_date, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END
                    AND DATE(n.created_at) = CURDATE()
              )", new { Warn = _warningDays }).ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int? projectId = (int?)r.project_id;
            int days = (int)r.days;
            string sev = (string)r.sev;
            string code = (string)r.project_code;

            // Destinatari mirati: item di commessa → PM+ADMIN; item di gruppo generico → creatore + ADMIN.
            List<int> recipients;
            if (projectId.HasValue)
                recipients = notif.GetProjectPmIds(projectId.Value);
            else
            {
                recipients = notif.GetAdminIds();
                if (r.created_by != null) recipients.Add((int)r.created_by);
            }
            if (recipients.Count == 0) continue;

            string label = string.IsNullOrEmpty(code) ? "Check list" : code;
            string title = (sev == "ALARM" ? "Attività scaduta — " : "Attività in scadenza — ") + label;
            string msg = $"{Trunc((string?)r.description, 300)} — {DueText(days)} ({((DateTime)r.due_date):dd/MM/yyyy})";

            notif.Create("CHECKLIST_DUE", sev, Trunc(title, 200), Trunc(msg, 500),
                "CHECKLIST", id, projectId, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} attività check list in scadenza/scadute notificate.", count);
        await Task.CompletedTask;
    }

    /// <summary>Azioni MoM (mom_action_items.data_check) in scadenza o scadute, non CLOSED.</summary>
    private async Task CheckMoMDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN mom_action_items a ON a.id = n.reference_id
            WHERE n.notification_type = 'MOM_DUE' AND n.reference_type = 'MOM_ACTION'
              AND (
                  a.id IS NULL OR a.status = 'CLOSED' OR a.data_check IS NULL
                  OR (n.severity = 'WARNING' AND NOT (DATEDIFF(a.data_check, CURDATE()) BETWEEN 0 AND @Warn))
                  OR (n.severity = 'ALARM'   AND NOT (DATEDIFF(a.data_check, CURDATE()) < 0))
              )", new { Warn = _warningDays });

        var rows = c.Query<dynamic>(@"
            SELECT a.id, a.attivita, a.data_check, a.resp1_id, a.resp2_id, a.resp3_id,
                   m.project_id,
                   DATEDIFF(a.data_check, CURDATE()) AS days,
                   CASE WHEN DATEDIFF(a.data_check, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END AS sev,
                   COALESCE(p.code, '') AS project_code
            FROM mom_action_items a
            JOIN mom_records m ON m.id = a.mom_id
            LEFT JOIN projects p ON p.id = m.project_id
            WHERE a.data_check IS NOT NULL
              AND a.status <> 'CLOSED'
              AND DATEDIFF(a.data_check, CURDATE()) <= @Warn
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'MOM_DUE' AND n.reference_type = 'MOM_ACTION'
                    AND n.reference_id = a.id
                    AND n.severity = CASE WHEN DATEDIFF(a.data_check, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END
                    AND DATE(n.created_at) = CURDATE()
              )", new { Warn = _warningDays }).ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int? projectId = (int?)r.project_id;
            int days = (int)r.days;
            string sev = (string)r.sev;
            string code = (string)r.project_code;

            // Responsabili reali dell'azione (tabella N + fallback legacy resp1/2/3), esclusi i
            // dipendenti "wildcard" ([PM] Generico, …) e i cessati.
            List<int> recipients = c.Query<int>(@"
                SELECT DISTINCT e.id FROM employees e
                WHERE e.status = 'ACTIVE'
                  AND NOT (e.first_name LIKE '[%]' AND e.last_name = 'Generico')
                  AND e.id IN (
                      SELECT employee_id FROM mom_action_item_responsibles WHERE action_item_id = @Id
                      UNION
                      SELECT resp_id FROM (
                          SELECT @R1 AS resp_id UNION SELECT @R2 UNION SELECT @R3
                      ) t WHERE resp_id IS NOT NULL
                  )",
                new { Id = id, R1 = (int?)r.resp1_id, R2 = (int?)r.resp2_id, R3 = (int?)r.resp3_id }).ToList();

            // Le scadenze MoM interessano il PM della commessa (NON gli admin): solo il pm_id
            // specifico della commessa, oltre ai responsabili dell'azione.
            if (projectId.HasValue)
            {
                int? pmId = c.ExecuteScalar<int?>(
                    @"SELECT p.pm_id FROM projects p
                      JOIN employees e ON e.id = p.pm_id AND e.status = 'ACTIVE'
                      WHERE p.id = @Pid", new { Pid = projectId.Value });
                if (pmId.HasValue) recipients.Add(pmId.Value);
            }
            recipients = recipients.Distinct().ToList();
            if (recipients.Count == 0) continue;

            string label = string.IsNullOrEmpty(code) ? "MoM" : code;
            string title = (sev == "ALARM" ? "Azione MoM scaduta — " : "Azione MoM in scadenza — ") + label;
            string msg = $"{Trunc((string?)r.attivita, 300)} — {DueText(days)} ({((DateTime)r.data_check):dd/MM/yyyy})";

            notif.Create("MOM_DUE", sev, Trunc(title, 200), Trunc(msg, 500),
                "MOM_ACTION", id, projectId, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} azioni MoM in scadenza/scadute notificate.", count);
        await Task.CompletedTask;
    }

    /// <summary>Commesse (projects.end_date_planned) in scadenza o scadute, attive e non ancora consegnate.</summary>
    private async Task CheckProjectDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN projects p ON p.id = n.reference_id
            WHERE n.notification_type = 'PROJECT_DUE' AND n.reference_type = 'PROJECT'
              AND (
                  p.id IS NULL OR p.status <> 'ACTIVE' OR p.end_date_planned IS NULL
                  OR p.end_date_actual IS NOT NULL
                  OR (n.severity = 'WARNING' AND NOT (DATEDIFF(p.end_date_planned, CURDATE()) BETWEEN 0 AND @Warn))
                  OR (n.severity = 'ALARM'   AND NOT (DATEDIFF(p.end_date_planned, CURDATE()) < 0))
              )", new { Warn = _warningDays });

        var rows = c.Query<dynamic>(@"
            SELECT p.id, p.code, p.title, p.end_date_planned,
                   DATEDIFF(p.end_date_planned, CURDATE()) AS days,
                   CASE WHEN DATEDIFF(p.end_date_planned, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END AS sev
            FROM projects p
            WHERE p.end_date_planned IS NOT NULL
              AND p.end_date_actual IS NULL
              AND p.status = 'ACTIVE'
              AND DATEDIFF(p.end_date_planned, CURDATE()) <= @Warn
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'PROJECT_DUE' AND n.reference_type = 'PROJECT'
                    AND n.reference_id = p.id
                    AND n.severity = CASE WHEN DATEDIFF(p.end_date_planned, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END
                    AND DATE(n.created_at) = CURDATE()
              )", new { Warn = _warningDays }).ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int days = (int)r.days;
            string sev = (string)r.sev;
            string code = (string)r.code;

            // Destinatari: PM + dipendenti assegnati alla commessa (fallback PM/ADMIN).
            List<int> recipients = notif.GetProjectEmployeeIds(id);
            if (recipients.Count == 0) recipients = notif.GetProjectPmIds(id);
            if (recipients.Count == 0) continue;

            string title = (sev == "ALARM" ? "Commessa scaduta — " : "Commessa in scadenza — ") + code;
            string msg = $"{Trunc((string?)r.title, 300)} — {DueText(days)} ({((DateTime)r.end_date_planned):dd/MM/yyyy})";

            notif.Create("PROJECT_DUE", sev, Trunc(title, 200), Trunc(msg, 500),
                "PROJECT", id, id, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} commesse in scadenza/scadute notificate.", count);
        await Task.CompletedTask;
    }

    /// <summary>Step/scadenze SAL (sal_rows.data_fatt) in scadenza o scaduti, non ancora emessi (stato '' o 'daEmettere').</summary>
    private async Task CheckSalDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        // 1. Pulisce le vecchie notifiche SAL_DUE ormai risolte (fattura emessa, data rimossa o riga
        //    eliminata). Il nuovo stato 'daEmettere' NON risolve: continua ad allertare (parità v10).
        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN sal_rows sr ON sr.id = n.reference_id
            WHERE n.notification_type = 'SAL_DUE' AND n.reference_type = 'SAL_ROW'
              AND (
                  sr.id IS NULL OR sr.data_fatt IS NULL OR sr.stato = 'emessa'
                  OR (n.severity = 'WARNING' AND NOT (DATEDIFF(sr.data_fatt, CURDATE()) BETWEEN 0 AND @Warn))
                  OR (n.severity = 'ALARM'   AND NOT (DATEDIFF(sr.data_fatt, CURDATE()) < 0))
              )", new { Warn = _warningDays });

        // 2. Query per nuove scadenze SAL non ancora emesse ('' o 'daEmettere') da notificare
        var rows = c.Query<dynamic>(@"
            SELECT sr.id, sr.project_id, sr.step, sr.data_fatt, sr.perc, ps.valore, p.code,
                   DATEDIFF(sr.data_fatt, CURDATE()) AS days,
                   CASE WHEN DATEDIFF(sr.data_fatt, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END AS sev
            FROM sal_rows sr
            JOIN projects p ON p.id = sr.project_id
            LEFT JOIN project_sal ps ON ps.project_id = sr.project_id
            WHERE sr.data_fatt IS NOT NULL
              AND sr.stato <> 'emessa'
              AND DATEDIFF(sr.data_fatt, CURDATE()) <= @Warn
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'SAL_DUE' AND n.reference_type = 'SAL_ROW'
                    AND n.reference_id = sr.id
                    AND n.severity = CASE WHEN DATEDIFF(sr.data_fatt, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END
                    AND DATE(n.created_at) = CURDATE()
              )", new { Warn = _warningDays }).ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int projectId = (int)r.project_id;
            int days = (int)r.days;
            string sev = (string)r.sev;
            string code = (string)r.code;
            decimal? perc = (decimal?)r.perc;
            decimal? valore = (decimal?)r.valore;

            // Destinatari: PM della commessa + ADMIN
            List<int> recipients = notif.GetProjectPmIds(projectId);
            if (recipients.Count == 0) continue;

            string importoStr = "";
            if (valore.HasValue && perc.HasValue)
            {
                var importo = valore.Value * (perc.Value / 100m);
                importoStr = " — " + importo.ToString("N2", new System.Globalization.CultureInfo("it-IT")) + " €";
            }

            string title = (sev == "ALARM" ? "Fatturazione SAL scaduta — " : "Fatturazione SAL in scadenza — ") + code;
            string msg = $"{Trunc((string?)r.step, 300)} — {DueText(days)} ({((DateTime)r.data_fatt):dd/MM/yyyy}){importoStr}";

            notif.Create("SAL_DUE", sev, Trunc(title, 200), Trunc(msg, 500),
                "SAL_ROW", id, projectId, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} scadenze SAL in scadenza/scadute notificate.", count);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Incasso fatture SAL: righe con data fattura e gg saldo valorizzati, non ancora pagate,
    /// il cui saldo previsto (data_fatt + gg_saldo) è già passato. Regola v10 (salIncassoState):
    /// sempre ALARM dal giorno dopo la scadenza, nessuna fascia pre-warning, indipendente dallo
    /// stato di fatturazione.
    /// </summary>
    private async Task CheckSalIncassoDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        // 1. Pulisce le vecchie notifiche SAL_INCASSO_DUE ormai risolte: riga eliminata, pagata,
        //    senza data fattura / gg saldo, o con saldo previsto non più scaduto.
        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN sal_rows sr ON sr.id = n.reference_id
            WHERE n.notification_type = 'SAL_INCASSO_DUE' AND n.reference_type = 'SAL_ROW'
              AND (
                  sr.id IS NULL
                  OR sr.pagamento = 'Pagata'
                  OR sr.data_fatt IS NULL
                  OR sr.gg_saldo IS NULL
                  OR DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) >= CURDATE()
              )");

        // 2. Fatture con incasso scaduto da notificare (dedupe giornaliero come gli altri check:
        //    al massimo una notifica per riga al giorno — severità sempre ALARM)
        var rows = c.Query<dynamic>(@"
            SELECT sr.id, sr.project_id, sr.step, sr.n_fatt, sr.perc, ps.valore, p.code,
                   DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) AS data_saldo
            FROM sal_rows sr
            JOIN projects p ON p.id = sr.project_id
            LEFT JOIN project_sal ps ON ps.project_id = sr.project_id
            WHERE sr.data_fatt IS NOT NULL
              AND sr.gg_saldo IS NOT NULL
              AND sr.pagamento <> 'Pagata'
              AND DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) < CURDATE()
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'SAL_INCASSO_DUE' AND n.reference_type = 'SAL_ROW'
                    AND n.reference_id = sr.id
                    AND n.severity = 'ALARM'
                    AND DATE(n.created_at) = CURDATE()
              )").ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int projectId = (int)r.project_id;
            string code = (string)r.code;
            string nFatt = (string?)r.n_fatt ?? "";
            decimal? perc = (decimal?)r.perc;
            decimal? valore = (decimal?)r.valore;
            DateTime dataSaldo = (DateTime)r.data_saldo;

            // Destinatari: PM della commessa + ADMIN (stessa platea di CheckSalDeadlines)
            List<int> recipients = notif.GetProjectPmIds(projectId);
            if (recipients.Count == 0) continue;

            // N° fattura e importo (valore ordine × %SAL) sono facoltativi nel messaggio
            string nFattStr = string.IsNullOrEmpty(nFatt) ? "" : $" — N° fatt. {nFatt}";
            string importoStr = "";
            if (valore.HasValue && perc.HasValue)
            {
                decimal importo = valore.Value * (perc.Value / 100m);
                importoStr = " — " + importo.ToString("N2", new System.Globalization.CultureInfo("it-IT")) + " €";
            }

            string title = $"Incasso fattura scaduto — {code}";
            string msg = $"{Trunc((string?)r.step, 300)} — saldo previsto {dataSaldo:dd/MM/yyyy}{nFattStr}{importoStr}";

            notif.Create("SAL_INCASSO_DUE", "ALARM", Trunc(title, 200), Trunc(msg, 500),
                "SAL_ROW", id, projectId, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} incassi fattura SAL scaduti notificati.", count);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Trattamenti lavorazioni (project_work_requests.treatment_date) in scadenza o scaduti:
    /// richiesti (has_treatment=1), non ancora confermati e non in staging.
    /// </summary>
    private async Task CheckTreatmentDeadlines()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
        using var c = db.Open();

        // Pulizia: notifiche non più pertinenti (riga eliminata, trattamento tolto/confermato,
        // data rimossa o severità superata)
        c.Execute(@"
            DELETE nr FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            LEFT JOIN project_work_requests wr ON wr.id = n.reference_id
            WHERE n.notification_type = 'WORKREQUEST_TREATMENT_DUE' AND n.reference_type = 'WORK_REQUEST'
              AND (
                  wr.id IS NULL OR wr.has_treatment = 0 OR wr.is_treatment_confirmed = 1
                  OR wr.treatment_date IS NULL
                  OR (n.severity = 'WARNING' AND NOT (DATEDIFF(wr.treatment_date, CURDATE()) BETWEEN 0 AND @Warn))
                  OR (n.severity = 'ALARM'   AND NOT (DATEDIFF(wr.treatment_date, CURDATE()) < 0))
              )", new { Warn = _warningDays });

        var rows = c.Query<dynamic>(@"
            SELECT wr.id, wr.project_id, wr.description, wr.treatment_date, p.code,
                   DATEDIFF(wr.treatment_date, CURDATE()) AS days,
                   CASE WHEN DATEDIFF(wr.treatment_date, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END AS sev
            FROM project_work_requests wr
            JOIN projects p ON p.id = wr.project_id
            WHERE wr.treatment_date IS NOT NULL
              AND wr.has_treatment = 1
              AND wr.is_treatment_confirmed = 0
              AND wr.is_staging = 0
              AND DATEDIFF(wr.treatment_date, CURDATE()) <= @Warn
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'WORKREQUEST_TREATMENT_DUE' AND n.reference_type = 'WORK_REQUEST'
                    AND n.reference_id = wr.id
                    AND n.severity = CASE WHEN DATEDIFF(wr.treatment_date, CURDATE()) < 0 THEN 'ALARM' ELSE 'WARNING' END
                    AND DATE(n.created_at) = CURDATE()
              )", new { Warn = _warningDays }).ToList();

        int count = 0;
        foreach (var r in rows)
        {
            int id = (int)r.id;
            int projectId = (int)r.project_id;
            int days = (int)r.days;
            string sev = (string)r.sev;
            string code = (string)r.code;

            // Destinatari: PM della commessa + ADMIN (stessa platea delle scadenze commessa)
            List<int> recipients = notif.GetProjectPmIds(projectId);
            if (recipients.Count == 0) continue;

            // DueText è declinato al femminile: per "trattamento" serve il maschile
            string dueText = days < 0 ? $"scaduto da {-days} g" : days == 0 ? "scade oggi" : $"scade tra {days} g";
            string title = (sev == "ALARM" ? "Trattamento scaduto — " : "Trattamento in scadenza — ") + code;
            string msg = $"{Trunc((string?)r.description, 300)} — {dueText} ({((DateTime)r.treatment_date):dd/MM/yyyy})";

            notif.Create("WORKREQUEST_TREATMENT_DUE", sev, Trunc(title, 200), Trunc(msg, 500),
                "WORK_REQUEST", id, projectId, null, recipients);
            count++;
        }

        if (count > 0) _log.LogInformation("[Notifications] {Count} trattamenti lavorazioni in scadenza/scaduti notificati.", count);
        await Task.CompletedTask;
    }

    private async Task CleanupRetention()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        using var c = db.Open();

        int deletedRead = c.Execute(
            "DELETE FROM notification_recipients WHERE is_read = TRUE AND read_at < DATE_SUB(NOW(), INTERVAL @Days DAY)",
            new { Days = _retentionReadDays });

        int deletedUnread = c.Execute(
            "DELETE FROM notification_recipients WHERE is_read = FALSE AND notification_id IN (SELECT id FROM notifications WHERE created_at < DATE_SUB(NOW(), INTERVAL @Days DAY))",
            new { Days = _retentionUnreadDays });

        int deletedOrphan = c.Execute(
            "DELETE FROM notifications WHERE id NOT IN (SELECT DISTINCT notification_id FROM notification_recipients)");

        if (deletedRead + deletedUnread + deletedOrphan > 0)
            _log.LogInformation($"[Notifications] Pulizia: {deletedRead} lette (>{_retentionReadDays}gg), {deletedUnread} non lette (>{_retentionUnreadDays}gg), {deletedOrphan} orfane rimosse.");

        await Task.CompletedTask;
    }
} 
