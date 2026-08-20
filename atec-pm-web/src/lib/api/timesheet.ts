import { apiDelete, apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  LookupItem,
  TimesheetEntryDto,
  TimesheetPhaseOption,
  TimesheetProjectOption,
  TimesheetSaveRequest,
} from "@/lib/api/types"

// ── Helper date locali (no UTC drift) ──────────────────────────────────
// Le date timesheet sono "date pure": formattiamo/parsiamo sui componenti
// locali per evitare lo slittamento di un giorno di toISOString().

/** Formatta una Date come `yyyy-MM-dd` usando i componenti locali. */
export function toIsoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, "0")
  const day = String(date.getDate()).padStart(2, "0")
  return `${year}-${month}-${day}`
}

/** Parsa una stringa `yyyy-MM-dd[...]` come data locale a mezzanotte. */
export function parseIsoDate(iso: string): Date {
  const [year, month, day] = iso.slice(0, 10).split("-").map(Number)
  return new Date(year, month - 1, day)
}

/** Lunedì della settimana che contiene `date` (ISO `yyyy-MM-dd`). */
export function mondayOfWeek(date: Date): string {
  const day = date.getDay() // 0=domenica … 6=sabato
  const diff = day === 0 ? -6 : 1 - day
  const monday = new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate() + diff
  )
  return toIsoDate(monday)
}

/** Aggiunge `days` giorni a una data ISO e restituisce ISO. */
export function addDays(isoDate: string, days: number): string {
  const base = parseIsoDate(isoDate)
  base.setDate(base.getDate() + days)
  return toIsoDate(base)
}

// ── Chiamate API ───────────────────────────────────────────────────────

export async function fetchMyWeek(
  employeeId: number,
  weekStart?: string
): Promise<TimesheetEntryDto[]> {
  const start = weekStart ?? mondayOfWeek(new Date())
  const response = await apiGet<ApiResponse<TimesheetEntryDto[]>>(
    `/api/timesheet/week?employeeId=${employeeId}&weekStart=${start}`
  )
  return unwrapApi(response)
}

/** Voci timesheet in un intervallo di date (vista calendario mese/settimana). */
export async function fetchRange(
  employeeId: number,
  start: string,
  end: string
): Promise<TimesheetEntryDto[]> {
  const response = await apiGet<ApiResponse<TimesheetEntryDto[]>>(
    `/api/timesheet/range?employeeId=${employeeId}&start=${start}&end=${end}`
  )
  return unwrapApi(response)
}

/** Somma ore già registrate per dipendente/giorno (esclude la riga in modifica). */
export async function fetchDayTotal(
  employeeId: number,
  date: string,
  excludeId = 0
): Promise<number> {
  const response = await apiGet<ApiResponse<number>>(
    `/api/timesheet/day-total?employeeId=${employeeId}&date=${date}&excludeId=${excludeId}`
  )
  return unwrapApi(response)
}

export async function fetchRegistrableEmployees(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/timesheet/registrable-employees"
  )
  return unwrapApi(response)
}

/**
 * Commesse selezionabili nel dialogo ore. Dal #86 sono le sole commesse vere:
 * `includeProjectId` tiene in elenco quella della registrazione che si sta modificando,
 * anche quando è un'Altra Attività registrata prima di questa regola.
 */
export async function fetchProjectsForEmployee(
  employeeId: number,
  includeProjectId = 0
): Promise<TimesheetProjectOption[]> {
  const response = await apiGet<ApiResponse<TimesheetProjectOption[]>>(
    `/api/timesheet/projects-for-employee?employeeId=${employeeId}&includeProjectId=${includeProjectId}`
  )
  return unwrapApi(response)
}

export async function fetchPhasesForEmployee(
  employeeId: number,
  projectId: number
): Promise<TimesheetPhaseOption[]> {
  const response = await apiGet<ApiResponse<TimesheetPhaseOption[]>>(
    `/api/timesheet/phases-for-employee?employeeId=${employeeId}&projectId=${projectId}`
  )
  return unwrapApi(response)
}

/** Risolve la commessa a cui appartiene una fase (per preselezionare in modifica). */
export async function fetchProjectIdForPhase(
  phaseId: number
): Promise<number> {
  const response = await apiGet<ApiResponse<number>>(
    `/api/phases/${phaseId}/project-id`
  )
  return unwrapApi(response)
}

export async function saveTimesheetEntry(
  request: TimesheetSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/timesheet", request)
  return unwrapApi(response)
}

export async function deleteTimesheetEntry(entryId: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/timesheet/${entryId}`
  )
  return unwrapApi(response)
}
