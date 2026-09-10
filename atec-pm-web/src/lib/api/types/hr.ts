/** HR attendance and requests module DTOs — mirror of Hr_DTOs.cs. */

export interface HrPunch {
  id: number
  /** L'ora timbrata: non cambia mai, nemmeno dopo un «Invia a Ecos». */
  punchedAt: string
  direction: string
  source: string
  reason?: string | null
  createdBy?: string | null
  // ── «Invia a Ecos» (08/09/2026): decide tutto il server, qui si mostra ──
  /** StampID di Ecos; solo per le timbrature di Ecos. */
  ecosStampId?: string | null
  /** L'orario che Ecos ha dopo un nostro invio; null = mai inviato (Ecos ha il timbrato). */
  ecosPunchedAt?: string | null
  ecosSentAt?: string | null
  /** L'orario arrotondato dal motore: quello che il pulsante manda. */
  roundedAt?: string | null
  /** Di Ecos e con l'arrotondamento nello stesso giorno. */
  canSendToEcos: boolean
  /** Inviabile e con un orario su Ecos diverso da quello arrotondato. */
  toSendToEcos: boolean
  /** Rettifica che su Ecos non esiste: l'invio la INSERISCE (poi diventa una timbratura di Ecos). */
  ecosInsert: boolean
  /** Inserimento tentato senza risposta certa: non si rimanda da sola, da verificare su Ecos. */
  ecosUncertain: boolean
}

/** Una riga del registro degli invii a Ecos di una giornata. */
export interface HrEcosSend {
  id: number
  punchId?: number | null
  ecosStampId: string
  direction: string
  /** L'ora timbrata originale: quella che Ecos perde. */
  punchedAt: string
  /** L'ora inviata (arrotondata). */
  sentTime: string
  previousTime?: string | null
  /** OK oppure ERROR. */
  outcome: string
  message?: string | null
  sentBy?: string | null
  sentAt: string
}

/**
 * Un orario deciso a mano da HR per «Scrivi su Ecos» (09/09/2026 sera): per timbratura
 * (`punchId`) o, per la pausa dedotta, per verso senza `punchId`. «HH:mm».
 */
export interface HrEcosTime {
  punchId?: number | null
  direction: string
  time: string
  /** Senza `punchId`: «BREAK» (pausa dedotta, default) oppure «MISSING» (la timbratura che manca). */
  kind?: "BREAK" | "MISSING"
}

/** Esito del pulsante «Invia a Ecos»: viaggia sempre come dato, anche se fallito. */
export interface HrEcosSendResult {
  success: boolean
  message: string
  total: number
  /** Orari modificati su Ecos. */
  sent: number
  /** Rettifiche inserite su Ecos come timbrature nuove. */
  inserted: number
  /** Timbrature della pausa dedotta inserite su Ecos. */
  breakInserted: number
  /** true = dopo la scrittura la giornata e stata riletta da Ecos con successo. */
  resynced: boolean
  failed: number
  skipped: number
  errors: string[]
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
  /** Il registro degli invii a Ecos di questa giornata, dal più recente. */
  ecosSends: HrEcosSend[]
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
  /**
   * La pausa pranzo DEDOTTA dal motore (le 12:30 e 13:30 con l'asterisco, o il rientro
   * un'ora dopo l'uscita) che su Ecos non esiste: «Allinea Ecos» la inserisce come
   * timbrature vere (09/09/2026).
   */
  /**
   * Quanti minuti mancano alle ore previste dal contratto; 0 = giornata piena. Chi ha otto
   * ore non può farne sette e mezza e passare per «tutto regolare» (Diego, 10/09/2026).
   */
  shortMinutes?: number
  /**
   * La causale che copre le ore mancanti di una giornata comunque lavorata (permesso o ferie
   * di mezza giornata): serve a farla vedere, così nessuno la mette due volte.
   */
  justifiedType?: string | null
  justifiedHours?: number | null
  /**
   * Di quanti minuti l'entrata arrotondata sta prima delle 8; 0 = nessun anticipo.
   * Diego, 10/09/2026: «c'è gente che arriva, timbra alle 7:30 e si fa mezz'ora di
   * straordinario non autorizzato tutti i giorni».
   */
  earlyEntryMinutes?: number
  /**
   * La decisione sull'anticipo: true = vale l'orario timbrato, false = la giornata parte
   * dalle 8, null/assente = nessuno ha ancora deciso, e intanto vale il no.
   */
  earlyEntryAuthorized?: boolean | null
  ecosBreakToInsert: boolean
  /**
   * Il verso della timbratura che MANCA («OUT» per la sola entrata o l'uscita mancante), o
   * null: HR scrive l'orario nella sua riga del dettaglio e «Scrivi su Ecos» la inserisce là.
   */
  ecosMissingToInsert?: string | null
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
  /** Fascia oraria «HH:mm» delle richieste a ore; assente a giornata intera. */
  hourFrom?: string | null
  hourTo?: string | null
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
  /** Fascia oraria «HH:mm» delle richieste a ore: da qui vengono le ore, ed è quella che va su Ecos. */
  hourFrom?: string | null
  hourTo?: string | null
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
  /** Chi non ha ricevuto la mail e perché (l'esito del server di posta). */
  errors?: string[]
  message: string
}

