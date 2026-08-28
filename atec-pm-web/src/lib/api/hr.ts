import { apiDelete, apiGet, apiGetBlob, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import { downloadFile, safeFileName } from "@/lib/download"
import type {
  ApiResponse,
  HrApproveAbsenceRequest,
  HrAbsence,
  HrBadges,
  HrCreateAbsenceRequest,
  HrGiustificaInfo,
  HrGiustificaRequest,
  HrImportResult,
  HrMappingRow,
  HrEcosSettings,
  HrEcosTestResult,
  HrMonthlyCalendar,
  HrMonthlyTimesheet,
  HrReminders,
  HrRemindersResult,
  HrQuadraturaMonth,
  HrStatus,
} from "@/lib/api/types"

/**
 * Monthly timesheet. Without `employeeId` the server returns the caller's own sheet:
 * read-only users see only themselves; another employee's sheet requires write on
 * `nav.hr_timbrature` (403 with a clear message, not an empty month).
 */
export async function fetchHrTimesheet(
  year: number,
  month: number,
  employeeId?: number | null
): Promise<HrMonthlyTimesheet> {
  const extra = employeeId != null ? `&employeeId=${employeeId}` : ""
  const r = await apiGet<ApiResponse<HrMonthlyTimesheet>>(
    `/api/hr/timesheet?year=${year}&month=${month}${extra}`
  )
  return unwrapApi(r)
}

export async function fetchHrStatus(): Promise<HrStatus> {
  const r = await apiGet<ApiResponse<HrStatus>>("/api/hr/status")
  return unwrapApi(r)
}

/** Manual import from Ecos; `full` ignores the cursor and reprocesses all history
 *  (needed after linking a new employee: their old punches were skipped). */
export async function importHrPunches(full = false): Promise<HrImportResult> {
  const r = await apiPost<ApiResponse<HrImportResult>>(
    `/api/hr/import${full ? "?full=true" : ""}`
  )
  return unwrapApi(r)
}

export async function fetchHrMapping(): Promise<HrMappingRow[]> {
  const r = await apiGet<ApiResponse<HrMappingRow[]>>("/api/hr/mapping")
  return unwrapApi(r)
}

/** Badges read live from Ecos; `configured=false` means missing server credentials. */
export async function fetchHrBadges(): Promise<HrBadges> {
  const r = await apiGet<ApiResponse<HrBadges>>("/api/hr/mapping/badges")
  return unwrapApi(r)
}

export async function saveHrMapping(
  employeeId: number,
  ecosEmplCode: string | null
): Promise<void> {
  const r = await apiPut<ApiResponse<boolean>>(`/api/hr/mapping/${employeeId}`, {
    ecosEmplCode,
  })
  unwrapApi(r)
}

export async function sendHrAdjustment(payload: {
  employeeId: number
  /** Local ISO «yyyy-MM-ddTHH:mm:ss». */
  punchedAt: string
  direction: "IN" | "OUT"
  reason: string
}): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>("/api/hr/adjustment", payload)
  unwrapApi(r)
}

export async function deleteHrAdjustment(id: number): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(`/api/hr/adjustment/${id}`)
  unwrapApi(r)
}

// ── CREDENZIALI ECOS ──────────────────────────────────────────────────────

/** Credenziali con cui il server entra in Ecos. La password non torna mai indietro. */
export async function fetchHrEcosSettings(): Promise<HrEcosSettings> {
  const r = await apiGet<ApiResponse<HrEcosSettings>>("/api/hr/ecos/settings")
  return unwrapApi(r)
}

/** Salva le credenziali; `password: null` lascia quella che c'e'. */
export async function saveHrEcosSettings(payload: {
  baseUrl: string
  userId: string
  clientId: string
  password: string | null
}): Promise<HrEcosSettings> {
  const r = await apiPost<ApiResponse<HrEcosSettings>>("/api/hr/ecos/settings", payload)
  return unwrapApi(r)
}

/** Prova le credenziali con una sola TokenGet: non legge e non scrive nulla su Ecos. */
export async function testHrEcosSettings(): Promise<HrEcosTestResult> {
  const r = await apiPost<ApiResponse<HrEcosTestResult>>("/api/hr/ecos/settings/test")
  return unwrapApi(r)
}

// ── ASSENZE E RICHIESTE FERIE/PERMESSI ────────────────────────────────────

export async function fetchHrAbsences(params?: {
  employeeId?: number | null
  departmentId?: number | null
  year?: number | null
  month?: number | null
  status?: string | null
}): Promise<HrAbsence[]> {
  const q = new URLSearchParams()
  if (params?.employeeId != null) q.set("employeeId", String(params.employeeId))
  if (params?.departmentId != null) q.set("departmentId", String(params.departmentId))
  if (params?.year != null) q.set("year", String(params.year))
  if (params?.month != null) q.set("month", String(params.month))
  if (params?.status != null) q.set("status", params.status)

  const qs = q.toString()
  const r = await apiGet<ApiResponse<HrAbsence[]>>(`/api/hr/absences${qs ? `?${qs}` : ""}`)
  return unwrapApi(r)
}

export async function createHrAbsence(payload: HrCreateAbsenceRequest): Promise<number> {
  const r = await apiPost<ApiResponse<number>>("/api/hr/absences", payload)
  return unwrapApi(r)
}

