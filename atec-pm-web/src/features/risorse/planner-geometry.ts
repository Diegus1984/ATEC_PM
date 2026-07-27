// ── Geometria e costanti del Gantt risorse (nessun React) ──────────────────
// Larghezze colonna, lane-packing e overlay calendario: tutto derivabile dai
// dati, quindi testabile senza montare la pagina.

import type { LookupItem, ResAssignmentDto, ResTipo } from "@/lib/api/types"

import { addDays, diffDays, isHoliday, monthName, parseDate } from "./planner-logic"
import type { PlannerUiSettings } from "./use-planner-settings"

export const LANE_HEIGHT = 26
export const BAR_HEIGHT = 18
export const ALL_TIPI: ResTipo[] = ["OP", "FLEX", "FERIE"]
export const TIPO_LABELS: Record<ResTipo, string> = {
  OP: "Operativo",
  FLEX: "Flessibile",
  FERIE: "Ferie",
}

// Zoom: giorni "di riferimento" → larghezza colonna giorno.
const DAY_WIDTH: Record<number, number> = { 14: 46, 30: 32, 60: 20 }
export function dayWidthFor(windowDays: number): number {
  return DAY_WIDTH[windowDays] ?? 32
}

export const NAME_COL_WIDTH_EXPANDED = 200
export function nameWidthFor(mode: PlannerUiSettings["nameColMode"]): number {
  if (mode === "badge") return 52
  if (mode === "surname") return 96
  return NAME_COL_WIDTH_EXPANDED
}

export interface PlacedBar {
  a: ResAssignmentDto
  lane: number
}

export interface RowData {
  resource: LookupItem
  bars: PlacedBar[]
  lanes: number
}

/** Lane-packing: distribuisce le allocazioni sovrapposte su corsie verticali. */
export function packLanes(items: ResAssignmentDto[]): { placed: PlacedBar[]; lanes: number } {
  const sorted = [...items].sort(
    (a, b) => parseDate(a.dataInizio).getTime() - parseDate(b.dataInizio).getTime()
  )
  const laneEnds: Date[] = []
  const placed: PlacedBar[] = sorted.map((a) => {
    const s = parseDate(a.dataInizio)
    const e = parseDate(a.dataFine)
    let lane = laneEnds.findIndex((end) => end < s)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(e)
    } else {
      laneEnds[lane] = e
    }
    return { a, lane }
  })
  return { placed, lanes: Math.max(1, laneEnds.length) }
}

/** Intestazione mesi: un blocco per mese, largo quanto i giorni che copre. */
export function monthSpans(
  bandStart: Date,
  bandDays: number,
  dayW: number
): { label: string; width: number }[] {
  const out: { label: string; width: number }[] = []
  let i = 0
  while (i < bandDays) {
    const d = addDays(bandStart, i)
    const m = d.getMonth()
    let span = 0
    while (i + span < bandDays) {
      const dd = addDays(bandStart, i + span)
      if (dd.getMonth() !== m) break
      span++
    }
    out.push({
      label: `${monthName(m + 1)} ${d.getFullYear()}`,
      width: span * dayW,
    })
    i += span
  }
  return out
}

/** Festività (bande verticali) e posizione della colonna "oggi". */
export function dayOverlays(
  bandStart: Date,
  bandDays: number,
  dayW: number,
  today: Date
): { holidays: { left: number; width: number }[]; todayLeft: number } {
  const holidays: { left: number; width: number }[] = []
  for (let i = 0; i < bandDays; i++) {
    const d = addDays(bandStart, i)
    if (isHoliday(d)) holidays.push({ left: i * dayW, width: dayW })
  }
  const todayIdx = diffDays(today, bandStart)
  const todayLeft = todayIdx >= 0 && todayIdx < bandDays ? todayIdx * dayW : -1
  return { holidays, todayLeft }
}

/** Sfondo track: weekend (gradiente, banda parte di lunedì) + linee giorno. */
export function trackBackgroundCss(dayW: number): string {
  const weekend = `repeating-linear-gradient(90deg, transparent 0, transparent ${
    5 * dayW
  }px, var(--g-weekend) ${5 * dayW}px, var(--g-weekend) ${7 * dayW}px)`
  const lines = `repeating-linear-gradient(90deg, var(--g-line-soft) 0, var(--g-line-soft) 1px, transparent 1px, transparent ${dayW}px)`
  return `${lines}, ${weekend}`
}