// ── CALENDARIO MENSILE ────────────────────────────────────────────────────
//
// Una riga per VOCE (ore ordinarie, le nove fasce di straordinario, presenza, ferie,
// permessi, malattia, infortunio), non una per dipendente: è la griglia del progetto
// Timbrature. Testo, colore e tooltip arrivano già decisi dal server, che con gli stessi
// dati genera anche il file Excel.

export interface HrCalendarCell {
  /**
   * true = la casella si può cambiare da qui: oggi solo la riga della trasferta, sui giorni
   * lavorati. Le altre voci vengono dalle timbrature e dalle assenze, e si sistemano là.
   */
  editable?: boolean
  text: string
  /** GRAY · GREEN · RED · ORANGE · BLUE · PURPLE · YELLOW · TEAL */
  color: string
  tooltip: string
}

export interface HrCalendarRow {
  /** I protocolli della mutua del mese: valorizzati solo sulla riga che porta il nome. */
  sicknessProtocols?: string[]
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

/** Cosa serve al dialogo del protocollo della mutua. */
export interface HrSicknessProtocolInfo {
  employeeId: number
  employeeName: string
  date: string
  /** Il protocollo già scritto su questa giornata, «» se non c'è. */
  current: string
  /** L'ultimo della persona nei giorni prima, da proporre; «» se non ce n'è. */
  last: string
  lastDate?: string | null
  /** Perché non si può mettere il protocollo qui; «» = si può. */
  blocco: string
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
  /** L'ultimo protocollo della mutua nei giorni prima, da proporre; «» se non ce n'è. */
  lastProtocol?: string
  lastProtocolDate?: string | null
  /** Il protocollo già scritto su questa giornata, «» se non c'è. */
  protocol?: string
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
  /** Il protocollo della mutua, solo con MA; facoltativo, si aggiunge anche dopo. */
  protocol?: string
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

// ── CONTROLLO DI IERI (09/09/2026) — specchio di HrDailyCheckDto ────────────

/** Un giorno del blocco; `isWorkingDay` false = sabato, domenica o festivo. */
export interface HrDailyCheckDay {
  date: string
  isWorkingDay: boolean
}

export interface HrDailyCheckEmployee {
  employeeId: number
  employeeName: string
  departmentName?: string | null
  /** false = senza codice Ecos: nessuna timbratura può arrivare, non è un'anomalia sua. */
  ecosLinked: boolean
  /** Una giornata per ogni giorno del blocco, nello stesso ordine di `days`. */
  days: HrDay[]
}

/**
 * Le giornate di tutti i dipendenti che timbrano in un blocco di giorni deciso dal server:
 * il giorno scelto (di norma ieri) e, se è di riposo, i riposi prima di lui fino all'ultimo
 * giorno lavorativo — di lunedì: venerdì, sabato e domenica.
 */
export interface HrDailyCheck {
  /** Il giorno scelto: è la fine del blocco. */
  date: string
  from: string
  to: string
  /** Il giorno da chiedere per il blocco prima. */
  previousDate: string
  /** Il giorno da chiedere per il blocco dopo; null quando il blocco arriva a oggi. */
  nextDate?: string | null
  days: HrDailyCheckDay[]
  employees: HrDailyCheckEmployee[]
}

// ── ALLINEA ECOS: il resoconto prima di scrivere (09/09/2026) — specchio di HrEcosPlanDto ──

/** Una riga del resoconto: cosa si modifica, cosa si inserisce, cosa resta fuori. */
export interface HrEcosPlannedOp {
  /** UPDATE · INSERT (rettifica) · INSERT_BREAK (pausa dedotta) · MISSING (l'uscita che manca, la scrive HR nel dettaglio) · SKIP · UNCERTAIN */
  kind: "UPDATE" | "INSERT" | "INSERT_BREAK" | "MISSING" | "SKIP" | "UNCERTAIN"
  direction: string
  /** Per le modifiche: l'ora che Ecos ha adesso. */
  from?: string | null
  /** L'ora che andrà (o sarebbe andata) su Ecos. */
  to?: string | null
  /** La frase pronta: «Uscita 17:12 → 17:00». */
  label: string
  detail?: string | null
}

/** La giornata calcolata e, riga per riga, cosa «Allinea Ecos» scriverà. Sola lettura. */
export interface HrEcosPlan {
  employeeId: number
  employeeName: string
  workDate: string
  configured: boolean
  note: string
  hasAnomaly: boolean
  clockIn1: string
  clockOut1: string
  clockIn2: string
  clockOut2: string
  regularHours: string
  overtime: string
  breakTime: string
  operations: HrEcosPlannedOp[]
  /** Quante righe scrivono davvero su Ecos. */
  toWrite: number
  canSend: boolean
  /** Perché non si può, o cosa c'è da sapere; vuoto quando tutto è pronto. */
  message: string
}
