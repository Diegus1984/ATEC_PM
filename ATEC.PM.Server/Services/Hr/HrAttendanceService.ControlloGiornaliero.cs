using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Controllo di ieri» di HrAttendanceService (09/09/2026): le giornate di tutti i
// dipendenti che timbrano, in un blocco di giorni, per il controllo del mattino dell'ufficio HR.
public partial class HrAttendanceService
{
    /// <summary>
    /// Le giornate di tutti i dipendenti che timbrano nel blocco che finisce in <paramref name="date"/>
    /// (null = ieri). Il blocco lo decide <see cref="HrControlloGiornaliero"/>: di lunedì è
    /// venerdì-sabato-domenica. Ogni giornata è la stessa <see cref="HrDayDto"/> del cartellino,
    /// composta dalla stessa funzione (<see cref="CostruisciGiornata"/>): la pagina di tutti e quella
    /// della singola persona non possono dire due cose diverse dello stesso giorno.
    ///
    /// <para>Le letture sono cinque in tutto, sull'intero blocco e per tutti: un cartellino per
    /// persona sarebbero trentasette volte le stesse query per tre giorni utili.</para>
    /// </summary>
    public HrDailyCheckDto GetDailyCheck(DateTime? date)
    {
        DateTime oggi = DateTime.Today;
        DateTime fine = (date ?? HrControlloGiornaliero.Predefinito(oggi)).Date;
        // Più avanti di oggi non c'è niente da controllare.
        if (fine > oggi) fine = oggi;
        (DateTime da, DateTime a) = HrControlloGiornaliero.Intervallo(fine);

        using MySqlConnection c = _db.Open();

        // Gli stessi dipendenti dell'elenco laterale del cartellino (/api/employees/real?mustPunch=true):
        // chi è a forfait non timbra e non ha niente da controllare. Il reparto è quello primario,
        // come nel calendario di tutti.
        List<DipendenteControllo> dipendenti = c.Query<DipendenteControllo>(@"
            SELECT e.id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   d.name AS DepartmentName,
                   e.ecos_empl_code AS EcosCode,
                   e.hr_must_punch AS MustPunch,
                   e.hr_daily_hours AS DailyHours
            FROM employees e
            LEFT JOIN employee_departments ed ON ed.employee_id = e.id AND ed.is_primary = 1
            LEFT JOIN departments d ON d.id = ed.department_id
            WHERE e.status = 'ACTIVE'
              AND e.emp_type = 'INTERNAL'
              AND e.user_role <> 'ADMIN'
              AND e.first_name NOT LIKE '[%'
              AND e.hr_must_punch = 1
            ORDER BY e.last_name, e.first_name")
            // Due reparti primari darebbero due righe: la persona resta una.
            .GroupBy(e => e.EmployeeId)
            .Select(g => g.First())
            .ToList();

        var p = new { Da = da, A = a, DaTimbrature = da.AddDays(-1), ATimbrature = a.AddDays(1) };

        // 🪤 Ogni colonna col suo alias (vedi il cartellino): senza, Dapper lascia le date a
        // DateTime.MinValue e i dizionari per giorno saltano.
        Dictionary<int, Dictionary<DateTime, DayRow>> giornate = c.Query<DayRow>(@"
            SELECT employee_id AS EmployeeId, work_date AS WorkDate,
                   clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1,
                   clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                   regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                   break_minutes AS BreakMinutes, bands_json AS BandsJson,
                   note AS Note, has_anomaly AS HasAnomaly
            FROM hr_days
            WHERE work_date BETWEEN @Da AND @A", p)
            .GroupBy(g => g.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.WorkDate.Date));

        // Un giorno in più da ogni parte, come nel cartellino: serve a riconoscere il turno di
        // notte a cavallo del blocco. Le giornate a video restano quelle del blocco.
        Dictionary<int, Dictionary<DateTime, List<PunchRow>>> timbrature = c.Query<PunchRow>(@"
            SELECT t.employee_id AS EmployeeId, t.id AS Id, t.work_date AS WorkDate, t.punched_at AS PunchedAt,
                   t.direction AS Direction, t.source AS Source, t.reason AS Reason,
                   t.external_id AS ExternalId, t.ecos_punched_at AS EcosPunchedAt, t.ecos_sent_at AS EcosSentAt,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS CreatedBy
            FROM hr_punches t
            LEFT JOIN employees e ON e.id = t.created_by
            WHERE t.work_date BETWEEN @DaTimbrature AND @ATimbrature
            ORDER BY t.punched_at", p)
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(t => t.WorkDate.Date).ToDictionary(x => x.Key, x => x.ToList()));

        Dictionary<int, Dictionary<DateTime, List<HrEcosSendDto>>> inviiEcos =
            InviiEcosPerDipendenteEGiorno(c, da, a);

        // Assenze approvate: le nostre a intervallo, poi quelle che Ecos ha spezzato giorno per
        // giorno, che vincono (sono le ore vere di quel giorno) — la stessa regola del cartellino.
        var assenzeGiorno = new Dictionary<int, Dictionary<DateTime, HrAbsenceDto>>();
        Dictionary<DateTime, HrAbsenceDto> AssenzeDi(int employeeId)
        {
            if (!assenzeGiorno.TryGetValue(employeeId, out Dictionary<DateTime, HrAbsenceDto>? perGiorno))
                assenzeGiorno[employeeId] = perGiorno = new Dictionary<DateTime, HrAbsenceDto>();
            return perGiorno;
        }

        foreach (HrAbsenceDto ab in c.Query<HrAbsenceDto>(@"
            SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType, a.status AS Status
            FROM hr_absences a
            WHERE a.status = 'APPROVED' AND a.date_from <= @A AND a.date_to >= @Da", p))
        {
            DateTime start = ab.DateFrom < da ? da : ab.DateFrom;
            DateTime end = ab.DateTo > a ? a : ab.DateTo;
            Dictionary<DateTime, HrAbsenceDto> perGiorno = AssenzeDi(ab.EmployeeId);
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                perGiorno[dt.Date] = ab;
        }

        Dictionary<int, decimal> oreGiornaliere = dipendenti.ToDictionary(e => e.EmployeeId, e => e.DailyHours);
        foreach (var (chiave, g) in AssenzeEcosPerGiorno(c, da, a, anchePending: false))
        {
            if (!oreGiornaliere.TryGetValue(chiave.EmployeeId, out decimal oreContratto)) continue;
            AssenzeDi(chiave.EmployeeId)[chiave.WorkDate] = new HrAbsenceDto
            {
                EmployeeId = chiave.EmployeeId,
                DateFrom = chiave.WorkDate,
                DateTo = chiave.WorkDate,
                Hours = OreAssenzaGiorno(g, oreContratto),
                IsFullDay = GiornataIntera(g, oreContratto),
                AbsenceType = g.AbsenceType,
                Status = g.Status,
                Source = "ECOS",
            };
        }

        Dictionary<int, Dictionary<DateTime, DateTime>> solleciti = c.Query<(int EmployeeId, DateTime WorkDate, DateTime SentAt)>(@"
            SELECT employee_id AS EmployeeId, work_date AS WorkDate, sent_at AS SentAt
            FROM hr_reminders
            WHERE work_date BETWEEN @Da AND @A", p)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.WorkDate.Date, x => x.SentAt));

        var dto = new HrDailyCheckDto
        {
            Date = fine,
            From = da,
            To = a,
            PreviousDate = HrControlloGiornaliero.Precedente(da),
            NextDate = HrControlloGiornaliero.Successivo(a, oggi),
        };
        for (DateTime giorno = da; giorno <= a; giorno = giorno.AddDays(1))
        {
            dto.Days.Add(new HrDailyCheckDayDto
            {
                Date = giorno,
                IsWorkingDay = HrControlloGiornaliero.GiornoLavorativo(giorno),
            });
        }

        foreach (DipendenteControllo emp in dipendenti)
        {
            var profilo = new ProfiloCartellino(emp.MustPunch, emp.DailyHours);
            var riga = new HrDailyCheckEmployeeDto
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = emp.EmployeeName,
                DepartmentName = emp.DepartmentName,
                EcosLinked = !string.IsNullOrWhiteSpace(emp.EcosCode),
            };

            Dictionary<DateTime, DayRow> giornateEmp = giornate.GetValueOrDefault(emp.EmployeeId) ?? new();
            Dictionary<DateTime, List<PunchRow>> timbratureEmp = timbrature.GetValueOrDefault(emp.EmployeeId) ?? new();
            Dictionary<DateTime, HrAbsenceDto> assenzeEmp = assenzeGiorno.GetValueOrDefault(emp.EmployeeId) ?? new();
            Dictionary<DateTime, List<HrEcosSendDto>> inviiEmp = inviiEcos.GetValueOrDefault(emp.EmployeeId) ?? new();
            Dictionary<DateTime, DateTime> sollecitiEmp = solleciti.GetValueOrDefault(emp.EmployeeId) ?? new();

            foreach (HrDailyCheckDayDto giorno in dto.Days)
            {
                riga.Days.Add(CostruisciGiornata(
                    giorno.Date, oggi, profilo, giornateEmp, timbratureEmp, assenzeEmp, inviiEmp, sollecitiEmp));
            }

            dto.Employees.Add(riga);
        }

        return dto;
    }

    private sealed class DipendenteControllo
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? DepartmentName { get; set; }
        public string? EcosCode { get; set; }
        public bool MustPunch { get; set; }
        public decimal DailyHours { get; set; }
    }
}
