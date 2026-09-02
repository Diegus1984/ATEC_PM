/** HR attendance and requests module DTOs — mirror of Hr_DTOs.cs. */

export interface HrPunch {
  id: number
  punchedAt: string
  direction: string
  source: string
  reason?: string | null
  createdBy?: string | null
}

export interface HrDay {
  workDate: string
  isHoliday: boolean
  hasData: boolean
  clockIn1: string
  clockOut1: string
  clockIn2: string
  clockOut2: string
  /** «8h 0m», or «---» when the day cannot be calculated. */
  regularHours: string
  overtime: string
  breakTime: string
  /** CCNL bands with non-zero values (key = circular letter). */
  bands: Record<string, string>
  note: string
  hasAnomaly: boolean
  punches: HrPunch[]
  /** 🔸 Le timbrature come sono arrivate dal rilevatore. */
  raw: HrDayStage
  /** 🔷 Le stesse dopo l'arrotondamento (scatto 30', tolleranza 10'). */
  normalized: HrDayStage
  /**
   * true = giornata da segnalare al dipendente. La regola la decide il SERVER
   * (HrDayReminder.Serve): il pulsante 📧 e il filtro «Da segnalare» leggono questo flag,
   * cosi non possono divergere.
   */
  canRemind: boolean
  /** Quando e stato mandato l'ultimo sollecito per questa giornata; null = mai. */
  lastReminderAt?: string | null
}

/** Uno stadio della giornata: i quattro orari, la pausa e il totale di quello stadio. */
export interface HrDayStage {
  clockIn1: string
  clockOut1: string
  clockIn2: string
  clockOut2: string
  breakTime: string
  totalHours: string
}

export interface HrMonthlyTimesheet {
  employeeId: number
  employeeName: string
  year: number
  month: number
  ecosLinked: boolean
  days: HrDay[]
}

export interface HrStatus {
  configured: boolean
  importInProgress: boolean
  lastImport?: string | null
  lastResult: string
  totalPunches: number
  totalDays: number
  linkedEmployees: number
  activeEmployees: number
  /** Ultima lettura riuscita dell'anagrafica badge da Ecos. */
  lastBadgeRead?: string | null
  /** Avanzamento dell'import a video: vive in memoria nel server. */
  progress: HrImportProgress
}

/**
 * L'avanzamento dell'import (port della barra + txtLog di SyncEcosPage).
 *
 * 🪤 Lo stato vive in memoria nel server: un riavvio a meta import lo azzera. Si riconosce
 * da `startedAt` nullo con `running` falso — la pagina lo dice invece di girare per sempre.
 */
export interface HrImportProgress {
  running: boolean
  title: string
  phase: string
  percent: number
  downloaded: number
  added: number
  updated: number
  removed: number
  daysRecalculated: number
  startedAt?: string | null
  endedAt?: string | null
  log: string[]
}

export interface HrImportResult {
  success: boolean
  message: string
  punchesAdded: number
  punchesUpdated: number
  daysRecalculated: number
  unmatched: string[]
}

export interface HrMappingRow {
  employeeId: number
  name: string
  ecosEmplCode?: string | null
}

export interface HrBadge {
  emplCode: string
  name: string
  isActive: boolean
}

export interface HrBadges {
  configured: boolean
  badges: HrBadge[]
}

// ── ABSENCES & REQUESTS (FASE 2) ──────────────────────────────────────────

export interface HrAbsence {
  id: number
  employeeId: number
  employeeName: string
  departmentName?: string | null
  dateFrom: string
  dateTo: string
  hours?: number | null
  isFullDay: boolean
  absenceType: "VACATION" | "PERMIT" | "SICKNESS" | "INJURY" | "OTHER"
  status: "PENDING" | "APPROVED" | "REJECTED" | "CANCELLED"
  source: "ATEC" | "ECOS" | "MANUAL"
  ecosAbsenceId?: string | null
  approvedBy?: number | null
  approvedByName?: string | null
  approvedAt?: string | null
  rejectionReason?: string | null
  notes?: string | null
  createdBy?: number | null
  createdByName?: string | null
  createdAt: string
}

export interface HrCreateAbsenceRequest {
  employeeId?: number | null
  dateFrom: string
  dateTo: string
  hours?: number | null
  isFullDay: boolean
  absenceType: string
  notes?: string | null
}

export interface HrApproveAbsenceRequest {
  approved: boolean
  rejectionReason?: string | null
}

// ── CREDENZIALI ECOS ──────────────────────────────────────────────────────

export interface HrEcosSettings {
  baseUrl: string
  userId: string
  clientId: string
  /** Write-only: si manda per cambiarla, non torna mai indietro. */
  password?: string | null
  /** true = una password c'e' (non si dice quale). */
  hasPassword: boolean
  /** DATABASE = messe dalla pagina; APPSETTINGS = ancora nel file del server. */
  source: string
  configured: boolean
}

export interface HrEcosTestResult {
  ok: boolean
  message: string
}

// ── SOLLECITI TIMBRATURE MANCANTI ─────────────────────────────────────────

export interface HrReminderTarget {
  employeeId: number
  employeeName: string
  email?: string | null
  /** I giorni del mese col «?». */
  missingDays: number[]
  lastReminderAt?: string | null
  subject: string
  /** Testo per il client di posta. */
  mailtoBody: string
  /** Testo dell'invio diretto. */
  body: string
}

export interface HrReminders {
  year: number
  month: number
  targets: HrReminderTarget[]
  /** false = SMTP non configurato: resta solo il client di posta. */
  smtpEnabled: boolean
}

