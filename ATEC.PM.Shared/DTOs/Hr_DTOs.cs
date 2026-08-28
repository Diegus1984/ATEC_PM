namespace ATEC.PM.Shared.DTOs;

/// <summary>HR attendance and requests module DTOs (PIANO-HR-PRESENZE.md).</summary>
public class HrMonthlyTimesheetDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public bool EcosLinked { get; set; }
    public List<HrDayDto> Days { get; set; } = new();
}

public class HrDayDto
{
    public DateTime WorkDate { get; set; }
    public bool IsHoliday { get; set; }
    public bool HasData { get; set; }
    public string ClockIn1 { get; set; } = "";
    public string ClockOut1 { get; set; } = "";
    public string ClockIn2 { get; set; } = "";
    public string ClockOut2 { get; set; } = "";
    public string RegularHours { get; set; } = "";
    public string Overtime { get; set; } = "";
    public string BreakTime { get; set; } = "";
    public Dictionary<string, string> Bands { get; set; } = new();
    public string Note { get; set; } = "";
    public bool HasAnomaly { get; set; }
    public List<HrPunchDto> Punches { get; set; } = new();

    // ── I due stadi che precedono il cartellino ───────────────────────────────
    //
    // Il ReportPage del programma «Timbrature» mostra tre blocchi di colonne per la stessa
    // giornata — 🔸 grezzo, 🔷 normalizzato, ✅ finale — perché è l'unico modo per vedere
    // DOVE una giornata è cambiata: quanto ha spostato l'arrotondamento, e quanto la pausa
    // dedotta. Si ricalcolano al volo dalle timbrature: non sono un secondo dato salvato.

    /// <summary>🔸 Come sono arrivate dal rilevatore.</summary>
    public HrDayStageDto Raw { get; set; } = new();

    /// <summary>🔷 Dopo l'arrotondamento (scatto 30', tolleranza 10').</summary>
    public HrDayStageDto Normalized { get; set; } = new();
}

/// <summary>Uno stadio della giornata: i quattro orari, la pausa e il totale di quello stadio.</summary>
public class HrDayStageDto
{
    public string ClockIn1 { get; set; } = "--:--";
    public string ClockOut1 { get; set; } = "--:--";
    public string ClockIn2 { get; set; } = "--:--";
    public string ClockOut2 { get; set; } = "--:--";
    public string BreakTime { get; set; } = "0h 0m";
    public string TotalHours { get; set; } = "0h 0m";
}

public class HrPunchDto
{
    public long Id { get; set; }
    public DateTime PunchedAt { get; set; }
    public string Direction { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

public class HrImportResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int PunchesAdded { get; set; }
    public int PunchesUpdated { get; set; }
    public int DaysRecalculated { get; set; }
    public List<string> Unmatched { get; set; } = new();
}

public class HrStatusDto
{
    public bool Configured { get; set; }
    public bool ImportInProgress { get; set; }
    public DateTime? LastImport { get; set; }
    public string LastResult { get; set; } = "";
    public long TotalPunches { get; set; }
    public long TotalDays { get; set; }
    public int LinkedEmployees { get; set; }
    public int ActiveEmployees { get; set; }
}

public class HrMappingRowDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string? EcosEmplCode { get; set; }
}

