import type { ResAssignmentDto, ResTipo } from "@/lib/api/types"
import { wildcardMatch } from "@/lib/wildcard"

// Port fedele di ATEC.Risorse.Shared/Helpers/PlannerLogic.cs (+ WildcardMatcher,
// EmployeeDisplay). Tutto in TypeScript per il client React. Le date delle
// allocazioni sono trattate come date "pure" (yyyy-MM-dd, niente fuso orario).

// ── Date pure (no UTC drift) ────────────────────────────────────

/** Parsa la porzione yyyy-MM-dd di una stringa ISO come data locale a mezzanotte. */
export function parseDate(iso: string): Date {
  const [y, m, d] = iso.slice(0, 10).split("-").map(Number)
  return new Date(y, m - 1, d)
}

/** Formatta una Date come yyyy-MM-dd usando i componenti locali. */
export { dateToIso as toIso } from "@/lib/date-iso"

/** Numero di giorni interi tra due date (a-b in giorni). */
export function diffDays(a: Date, b: Date): number {
  const ms = startOfDay(a).getTime() - startOfDay(b).getTime()
  return Math.round(ms / 86_400_000)
}

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

export function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
}

// ── Calendario ──────────────────────────────────────────────────

const FIXED_HOLIDAYS: ReadonlyArray<[number, number]> = [
  [1, 1],
  [1, 6],
  [4, 25],
  [5, 1],
  [6, 2],
  [8, 15],
  [11, 1],
  [12, 8],
  [12, 25],
  [12, 26],
]

const MONTH_NAMES = [
  "",
  "Gennaio",
  "Febbraio",
  "Marzo",
  "Aprile",
  "Maggio",
  "Giugno",
  "Luglio",
  "Agosto",
  "Settembre",
  "Ottobre",
  "Novembre",
  "Dicembre",
]

// getDay(): 0=domenica … 6=sabato.
const DOW_LETTERS = ["D", "L", "M", "M", "G", "V", "S"]

/** Lunedì della settimana che contiene `d` (ISO 8601). */
export function mondayOf(d: Date): Date {
  const dow = (d.getDay() + 6) % 7 // 0=lunedì … 6=domenica
  return addDays(d, -dow)
}

export function isWeekend(d: Date): boolean {
  const day = d.getDay()
  return day === 0 || day === 6
}

export function isHoliday(d: Date): boolean {
  for (const [m, day] of FIXED_HOLIDAYS) {
    if (d.getMonth() + 1 === m && d.getDate() === day) return true
  }
  const em = easterMonday(d.getFullYear())
  return d.getMonth() === em.getMonth() && d.getDate() === em.getDate()
}

export function monthName(m: number): string {
  return MONTH_NAMES[m]
}

export function dowLetter(d: Date): string {
  return DOW_LETTERS[d.getDay()]
}

export function dayCount(start: Date, end: Date): number {
  return diffDays(end, start) + 1
}

/** Giorni lavorativi (esclude sab/dom). */
export function workingDayCount(start: Date, end: Date): number {
  if (startOfDay(end) < startOfDay(start)) return 0
  let n = 0
  for (let d = startOfDay(start); d <= startOfDay(end); d = addDays(d, 1)) {
    if (!isWeekend(d)) n++
  }
  return n
}

/** FERIE = giorni lavorativi; OP/FLEX = giorni di calendario. */
export function displayDayCount(tipo: string, start: Date, end: Date): number {
  return tipo === "FERIE" ? workingDayCount(start, end) : dayCount(start, end)
}

function easterMonday(year: number): Date {
  const a = year % 19
  const b = Math.floor(year / 100)
  const c = year % 100
  const d = Math.floor(b / 4)
  const e = b % 4
  const f = Math.floor((b + 8) / 25)
  const g = Math.floor((b - f + 1) / 3)
  const h = (19 * a + b - d - g + 15) % 30
  const i = Math.floor(c / 4)
  const k = c % 4
  const l = (32 + 2 * e + 2 * i - h - k) % 7
  const m = Math.floor((a + 11 * h + 22 * l) / 451)
  const month = Math.floor((h + l - 7 * m + 114) / 31)
  const day = ((h + l - 7 * m + 114) % 31) + 1
  return new Date(year, month - 1, day + 1) // Pasqua + 1 = Lunedì dell'Angelo
}

