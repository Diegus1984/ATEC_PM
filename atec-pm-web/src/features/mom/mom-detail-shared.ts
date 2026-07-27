import type { MoMActionItem, MoMActionItemSaveRequest } from "@/lib/api/types"
import { toDateOnly } from "@/lib/date-iso"

export interface HeaderState {
  id: number
  tipo: string
  projectId: number | null
  projectCode: string | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
  rev: number
}

export type FilterKey = "tutte" | "aperte" | "critiche" | "scadute" | "mie"

// Priorità MoM 1–3 (v9), stile allineato alla check list.
export const MOM_PRIORITY_META = [
  { value: 1, code: "P1", name: "Alta", dot: "bg-red-500" },
  { value: 2, code: "P2", name: "Media", dot: "bg-amber-500" },
  { value: 3, code: "P3", name: "Bassa", dot: "bg-teal-500" },
] as const

const MOM_PRIORITY_ROW_CLASS: Record<number, string> = {
  1: "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]",
  2: "bg-amber-50/35 hover:bg-amber-50/55 shadow-[inset_0_0_0_1px_theme(colors.amber.200),inset_3px_0_0_0_theme(colors.amber.300)]",
  3: "bg-teal-50/35 hover:bg-teal-50/55 shadow-[inset_0_0_0_1px_theme(colors.teal.200),inset_3px_0_0_0_theme(colors.teal.300)]",
}

const MOM_ROW_CRITICAL_CLASS =
  "bg-red-50/85 hover:bg-red-50 shadow-[inset_0_0_0_1px_theme(colors.red.300),inset_4px_0_0_0_theme(colors.red.500)]"

const MOM_ROW_STANDBY_CLASS =
  "bg-amber-50/50 hover:bg-amber-50/70 shadow-[inset_0_0_0_1px_theme(colors.amber.200),inset_3px_0_0_0_theme(colors.amber.400)]"

const MOM_ROW_CLOSED_CLASS =
  "bg-emerald-50/35 hover:bg-emerald-50/55 shadow-[inset_0_0_0_1px_theme(colors.emerald.200),inset_3px_0_0_0_theme(colors.emerald.300)]"

/** Sfondo riga del foglio MoM — stesso linguaggio visivo della check list. */
export function momRowClass(item: {
  priorita: number
  status: string
  isCritical: boolean
}): string {
  if (item.status === "CLOSED") return MOM_ROW_CLOSED_CLASS
  if (item.isCritical) return MOM_ROW_CRITICAL_CLASS
  if (item.status === "STANDBY") return MOM_ROW_STANDBY_CLASS
  return MOM_PRIORITY_ROW_CLASS[item.priorita] ?? MOM_PRIORITY_ROW_CLASS[2]
}

// Colonne ordinabili del foglio (v9): campo → etichetta. `field: null` = ordine
// manuale (drag&drop), attivato dal riordino righe o dall'inserimento in fondo.
export type SheetSortField =
  | "attivita"
  | "descrizione"
  | "azione"
  | "priorita"
  | "responsabili"
  | "dataCheck"
  | "dataClose"

export interface SheetSort {
  field: SheetSortField | null
  dir: 1 | -1
}

