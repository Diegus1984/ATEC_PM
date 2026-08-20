import type { CSSProperties } from "react"

import type { PmSidebarDot } from "@/components/shared/pm-sidebar"
import type { SalCondition } from "@/lib/api/types"
import { addDays, mondayOf, parseDate, toIso } from "@/features/risorse/planner-logic"

/** Calcola il lunedì della settimana precedente a una data ISO YYYY-MM-DD. */
export function mondayPrevWeek(isoDate: string): string {
  const d = parseDate(isoDate)
  const mon = mondayOf(d)
  const prevMon = addDays(mon, -7)
  return toIso(prevMon)
}

/** True se il valore di Pagamento equivale a «Pagata» (case-insensitive, coerente col server). */
export function salIsPagata(pagamento: string | null | undefined): boolean {
  return (pagamento ?? "").trim().toLowerCase() === "pagata"
}

/** True se il valore di Pagamento equivale a «Parzialmente Pagata» (case-insensitive). */
export function salIsParzialmentePagata(pagamento: string | null | undefined): boolean {
  return (pagamento ?? "").trim().toLowerCase() === "parzialmente pagata"
}

/** Valore effettivo GG saldo: null/undefined → 0 (default di business). */
export function salGgSaldoValue(ggSaldo: number | null | undefined): number {
  return ggSaldo ?? 0
}

/**
 * Data prevista saldo (regola v10, D4): data fattura + gg saldo, in ISO yyyy-MM-dd.
 * Derivata, mai persistita. Ritorna null se manca la data fattura.
 */
export function salDataSaldo(dataFatt: string | null, ggSaldo: number | null): string | null {
  if (!dataFatt) return null
  const gg = salGgSaldoValue(ggSaldo)
  if (Number.isNaN(gg)) return null
  return toIso(addDays(parseDate(dataFatt), gg))
}

/**
 * Warning incasso fattura (regola esatta v10 / D11): incasso scaduto se OGGI è
 * OLTRE la data prevista saldo e la riga non è «Pagata» — indipendente dallo
 * stato di fatturazione. Nessun pre-warning: scatta dal giorno dopo.
 */
export function salIncassoScaduto(
  row: { pagamento: string; dataFatt: string | null; ggSaldo: number | null },
  today?: string
): boolean {
  if (salIsPagata(row.pagamento)) return false
  const dataSaldo = salDataSaldo(row.dataFatt, row.ggSaldo)
  if (!dataSaldo) return false
  const todayIso = today ?? toIso(new Date())
  return todayIso > dataSaldo
}

/**
 * Semaforo fatturazione di una riga SAL rispetto ad oggi (warn = ipotesi scaduta,
 * pre = imminente dal lunedì della settimana precedente).
 * Regola v10: l'alert è escluso SOLO se la fattura è già emessa (non più per
 * 'pagata': lo stato legacy viene mappato dal server in emessa + Pagamento='Pagata').
 */
export function salAlertState(
  row: { stato: string; dataFatt: string | null },
  todayIso: string
): "warn" | "pre" | "emessa" | "pagata" | "none" {
  if (row.stato === "emessa") return "emessa"

  if (!row.dataFatt) return "none"
  const dateStr = row.dataFatt.slice(0, 10)
  if (dateStr <= todayIso) return "warn"
  const prevMonStr = mondayPrevWeek(dateStr)
  if (todayIso >= prevMonStr) return "pre"

  return "none"
}

/**
 * Testo leggibile per uno sfondo dato: nero sui fondi chiari, bianco sui pieni
 * (regola di Diego 17/08: «background rosso pieno → testo bianco, il resto nero»).
 * Accetta #rgb/#rrggbb; colore non interpretabile → undefined (testo di tema).
 */