public class HrBadgeDto
{
    public string EmplCode { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class HrBadgesDto
{
    public bool Configured { get; set; }
    public List<HrBadgeDto> Badges { get; set; } = new();
}

public class HrMappingRequest
{
    public string? EcosEmplCode { get; set; }
}

public class HrAdjustmentRequest
{
    public int EmployeeId { get; set; }
    public DateTime PunchedAt { get; set; }
    public string Direction { get; set; } = "";
    public string Reason { get; set; } = "";
}

// ── ABSENCES & REQUESTS (FASE 2) ──────────────────────────────────────────

public class HrAbsenceDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? DepartmentName { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal? Hours { get; set; }
    public bool IsFullDay { get; set; } = true;
    public string AbsenceType { get; set; } = "VACATION"; // VACATION, PERMIT, SICKNESS, INJURY, OTHER
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED, CANCELLED
    public string Source { get; set; } = "ATEC"; // ATEC, ECOS, MANUAL
    public string? EcosAbsenceId { get; set; }
    public int? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }
    public int? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrCreateAbsenceRequest
{
    public int? EmployeeId { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal? Hours { get; set; }
    public bool IsFullDay { get; set; } = true;
    public string AbsenceType { get; set; } = "VACATION";
    public string? Notes { get; set; }
}

public class HrApproveAbsenceRequest
{
    public bool Approved { get; set; }
    public string? RejectionReason { get; set; }
}

// ── CALENDARIO MENSILE ────────────────────────────────────────────────────
//
// Port della vista «Calendario Mensile» del progetto Timbrature (CalendarPage.xaml.vb):
// la griglia NON ha una riga per dipendente, ne ha una per VOCE — ore ordinarie, le nove
// fasce di straordinario della Circolare 12/2024, presenza, ferie, permessi, malattia,
// infortunio — così le maggiorazioni restano leggibili una per una invece di sommarsi in
// un totale unico. Le celle portano testo, colore e tooltip già decisi dal server: la
// pagina web e l'export Excel disegnano la STESSA griglia, non due interpretazioni.

/// <summary>Una cella del calendario: cosa scrivere, di che colore, cosa dice il tooltip.</summary>
public class HrCalendarCellDto
{
    public string Text { get; set; } = "";

    /// <summary>GRAY · GREEN · RED · ORANGE · BLUE · PURPLE · YELLOW · TEAL (vuoto = nessuno).</summary>
    public string Color { get; set; } = "";

    public string Tooltip { get; set; } = "";
}

/// <summary>Una riga del calendario: una voce di un dipendente lungo tutto il mese.</summary>
public class HrCalendarRowDto
{
    public int EmployeeId { get; set; }

    /// <summary>Nome + matricola: valorizzato SOLO sulla prima riga del dipendente, come nel VB.</summary>
    public string Employee { get; set; } = "";

    /// <summary>Nome pieno su ogni riga: serve al filtro e all'export per dipendente.</summary>
    public string EmployeeKey { get; set; } = "";

    public string? DepartmentName { get; set; }

    /// <summary>Etichetta della voce: «ORE ORDINARIE», «STRAORD. 20%», «FERIE»…</summary>
    public string Voce { get; set; } = "";

    /// <summary>
    /// Tipo della voce: ORE_ORDINARIE, STRAORD_A/C/D/E/F/G/H/L/M, PRESENZA, FERIE,
    /// PERMESSI, MALATTIA, INFORTUNIO. L'ultima chiude il dipendente (riga di separazione).
    /// </summary>
    public string VoceType { get; set; } = "";

    /// <summary>Chiave = numero del giorno (1..31).</summary>
    public Dictionary<int, HrCalendarCellDto> Days { get; set; } = new();

    /// <summary>Somma dei valori numerici della riga, «12,5h»; vuoto se zero.</summary>
    public string Total { get; set; } = "";
}

/// <summary>Voce del filtro «Dipendente» del calendario.</summary>
public class HrCalendarEmployeeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class HrMonthlyCalendarDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DaysInMonth { get; set; }

    /// <summary>Iniziale del giorno della settimana per ogni giorno del mese (L, Ma, Me, G, V, S, D).</summary>
    public Dictionary<int, string> DayLabels { get; set; } = new();

    /// <summary>Giorni non lavorativi (sabato, domenica, festività): intestazione in rosso.</summary>
    public Dictionary<int, bool> NonWorkingDays { get; set; } = new();

    public List<HrCalendarRowDto> Rows { get; set; } = new();

    public List<HrCalendarEmployeeDto> Employees { get; set; } = new();
}

// ── CREDENZIALI ECOS ──────────────────────────────────────────────────────
//
// Le credenziali del programma «Timbrature» si mettevano da dentro l'applicazione, non in
// un file: qui è uguale. Vivono in `res_settings` (chiavi `ecos.*`) con la password cifrata
// come quella SMTP; l'appsettings del server resta come ripiego per chi le ha già là.

public class HrEcosSettingsDto
{
    public string BaseUrl { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ClientId { get; set; } = "";

    /// <summary>Write-only: si scrive per cambiarla, non torna MAI indietro.</summary>
    public string? Password { get; set; }

    /// <summary>true = una password c'è (non si dice quale).</summary>
    public bool HasPassword { get; set; }

    /// <summary>DATABASE = messe da qui; APPSETTINGS = ancora nel file del server.</summary>
    public string Source { get; set; } = "";

    /// <summary>true = utente, password e ClientID ci sono tutti e tre.</summary>
    public bool Configured { get; set; }
}

/// <summary>Esito della prova di collegamento a Ecos (una TokenGet e basta).</summary>
public class HrEcosTestResultDto
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

// ── SOLLECITI TIMBRATURE MANCANTI ─────────────────────────────────────────
//
// Il calendario segna col «?» rosso le giornate feriali senza timbrature né assenze. Il
// sollecito chiede alla persona di sistemarle: o timbra la rettifica, o dichiara la
// causale. Due strade, come nel programma originale — il client di posta (mailto), che
// lascia scegliere cosa scrivere, e l'invio diretto dal server, per farne trenta in fila.

public class HrReminderTargetDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? Email { get; set; }

    /// <summary>I giorni del mese col «?», in ordine.</summary>
    public List<int> MissingDays { get; set; } = new();

    /// <summary>Ultimo sollecito su una di queste giornate: per non ripetersi addosso.</summary>
    public DateTime? LastReminderAt { get; set; }

    public string Subject { get; set; } = "";

    /// <summary>Testo per il client di posta (dice anche di inserire la causale su eTime).</summary>
    public string MailtoBody { get; set; } = "";

    /// <summary>Testo dell'invio diretto.</summary>
    public string Body { get; set; } = "";
}

public class HrRemindersDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<HrReminderTargetDto> Targets { get; set; } = new();

    /// <summary>false = SMTP non configurato: resta solo la strada del client di posta.</summary>
    public bool SmtpEnabled { get; set; }
}

public class HrRemindersResultDto
{
    public int Sent { get; set; }
    public int Failed { get; set; }
    public List<string> WithoutEmail { get; set; } = new();
    public string Message { get; set; } = "";
}

/// <summary>Registra come già sollecitate le giornate aperte nel client di posta.</summary>
public class HrMarkRemindersRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<int> EmployeeIds { get; set; } = new();
}

// ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

public class HrQuadraturaRowDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string? DepartmentName { get; set; }
    public decimal PresenzeHours { get; set; }
    public decimal DirectTimesheetHours { get; set; }
    public decimal InternalTimesheetHours { get; set; }
    public decimal AbsenceHours { get; set; }
    public decimal TotalTimesheetHours { get; set; }
    public decimal DifferenceHours { get; set; }
    public decimal CoveragePercent { get; set; }
}

public class HrQuadraturaDepartmentDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";
    public decimal TotalPresenzeHours { get; set; }
    public decimal TotalDirectHours { get; set; }
    public decimal TotalInternalHours { get; set; }
    public decimal TotalAbsenceHours { get; set; }
    public decimal TotalTimesheetHours { get; set; }
    public decimal DifferenceHours { get; set; }
    public decimal CoveragePercent { get; set; }
}

public class HrQuadraturaMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<HrQuadraturaRowDto> Rows { get; set; } = new();
    public List<HrQuadraturaDepartmentDto> Departments { get; set; } = new();
    public decimal TotalPresenzeHours { get; set; }
    public decimal TotalDirectHours { get; set; }
    public decimal TotalInternalHours { get; set; }
    public decimal TotalAbsenceHours { get; set; }
    public decimal TotalTimesheetHours { get; set; }
    public decimal OverallCoveragePercent { get; set; }
}