export function todayIso(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(
    now.getDate()
  ).padStart(2, "0")}`
}

export function formatDate(value: string | null): string {
  const day = toDateOnly(value)
  if (!day) return "—"
  const [year, month, date] = day.split("-")
  return `${date}/${month}/${year}`
}

export function isOverdue(item: MoMActionItem, today: string): boolean {
  const day = toDateOnly(item.dataCheck)
  return day != null && item.status !== "CLOSED" && day < today
}

export function headerTitle(header: HeaderState): string {
  return header.tipo === "COMMESSA" && header.projectCode
    ? `${header.projectCode} — ${header.title}`
    : header.title
}

/** Fascia per il drag&drop: stesso stato/gruppo e stessa priorità P1–P3. */
export function rowDragBucket(item: MoMActionItem): string {
  if (item.status === "CLOSED") return `closed-${item.priorita}`
  if (item.isCritical) return `critical-${item.priorita}`
  if (item.status === "STANDBY") return `standby-${item.priorita}`
  return `open-${item.priorita}`
}

export function canReorderRows(
  drag: MoMActionItem,
  target: MoMActionItem
): boolean {
  return rowDragBucket(drag) === rowDragBucket(target)
}

/**
 * Riordina solo all'interno della fascia (priorità + gruppo stato).
 * Le altre righe restano al loro posto nel foglio.
 */
export function reorderWithinBucket(
  rows: MoMActionItem[],
  dragId: number,
  targetId: number,
  after: boolean
): MoMActionItem[] | null {
  const drag = rows.find((r) => r.id === dragId)
  const target = rows.find((r) => r.id === targetId)
  if (!drag || !target || !canReorderRows(drag, target)) return null

  const bucket = rowDragBucket(drag)
  const slotIndices: number[] = []
  const bucketRows: MoMActionItem[] = []
  for (let i = 0; i < rows.length; i++) {
    if (rowDragBucket(rows[i]) === bucket) {
      slotIndices.push(i)
      bucketRows.push(rows[i])
    }
  }

  const subFrom = bucketRows.findIndex((r) => r.id === dragId)
  const subTo = bucketRows.findIndex((r) => r.id === targetId)
  if (subFrom < 0 || subTo < 0) return null

  const sub = [...bucketRows]
  const [moved] = sub.splice(subFrom, 1)
  const insertAt = sub.findIndex((r) => r.id === targetId)
  if (insertAt < 0) return null
  sub.splice(after ? insertAt + 1 : insertAt, 0, moved)

  const out = [...rows]
  slotIndices.forEach((idx, i) => {
    out[idx] = sub[i]
  })
  const base = Math.min(...bucketRows.map((r) => r.sortOrder))
  for (let i = 0; i < sub.length; i++) {
    const idx = out.findIndex((r) => r.id === sub[i].id)
    if (idx >= 0) out[idx] = { ...out[idx], sortOrder: base + i }
  }
  return out
}

/** Ordine manuale: priorità P1→P3, poi sort_order (inserimento / drag nella fascia). */
function manualRowCmp(a: MoMActionItem, b: MoMActionItem): number {
  if (a.priorita !== b.priorita) return a.priorita - b.priorita
  if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder
  return a.id - b.id
}

/**
 * Ordinamento del foglio (v9): gruppi fissi rosse → aperte → stand-by → chiuse;
 * con ordinamento colonna attivo si ordina per quella colonna nel gruppo;
 * senza colonna (default) si ordina per priorità + sort_order nel gruppo.
 */
export function orderRows(
  rows: MoMActionItem[],
  sort: SheetSort
): MoMActionItem[] {
  const red = rows.filter((r) => r.status !== "CLOSED" && r.isCritical)
  const standby = rows.filter((r) => r.status === "STANDBY" && !r.isCritical)
  const open = rows.filter(
    (r) => r.status === "OPEN" && !r.isCritical
  )
  const closed = rows.filter((r) => r.status === "CLOSED")

  if (sort.field) {
    const field = sort.field
    const value = (item: MoMActionItem): string | number => {
      if (field === "responsabili") return responsibleNamesOf(item).join(" ")
      if (field === "priorita") return item.priorita
      const raw = item[field]
      return raw ?? ""
    }
    const cmp = (a: MoMActionItem, b: MoMActionItem): number => {
      const va = value(a)
      const vb = value(b)
      if (field === "priorita") return ((va as number) - (vb as number)) * sort.dir
      return (
        String(va).localeCompare(String(vb), "it", { numeric: true }) * sort.dir
      )
    }
    red.sort(cmp)
    open.sort(cmp)
    standby.sort(cmp)
    closed.sort(cmp)
  } else {
    red.sort(manualRowCmp)
    open.sort(manualRowCmp)
    standby.sort(manualRowCmp)
    closed.sort(manualRowCmp)
  }
  return [...red, ...open, ...standby, ...closed]
}

export function selectedIds(item: MoMActionItem): number[] {
  if (item.responsibleIds.length > 0) return item.responsibleIds
  return [item.resp1Id, item.resp2Id, item.resp3Id].filter(
    (value): value is number => value != null
  )
}

export function responsibleNamesOf(item: MoMActionItem): string[] {
  return item.responsibleNames.length > 0
    ? item.responsibleNames.filter(Boolean)
    : [item.resp1Name, item.resp2Name, item.resp3Name].filter(
        (value): value is string => Boolean(value)
      )
}

export function matchesFilter(
  item: MoMActionItem,
  filter: FilterKey,
  myId: number | null,
  today: string
): boolean {
  switch (filter) {
    case "aperte":
      return item.status !== "CLOSED"
    case "critiche":
      return item.isCritical && item.status !== "CLOSED"
    case "scadute":
      return isOverdue(item, today)
    case "mie":
      return myId != null && selectedIds(item).includes(myId)
    default:
      return true
  }
}

export function buildSaveRequest(item: MoMActionItem): MoMActionItemSaveRequest {
  const ids = selectedIds(item)
  return {
    attivita: item.attivita.trim(),
    descrizione: item.descrizione,
    azione: item.azione,
    priorita: item.priorita,
    status: item.status,
    isCritical: item.isCritical,
    resp1Id: item.resp1Id,
    resp2Id: item.resp2Id,
    resp3Id: item.resp3Id,
    responsibleIds: ids,
    dataCheck: item.dataCheck ? item.dataCheck.slice(0, 10) : null,
    dataClose: item.dataClose ? item.dataClose.slice(0, 10) : null,
    rowVersion: item.rowVersion,
  }
}
