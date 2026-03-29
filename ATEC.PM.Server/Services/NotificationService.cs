using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

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
}

/// <summary>
/// BackgroundService che ogni mattina:
/// 1. Controlla articoli DDP scaduti (date_needed < oggi e stato non DELIVERED/CANCELLED)
/// 2. Pulisce notifiche vecchie (retention 5gg lette, 30gg non lette)
/// </summary>
public class NotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<NotificationBackgroundService> _log;
    private readonly int _retentionReadDays;
    private readonly int _retentionUnreadDays;

    public NotificationBackgroundService(IServiceProvider sp, IConfiguration config, ILogger<NotificationBackgroundService> log)
    {
        _sp = sp;
        _log = log;
        _retentionReadDays = int.TryParse(config["Notifications:RetentionReadDays"], out int r) ? r : 5;
        _retentionUnreadDays = int.TryParse(config["Notifications:RetentionUnreadDays"], out int u) ? u : 30;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Attendi avvio server
        await Task.Delay(10000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckOverdueDdp();
                await CheckTimesheetAnomalies();
                await CleanupRetention();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Notifications] Errore nel job giornaliero");
            }

            // Prossimo check tra 6 ore (o configurabile)
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }

    private async Task CheckOverdueDdp()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        var notifService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        using var c = db.Open();

        // Articoli scaduti non ancora notificati oggi
        var overdue = c.Query<dynamic>(@"
            SELECT b.id, b.project_id, b.part_number, b.description, b.date_needed,
                   COALESCE(p.code, '') AS project_code
            FROM bom_items b
            JOIN projects p ON p.id = b.project_id
            WHERE b.date_needed < CURDATE()
              AND b.item_status NOT IN ('DELIVERED', 'CANCELLED')
              AND b.date_needed IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM notifications n
                  WHERE n.notification_type = 'DDP_OVERDUE'
                    AND n.reference_type = 'BOM'
                    AND n.reference_id = b.id
                    AND DATE(n.created_at) = CURDATE()
              )").ToList();

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
            WHERE te.work_date >= DATE_SUB(CURDATE(), INTERVAL 2 DAY)
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
