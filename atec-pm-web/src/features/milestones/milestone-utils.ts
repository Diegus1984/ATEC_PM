import { dateToIso, isoToDate } from "@/lib/date-iso"
import type { Milestone, MilestoneSaveRequest } from "@/lib/api/types"

/** Numero settimana ISO 8601 (equivalente a WEEKNUM(data;21) di Excel del prototipo). */
export function isoWeek(d: Date): number {
  const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()))
  const dayNum = (t.getUTCDay() + 6) % 7
  t.setUTCDate(t.getUTCDate() - dayNum + 3)
  const firstThursday = new Date(Date.UTC(t.getUTCFullYear(), 0, 4))
  const diff = (t.getTime() - firstThursday.getTime()) / 86_400_000
  return 1 + Math.round((diff - 3 + ((firstThursday.getUTCDay() + 6) % 7)) / 7)
}

/** Etichetta «Wnn» della settimana ISO di una data (vuota se manca la data). */
export function weekLabel(iso: string | null): string {
  const d = isoToDate(iso)
  return d ? "W" + String(isoWeek(d)).padStart(2, "0") : ""
}

/** W.Tot = ROUNDUP((fine - inizio) / 7) come nel master Excel; vuoto se manca una data. */
export function weekTot(inizio: string | null, fine: string | null): string {
  const a = isoToDate(inizio)
  const b = isoToDate(fine)
  if (!a || !b) return ""
  const days = Math.round((b.getTime() - a.getTime()) / 86_400_000)
  return days < 0 ? "" : String(Math.ceil(days / 7))
}

export type MilestoneStatus = "done" | "late" | "current" | "none"

/** Stato della milestone rispetto a oggi (guida i colori riga). Le stringhe ISO si confrontano lessicograficamente. */
export function msStatus(m: Milestone): MilestoneStatus {
  const today = dateToIso(new Date())
  if (m.avanzamento === 100) return "done"
  if (m.dataFine && m.dataFine.slice(0, 10) < today) return "late"
  if (
    m.dataInizio &&
    m.dataFine &&
    m.dataInizio.slice(0, 10) <= today &&
    today <= m.dataFine.slice(0, 10)
  )
    return "current"
  if (m.dataInizio && m.dataInizio.slice(0, 10) === today) return "current"
  return "none"
}

/**
 * Pallini di stato per la sidebar PM (stessa idea delle priorità del Check list): un pallino per
 * ogni stato presente, nell'ordine ritardo → in corso → completate. Colori allineati a
 * {@link statusRowClass}: rosso=in ritardo · blu=in corso · teal=completate.
 */
export function summaryStatusDots(s: {
  late: number
  current: number
  done: number
}): { dotClass: string; label: string }[] {
  const dots: { dotClass: string; label: string }[] = []
  if (s.late > 0) dots.push({ dotClass: "bg-red-500", label: `${s.late} in ritardo` })
  if (s.current > 0) dots.push({ dotClass: "bg-blue-500", label: `${s.current} in corso` })
  if (s.done > 0) dots.push({ dotClass: "bg-teal-500", label: `${s.done} completate` })
  return dots
}

// Tenuità allineata al Check list (bg-{c}-50/35 + anello inset + barretta sinistra): stesso standard,
// meno affaticamento visivo. Famiglie: teal=completata · rosso=in ritardo · blu=in corso.
export function statusRowClass(s: MilestoneStatus): string {
  switch (s) {
    case "done":
      return "bg-teal-50/35 hover:bg-teal-50/55 shadow-[inset_0_0_0_1px_theme(colors.teal.200),inset_3px_0_0_0_theme(colors.teal.300)]"
    case "late":
      return "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]"
    case "current":
      return "bg-blue-50/35 hover:bg-blue-50/55 shadow-[inset_0_0_0_1px_theme(colors.blue.200),inset_3px_0_0_0_theme(colors.blue.300)]"
    default:
      return ""
  }
}

/**
 * Avanzamento medio: media aritmetica sugli avanzamenti delle sole righe ATTIVE (non spente),
 * contando come 0 le righe senza valore — semantica identica al prototipo (somma / n° righe attive).
 */
export function avgAvanz(items: Milestone[]): number | null {
  const active = items.filter((m) => !m.spento)
  if (active.length === 0) return null
  const sum = active.reduce(
    (acc, m) => acc + (typeof m.avanzamento === "number" ? m.avanzamento : 0),
    0
  )
  return Math.round(sum / active.length)
}

/** Periodo min→max sulle date delle righe attive (per la testata). */
export function periodo(items: Milestone[]): { min: string | null; max: string | null } {
  const dates: string[] = []
  for (const m of items) {
    if (m.spento) continue
    if (m.dataInizio) dates.push(m.dataInizio.slice(0, 10))
    if (m.dataFine) dates.push(m.dataFine.slice(0, 10))
  }
  if (dates.length === 0) return { min: null, max: null }
  dates.sort()
  return { min: dates[0], max: dates[dates.length - 1] }
}

/** Costruisce il payload di salvataggio unendo la milestone corrente con le sole modifiche. */
export function buildMilestoneSave(
  m: Milestone,
  patch: Partial<MilestoneSaveRequest>
): MilestoneSaveRequest {
  return {
    descrizione: patch.descrizione ?? m.descrizione,
    dataInizio: patch.dataInizio !== undefined ? patch.dataInizio : m.dataInizio,
    dataFine: patch.dataFine !== undefined ? patch.dataFine : m.dataFine,
    avanzamento:
      patch.avanzamento !== undefined ? patch.avanzamento : m.avanzamento,
    note: patch.note ?? m.note,
    evidenza: patch.evidenza ?? m.evidenza,
    spento: patch.spento ?? m.spento,
    rowVersion: m.rowVersion,
  }
}
