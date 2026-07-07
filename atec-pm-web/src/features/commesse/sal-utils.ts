import type { SalRow } from "@/lib/api/types"
import { addDays, mondayOf, parseDate, toIso } from "@/features/risorse/planner-logic"

/** Calcola il lunedì della settimana precedente a una data ISO YYYY-MM-DD. */
export function mondayPrevWeek(isoDate: string): string {
  const d = parseDate(isoDate)
  const mon = mondayOf(d)
  const prevMon = addDays(mon, -7)
  return toIso(prevMon)
}

/** Determina lo stato di avviso di una riga SAL rispetto ad oggi. */
export function salAlertState(
  row: SalRow,
  todayIso: string
): "warn" | "pre" | "emessa" | "pagata" | "none" {
  if (row.stato === "pagata") return "pagata"
  if (row.stato === "emessa") return "emessa"
  
  if (!row.stato) {
    if (!row.dataFatt) return "none"
    const dateStr = row.dataFatt.slice(0, 10)
    if (dateStr <= todayIso) return "warn"
    const prevMonStr = mondayPrevWeek(dateStr)
    if (todayIso >= prevMonStr) return "pre"
  }
  
  return "none"
}

/** Assegna le classi CSS Tailwind per la tenuità dei colori e l'anello inset della riga. */
export function salRowClass(
  s: "warn" | "pre" | "emessa" | "pagata" | "none"
): string {
  switch (s) {
    case "pagata":
      return "bg-emerald-50/35 hover:bg-emerald-50/55 shadow-[inset_0_0_0_1px_theme(colors.emerald.200),inset_3px_0_0_0_theme(colors.emerald.300)]"
    case "emessa":
      return "bg-amber-50/35 hover:bg-amber-50/55 shadow-[inset_0_0_0_1px_theme(colors.amber.200),inset_3px_0_0_0_theme(colors.amber.300)]"
    case "warn":
      return "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]"
    case "pre":
      return "bg-yellow-50/35 hover:bg-yellow-50/55 shadow-[inset_0_0_0_1px_theme(colors.yellow.200),inset_3px_0_0_0_theme(colors.yellow.300)]"
    default:
      return ""
  }
}

import type { PmSidebarDot } from "@/components/shared/pm-sidebar"
/** Pallini di stato per la sidebar PM a partire dal riepilogo SAL di una commessa. */
export function salSummaryDots(s: { warn: number; pre: number; open: number }): PmSidebarDot[] {
  const dots: PmSidebarDot[] = []
  if (s.warn > 0) dots.push({ dotClass: "bg-red-500", label: `${s.warn} scadute` })
  if (s.pre > 0) dots.push({ dotClass: "bg-yellow-500", label: `${s.pre} imminenti` })
  if (dots.length === 0 && s.open > 0) dots.push({ dotClass: "bg-emerald-500", label: "in programma" })
  return dots
}

