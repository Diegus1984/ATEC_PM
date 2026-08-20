import type {
  ChecklistGroup,
  ChecklistItem,
  ChecklistItemSaveRequest,
} from "@/lib/api/types"
import { dateToIso, isoToDate } from "@/lib/date-iso"

// Logica pura del modulo Check list (separata dai componenti per l'HMR fast-refresh).
// Priorità 0–3 come nel prototipo: 0=Critica · 1=Alta · 2=Media · 3=Bassa.

export const PRIORITY_META = [
  { value: 0, code: "P0", name: "Critica", trigger: "border-red-300 bg-red-50 text-red-700", dot: "bg-red-500" },
  { value: 1, code: "P1", name: "Alta", trigger: "border-amber-300 bg-amber-50 text-amber-700", dot: "bg-amber-500" },
  { value: 2, code: "P2", name: "Media", trigger: "border-blue-300 bg-blue-50 text-blue-700", dot: "bg-blue-500" },
  { value: 3, code: "P3", name: "Bassa", trigger: "border-teal-300 bg-teal-50 text-teal-700", dot: "bg-teal-500" },
] as const

/** Acronimi fetta torta + descrizione in legenda (grafico scadenze). */
export const DUE_STATUS_META = [
  { key: "overdue" as const, code: "SCAD", name: "Scadute" },
  { key: "today" as const, code: "OGG", name: "Oggi" },
  { key: "soon" as const, code: "3GG", name: "Entro 3 giorni" },
  { key: "ok" as const, code: "OK", name: "In tempo" },
  { key: "none" as const, code: "ND", name: "Senza data" },
] as const

/** Sfondo tenue + bordo inset nel colore priorità (righe non ultra-critiche). */
const PRIORITY_ROW_CLASS = [
  "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]",
  "bg-amber-50/35 hover:bg-amber-50/55 shadow-[inset_0_0_0_1px_theme(colors.amber.200),inset_3px_0_0_0_theme(colors.amber.300)]",
  "bg-blue-50/35 hover:bg-blue-50/55 shadow-[inset_0_0_0_1px_theme(colors.blue.200),inset_3px_0_0_0_theme(colors.blue.300)]",
  "bg-teal-50/35 hover:bg-teal-50/55 shadow-[inset_0_0_0_1px_theme(colors.teal.200),inset_3px_0_0_0_theme(colors.teal.300)]",
] as const

const PRIORITY_ROW_CRITICAL_CLASS =
  "bg-red-50/85 hover:bg-red-50 shadow-[inset_0_0_0_1px_theme(colors.red.300),inset_4px_0_0_0_theme(colors.red.500)]"

export function priorityRowClass(priority: number, isCritical: boolean): string {
  if (isCritical) return PRIORITY_ROW_CRITICAL_CLASS
  return PRIORITY_ROW_CLASS[priority] ?? PRIORITY_ROW_CLASS[2]
}

export function priorityMeta(p: number) {
  return PRIORITY_META[p] ?? PRIORITY_META[2]
}

/** Pallini priorità per icona compatta sidebar (critica in cima, poi P0–P3). Ignora i chiusi. */
export function containerPriorityDots(
  items: Pick<ChecklistItem, "priority" | "isCritical" | "status">[]
): { dotClass: string; label: string }[] {
  const dots: { dotClass: string; label: string }[] = []
  const open = items.filter((i) => i.status !== "CLOSED")
  const hasCritical = open.some((i) => i.isCritical)

  if (hasCritical) {
    dots.push({ dotClass: "bg-red-600", label: "Attività critiche" })
  }

  for (const p of PRIORITY_META) {
    if (!open.some((i) => i.priority === p.value)) continue
    if (p.value === 0 && hasCritical) continue
    dots.push({ dotClass: p.dot, label: `${p.code} · ${p.name}` })
  }

  return dots
}

// Lettura data e ordinamento dei codici commessa: spostati in `lib/project-code`
// perché servono anche fuori dalla Check list (stessa separazione nel MoM).
// Codici liberi come `INTERNA` o `SERVICE _ SANGRATO` restano senza data.
export { compareProjectCodes, isCommessaCode, projectCodeDate } from "@/lib/project-code"