// ── Etichette / tooltip ─────────────────────────────────────────

export function tipoLabel(t: string): string {
  if (t === "FERIE") return "Ferie"
  if (t === "FLEX") return "Flessibile"
  return "Operativo"
}

export function firstNonEmpty(
  ...vals: Array<string | null | undefined>
): string | null {
  for (const v of vals) {
    if (v && v.trim().length > 0) return v
  }
  return null
}

function projectLabel(a: ResAssignmentDto): string | null {
  const hasCode = !!a.projectCode && a.projectCode.trim().length > 0
  const hasTitle = !!a.projectTitle && a.projectTitle.trim().length > 0
  if (hasCode && hasTitle) return `${a.projectCode} — ${a.projectTitle}`
  if (hasCode) return a.projectCode
  return hasTitle ? a.projectTitle : null
}

export function assignmentLabel(a: ResAssignmentDto): string {
  if (a.tipo === "FERIE") {
    return firstNonEmpty(a.descrizione) ?? tipoLabel(a.tipo)
  }
  return (
    firstNonEmpty(
      projectLabel(a),
      a.serviceCod,
      a.otherActivityDesc,
      a.descrizione
    ) ?? tipoLabel(a.tipo)
  )
}

function fmtDate(d: Date): string {
  const dd = String(d.getDate()).padStart(2, "0")
  const mm = String(d.getMonth() + 1).padStart(2, "0")
  return `${dd}/${mm}/${d.getFullYear()}`
}

function fmtDayMonthTime(iso: string): string {
  const dt = new Date(iso)
  const dd = String(dt.getDate()).padStart(2, "0")
  const mm = String(dt.getMonth() + 1).padStart(2, "0")
  const hh = String(dt.getHours()).padStart(2, "0")
  const mi = String(dt.getMinutes()).padStart(2, "0")
  return `${dd}/${mm} ${hh}:${mi}`
}

export function buildTooltip(a: ResAssignmentDto, start: Date, end: Date): string {
  const giorni = displayDayCount(a.tipo, start, end)
  const parts: string[] = [
    a.employeeName,
    `${tipoLabel(a.tipo)} · ${fmtDate(start)} → ${fmtDate(end)} (${giorni} gg)`,
  ]
  if (a.tipo !== "FERIE") {
    if (
      (a.projectCode && a.projectCode.trim()) ||
      (a.projectTitle && a.projectTitle.trim())
    ) {
      const joined = [a.projectCode, a.projectTitle]
        .filter((x) => x && x.trim())
        .join(" — ")
      parts.push(`Commessa: ${joined}`)
    }
    if (a.serviceCod && a.serviceCod.trim()) parts.push(`Service: ${a.serviceCod}`)
    if (a.otherActivityDesc && a.otherActivityDesc.trim())
      parts.push(`Attività: ${a.otherActivityDesc}`)
  }
  if (a.descrizione && a.descrizione.trim()) parts.push(a.descrizione)
  if (a.hasConflict) parts.push("⚠ In conflitto")
  if (a.updatedAt) {
    const chi = a.updatedByName && a.updatedByName.trim() ? a.updatedByName : "—"
    parts.push(`Modificato da ${chi} · ${fmtDayMonthTime(a.updatedAt)}`)
  }
  return parts.join("\n")
}

// ── Ricerca wildcard (port di WildcardMatcher) ──────────────────

export { wildcardMatch } from "@/lib/wildcard"

export function assignmentMatchesSearch(
  a: ResAssignmentDto,
  pattern: string
): boolean {
  if (!pattern || pattern.trim().length === 0) return true
  const fields = [
    a.employeeName,
    tipoLabel(a.tipo),
    assignmentLabel(a),
    a.projectCode ?? "",
    a.projectTitle ?? "",
    a.serviceCod ?? "",
    a.otherActivityDesc ?? "",
    a.descrizione ?? "",
  ]
  return fields.some((f) => wildcardMatch(f, pattern))
}

// ── Conflitti (port: FLEX mai in conflitto, tutto il resto sì) ───