export function salTestoSuFondo(colorBg: string): string | undefined {
  const hex = colorBg.trim().replace(/^#/, "")
  const full =
    hex.length === 3
      ? hex.split("").map((c) => c + c).join("")
      : hex.length === 6
        ? hex
        : null
  if (!full || !/^[0-9a-fA-F]{6}$/.test(full)) return undefined
  const r = parseInt(full.slice(0, 2), 16)
  const g = parseInt(full.slice(2, 4), 16)
  const b = parseInt(full.slice(4, 6), 16)
  // Luminanza percepita (Rec. 601): sotto la metà lo sfondo è "pieno" → bianco.
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
  return luminance < 0.55 ? "#ffffff" : "#18181b"
}

/**
 * Stile inline della riga SAL dal colore CONFIGURATO in anagrafica per lo stato
 * pagamento selezionato (match per etichetta, case-insensitive — l'identità
 * degli stati è la label, parità v10). Ritorna undefined se il valore è vuoto,
 * lo stato non è (più) in anagrafica o non ha colore configurato (colorBg null):
 * in quel caso valgono le classi cablate di fallback di salRowClass.
 * Il testo della riga NON usa il colorFg dell'anagrafica (il verde di «Pagata»
 * colorava tutta la riga): si calcola dallo sfondo con salTestoSuFondo.
 * Pura estetica: la semantica (lock, incasso, warning, bucket Cash Flow) resta
 * cablata SOLO sulle etichette «Pagata»/«Parzialmente Pagata».
 */
export function salPagamentoStyle(
  pagamento: string,
  states: SalCondition[]
): CSSProperties | undefined {
  const label = (pagamento ?? "").trim().toLowerCase()
  if (!label) return undefined
  const state = states.find((s) => s.label.trim().toLowerCase() === label)
  if (!state?.colorBg) return undefined
  return { backgroundColor: state.colorBg, color: salTestoSuFondo(state.colorBg) }
}

/**
 * Stato visivo complessivo di una riga del foglio SAL, con priorità v10
 * Pagamento > Stato > semaforo fatturazione:
 * «Pagata» → verde · «Parzialmente Pagata» → rosso · stato 'emessa' → giallo ·
 * altrimenti l'eventuale warn/pre dell'ipotesi di fatturazione.
 *
 * Se viene passata l'anagrafica `paymentStates` e lo stato pagamento della riga
 * ha un colore configurato (→ tinta inline via salPagamentoStyle), ritorna
 * 'custom': la riga NON deve avere anche le classi bg cablate (doppia tinta).
 * Le classi cablate restano il FALLBACK per le voci senza colore o storiche
 * non più in anagrafica, e per il giallo 'emessa'.
 */
export function salRowVisualState(
  row: { pagamento: string; stato: string; dataFatt: string | null },
  todayIso: string,
  paymentStates?: SalCondition[]
): "custom" | "pagata" | "parziale" | "emessa" | "warn" | "pre" | "none" {
  if (paymentStates && salPagamentoStyle(row.pagamento, paymentStates)) return "custom"
  if (salIsPagata(row.pagamento)) return "pagata"
  if (salIsParzialmentePagata(row.pagamento)) return "parziale"
  if (row.stato === "emessa") return "emessa"
  const alert = salAlertState(row, todayIso)
  if (alert === "warn" || alert === "pre") return alert
  return "none"
}

/**
 * Classi Tailwind (tenuità colore + anello inset) per lo stato visivo di una riga.
 * Accetta anche gli alert del prospetto ('incasso', 'attesa'): valori sconosciuti
 * ricadono senza crash sul default neutro.
 */
export function salRowClass(s: string): string {
  switch (s) {
    // Colore configurato in anagrafica → tinta via style inline
    // (salPagamentoStyle): nessuna classe cablata, né bg né ring.
    case "custom":
      return ""
    case "pagata":
      return "bg-emerald-50/35 hover:bg-emerald-50/55 shadow-[inset_0_0_0_1px_theme(colors.emerald.200),inset_3px_0_0_0_theme(colors.emerald.300)]"
    case "parziale":
      return "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]"
    case "emessa":
      return "bg-amber-50/35 hover:bg-amber-50/55 shadow-[inset_0_0_0_1px_theme(colors.amber.200),inset_3px_0_0_0_theme(colors.amber.300)]"
    case "warn":
      return "bg-red-50/35 hover:bg-red-50/55 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]"
    case "pre":
      return "bg-yellow-50/35 hover:bg-yellow-50/55 shadow-[inset_0_0_0_1px_theme(colors.yellow.200),inset_3px_0_0_0_theme(colors.yellow.300)]"
    case "incasso":
      return "bg-rose-50/45 hover:bg-rose-50/65 shadow-[inset_0_0_0_1px_theme(colors.rose.300),inset_3px_0_0_0_theme(colors.rose.400)]"
    case "attesa":
      return "bg-sky-50/35 hover:bg-sky-50/55 shadow-[inset_0_0_0_1px_theme(colors.sky.200),inset_3px_0_0_0_theme(colors.sky.300)]"
    default:
      return ""
  }
}

/** Pallini di stato per la sidebar PM a partire dal riepilogo SAL di una commessa. */
export function salSummaryDots(s: {
  warn: number
  pre: number
  open: number
  incasso?: number
}): PmSidebarDot[] {
  const dots: PmSidebarDot[] = []
  const incasso = s.incasso ?? 0
  if (incasso > 0)
    dots.push({
      dotClass: "bg-rose-700",
      label: incasso === 1 ? "1 incasso scaduto" : `${incasso} incassi scaduti`,
    })
  if (s.warn > 0) dots.push({ dotClass: "bg-red-500", label: `${s.warn} scadute` })
  if (s.pre > 0) dots.push({ dotClass: "bg-yellow-500", label: `${s.pre} imminenti` })
  if (dots.length === 0 && s.open > 0) dots.push({ dotClass: "bg-emerald-500", label: "in programma" })
  return dots
}
