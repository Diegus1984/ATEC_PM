using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Assenze» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── GESTIONE RICHIESTE ED ASSENZE (FASE 2) ─────────────────────────────────

    public List<HrAbsenceDto> GetAbsences(
        int? employeeId, int? departmentId, int? year, int? month, string? status, int currentUserId, bool isManagerOrAdmin)
    {
        using MySqlConnection c = _db.Open();

        int? targetEmployeeId = employeeId;
        if (!isManagerOrAdmin)
            targetEmployeeId = currentUserId;

        var sql = @"
            SELECT a.id AS Id, a.employee_id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   d.name AS DepartmentName,
                   a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay,
                   TIME_FORMAT(a.hour_from, '%H:%i') AS HourFrom, TIME_FORMAT(a.hour_to, '%H:%i') AS HourTo,
                   a.absence_type AS AbsenceType, a.status AS Status,
                   a.source AS Source, a.ecos_absence_id AS EcosAbsenceId,
                   a.approved_by AS ApprovedBy,
                   CONCAT_WS(' ', app.first_name, app.last_name) AS ApprovedByName,
                   a.approved_at AS ApprovedAt,
                   a.rejection_reason AS RejectionReason,
                   a.notes AS Notes,
                   a.created_by AS CreatedBy,
                   CONCAT_WS(' ', cr.first_name, cr.last_name) AS CreatedByName,
                   a.created_at AS CreatedAt
            FROM hr_absences a
            JOIN employees e ON e.id = a.employee_id
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            LEFT JOIN employees app ON app.id = a.approved_by
            LEFT JOIN employees cr ON cr.id = a.created_by
            WHERE 1=1";

        var p = new DynamicParameters();

        if (targetEmployeeId.HasValue)
        {
            sql += " AND a.employee_id = @EmpId";
            p.Add("EmpId", targetEmployeeId.Value);
        }
        else if (departmentId.HasValue)
        {
            sql += " AND ed.department_id = @DeptId";
            p.Add("DeptId", departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND a.status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }

        if (year.HasValue && month.HasValue)
        {
            var primo = new DateTime(year.Value, month.Value, 1);
            var ultimo = primo.AddMonths(1).AddDays(-1);
            sql += " AND a.date_from <= @Ultimo AND a.date_to >= @Primo";
            p.Add("Primo", primo);
            p.Add("Ultimo", ultimo);
        }
        else if (year.HasValue)
        {
            var primo = new DateTime(year.Value, 1, 1);
            var ultimo = new DateTime(year.Value, 12, 31);
            sql += " AND a.date_from <= @Ultimo AND a.date_to >= @Primo";
            p.Add("Primo", primo);
            p.Add("Ultimo", ultimo);
        }

        // Dalla più recente INSERITA (Diego, 09/09/2026): ordinando per data della richiesta le
        // ferie di novembre stavano sopra il permesso appena messo, che sembrava sparito.
        sql += " ORDER BY a.created_at DESC, a.id DESC";

        return c.Query<HrAbsenceDto>(sql, p).ToList();
    }

    public (int? Id, string? Error) CreateAbsenceRequest(
        HrCreateAbsenceRequest req, int currentUserId, bool isManagerOrAdmin)
    {
        int targetEmployeeId = req.EmployeeId ?? currentUserId;
        if (targetEmployeeId != currentUserId && !isManagerOrAdmin)
            return (null, "Puoi inserire richieste solo per te stesso.");

        if (req.DateFrom.Date > req.DateTo.Date)
            return (null, "La data di inizio non può essere successiva alla data di fine.");

        // Fascia oraria («HH:mm»): se c'è, le ore vengono da lì (ed è quella che va su Ecos, #151).
        TimeSpan? oraDa = null, oraA = null;
        if (!req.IsFullDay && (!string.IsNullOrWhiteSpace(req.HourFrom) || !string.IsNullOrWhiteSpace(req.HourTo)))
        {
            if (!ProvaOra(req.HourFrom, out TimeSpan da) || !ProvaOra(req.HourTo, out TimeSpan a))
                return (null, "La fascia oraria non è valida: servono ora di inizio e di fine (es. 14:30 e 17:00).");
            if (a <= da)
                return (null, "La fine della fascia oraria deve venire dopo l'inizio.");
            oraDa = da;
            oraA = a;
            req.Hours = Math.Round((decimal)(a - da).TotalMinutes / 60m, 1);
        }

        if (!req.IsFullDay && (!req.Hours.HasValue || req.Hours.Value <= 0 || req.Hours.Value > 24))
            return (null, "Le ore di permesso devono essere maggiori di 0.");

        string type = (req.AbsenceType ?? "VACATION").Trim().ToUpperInvariant();
        if (type is not ("VACATION" or "PERMIT" or "SICKNESS" or "INJURY" or "OTHER"))
            return (null, "Tipologia assenza non valida.");

        using MySqlConnection c = _db.Open();

        var targetEmp = c.QueryFirstOrDefault<(string Name, int? PrimaryDeptId)>(
            @"SELECT CONCAT_WS(' ', e.first_name, e.last_name) AS Name, ed.department_id AS PrimaryDeptId
              FROM employees e
              LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
              WHERE e.id = @Id AND e.status <> 'TERMINATED'",
            new { Id = targetEmployeeId });

        if (targetEmp == default)
            return (null, "Dipendente non trovato o cessato.");

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO hr_absences
                (employee_id, date_from, date_to, hours, is_full_day, hour_from, hour_to, absence_type, status, source, notes, created_by)
            VALUES
                (@EmployeeId, @DateFrom, @DateTo, @Hours, @IsFullDay, @OraDa, @OraA, @AbsenceType, 'PENDING', 'ATEC', @Notes, @CreatedBy);
            SELECT LAST_INSERT_ID();",
            new
            {
                EmployeeId = targetEmployeeId,
                DateFrom = req.DateFrom.Date,
                DateTo = req.DateTo.Date,
                req.Hours,
                req.IsFullDay,
                OraDa = oraDa,
                OraA = oraA,
                AbsenceType = type,
                Notes = req.Notes?.Trim(),
                CreatedBy = currentUserId
            });

        // Notifica ai responsabili di reparto
        try
        {
            var managerIds = c.Query<int>(@"
                SELECT DISTINCT ed.employee_id
                FROM employee_departments ed
                WHERE ed.is_responsible = 1
                  AND ed.department_id IN (
                      SELECT department_id FROM employee_departments WHERE employee_id = @EmpId
                  )
                  AND ed.employee_id <> @CreatedBy",
                new { EmpId = targetEmployeeId, CreatedBy = currentUserId }).ToList();

            if (managerIds.Count > 0)
            {
                string descr = req.IsFullDay
                    ? (req.DateFrom.Date == req.DateTo.Date ? $"il {req.DateFrom:dd/MM/yyyy}" : $"dal {req.DateFrom:dd/MM} al {req.DateTo:dd/MM/yyyy}")
                    : $"il {req.DateFrom:dd/MM/yyyy} ({req.Hours}h)";

                _notif.Create(
                    type: "HR_ABSENCE_REQUEST",
                    severity: "INFO",
                    title: $"Richiesta {type} — {targetEmp.Name}",
                    message: $"{targetEmp.Name} ha richiesto {type.ToLower()} {descr}.",
                    refType: "HR_ABSENCE",
                    refId: id,
                    projectId: null,
                    createdBy: currentUserId,
                    recipientIds: managerIds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HR] Errore invio notifica richiesta ferie a responsabili.");
        }

        return (id, null);
    }

    /// <summary>
    /// Una richiesta che vive su Ecos si decide passando da Ecos (<c>ApproveAbsenceRequestAsync</c>,
    /// <c>CancelAbsenceRequestAsync</c>): il ramo sincrono da solo lascerebbe Ecos indietro, e al
    /// prossimo import vincerebbe Ecos rimettendo tutto com'era.
    /// </summary>
    internal const string VaDecisaSuEcos =
        "Questa richiesta vive anche su Ecos: la decisione passa da lì. Riprova dalla pagina Richieste.";

    /// <summary>«14:30» o «14:30:00» → TimeSpan; false se non è un'ora.</summary>
    private static bool ProvaOra(string? valore, out TimeSpan ora) =>
        TimeSpan.TryParseExact((valore ?? "").Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" },
            System.Globalization.CultureInfo.InvariantCulture, out ora);

    /// <summary>I controlli di un'approvazione: stato e permessi. Null = si può.</summary>
    private static string? ControlloApprovazione(
        MySqlConnection c, string status, int employeeId, int approverId, bool isManagerOrAdmin)
    {
        if (status != "PENDING")
            return $"La richiesta è già in stato {status}.";

        if (!isManagerOrAdmin)
        {
            bool isResponsible = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM employee_departments ed_resp
                JOIN employee_departments ed_emp ON ed_emp.department_id = ed_resp.department_id
                WHERE ed_resp.employee_id = @ApproverId AND ed_resp.is_responsible = 1
                  AND ed_emp.employee_id = @TargetEmpId",
                new { ApproverId = approverId, TargetEmpId = employeeId }) > 0;

            if (!isResponsible)
                return "Non hai i permessi per approvare richieste per questo dipendente (non sei responsabile del suo reparto).";
        }
        return null;
    }

    /// <summary>I controlli di un annullamento: stato, chi è, richieste già approvate. Null = si può.</summary>
    private static string? ControlloAnnullamento(string status, int employeeId, int currentUserId, bool isAdmin, int? createdBy)
    {
        if (status == "CANCELLED")
            return "La richiesta è già annullata.";
        if (!isAdmin && employeeId != currentUserId && createdBy != currentUserId)
            return "Puoi annullare solo le tue richieste.";
        if (status == "APPROVED" && !isAdmin)
            return "Le richieste già approvate possono essere annullate solo da un amministratore.";
        return null;
    }

    /// <param name="ecosAllineato">
    /// true = Ecos ha già ricevuto la decisione (<c>ApproveAbsenceRequestAsync</c>); false = ramo
    /// locale, che rifiuta le richieste con un id di Ecos.
    /// </param>
    public string? ApproveAbsenceRequest(
        int absenceId, bool approved, string? rejectionReason, int approverId, bool isManagerOrAdmin,
        bool ecosAllineato = false)
    {
        using MySqlConnection c = _db.Open();

        var absence = c.QueryFirstOrDefault<(int Id, int EmployeeId, string EmployeeName, string Status, string AbsenceType, DateTime DateFrom, DateTime DateTo, decimal? Hours, bool IsFullDay, string? EcosId)>(
            @"SELECT a.id AS Id, a.employee_id AS EmployeeId,
                     CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                     a.status AS Status, a.absence_type AS AbsenceType,
                     a.date_from AS DateFrom, a.date_to AS DateTo, a.hours AS Hours, a.is_full_day AS IsFullDay,
                     a.ecos_absence_id AS EcosId
              FROM hr_absences a
              JOIN employees e ON e.id = a.employee_id
              WHERE a.id = @Id",
            new { Id = absenceId });

        if (absence == default)
            return "Richiesta non trovata.";

        string? controllo = ControlloApprovazione(c, absence.Status, absence.EmployeeId, approverId, isManagerOrAdmin);
        if (controllo != null) return controllo;

        if (!ecosAllineato && !string.IsNullOrEmpty(absence.EcosId))
            return VaDecisaSuEcos;

        string newStatus = approved ? "APPROVED" : "REJECTED";

        c.Execute(@"
            UPDATE hr_absences
            SET status = @Status, approved_by = @ApproverId, approved_at = NOW(),
                rejection_reason = @RejectionReason
            WHERE id = @Id",
            new
            {
                Status = newStatus,
                ApproverId = approverId,
                RejectionReason = approved ? null : rejectionReason?.Trim(),
                Id = absenceId
            });

        // Sincronizza su res_assignments (Planner Risorse Gantt Ferie)
        SyncToResourcePlanner(c, absence.EmployeeId, absence.DateFrom, absence.DateTo, absence.AbsenceType, isApproved: approved);

        // Notifica al dipendente
        try
        {
            string approverName = c.ExecuteScalar<string>(
                "SELECT CONCAT_WS(' ', first_name, last_name) FROM employees WHERE id = @Id",
                new { Id = approverId }) ?? "Responsabile";

            string esitoStr = approved ? "APPROVATA" : "RIFIUTATA";
            string msg = approved
                ? $"La tua richiesta di {absence.AbsenceType.ToLower()} è stata approvata da {approverName}."
                : $"La tua richiesta di {absence.AbsenceType.ToLower()} è stata rifiutata da {approverName}."
                  + (!string.IsNullOrWhiteSpace(rejectionReason) ? $" Motivo: {rejectionReason}" : "");

            _notif.Create(
                type: "HR_ABSENCE_STATUS",
                severity: approved ? "SUCCESS" : "WARNING",
                title: $"Richiesta {absence.AbsenceType} {esitoStr}",
                message: msg,
                refType: "HR_ABSENCE",
                refId: absenceId,
                projectId: null,
                createdBy: approverId,
                recipientIds: new[] { absence.EmployeeId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HR] Errore invio notifica esito richiesta a dipendente.");
        }

        return null;
    }

    public string? CancelAbsenceRequest(int absenceId, int currentUserId, bool isAdmin, bool ecosAllineato = false)
    {
        using MySqlConnection c = _db.Open();

        var absence = c.QueryFirstOrDefault<(int Id, int EmployeeId, int? CreatedBy, string Status, string AbsenceType, DateTime DateFrom, DateTime DateTo, string? EcosId)>(
            "SELECT id, employee_id AS EmployeeId, created_by AS CreatedBy, status AS Status, absence_type AS AbsenceType, date_from AS DateFrom, date_to AS DateTo, ecos_absence_id AS EcosId FROM hr_absences WHERE id = @Id",
            new { Id = absenceId });

        if (absence == default)
            return "Richiesta non trovata.";

        string? controllo = ControlloAnnullamento(absence.Status, absence.EmployeeId, currentUserId, isAdmin, absence.CreatedBy);
        if (controllo != null) return controllo;

        // Vive anche su Ecos: si passa da CancelAbsenceRequestAsync, che prima la cancella là.
        if (!ecosAllineato && !string.IsNullOrEmpty(absence.EcosId))
            return VaDecisaSuEcos;

        c.Execute("UPDATE hr_absences SET status = 'CANCELLED' WHERE id = @Id", new { Id = absenceId });

        // Rimuovi dal Planner Risorse se era approvata
        SyncToResourcePlanner(c, absence.EmployeeId, absence.DateFrom, absence.DateTo, absence.AbsenceType, isApproved: false);

        return null;
    }
}