export interface HrRemindersResult {
  sent: number
  failed: number
  withoutEmail: string[]
  message: string
}

// ── CALENDARIO MENSILE ────────────────────────────────────────────────────
//
// Una riga per VOCE (ore ordinarie, le nove fasce di straordinario, presenza, ferie,
// permessi, malattia, infortunio), non una per dipendente: è la griglia del progetto
// Timbrature. Testo, colore e tooltip arrivano già decisi dal server, che con gli stessi
// dati genera anche il file Excel.

export interface HrCalendarCell {
  text: string
  /** GRAY · GREEN · RED · ORANGE · BLUE · PURPLE · YELLOW · TEAL */
  color: string
  tooltip: string
}

export interface HrCalendarRow {
  employeeId: number
  /** Nome + matricola: valorizzato solo sulla prima riga del dipendente. */
  employee: string
  employeeKey: string
  departmentName?: string | null
  voce: string
  /** ORE_ORDINARIE · STRAORD_A…M · PRESENZA · FERIE · PERMESSI · MALATTIA · INFORTUNIO */
  voceType: string
  days: Record<number, HrCalendarCell>
  total: string
}

export interface HrCalendarEmployee {
  id: number
  name: string
}

export interface HrMonthlyCalendar {
  year: number
  month: number
  daysInMonth: number
  dayLabels: Record<number, string>
  nonWorkingDays: Record<number, boolean>
  rows: HrCalendarRow[]
  employees: HrCalendarEmployee[]
}

// ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

export interface HrQuadraturaRow {
  employeeId: number
  employeeName: string
  departmentName?: string | null
  presenzeHours: number
  directTimesheetHours: number
  internalTimesheetHours: number
  absenceHours: number
  totalTimesheetHours: number
  differenceHours: number
  coveragePercent: number
}

export interface HrQuadraturaDepartment {
  departmentId: number
  departmentName: string
  totalPresenzeHours: number
  totalDirectHours: number
  totalInternalHours: number
  totalAbsenceHours: number
  totalTimesheetHours: number
  differenceHours: number
  coveragePercent: number
}

export interface HrQuadraturaMonth {
  year: number
  month: number
  rows: HrQuadraturaRow[]
  departments: HrQuadraturaDepartment[]
  totalPresenzeHours: number
  totalDirectHours: number
  totalInternalHours: number
  totalAbsenceHours: number
  totalTimesheetHours: number
  overallCoveragePercent: number
}

// ── #132 GIUSTIFICAZIONE ORE MANCANTI DAL CALENDARIO ─────────────────────

/** Codici causale dell'ufficio: gli stessi del programma «Timbrature». */
export type HrCausale = "FE" | "PE" | "MA" | "IN"

/** Etichette del `CausaleDialog` originale, parola per parola. */
export const HR_CAUSALE_LABEL: Record<HrCausale, string> = {
  FE: "FE - Ferie",
  PE: "PE - Permesso",
  MA: "MA - Malattia",
  IN: "IN - Infortunio",
}

/** Cosa si può fare sulla giornata cliccata (lo decide il server). */
export interface HrGiustificaInfo {
  employeeId: number
  employeeName: string
  date: string
  /** Ore di contratto della giornata. */
  dailyHours: number
  /** Ore già coperte da timbrature (ordinario + straordinario). */
  oreLavorate: number
  /** Ore da giustificare: contratto − lavorate, mai negative. */
  oreMancanti: number
  /** Codici ammessi su QUESTA giornata. */
  causali: HrCausale[]
  /** Codice già presente sulla giornata, "" se non c'è niente. */
  causaleCorrente: string
  oreCorrenti: number | null
  /** true = la causale presente si può togliere. */
  puoRimuovere: boolean
  /** Vuoto = si può giustificare; altrimenti il motivo per cui no, già scritto. */
  blocco: string
}

export interface HrGiustificaRequest {
  employeeId: number
  /** Data della giornata (ISO, senza ora). */
  date: string
  /** FE | PE | MA | IN, oppure "" per togliere la causale. */
  causale: string
  hours?: number | null
}

// ── SOLLECITO DELLA SINGOLA GIORNATA (voce 1 del port) ────────────────────

export interface HrDayReminder {
  employeeId: number
  employeeName: string
  date: string
  email?: string | null
  subject: string
  /** Il corpo integrale, quello che la persona leggera. */
  body: string
  canRemind: boolean
  lastReminderAt?: string | null
  /** false = SMTP non configurato: resta il client di posta. */
  smtpEnabled: boolean
  /** Vuoto = si puo spedire; altrimenti il motivo, gia scritto. */
  blocco: string
}

// ── CRONOLOGIA EMAIL (voce 6 del port) ────────────────────────────────────

export interface HrReminderLogRow {
  id: number
  sentAt: string
  employeeId: number
  employeeName: string
  email?: string | null
  /** La giornata per cui e stato chiesto il chiarimento. */
  workDate: string
  subject?: string | null
  /** null = riga scritta prima della M117: «testo non conservato». */
  body?: string | null
  /** SMTP = spedita dal server · MAILTO = aperta nel client di posta. */
  channel: string
  sentByName?: string | null
}

export interface HrReminderLog {
  year: number
  month: number
  rows: HrReminderLogRow[]
}

/** Evento real-time «HrChanged» (gruppo hr-all su /hubs/project). */
export interface HrChange {
  /** import-progress · import · adjustment · reminder · giustifica · absence · mapping · settings */
  action: string
  employeeId: number | null
  date: string | null
}