export async function approveHrAbsence(
  id: number,
  payload: HrApproveAbsenceRequest
): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>(`/api/hr/absences/${id}/approve`, payload)
  unwrapApi(r)
}

export async function cancelHrAbsence(id: number): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(`/api/hr/absences/${id}`)
  unwrapApi(r)
}

// ── CALENDARIO MENSILE ────────────────────────────────────────────────────

/**
 * Calendario mensile dell'azienda, una riga per voce. Richiede la scrittura su
 * `nav.hr_timbrature`: con la sola lettura si vede il proprio cartellino e basta.
 */
export async function fetchHrCalendar(
  year: number,
  month: number,
  departmentId?: number | null
): Promise<HrMonthlyCalendar> {
  const extra = departmentId != null ? `&departmentId=${departmentId}` : ""
  const r = await apiGet<ApiResponse<HrMonthlyCalendar>>(
    `/api/hr/calendar?year=${year}&month=${month}${extra}`
  )
  return unwrapApi(r)
}

/**
 * Scarica il calendario in Excel. Il foglio lo compone il server (stessi colori e stessa
 * impaginazione del programma Timbrature): qui si salva e basta.
 */
export async function downloadHrCalendarExcel(
  year: number,
  month: number,
  departmentId?: number | null,
  employeeId?: number | null,
  employeeName?: string
): Promise<void> {
  const parametri = new URLSearchParams({ year: String(year), month: String(month) })
  if (departmentId != null) parametri.set("departmentId", String(departmentId))
  if (employeeId != null) parametri.set("employeeId", String(employeeId))

  const blob = await apiGetBlob(`/api/hr/calendar/export?${parametri.toString()}`)

  // Come il vecchio programma: «Calendario_Agosto_2026» o, filtrando, col nome della persona.
  const mese = MESI[month - 1]
  const nome = employeeName
    ? `Calendario_${safeFileName(employeeName)}_${mese}_${year}.xlsx`
    : `Calendario_${mese}_${year}.xlsx`
  downloadFile(nome, blob, blob.type)
}

const MESI = [
  "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno",
  "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre",
]

// ── SOLLECITI TIMBRATURE MANCANTI ─────────────────────────────────────────

/** Chi ha giornate col «?» nel mese, col testo del sollecito già composto dal server. */
export async function fetchHrReminders(
  year: number,
  month: number,
  departmentId?: number | null,
  employeeId?: number | null
): Promise<HrReminders> {
  const q = new URLSearchParams({ year: String(year), month: String(month) })
  if (departmentId != null) q.set("departmentId", String(departmentId))
  if (employeeId != null) q.set("employeeId", String(employeeId))

  const r = await apiGet<ApiResponse<HrReminders>>(`/api/hr/calendar/reminders?${q.toString()}`)
  return unwrapApi(r)
}

/** Invia i solleciti via SMTP e segna le giornate come già chieste. */
export async function sendHrReminders(
  year: number,
  month: number,
  departmentId?: number | null,
  employeeId?: number | null
): Promise<HrRemindersResult> {
  const q = new URLSearchParams({ year: String(year), month: String(month) })
  if (departmentId != null) q.set("departmentId", String(departmentId))
  if (employeeId != null) q.set("employeeId", String(employeeId))

  const r = await apiPost<ApiResponse<HrRemindersResult>>(
    `/api/hr/calendar/reminders?${q.toString()}`
  )
  return unwrapApi(r)
}

/** Registra i solleciti aperti nel client di posta (là la mail la spedisce l'utente). */
export async function markHrReminders(
  year: number,
  month: number,
  employeeIds: number[]
): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>("/api/hr/calendar/reminders/mark", {
    year,
    month,
    employeeIds,
  })
  unwrapApi(r)
}

// ── QUADRATURA PRESENZE ↔ COMMESSE (FASE 3) ─────────────────────────────

export async function fetchHrQuadratura(
  year: number,
  month: number,
  departmentId?: number | null
): Promise<HrQuadraturaMonth> {
  const extra = departmentId != null ? `&departmentId=${departmentId}` : ""
  const r = await apiGet<ApiResponse<HrQuadraturaMonth>>(
    `/api/hr/quadratura?year=${year}&month=${month}${extra}`
  )
  return unwrapApi(r)
}

// ── #132 GIUSTIFICAZIONE ORE MANCANTI DAL CALENDARIO ─────────────────────

/** Cosa si può fare sulla giornata cliccata: ore mancanti, causali ammesse, cosa c'è già. */
export async function fetchHrGiustificaInfo(
  employeeId: number,
  date: string
): Promise<HrGiustificaInfo> {
  const r = await apiGet<ApiResponse<HrGiustificaInfo>>(
    `/api/hr/calendar/giustifica?employeeId=${employeeId}&date=${date}`
  )
  return unwrapApi(r)
}

/** Scrive la causale scelta (causale vuota = toglie quella che c'è). */
export async function saveHrGiustifica(request: HrGiustificaRequest): Promise<boolean> {
  const r = await apiPost<ApiResponse<boolean>>("/api/hr/calendar/giustifica", request)
  return unwrapApi(r)
}
