using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Quadratura» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

    public HrQuadraturaMonthDto GetQuadratura(int year, int month, int? departmentId)
    {
        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var p = new DynamicParameters();
        p.Add("Primo", primo);
        p.Add("Ultimo", ultimo);

        string empSql = @"
            SELECT DISTINCT e.id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   COALESCE(ed.department_id, 0) AS DepartmentId,
                   COALESCE(d.name, 'Senza reparto') AS DepartmentName,
                   e.hr_must_punch AS MustPunch,
                   e.hr_daily_hours AS DailyHours,
                   -- 🪤 `SELECT DISTINCT` + `ORDER BY` su colonne che non sono nella SELECT:
                   -- MySQL lo rifiuta in blocco (ONLY_FULL_GROUP_BY). Cognome e nome stanno
                   -- qui solo per poter ordinare come si è sempre ordinato.
                   e.last_name AS LastName, e.first_name AS FirstName
            FROM employees e
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE e.status = 'ACTIVE' AND e.emp_type = 'INTERNAL' AND e.user_role <> 'ADMIN' AND e.first_name NOT LIKE '[%'";

        if (departmentId.HasValue)
        {
            empSql += " AND ed.department_id = @DeptId";
            p.Add("DeptId", departmentId.Value);
        }

        empSql += " ORDER BY d.name, e.last_name, e.first_name";

        var employees = c.Query<(int EmployeeId, string EmployeeName, int DepartmentId, string DepartmentName, bool MustPunch, decimal DailyHours)>(empSql, p).ToList();

        // 1. Ore presenze da hr_days nel mese
        var presenze = c.Query<(int EmployeeId, int RegularMin, int OvertimeMin)>(@"
            SELECT employee_id AS EmployeeId,
                   SUM(regular_minutes) AS RegularMin,
                   SUM(overtime_minutes) AS OvertimeMin
            FROM hr_days
            WHERE work_date BETWEEN @Primo AND @Ultimo
            GROUP BY employee_id", p)
            .ToDictionary(x => x.EmployeeId, x => Math.Round((decimal)(x.RegularMin + x.OvertimeMin) / 60m, 1));

        // 2. Ore assenze approvate da hr_absences nel mese
        var assenze = c.Query<HrAbsenceDto>(@"
            SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType
            FROM hr_absences a
            WHERE a.status = 'APPROVED'
              AND a.date_from <= @Ultimo AND a.date_to >= @Primo", p).ToList();

        var assenzePerEmp = new Dictionary<int, decimal>();
        foreach (var a in assenze)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            decimal h = 0;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
            {
                if (!TimesheetRules.IsHoliday(dt))
                {
                    h += a.Hours ?? 8.0m;
                }
            }
            assenzePerEmp[a.EmployeeId] = assenzePerEmp.GetValueOrDefault(a.EmployeeId) + h;
        }

        // 3. Ore consuntivate su commesse da timesheet_entries
        var timesheet = c.Query<(int EmployeeId, decimal DirectHours, decimal InternalHours)>(@"
            SELECT te.employee_id AS EmployeeId,
                   SUM(CASE WHEN COALESCE(p.is_internal, 0) = 0 THEN te.hours ELSE 0 END) AS DirectHours,
                   SUM(CASE WHEN COALESCE(p.is_internal, 0) = 1 THEN te.hours ELSE 0 END) AS InternalHours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN projects p ON p.id = pp.project_id
            WHERE te.work_date BETWEEN @Primo AND @Ultimo
            GROUP BY te.employee_id", p)
            .ToDictionary(x => x.EmployeeId);

        var rows = new List<HrQuadraturaRowDto>();
        var deptDict = new Dictionary<int, HrQuadraturaDepartmentDto>();

        decimal totPres = 0, totDir = 0, totInt = 0, totAbs = 0;

        foreach (var emp in employees)
        {
            decimal presHours = presenze.GetValueOrDefault(emp.EmployeeId, 0);

            if (!emp.MustPunch && presHours == 0)
            {
                int ggLavorativi = 0;
                for (DateTime dt = primo; dt <= ultimo; dt = dt.AddDays(1))
                    if (!TimesheetRules.IsHoliday(dt) && dt < DateTime.Today) ggLavorativi++;
                presHours = ggLavorativi * emp.DailyHours;
            }

            timesheet.TryGetValue(emp.EmployeeId, out var tsData);
            decimal dirHours = Math.Round(tsData.DirectHours, 1);
            decimal intHours = Math.Round(tsData.InternalHours, 1);
            decimal absHours = Math.Round(assenzePerEmp.GetValueOrDefault(emp.EmployeeId, 0), 1);
            decimal totTs = dirHours + intHours;
            decimal diff = Math.Round(totTs - presHours, 1);
            decimal cov = presHours > 0 ? Math.Round((totTs / presHours) * 100m, 1) : 100m;

            var riga = new HrQuadraturaRowDto
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = emp.EmployeeName,
                DepartmentName = emp.DepartmentName,
                PresenzeHours = presHours,
                DirectTimesheetHours = dirHours,
                InternalTimesheetHours = intHours,
                AbsenceHours = absHours,
                TotalTimesheetHours = totTs,
                DifferenceHours = diff,
                CoveragePercent = cov
            };
            rows.Add(riga);

            totPres += presHours;
            totDir += dirHours;
            totInt += intHours;
            totAbs += absHours;

            if (!deptDict.TryGetValue(emp.DepartmentId, out var dept))
            {
                dept = new HrQuadraturaDepartmentDto
                {
                    DepartmentId = emp.DepartmentId,
                    DepartmentName = emp.DepartmentName,
                };
                deptDict[emp.DepartmentId] = dept;
            }
            dept.TotalPresenzeHours += presHours;
            dept.TotalDirectHours += dirHours;
            dept.TotalInternalHours += intHours;
            dept.TotalAbsenceHours += absHours;
            dept.TotalTimesheetHours += totTs;
            dept.DifferenceHours += diff;
        }

        foreach (var d in deptDict.Values)
        {
            d.CoveragePercent = d.TotalPresenzeHours > 0
                ? Math.Round((d.TotalTimesheetHours / d.TotalPresenzeHours) * 100m, 1)
                : 100m;
        }

        decimal totalTimesheet = totDir + totInt;
        decimal overallCov = totPres > 0 ? Math.Round((totalTimesheet / totPres) * 100m, 1) : 100m;

        return new HrQuadraturaMonthDto
        {
            Year = year,
            Month = month,
            Rows = rows,
            Departments = deptDict.Values.OrderBy(d => d.DepartmentName).ToList(),
            TotalPresenzeHours = totPres,
            TotalDirectHours = totDir,
            TotalInternalHours = totInt,
            TotalAbsenceHours = totAbs,
            TotalTimesheetHours = totalTimesheet,
            OverallCoveragePercent = overallCov
        };
    }

    /// <summary>
    /// Ferie approvate → riga FERIE nel planner Risorse; rifiutate → via. In approvazione si
    /// cerca una FERIE dello stesso dipendente che si <b>sovrappone</b> al periodo, non una con
    /// le date identiche (PIANO-SYNC-RISORSE.md §8, «doppie ferie»): se la ferie è già nel piano
    /// — magari messa dal VPS con date più larghe — non se ne mette una seconda, che farebbe due
    /// barre in conflitto su entrambi i lati. L'<c>updated_at</c> si valorizza (prima restava
    /// NULL) perché il motore di sincronizzazione lo usa per decidere chi vince in un conflitto.
    /// Internal, non private, per il test.
    /// </summary>
    internal static void SyncToResourcePlanner(
        MySqlConnection c, int employeeId, DateTime dateFrom, DateTime dateTo, string absenceType, bool isApproved)
    {
        if (!string.Equals(absenceType, "VACATION", StringComparison.OrdinalIgnoreCase)) return;

        int tableExists = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = 'res_assignments'");
        if (tableExists == 0) return;

        if (isApproved)
        {
            int sovrapposte = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM res_assignments
                WHERE employee_id = @EmployeeId AND tipo = 'FERIE'
                  AND data_inizio <= @DateTo AND data_fine >= @DateFrom",
                new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });

            if (sovrapposte == 0)
            {
                c.Execute(@"
                    INSERT INTO res_assignments
                        (employee_id, tipo, data_inizio, data_fine, descrizione, created_at, updated_at)
                    VALUES
                        (@EmployeeId, 'FERIE', @DateFrom, @DateTo, 'Ferie approvate (HR)', NOW(), NOW())",
                    new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });
            }
        }
        else
        {
            c.Execute(@"
                DELETE FROM res_assignments
                WHERE employee_id = @EmployeeId AND tipo = 'FERIE'
                  AND data_inizio = @DateFrom AND data_fine = @DateTo",
                new { EmployeeId = employeeId, DateFrom = dateFrom.Date, DateTo = dateTo.Date });
        }
    }
}