/** true se un altro gruppo (≠ `excludeId`) ha già questo nome (confronto senza maiuscole). */
export function groupNameExists(
  groups: ChecklistGroup[],
  name: string,
  excludeId?: number
): boolean {
  const normalized = name.trim().toLowerCase()
  return groups.some(
    (g) => g.id !== excludeId && g.name.trim().toLowerCase() === normalized
  )
}

/** Container di destinazione di un'attività: commessa (projectId) o gruppo (groupId). */
export interface ItemContainer {
  projectId?: number
  groupId?: number
}

export function daysFromToday(dueDate: string | null): number | null {
  const d = isoToDate(dueDate)
  if (!d) return null
  const t = new Date()
  t.setHours(0, 0, 0, 0)
  return Math.round((d.getTime() - t.getTime()) / 86_400_000)
}

export function addDaysIso(dueDate: string | null, n: number): string {
  const base = isoToDate(dueDate) ?? new Date()
  base.setHours(0, 0, 0, 0)
  base.setDate(base.getDate() + n)
  return dateToIso(base)
}

/** Costruisce la richiesta di salvataggio da un item + patch, preservando il container. */
export function buildSave(
  item: ChecklistItem,
  patch: Partial<ChecklistItemSaveRequest>
): ChecklistItemSaveRequest {
  return {
    projectId: item.projectId,
    groupId: item.groupId,
    description: patch.description ?? item.description,
    priority: patch.priority ?? item.priority,
    dueDate: "dueDate" in patch ? (patch.dueDate ?? null) : item.dueDate,
    isCritical: patch.isCritical ?? item.isCritical,
    status: patch.status ?? item.status,
    rowVersion: item.rowVersion,
  }
}

type SortKey = "priority" | "date"

export function sortChecklistItems(
  items: ChecklistItem[],
  key: SortKey
): ChecklistItem[] {
  return [...items].sort((a, b) => {
    // Le attività chiuse (gestite) vanno sempre in fondo.
    const aClosed = a.status === "CLOSED"
    const bClosed = b.status === "CLOSED"
    if (aClosed !== bClosed) return aClosed ? 1 : -1
    if (a.isCritical !== b.isCritical) return a.isCritical ? -1 : 1
    const da = a.dueDate ?? "9999-99-99"
    const db = b.dueDate ?? "9999-99-99"
    if (key === "date") return da.localeCompare(db)
    if (a.priority !== b.priority) return a.priority - b.priority
    return da.localeCompare(db)
  })
}

export interface ChecklistStats {
  total: number
  overdue: number
  today: number
  soon: number
  ok: number
  none: number
  critical: number
  closed: number
  byPriority: Record<number, number>
}

export type ChecklistPriorityFilter = "all" | number
export type ChecklistDueFilter = "all" | "overdue" | "today" | "soon" | "ok" | "none"

/** Conteggi rapidi per dashboard, grafici e filtri nella vista commessa. */
export function computeChecklistStats(items: ChecklistItem[]): ChecklistStats {
  const byPriority: Record<number, number> = { 0: 0, 1: 0, 2: 0, 3: 0 }
  let overdue = 0
  let today = 0
  let soon = 0
  let ok = 0
  let none = 0
  let critical = 0
  let closed = 0

  for (const item of items) {
    // Le attività chiuse (gestite) non concorrono a scadenze/priorità/urgenza.
    if (item.status === "CLOSED") {
      closed++
      continue
    }
    byPriority[item.priority] = (byPriority[item.priority] ?? 0) + 1
    if (item.isCritical) critical++
    const n = daysFromToday(item.dueDate)
    if (n === null) {
      none++
      continue
    }
    if (n < 0) overdue++
    else if (n === 0) today++
    else if (n <= 3) soon++
    else ok++
  }

  return { total: items.length, overdue, today, soon, ok, none, critical, closed, byPriority }
}

export function filterChecklistItems(
  items: ChecklistItem[],
  priority: ChecklistPriorityFilter,
  due: ChecklistDueFilter
): ChecklistItem[] {
  return items.filter((item) => {
    if (priority !== "all" && item.priority !== priority) return false
    if (due === "all") return true
    // Le attività chiuse non rientrano nei filtri per scadenza (solo in "tutte").
    if (item.status === "CLOSED") return false
    const n = daysFromToday(item.dueDate)
    if (due === "none") return n === null
    if (n === null) return false
    if (due === "overdue") return n < 0
    if (due === "today") return n === 0
    if (due === "soon") return n > 0 && n <= 3
    if (due === "ok") return n > 3
    return true
  })
}
