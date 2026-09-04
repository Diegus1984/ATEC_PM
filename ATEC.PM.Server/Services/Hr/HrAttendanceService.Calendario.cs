using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Calendario» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── CALENDARIO MENSILE (PORT DELLA VISTA «Calendario Mensile») ────────────
    //
    // Port di CalendarPage.xaml.vb (CaricaDatiMensili) del progetto Timbrature: una riga
    // per VOCE — ore ordinarie, le nove fasce della Circolare 12/2024, presenza, ferie,
    // permessi, malattia, infortunio — e non una riga per dipendente. Le regole di colore
    // sono quelle dell'originale, tarate sul campo: non si ritoccano a intuito. Testo,
    // colore e tooltip li decide QUI il server, così la pagina web e il file Excel
    // disegnano la stessa griglia invece di due interpretazioni della stessa cosa.

    /// <summary>Le voci di maggiorazione (nove del VB + le due della fascia b), nell'ordine del CCNL.</summary>
    private static readonly (string VoceType, string Band, string Label)[] VociStraordinario =
    {
        ("STRAORD_A", "A", "STRAORD. 20%"),
        // Fascia b (#145): lavoro notturno ordinario, due percentuali della tabella Confapi.
        ("NOTT_B1", "B1", "NOTT. FINO ALLE 22 25%"),
        ("NOTT_B2", "B2", "NOTT. OLTRE LE 22 35%"),
        ("STRAORD_C", "C", "STRAORD. FEST. 55%"),
        ("STRAORD_D", "D", "STRAORD. FEST. RIP. 10%"),
        ("STRAORD_E", "E", "STRAORD. FEST. >8h 55%"),
        ("STRAORD_F", "F", "STRAORD. FEST. RIP. >8h 35%"),
        ("STRAORD_G", "G", "STRAORD. NOTT. 50/60%"),
        ("STRAORD_H", "H", "NOTT. FEST. 35%"),
        ("STRAORD_L", "L", "STRAORD. NOTT. FEST. 75%"),
        ("STRAORD_M", "M", "STRAORD. NOTT. FEST. RIP. 55%"),
    };

    /// <summary>Etichette del dettaglio straordinario nel tooltip (BuildStraordDetail).</summary>
    private static readonly Dictionary<string, string> EtichetteFasceTooltip = new()
    {
        ["A"] = "20%", ["B1"] = "Nott.<22 25%", ["B2"] = "Nott.>22 35%",
        ["C"] = "Fest.55%", ["D"] = "Fest.Rip.10%", ["E"] = "Fest.>8h 55%",
        ["F"] = "Fest.Rip.>8h 35%", ["G"] = "Nott.50/60%", ["H"] = "Nott.Fest.35%",
        ["L"] = "Nott.Fest.75%", ["M"] = "Nott.Fest.Rip.55%",
    };

    public HrMonthlyCalendarDto GetMonthlyCalendar(int year, int month, int? departmentId)
    {
        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        int daysInMonth = ultimo.Day;
        DateTime oggi = DateTime.Today;

        using MySqlConnection c = _db.Open();

        var p = new DynamicParameters();
        p.Add("Primo", primo);
        p.Add("Ultimo", ultimo);

        string empSql = @"
            SELECT DISTINCT e.id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS EmployeeName,
                   d.name AS DepartmentName,
                   e.ecos_empl_code AS EmplCode,
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

        var employees = c.Query<CalendarEmployee>(empSql, p).ToList();

        var days = c.Query<DayRow>(@"
            SELECT employee_id AS EmployeeId, work_date AS WorkDate,
                   clock_in_1 AS ClockIn1, clock_out_1 AS ClockOut1, clock_in_2 AS ClockIn2, clock_out_2 AS ClockOut2,
                   regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes,
                   break_minutes AS BreakMinutes, bands_json AS BandsJson, note AS Note, has_anomaly AS HasAnomaly
            FROM hr_days
            WHERE work_date BETWEEN @Primo AND @Ultimo", p)
            .GroupBy(d => (d.EmployeeId, d.WorkDate.Date))
            .ToDictionary(g => g.Key, g => g.First());

        // `source` serve al colore: un'assenza che arriva da Ecos è già approvata là (TEAL),
        // una nostra è ancora roba interna (BLUE/ORANGE/PURPLE/YELLOW per causale).
        var absences = c.Query<CalendarAbsence>(@"
            SELECT a.employee_id AS EmployeeId, a.date_from AS DateFrom, a.date_to AS DateTo,
                   a.hours AS Hours, a.is_full_day AS IsFullDay, a.absence_type AS AbsenceType,
                   a.status AS Status, a.source AS Source
            FROM hr_absences a
            WHERE a.status IN ('APPROVED', 'PENDING')
              AND a.date_from <= @Ultimo AND a.date_to >= @Primo", p).ToList();

        var absencesMap = new Dictionary<(int EmployeeId, DateTime WorkDate), CalendarAbsence>();
        foreach (var a in absences)
        {
            DateTime start = a.DateFrom < primo ? primo : a.DateFrom;
            DateTime end = a.DateTo > ultimo ? ultimo : a.DateTo;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
                absencesMap[(a.EmployeeId, dt.Date)] = a;
        }

        var result = new HrMonthlyCalendarDto
        {
            Year = year,
            Month = month,
            DaysInMonth = daysInMonth,
            Employees = employees
                .Select(e => new HrCalendarEmployeeDto { Id = e.EmployeeId, Name = e.EmployeeName })
                .ToList(),
        };

        for (int giorno = 1; giorno <= daysInMonth; giorno++)
        {
            var dt = new DateTime(year, month, giorno);
            result.DayLabels[giorno] = NomeGiorno(dt);
            result.NonWorkingDays[giorno] = dt.DayOfWeek == DayOfWeek.Saturday || TimesheetRules.IsHoliday(dt);
        }

        foreach (CalendarEmployee emp in employees)
        {
            // Il nome (con la matricola) sta SOLO sulla prima riga: sotto è la stessa persona.
            string etichetta = string.IsNullOrEmpty(emp.EmplCode)
                ? emp.EmployeeName
                : $"{emp.EmployeeName}\nMatr. {emp.EmplCode}";

            HrCalendarRowDto NuovaRiga(string voce, string voceType, string nome = "") => new()
            {
                EmployeeId = emp.EmployeeId,
                Employee = nome,
                EmployeeKey = emp.EmployeeName,
                DepartmentName = emp.DepartmentName,
                Voce = voce,
                VoceType = voceType,
            };

            HrCalendarRowDto rowOrd = NuovaRiga("ORE ORDINARIE", "ORE_ORDINARIE", etichetta);
            var straordRows = VociStraordinario.ToDictionary(
                v => v.Band, v => NuovaRiga(v.Label, v.VoceType));
            HrCalendarRowDto rowPres = NuovaRiga("PRESENZA", "PRESENZA");
            HrCalendarRowDto rowFerie = NuovaRiga("FERIE", "FERIE");
            HrCalendarRowDto rowPerm = NuovaRiga("PERMESSI", "PERMESSI");
            HrCalendarRowDto rowMal = NuovaRiga("MALATTIA", "MALATTIA");
            HrCalendarRowDto rowInf = NuovaRiga("INFORTUNIO", "INFORTUNIO");

            HrCalendarRowDto[] righeFisse = { rowOrd, rowPres, rowFerie, rowPerm, rowMal, rowInf };

            for (int giorno = 1; giorno <= daysInMonth; giorno++)
            {
                var data = new DateTime(year, month, giorno);
                bool isSabato = data.DayOfWeek == DayOfWeek.Saturday;
                bool isFestivo = isSabato || TimesheetRules.IsHoliday(data);

                days.TryGetValue((emp.EmployeeId, data), out DayRow? dayData);
                absencesMap.TryGetValue((emp.EmployeeId, data), out CalendarAbsence? dayAbsence);

                // Chi non timbra non ha righe in hr_days: il VB gli generava i «record
                // forfait» prima di disegnare (GenerateForfaitRecords), altrimenti ogni sua
                // giornata risulterebbe mancante. Qui la giornata piena si finge alla stessa
                // maniera, senza scriverla da nessuna parte.
                bool forfait = dayData == null && dayAbsence == null && !emp.MustPunch
                               && !isFestivo && data <= oggi;

                if (isFestivo && !isSabato)
                {
                    // ── Domeniche e festivi: grigio su tutto, e lo straordinario se ha lavorato
                    Colora(righeFisse, straordRows, giorno, "GRAY");
                    if (dayData != null)
                    {
                        PopolaStraordinario(straordRows, dayData, giorno);
                        string tip = Tooltip(emp.EmployeeName, data, dayData, "Festivo");
                        foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;
                        Scrivi(rowPres, giorno, "P", "GREEN", tip);
                    }
                }
                else if (isSabato)
                {
                    // ── Sabato: grigio, ma è tutto straordinario se ha lavorato
                    Colora(righeFisse, straordRows, giorno, "GRAY");
                    if (dayData != null)
                    {
                        PopolaStraordinario(straordRows, dayData, giorno);
                        string tip = Tooltip(emp.EmployeeName, data, dayData, "Sabato");
                        foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;
                        Scrivi(rowPres, giorno, "P", "GREEN", tip);
                    }
                }
                else if (dayData != null || forfait)
                {
                    // ── Giorno feriale lavorato
                    int minutiOrd = forfait ? (int)(emp.DailyHours * 60m) : dayData!.RegularMinutes;
                    int minutiStraord = forfait ? 0 : dayData!.OvertimeMinutes;

                    Scrivi(rowOrd, giorno, OreTesto(minutiOrd), "GREEN");
                    if (dayData != null) PopolaStraordinario(straordRows, dayData, giorno);

                    string tip = forfait
                        ? $"{emp.EmployeeName} — {data:dd/MM/yyyy}\nForfait: {emp.DailyHours:0.#}h"
                        : Tooltip(emp.EmployeeName, data, dayData!, null);
                    Cella(rowOrd, giorno).Tooltip = tip;
                    foreach (var r in straordRows.Values) Cella(r, giorno).Tooltip = tip;

                    Scrivi(rowPres, giorno, "P", "GREEN", tip);

                    // Ore mancanti: se la giornata non è piena, o la copre un permesso o è rossa.
                    decimal oreLavorate = (decimal)(minutiOrd + minutiStraord) / 60m;
                    decimal oreMancanti = emp.DailyHours - oreLavorate;
                    bool anomalia = dayData?.HasAnomaly == true;

                    // 🪤 Mezzo turno di notte non è mezza giornata mancante: le ore stanno
                    // tutte lì, spartite fra i due giorni che la mezzanotte separa. Senza
                    // questo un turno di notte regolare tinge di rosso DUE caselle.
                    // Vale SOLO per il rosso: se quel giorno c'è anche un permesso, il ramo
                    // qui sotto deve continuare a scriverlo sulla sua riga. E vale solo per
                    // la giornata che ha CEDUTO le ore: quella che se le è prese ce le ha
                    // tutte, e se le mancano il rosso ci va come sempre.
                    bool mezzaNotte = NightShift.HasHandedHours(dayData?.Note);

                    if (oreMancanti >= 0.25m && dayAbsence != null)
                    {
                        decimal ore = dayAbsence.Hours ?? emp.DailyHours;
                        switch (dayAbsence.AbsenceType)
                        {
                            case "PERMIT":
                                Scrivi(rowPerm, giorno, Ore(ore), "ORANGE");
                                break;
                            case "INJURY":
                                Scrivi(rowInf, giorno, Ore(ore), "YELLOW");
                                break;
                        }

                        // Permesso parziale ma nessuna timbratura vera: va sollecitato.
                        if (oreLavorate < 0.25m && ore < emp.DailyHours)
                        {
                            Scrivi(rowPres, giorno, "?", "RED", TooltipPermessoScoperto(emp.EmployeeName, data, ore));
                            Scrivi(rowOrd, giorno, "", "RED");
                        }
                    }
                    else if ((oreMancanti >= 0.25m && !mezzaNotte) || anomalia)
                    {
                        Cella(rowPres, giorno).Color = "RED";

                        // Il VB non aveva un flag di anomalia: il rosso lo deduceva dalle ore
                        // mancanti. Il nostro motore invece la marca (timbratura dispari,
                        // uscita che manca): una giornata così va guardata, e col «?» si vede.
                        if (anomalia)
                        {
                            Scrivi(rowPres, giorno, "?", "RED", tip);
                            Cella(rowOrd, giorno).Color = "RED";
                        }
                    }
                }
                else if (dayAbsence != null)
                {
                    // ── Giorno di assenza piena
                    decimal ore = dayAbsence.Hours ?? emp.DailyHours;
                    bool daEcos = string.Equals(dayAbsence.Source, "ECOS", StringComparison.OrdinalIgnoreCase);
                    string tipAss = $"{emp.EmployeeName} — {data:dd/MM/yyyy}\n{Causale(dayAbsence.AbsenceType)}"
                                    + $" {Ore(ore)}h ({dayAbsence.Status})";

                    switch (dayAbsence.AbsenceType)
                    {
                        case "VACATION":
                            Scrivi(rowFerie, giorno, Ore(ore), daEcos ? "TEAL" : "BLUE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "BLUE", tipAss);
                            break;
                        case "PERMIT":
                            Scrivi(rowPerm, giorno, Ore(ore), daEcos ? "TEAL" : "ORANGE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "ORANGE", tipAss);
                            break;
                        case "SICKNESS":
                            Scrivi(rowMal, giorno, Ore(ore), daEcos ? "TEAL" : "PURPLE", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "PURPLE", tipAss);
                            break;
                        case "INJURY":
                            Scrivi(rowInf, giorno, Ore(ore), daEcos ? "TEAL" : "YELLOW", tipAss);
                            Scrivi(rowPres, giorno, "", daEcos ? "TEAL" : "YELLOW", tipAss);
                            break;
                    }

                    // Assenza parziale senza timbrature: mezza giornata scoperta.
                    if (ore < emp.DailyHours)
                    {
                        Scrivi(rowPres, giorno, "?", "RED", TooltipPermessoScoperto(emp.EmployeeName, data, ore));
                        Cella(rowOrd, giorno).Color = "RED";
                    }
                }
                else if (!isFestivo && data < oggi)
                {
                    // ── Giorno feriale passato senza niente: è un buco, e si vede
                    Scrivi(rowPres, giorno, "?", "RED");
                    Colora(righeFisse.Where(r => r != rowPres).ToArray(), straordRows, giorno, "RED");
                }
            }

            rowOrd.Total = Totale(rowOrd);
            rowFerie.Total = Totale(rowFerie);
            rowPerm.Total = Totale(rowPerm);
            rowMal.Total = Totale(rowMal);
            rowInf.Total = Totale(rowInf);

            result.Rows.Add(rowOrd);

            // Le righe di straordinario compaiono solo dove c'è davvero straordinario:
            // nove righe vuote per persona renderebbero la griglia illeggibile.
            foreach (var (_, band, _) in VociStraordinario)
            {
                HrCalendarRowDto riga = straordRows[band];
                riga.Total = Totale(riga);
                if (riga.Days.Values.Any(d => !string.IsNullOrEmpty(d.Text)))
                    result.Rows.Add(riga);
            }

            result.Rows.Add(rowPres);
            result.Rows.Add(rowFerie);
            result.Rows.Add(rowPerm);
            result.Rows.Add(rowMal);
            result.Rows.Add(rowInf);
        }

        return result;
    }

    // ── Aiuti del calendario ──────────────────────────────────────────────────

    private static HrCalendarCellDto Cella(HrCalendarRowDto riga, int giorno)
    {
        if (!riga.Days.TryGetValue(giorno, out HrCalendarCellDto? cella))
        {
            cella = new HrCalendarCellDto();
            riga.Days[giorno] = cella;
        }
        return cella;
    }

    private static void Scrivi(HrCalendarRowDto riga, int giorno, string testo, string colore, string? tooltip = null)
    {
        HrCalendarCellDto cella = Cella(riga, giorno);
        cella.Text = testo;
        cella.Color = colore;
        if (tooltip != null) cella.Tooltip = tooltip;
    }

    private static void Colora(
        IEnumerable<HrCalendarRowDto> righe,
        Dictionary<string, HrCalendarRowDto> straordinario,
        int giorno,
        string colore)
    {
        foreach (HrCalendarRowDto r in righe) Cella(r, giorno).Color = colore;
        foreach (HrCalendarRowDto r in straordinario.Values) Cella(r, giorno).Color = colore;
    }

    private static void PopolaStraordinario(
        Dictionary<string, HrCalendarRowDto> righe, DayRow dayData, int giorno)
    {
        Dictionary<string, string> fasce = LeggiFasce(dayData.BandsJson);
        foreach (var (_, band, _) in VociStraordinario)
        {
            if (!fasce.TryGetValue(band, out string? valore)) continue;
            int minuti = MinutesFrom(valore);
            if (minuti > 0) Scrivi(righe[band], giorno, OreTesto(minuti), "ORANGE");
        }
    }

    /// <summary>Ore come le scrive il VB: «7,5» → «7.5», «8h 0m» → «8», zero → vuoto.</summary>
    private static string OreTesto(int minuti) =>
        minuti <= 0 ? "" : (minuti / 60.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Ore(decimal ore) => ore.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Somma i numeri della riga (salta «P» e «?»), come AppUtils.CalcolaTotale.</summary>
    private static string Totale(HrCalendarRowDto riga)
    {
        double totale = 0;
        foreach (HrCalendarCellDto cella in riga.Days.Values)
        {
            if (string.IsNullOrEmpty(cella.Text) || cella.Text is "P" or "?") continue;
            if (double.TryParse(cella.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                totale += v;
        }
        return totale > 0 ? totale.ToString("0.#", CultureInfo.InvariantCulture) + "h" : "";
    }

    private static string NomeGiorno(DateTime dt) => dt.DayOfWeek switch
    {
        DayOfWeek.Monday => "L",
        DayOfWeek.Tuesday => "Ma",
        DayOfWeek.Wednesday => "Me",
        DayOfWeek.Thursday => "G",
        DayOfWeek.Friday => "V",
        DayOfWeek.Saturday => "S",
        DayOfWeek.Sunday => "D",
        _ => "",
    };

    private static string Causale(string absenceType) => absenceType switch
    {
        "VACATION" => "FERIE",
        "PERMIT" => "PERMESSO",
        "SICKNESS" => "MALATTIA",
        "INJURY" => "INFORTUNIO",
        _ => absenceType,
    };

    private static string TooltipPermessoScoperto(string nome, DateTime data, decimal ore) =>
        $"{nome} — {data:dd/MM/yyyy}\n⚠ Permesso parziale {Ore(ore)}h ma nessuna timbratura\n"
        + "Sollecitare inserimento timbrature o estendere a giornata intera";

    private static string Tooltip(string nome, DateTime data, DayRow g, string? tipoGiorno)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(nome).Append(" — ").Append(data.ToString("dd/MM/yyyy"));
        if (tipoGiorno != null) sb.Append(" (").Append(tipoGiorno).Append(')');
        sb.Append('\n');
        sb.Append("E1: ").Append(Orario(g.ClockIn1)).Append("  U1: ").Append(Orario(g.ClockOut1)).Append('\n');
        if (!string.IsNullOrEmpty(g.ClockIn2) && g.ClockIn2 != "--:--")
            sb.Append("E2: ").Append(Orario(g.ClockIn2)).Append("  U2: ").Append(Orario(g.ClockOut2)).Append('\n');

        if (tipoGiorno == null)
        {
            sb.Append("Pausa: ").Append(TimesheetRules.FormatDuration(g.BreakMinutes)).Append('\n');
            sb.Append("Ore: ").Append(TimesheetRules.FormatDuration(g.RegularMinutes));
        }

        string dettaglio = DettaglioStraordinario(g);
        if (dettaglio.Length > 0) sb.Append(tipoGiorno == null ? "\n" : "").Append("Straord:").Append(dettaglio);

        if (!string.IsNullOrEmpty(g.Note) && g.Note != "OK") sb.Append("\nNote: ").Append(g.Note);
        return sb.ToString();
    }

    private static string Orario(string? valore) => string.IsNullOrEmpty(valore) ? "--:--" : valore;

    private static string DettaglioStraordinario(DayRow g)
    {
        Dictionary<string, string> fasce = LeggiFasce(g.BandsJson);
        var parti = new List<string>();
        foreach (var (_, band, _) in VociStraordinario)
        {
            if (!fasce.TryGetValue(band, out string? valore) || MinutesFrom(valore) <= 0) continue;
            parti.Add($" {EtichetteFasceTooltip[band]}: {valore}");
        }
        return string.Join(" |", parti);
    }

    private sealed class CalendarEmployee
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? DepartmentName { get; set; }
        public string? EmplCode { get; set; }
        public bool MustPunch { get; set; } = true;
        public decimal DailyHours { get; set; } = 8.0m;
    }

    private sealed class CalendarAbsence
    {
        public int EmployeeId { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal? Hours { get; set; }
        public bool IsFullDay { get; set; }
        public string AbsenceType { get; set; } = "";
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
    }

    // ── #132 GIUSTIFICAZIONE DELLE ORE MANCANTI (clic su cella del calendario) ─
    //
    // Port di `dgCalendar_MouseDoubleClick` + `CausaleDialog` del programma «Timbrature»:
    // si apre la giornata scoperta, si vedono le ore che mancano e si sceglie la causale
    // che le copre. Le regole (quali causali, quante ore) le decide QUI il server: la
    // pagina disegna quello che le viene detto, non una seconda interpretazione.
    //
    // 🪤 Nell'originale queste giornate erano righe di `Absences`; qui sono righe di
    // `hr_absences`, che è la stessa tabella delle richieste ferie della Fase 2. Ne segue
    // una regola che nel VB non serviva: **una causale scritta da qui copre un giorno
    // solo**. Se sulla giornata c'è già un'assenza che viene da una richiesta a più giorni
    // (o da Ecos) non la si tocca da qui — spezzarla in silenzio lascerebbe la richiesta
    // approvata diversa da quello che è stato approvato.
    //
    // 🪤 Chi giustifica NON deve essere per forza un'altra persona (la regola del «secondo
    // occhio» vale per le rettifiche, che riscrivono le timbrature). Qui si dichiara una
    // causale su una giornata già passata, ed è quello che l'ufficio fa da anni col
    // programma originale: aggiungere il divieto vorrebbe dire cambiargli il lavoro.

    /// <summary>
    /// Cosa si può fare sulla giornata cliccata: quante ore mancano, quali causali sono
    /// ammesse, cosa c'è già scritto. <see cref="HrGiustificaInfoDto.Blocco"/> valorizzato
    /// = non si giustifica, e dentro c'è il perché.
    /// </summary>
    public HrGiustificaInfoDto GetGiustificaInfo(int employeeId, DateTime data)
    {
        DateTime giorno = data.Date;
        var info = new HrGiustificaInfoDto { EmployeeId = employeeId, Date = giorno };

        using MySqlConnection c = _db.Open();

        var emp = c.QueryFirstOrDefault<CalendarEmployee>(@"
            SELECT id AS EmployeeId,
                   CONCAT_WS(' ', first_name, last_name) AS EmployeeName,
                   hr_must_punch AS MustPunch, hr_daily_hours AS DailyHours
            FROM employees WHERE id = @Id AND status <> 'TERMINATED'",
            new { Id = employeeId });

        if (emp == null)
        {
            info.Blocco = "Dipendente non trovato o cessato.";
            return info;
        }

        info.EmployeeName = emp.EmployeeName;
        info.DailyHours = emp.DailyHours;

        // Le due porte dell'originale: solo giorni già passati, e mai i non lavorativi.
        if (giorno >= DateTime.Today)
        {
            info.Blocco = "Si giustificano solo le giornate già passate.";
            return info;
        }
        if (giorno.DayOfWeek == DayOfWeek.Saturday || TimesheetRules.IsHoliday(giorno))
        {
            info.Blocco = "Giornata non lavorativa: non c'è niente da giustificare.";
            return info;
        }

        // Quello che risulta già scritto sulla giornata. Se ci fossero due assenze
        // sovrapposte vince quella di un giorno solo: è la nostra, quella modificabile.
        var assenza = c.QueryFirstOrDefault<GiustificaAssenza>(@"
            SELECT id AS Id, date_from AS DateFrom, date_to AS DateTo, hours AS Hours,
                   absence_type AS AbsenceType, source AS Source, status AS Status
            FROM hr_absences
            WHERE employee_id = @Id AND status IN ('APPROVED', 'PENDING')
              AND date_from <= @G AND date_to >= @G
            ORDER BY (date_from = date_to) DESC, id DESC
            LIMIT 1",
            new { Id = employeeId, G = giorno });

        var day = c.QueryFirstOrDefault<GiustificaGiornata>(@"
            SELECT regular_minutes AS RegularMinutes, overtime_minutes AS OvertimeMinutes
            FROM hr_days WHERE employee_id = @Id AND work_date = @G",
            new { Id = employeeId, G = giorno });

        if (assenza != null)
        {
            info.CausaleCorrente = HrCausali.Codice(assenza.AbsenceType);
            info.OreCorrenti = assenza.Hours ?? emp.DailyHours;

            if (string.Equals(assenza.Source, "ECOS", StringComparison.OrdinalIgnoreCase))
            {
                // Ecos è il padrone del suo dato: qui si guarda e basta.
                info.Blocco = "L'assenza arriva da Ecos: si corregge là, non da qui.";
                return info;
            }
            if (assenza.DateFrom.Date != assenza.DateTo.Date)
            {
                info.Blocco =
                    $"Coperta da una richiesta dal {assenza.DateFrom:dd/MM/yyyy} al {assenza.DateTo:dd/MM/yyyy}: "
                    + "si modifica dalle Richieste.";
                return info;
            }

            info.PuoRimuovere = true;
        }

        // Timbrature vere = giornata parziale: si può solo completarla (PE o IN). Senza
        // timbrature — assenza piena o forfettario — vale l'elenco intero, come nel VB.
        info.Causali = day != null
            ? new List<string> { HrCausali.Permesso, HrCausali.Infortunio }
            : new List<string> { HrCausali.Ferie, HrCausali.Permesso, HrCausali.Malattia, HrCausali.Infortunio };

        info.OreLavorate = day == null
            ? 0m
            : Math.Round((decimal)(day.RegularMinutes + day.OvertimeMinutes) / 60m, 2);
        info.OreMancanti = Math.Max(0m, emp.DailyHours - info.OreLavorate);

        // Niente da coprire e niente da togliere: è la stessa informazione che dava il
        // messaggio «Nessuna ora da giustificare per questo giorno» dell'originale.
        if (info.OreMancanti <= 0m && !info.PuoRimuovere)
            info.Blocco = "Nessuna ora da giustificare per questo giorno.";

        return info;
    }

    /// <summary>
    /// Scrive (o toglie) la causale della giornata. Torna null se è andata, altrimenti il
    /// motivo — le stesse guardie di <see cref="GetGiustificaInfo"/>, rifatte qui perché
    /// fra l'apertura del dialogo e il salvataggio può essere cambiato tutto.
    /// </summary>
    public string? SaveGiustifica(HrGiustificaRequest req, int autoreId)
    {
        DateTime giorno = req.Date.Date;
        HrGiustificaInfoDto info = GetGiustificaInfo(req.EmployeeId, giorno);
        if (!string.IsNullOrEmpty(info.Blocco)) return info.Blocco;

        string causale = (req.Causale ?? "").Trim().ToUpperInvariant();

        using MySqlConnection c = _db.Open();

        if (causale.Length == 0)
        {
            if (!info.PuoRimuovere) return "Su questa giornata non c'è nessuna causale da togliere.";

            c.Execute(@"DELETE FROM hr_absences
                        WHERE employee_id = @Id AND date_from = @G AND date_to = @G
                          AND source <> 'ECOS'",
                new { Id = req.EmployeeId, G = giorno });
            return null;
        }

        if (!info.Causali.Contains(causale))
        {
            return info.Causali.Count == 2
                ? "La giornata ha timbrature: si può solo completarla con PE (permesso) o IN (infortunio)."
                : "Causale non valida: ammesse FE, PE, MA, IN.";
        }

        string? tipo = HrCausali.TipoAssenza(causale);
        if (tipo == null) return "Causale non valida: ammesse FE, PE, MA, IN.";

        // Ore: quelle chieste se stanno dentro il buco, altrimenti il buco intero — come il
        // dialogo originale, che proponeva sempre e solo le ore mancanti.
        decimal ore = req.Hours is > 0m && req.Hours.Value <= info.OreMancanti
            ? req.Hours.Value
            : info.OreMancanti;
        if (ore <= 0m) return "Nessuna ora da giustificare per questo giorno.";

        bool giornataPiena = ore >= info.DailyHours;

        // 🪤 `created_by`/`approved_by` hanno la chiave esterna su `employees`: un id 0
        // (token senza dipendente collegato) farebbe fallire l'INSERT con un 500 invece che
        // con un messaggio. Null vuol dire «non lo sappiamo», ed è quello che la colonna ammette.
        int? autore = autoreId > 0 ? autoreId : null;

        // Una riga per giornata: se ce n'è già una nostra la si riscrive, non se ne aggiunge
        // una seconda (il calendario ne mostrerebbe una sola e l'altra resterebbe invisibile).
        int aggiornate = c.Execute(@"
            UPDATE hr_absences
               SET absence_type = @Tipo, hours = @Ore, is_full_day = @Piena,
                   status = 'APPROVED', source = 'MANUAL', created_by = @Autore,
                   approved_by = @Autore, approved_at = CURRENT_TIMESTAMP
             WHERE employee_id = @Id AND date_from = @G AND date_to = @G AND source <> 'ECOS'",
            new { Tipo = tipo, Ore = ore, Piena = giornataPiena, Autore = autore, Id = req.EmployeeId, G = giorno });

        if (aggiornate == 0)
        {
            c.Execute(@"
                INSERT INTO hr_absences
                    (employee_id, date_from, date_to, hours, is_full_day, absence_type,
                     status, source, notes, created_by, approved_by, approved_at)
                VALUES
                    (@Id, @G, @G, @Ore, @Piena, @Tipo, 'APPROVED', 'MANUAL',
                     'Giustificazione ore mancanti da Calendario mensile', @Autore, @Autore, CURRENT_TIMESTAMP)",
                new { Id = req.EmployeeId, G = giorno, Ore = ore, Piena = giornataPiena, Tipo = tipo, Autore = autore });
        }

        return null;
    }

    private sealed class GiustificaAssenza
    {
        public int Id { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal? Hours { get; set; }
        public string AbsenceType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private sealed class GiustificaGiornata
    {
        public int RegularMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
    }
}