export function overlap(a: ResAssignmentDto, b: ResAssignmentDto): boolean {
  return (
    parseDate(a.dataInizio) <= parseDate(b.dataFine) &&
    parseDate(b.dataInizio) <= parseDate(a.dataFine)
  )
}

export function overlapRange(
  a: ResAssignmentDto,
  start: Date,
  end: Date
): boolean {
  return parseDate(a.dataInizio) <= end && start <= parseDate(a.dataFine)
}

/**
 * Le attività FLESSIBILI non vanno mai in conflitto con nulla (nemmeno con le ferie).
 * Tutto il resto (Operativo e Ferie, in qualsiasi combinazione) va in conflitto se si
 * sovrappone — INCLUSO FERIE+FERIE (regola del programma Risorse aggiornata).
 */
export function forbidden(t1: string, t2: string): boolean {
  if (t1 === "FLEX" || t2 === "FLEX") return false
  return true
}

/** Ricalcola hasConflict in-place su tutte le allocazioni. */
export function recalcConflicts(assignments: ResAssignmentDto[]): void {
  for (const r of assignments) r.hasConflict = false
  const byEmp = new Map<number, ResAssignmentDto[]>()
  for (const a of assignments) {
    const arr = byEmp.get(a.employeeId)
    if (arr) arr.push(a)
    else byEmp.set(a.employeeId, [a])
  }
  for (const list of byEmp.values()) {
    for (let i = 0; i < list.length; i++) {
      for (let j = i + 1; j < list.length; j++) {
        if (overlap(list[i], list[j]) && forbidden(list[i].tipo, list[j].tipo)) {
          list[i].hasConflict = true
          list[j].hasConflict = true
        }
      }
    }
  }
}

export function wouldConflict(
  assignments: ResAssignmentDto[],
  employeeId: number,
  start: Date,
  end: Date,
  tipo: string,
  excludeId: number
): boolean {
  for (const a of assignments) {
    if (a.employeeId !== employeeId || a.id === excludeId) continue
    if (!overlapRange(a, start, end)) continue
    if (forbidden(tipo, a.tipo)) return true
  }
  return false
}

export function computeFeriePeak(
  ferie: ResAssignmentDto[],
  maxScanDays = 1500
): { peak: number; date: Date | null } {
  if (ferie.length === 0) return { peak: 0, date: null }
  let minStart = parseDate(ferie[0].dataInizio)
  let maxEnd = parseDate(ferie[0].dataFine)
  for (const a of ferie) {
    const s = parseDate(a.dataInizio)
    const e = parseDate(a.dataFine)
    if (s < minStart) minStart = s
    if (e > maxEnd) maxEnd = e
  }
  const span = Math.min(diffDays(maxEnd, minStart) + 1, maxScanDays)
  const byEmp = new Map<number, ResAssignmentDto[]>()
  for (const a of ferie) {
    const arr = byEmp.get(a.employeeId)
    if (arr) arr.push(a)
    else byEmp.set(a.employeeId, [a])
  }
  let peak = 0
  let peakDate: Date | null = null
  for (let i = 0; i < span; i++) {
    const d = addDays(minStart, i)
    let cnt = 0
    for (const list of byEmp.values()) {
      if (
        list.some(
          (a) => parseDate(a.dataInizio) <= d && d <= parseDate(a.dataFine)
        )
      ) {
        cnt++
      }
    }
    if (cnt > peak) {
      peak = cnt
      peakDate = d
    }
  }
  return { peak, date: peakDate }
}

// ── Display nominativi (port di EmployeeDisplay) ─────────────────

export function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return "?"
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

export function surname(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return fullName
  return parts[parts.length - 1]
}

// ── Colori barre ────────────────────────────────────────────────

export const TIPO_COLORS: Record<ResTipo, { bg: string; fg: string }> = {
  OP: { bg: "#3B82F6", fg: "#ffffff" },
  FLEX: { bg: "#9CA3AF", fg: "#1F2937" },
  FERIE: { bg: "#EF4444", fg: "#ffffff" },
}

export function tipoCssClass(t: string): string {
  if (t === "FERIE") return "tipo-ferie"
  if (t === "FLEX") return "tipo-flex"
  return "tipo-op"
}
