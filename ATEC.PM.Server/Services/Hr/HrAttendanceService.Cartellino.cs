using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Cartellino» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── CARTELLINO MENSILE ────────────────────────────────────────────────────

    public HrMonthlyTimesheetDto GetMonthlyTimesheet(int employeeId, int year, int month)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var dipendente = c.QueryFirstOrDefault<(string Name, string? EcosCode, bool MustPunch, decimal DailyHours)>(
            @"SELECT CONCAT_WS(' ', first_name, last_name) AS Name, ecos_empl_code AS EcosCode,
                     hr_must_punch AS MustPunch, hr_daily_hours AS DailyHours
              FROM employees WHERE id = @Id", new { Id = employeeId });

        // 🪤 Ogni colonna vuole il suo alias: Dapper NON abbina `work_date` a `WorkDate`
        // (`MatchNamesWithUnderscores` qui non è attivo). Senza alias la data resta a
        // DateTime.MinValue su OGNI riga, e il `ToDictionary` qui sotto muore al secondo
        // giorno con «An item with the same key has already been added: 01/01/0001» —
        // cioè il cartellino risponde 500 appena una persona ha due giornate.
        var giornate = c.Query<DayRow>(
                @"SELECT work_date AS WorkDate,
                         clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1,
                         clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                         regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                         break_minutes AS BreakMinutes, bands_json AS BandsJson,
                         note AS Note, has_anomaly AS HasAnomaly
                  FROM hr_days
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(g => g.WorkDate.Date);

        var timbrature = c.Query<PunchRow>(
                @"SELECT t.id AS Id, t.work_date AS WorkDate, t.punched_at AS PunchedAt,
                         t.direction AS Direction, t.source AS Source, t.reason AS Reason,
                         CONCAT_WS(' ', e.first_name, e.last_name) AS CreatedBy
                  FROM hr_punches t
                  LEFT JOIN employees e ON e.id = t.created_by
                  WHERE t.employee_id = @Id AND t.work_date BETWEEN @Da AND @A
                  ORDER BY t.punched_at",
                // 🪤 Un giorno in più da ogni parte: serve a riconoscere il turno di notte
                // a cavallo del 1° e dell'ultimo del mese. Il ciclo resta sulle sole
                // giornate del mese, quindi i due giorni in più non finiscono a video.
                new { Id = employeeId, Da = primo.AddDays(-1), A = ultimo.AddDays(1) })
            .GroupBy(t => t.WorkDate.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Assenze approvate del mese
        var assenze = c.Query<HrAbsenceDto>(
                @"SELECT a.id, a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                         a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType, a.status AS Status
                  FROM hr_absences a
                  WHERE a.employee_id = @Id AND a.status = 'APPROVED'
                    AND a.date_from <= @A AND a.date_to >= @Da",
                new { Id = employeeId, Da = primo, A = ultimo }).ToList();

        var assenzeGiorno = new Dictionary<DateTime, HrAbsenceDto>();
        foreach (var a in assenze)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                assenzeGiorno[dt.Date] = a;
        }

        // Solleciti già chiesti nel mese: il tooltip del pulsante 📧 dice QUANDO (come
        // GetLastMailSent nell'originale), non solo che è già stato mandato.
        var solleciti = c.Query<(DateTime WorkDate, DateTime SentAt)>(
                @"SELECT work_date AS WorkDate, sent_at AS SentAt
                  FROM hr_reminders
                  WHERE employee_id = @Id AND work_date BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(x => x.WorkDate.Date, x => x.SentAt);

        DateTime oggi = DateTime.Today;

        var dto = new HrMonthlyTimesheetDto
        {
            EmployeeId = employeeId,
            EmployeeName = dipendente.Name ?? "",
            Year = year,
            Month = month,
            EcosLinked = !string.IsNullOrWhiteSpace(dipendente.EcosCode),
        };

        for (DateTime work_date = primo; work_date <= ultimo; work_date = work_date.AddDays(1))
        {
            bool isHoliday = TimesheetRules.IsHoliday(work_date);
            var riga = new HrDayDto
            {
                WorkDate = work_date,
                IsHoliday = isHoliday,
            };

            if (giornate.TryGetValue(work_date, out DayRow? g))
            {
                bool nonCalcolabile = g.Note.StartsWith("⚠ ERR");
                riga.HasData = true;
                riga.ClockIn1 = g.ClockIn1 ?? "";
                riga.ClockOut1 = g.ClockOut1 ?? "";
                riga.ClockIn2 = g.ClockIn2 ?? "";
                riga.ClockOut2 = g.ClockOut2 ?? "";
                riga.RegularHours = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.RegularMinutes);
                riga.Overtime = nonCalcolabile ? "---" : TimesheetRules.FormatDuration(g.OvertimeMinutes);
                riga.BreakTime = TimesheetRules.FormatDuration(g.BreakMinutes);
                riga.Bands = LeggiFasce(g.BandsJson);
                riga.Note = g.Note;
                riga.HasAnomaly = g.HasAnomaly;
            }
            else if (assenzeGiorno.TryGetValue(work_date, out HrAbsenceDto? abs))
            {
                riga.HasData = true;
                riga.RegularHours = "0h 0m";
                riga.Overtime = "0h 0m";
                riga.BreakTime = "0h 0m";
                riga.Note = abs.IsFullDay ? abs.AbsenceType : $"{abs.AbsenceType} ({abs.Hours}h)";
            }
            else if (!dipendente.MustPunch && !isHoliday && work_date < DateTime.Today)
            {
                // Forfait su giorno passato
                riga.HasData = true;
                int minutiForfait = (int)(dipendente.DailyHours * 60m);
                riga.RegularHours = TimesheetRules.FormatDuration(minutiForfait);
                riga.Overtime = "0h 0m";
                riga.BreakTime = "0h 0m";
                riga.Note = "FORFAIT";
            }

            if (timbrature.TryGetValue(work_date, out List<PunchRow>? grezze))
            {
                riga.Punches = grezze.Select(t => new HrPunchDto
                {
                    Id = t.Id,
                    PunchedAt = t.PunchedAt,
                    Direction = t.Direction,
                    Source = t.Source,
                    Reason = t.Reason,
                    CreatedBy = t.CreatedBy,
                }).ToList();

                // Grezzo e normalizzato non stanno su hr_days — là c'è il risultato — ma
                // si ottengono ripassando le timbrature nel motore, che è puro: nessuna
                // scrittura, e per un mese sono trentun giornate.
                // La configurazione della persona qui non serve: incide sullo straordinario,
                // e i due stadi sono solo orari e somme.
                TimesheetDay stadi = TimesheetEngine.Calcola(
                    work_date,
                    grezze.Select(t => new RawPunch(t.PunchedAt, t.Direction, null)),
                    DateTime.Today,
                    null,
                    ContestoNotte(timbrature, work_date));

                riga.Raw = new HrDayStageDto
                {
                    ClockIn1 = stadi.RawEntrata1,
                    ClockOut1 = stadi.RawUscita1,
                    ClockIn2 = stadi.RawEntrata2,
                    ClockOut2 = stadi.RawUscita2,
                    BreakTime = stadi.RawBreak,
                    TotalHours = stadi.RawTotal,
                };
                riga.Normalized = new HrDayStageDto
                {
                    ClockIn1 = stadi.NormEntrata1,
                    ClockOut1 = stadi.NormUscita1,
                    ClockIn2 = stadi.NormEntrata2,
                    ClockOut2 = stadi.NormUscita2,
                    BreakTime = stadi.NormBreak,
                    TotalHours = stadi.NormTotal,
                };
            }

            // La regola sta in un posto solo (HrDayReminder): la usano il pulsante 📧 sulla
            // riga e il filtro «📧 Da segnalare», che così non possono divergere.
            riga.CanRemind = HrDayReminder.Serve(riga.Note, work_date, oggi);
            if (solleciti.TryGetValue(work_date.Date, out DateTime quando))
                riga.LastReminderAt = quando;

            dto.Days.Add(riga);
        }

        return dto;
    }

    // ── SOLLECITO DELLA SINGOLA GIORNATA (voce 1 del port) ────────────────────

    /// <summary>
    /// Il sollecito pronto per una giornata: destinatario, oggetto e corpo integrale, più lo
    /// stato («già chiesto il …»). Il testo lo compone il server, come per quello mensile: la
    /// pagina lo mostra e basta.
    /// </summary>
    /// <param name="firma">
    /// Nome del mittente da mettere in fondo (vuoto = solo la riga dell'ufficio).
    /// </param>
    public HrDayReminderDto GetDayReminder(int employeeId, DateTime date, string firma)
    {
        DateTime giorno = date.Date;
        HrMonthlyTimesheetDto mese = GetMonthlyTimesheet(employeeId, giorno.Year, giorno.Month);
        HrDayDto? giornata = mese.Days.FirstOrDefault(g => g.WorkDate.Date == giorno);

        var dto = new HrDayReminderDto
        {
            EmployeeId = employeeId,
            EmployeeName = mese.EmployeeName,
            Date = giorno,
        };

        if (giornata == null)
        {
            dto.Blocco = "Giornata fuori dal mese richiesto.";
            return dto;
        }

        using MySqlConnection c = _db.Open();

        // Il saluto usa il nome di battesimo dalla colonna, come fa il sollecito mensile:
        // ricavarlo tagliando il nome completo al primo spazio sbaglierebbe su «Maria Grazia».
        var recapito = c.QueryFirstOrDefault<(string? Email, string FirstName)>(
            "SELECT email AS Email, COALESCE(first_name, '') AS FirstName FROM employees WHERE id = @Id",
            new { Id = employeeId });
        dto.Email = recapito.Email;
        string saluto = string.IsNullOrWhiteSpace(recapito.FirstName)
            ? mese.EmployeeName
            : recapito.FirstName;

        dto.CanRemind = giornata.CanRemind;
        dto.LastReminderAt = giornata.LastReminderAt;
        dto.Subject = HrDayReminder.Oggetto(giorno);
        dto.Body = HrDayReminder.Corpo(saluto, giorno, giornata, firma);

        if (!giornata.CanRemind)
        {
            dto.Blocco = giorno == DateTime.Today
                ? "La giornata di oggi non si sollecita: è ancora aperta."
                : "Questa giornata non ha anomalie da segnalare.";
        }
        else if (string.IsNullOrWhiteSpace(dto.Email))
        {
            // Stesso messaggio dell'originale («Nessuna email configurata per …»).
            dto.Blocco = $"Nessuna email configurata per {mese.EmployeeName}.";
        }

        return dto;
    }

    /// <summary>
    /// Segna la giornata come sollecitata, conservando anche il testo (M117): è la riga che
    /// la Cronologia Email rilegge. Un secondo sollecito sulla stessa giornata aggiorna.
    /// </summary>
    public void MarkDayReminder(
        int employeeId, DateTime date, string? email, string subject, string body,
        int sentBy, string channel)
    {
        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel, email, subject, body)
            VALUES (@EmployeeId, @WorkDate, @SentBy, @Channel, @Email, @Subject, @Body)
            ON DUPLICATE KEY UPDATE sent_at = NOW(), sent_by = VALUES(sent_by), channel = VALUES(channel),
                                    email = VALUES(email), subject = VALUES(subject), body = VALUES(body)",
            new
            {
                EmployeeId = employeeId,
                WorkDate = date.Date,
                SentBy = sentBy,
                Channel = channel,
                Email = email,
                Subject = subject,
                Body = body,
            });
    }

    // ── CRONOLOGIA EMAIL (voce 6 del port) ───────────────────────────────────

    /// <summary>
    /// Le mail di sollecito di un mese. 🪤 Il mese è quello del <b>giorno di riferimento</b>
    /// (<c>work_date</c>), non della spedizione: come nell'originale, una mail mandata a
    /// settembre per un buco di agosto si cerca sotto agosto.
    /// </summary>
    public HrReminderLogDto GetReminderLog(int year, int month, int? employeeId)
    {
        var primo = new DateTime(year, month, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        string filtro = employeeId.HasValue ? " AND r.employee_id = @EmployeeId" : "";

        using MySqlConnection c = _db.Open();
        List<HrReminderLogRowDto> righe = c.Query<HrReminderLogRowDto>(
            @"SELECT r.id AS Id, r.sent_at AS SentAt, r.employee_id AS EmployeeId,
                     CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                     r.email AS Email, r.work_date AS WorkDate, r.subject AS Subject,
                     r.body AS Body, r.channel AS Channel,
                     CONCAT_WS(' ', a.first_name, a.last_name) AS SentByName
              FROM hr_reminders r
              JOIN employees e ON e.id = r.employee_id
              LEFT JOIN employees a ON a.id = r.sent_by
              WHERE r.work_date BETWEEN @Primo AND @Ultimo" + filtro + @"
              ORDER BY r.sent_at DESC, r.work_date DESC",
            new { Primo = primo, Ultimo = ultimo, EmployeeId = employeeId }).ToList();

        return new HrReminderLogDto { Year = year, Month = month, Rows = righe };
    }
}
