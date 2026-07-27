// Logica pura Cash Flow SAL / Analisi (Fase 5 SAL-V10-PLAN §6) — SOLO funzioni,
// niente React. Formule vincolanti dal prototipo Gestione_Pagamenti_SAL_v10.html:
// classificazione a 3 bucket mutuamente esclusivi, 5 totali cash flow (netto e
// con IVA), serie mensile continua del grafico Analisi e drill-down cliccabile.
// I dati arrivano da GET /api/sal/economics: le rows hanno già importo/iva/
// totIva/dataSaldo calcolati dal server (commesse ACTIVE).

import type { SalEconomics, SalEconomicsRow } from "@/lib/api/types"
import {
  salAlertState,
  salIncassoScaduto,
  salIsPagata,
} from "@/features/commesse/sal-utils"

/** Bucket v10 mutuamente esclusivi: Incassate / Emesse / da Fatturare. */
export type SalBucket = "inc" | "em" | "daf"

/** Tipo di drill-down: bucket + 'bar' (etichetta totale) + 'prev' (punto linea Incasso previsto). */
export type SalDrillKind = SalBucket | "bar" | "prev"

/** Coppia di importi Netto / Con IVA di una card del Cash Flow. */
export interface SalCashAmount {
  netto: number
  conIva: number
}

/** I 5 totali del Cash Flow SAL (Ordini / Incassate / Emesse / da Fatturare / Avere). */
export interface SalCashFlowTotals {
  ordini: SalCashAmount
  incassate: SalCashAmount
  emesse: SalCashAmount
  daFatturare: SalCashAmount
  avere: SalCashAmount
}

/** Punto mensile della serie del grafico Analisi (barre impilate + linea prev). */
export interface SalMonthPoint {
  /** Mese ISO `yyyy-MM`. */
  ym: string
  /** Etichetta breve del mese, es. `gen 26`. */
  label: string
  /** Incassate del mese (Σ importo righe pagate, per mese di dataFatt). */
  inc: number
  /** Emesse non pagate del mese (per mese di dataFatt). */
  em: number
  /** Da fatturare del mese (stato e pagamento vuoti, per mese di dataFatt). */
  daf: number
  /** Totale barra = inc + em + daf. */
  tot: number
  /** Incasso previsto: Σ importo delle righe NON pagate per mese di dataSaldo. */
  prev: number
}

/** Tonalità della pill di stato del drill-down. */
export type SalPillTone = "green" | "red" | "amber" | "sky" | "muted"

/** Pill di stato di una riga del drill-down. */
export interface SalDrillPill {
  label: string
  tone: SalPillTone
}

/** Normalizza numeri nullable/NaN a 0 per le somme. */
const num = (v: number | null | undefined): number =>
  v == null || Number.isNaN(v) ? 0 : v

const MESI_SHORT = [
  "gen", "feb", "mar", "apr", "mag", "giu",
  "lug", "ago", "set", "ott", "nov", "dic",
]

const MESI_FULL = [
  "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno",
  "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre",
]

/** Mese ISO `yyyy-MM` di una data ISO (`dataFatt.slice(0,7)`); null se assente. */
export function salYm(iso: string | null | undefined): string | null {
  if (!iso || iso.length < 7) return null
  return iso.slice(0, 7)
}

/** Etichetta breve del mese per l'asse X: `2026-01` → `gen 26`. */
export function salYmLabel(ym: string): string {
  const [y, m] = ym.split("-").map(Number)
  if (!y || !m || m < 1 || m > 12) return ym
  return `${MESI_SHORT[m - 1]} ${String(y).padStart(4, "0").slice(2)}`
}

/** Titolo esteso del mese per il dialog drill-down: `2026-01` → `gennaio 2026`. */
export function salYmTitle(ym: string): string {
  const [y, m] = ym.split("-").map(Number)
  if (!y || !m || m < 1 || m > 12) return ym
  return `${MESI_FULL[m - 1]} ${y}`
}

/** Mese ISO successivo: `2026-12` → `2027-01`. */
function ymNext(ym: string): string {
  const [y, m] = ym.split("-").map(Number)
  if (!y || !m) return ym
  const nextM = m === 12 ? 1 : m + 1
  const nextY = m === 12 ? y + 1 : y
  return `${nextY}-${String(nextM).padStart(2, "0")}`
}

/** Indice assoluto del mese (anno*12 + mese) per calcolare gli span. */
function ymIndex(ym: string): number {
  const [y, m] = ym.split("-").map(Number)
  return (y ?? 0) * 12 + ((m ?? 1) - 1)
}

/** Mese ISO `yyyy-MM` da un indice assoluto (inverso di `ymIndex`). */
function ymFromIndex(idx: number): string {
  const y = Math.floor(idx / 12)
  const m = (idx % 12) + 1
  return `${String(y).padStart(4, "0")}-${String(m).padStart(2, "0")}`
}

/**
 * Classificazione v10 di una riga SAL in 3 bucket MUTUAMENTE ESCLUSIVI:
 * - `inc` «Incassate»:    pagamento === 'Pagata' (case-insensitive difensivo);
 * - `em`  «Emesse»:       stato === 'emessa' e NON pagata;
 * - `daf` «da Fatturare»: stato vuoto E pagamento vuoto.
 * NB parità v10 voluta: righe con stato='daEmettere' (o pagamento valorizzato
 * ≠ Pagata con stato vuoto) NON rientrano in NESSUN bucket → null.
 */
export function classifySalRow(
  row: Pick<SalEconomicsRow, "stato" | "pagamento">
): SalBucket | null {
  if (salIsPagata(row.pagamento)) return "inc"
  const stato = (row.stato ?? "").trim()
  if (stato === "emessa") return "em"
  if (stato === "" && (row.pagamento ?? "").trim() === "") return "daf"
  return null
}

/**
 * Totali Cash Flow v10 da GET /api/sal/economics ({ headers, rows }):
 * - Totale Ordini: netto = Σ headers[].valore (TUTTI gli header, anche senza
 *   righe); con IVA = Σ headers[].valore + Σ iva di TUTTE le rows;
 * - Incassate / Emesse / da Fatturare: netto = Σ importo del bucket;
 *   con IVA = Σ (importo + iva) = Σ totIva del bucket;
 * - Avere = Emesse + da Fatturare (sia netto che con IVA).
 */
export function cashFlowTotals(data: SalEconomics): SalCashFlowTotals {
  const ordiniNetto = data.headers.reduce((acc, h) => acc + num(h.valore), 0)
  const ivaTotale = data.rows.reduce((acc, r) => acc + num(r.iva), 0)

  const bucketTotals: Record<SalBucket, SalCashAmount> = {
    inc: { netto: 0, conIva: 0 },
    em: { netto: 0, conIva: 0 },
    daf: { netto: 0, conIva: 0 },
  }
  for (const row of data.rows) {
    const bucket = classifySalRow(row)
    if (!bucket) continue
    bucketTotals[bucket].netto += num(row.importo)
    bucketTotals[bucket].conIva += num(row.totIva)
  }

  return {
    ordini: { netto: ordiniNetto, conIva: ordiniNetto + ivaTotale },
    incassate: bucketTotals.inc,
    emesse: bucketTotals.em,
    daFatturare: bucketTotals.daf,
    avere: {
      netto: bucketTotals.em.netto + bucketTotals.daf.netto,
      conIva: bucketTotals.em.conIva + bucketTotals.daf.conIva,
    },
  }
}

/** Guardia della serie mensile: massimo numero di mesi rappresentati. */
const MAX_SERIES_MONTHS = 600

/** Risultato della serie mensile: punti + flag di troncamento della finestra. */
export interface SalMonthlySeries {
  series: SalMonthPoint[]
  /** True se lo span supera la guardia: i mesi PIÙ VECCHI sono stati esclusi. */
  truncated: boolean
}

/**
 * Serie mensile del grafico Analisi (solo righe con importo > 0):
 * - barre impilate per MESE DI dataFatt (`ym = dataFatt.slice(0,7)`):
 *   inc se pagata, em se emessa non pagata, daf se nessuno stato/pagamento;
 *   tot = inc + em + daf (etichetta totale sopra la barra);
 * - linea «Incasso previsto» per MESE DI dataSaldo, per TUTTE le righe non
 *   pagate (indipendente dallo stato di fatturazione);
 * - serie CONTINUA dal primo all'ultimo mese presente (mesi vuoti a zero);
 * - guardia MAX_SERIES_MONTHS: se lo span la supera (es. una data outlier tipo
 *   1960) si tiene la finestra dei mesi PIÙ RECENTI e `truncated` = true.
 */
export function monthlySeries(rows: SalEconomicsRow[]): SalMonthlySeries {
  const bars = new Map<string, { inc: number; em: number; daf: number }>()
  const prevByMonth = new Map<string, number>()

  for (const row of rows) {
    const importo = num(row.importo)
    if (importo <= 0) continue

    const bucket = classifySalRow(row)
    const ymFatt = salYm(row.dataFatt)
    if (bucket && ymFatt) {
      const slot = bars.get(ymFatt) ?? { inc: 0, em: 0, daf: 0 }
      slot[bucket] += importo
      bars.set(ymFatt, slot)
    }

    // Incasso previsto: tutte le righe non pagate, collocate per data prevista saldo.
    if (!salIsPagata(row.pagamento)) {
      const ymSaldo = salYm(row.dataSaldo)
      if (ymSaldo) {
        prevByMonth.set(ymSaldo, (prevByMonth.get(ymSaldo) ?? 0) + importo)
      }
    }
  }

  const keys = [...new Set([...bars.keys(), ...prevByMonth.keys()])].sort()
  if (keys.length === 0) return { series: [], truncated: false }

  const last = keys[keys.length - 1]
  let first = keys[0]

  // Finestra dei mesi più recenti: un outlier antico non deve nascondere i dati attuali.
  const truncated = ymIndex(last) - ymIndex(first) + 1 > MAX_SERIES_MONTHS
  if (truncated) {
    first = ymFromIndex(ymIndex(last) - (MAX_SERIES_MONTHS - 1))
  }

  const out: SalMonthPoint[] = []
  let cur = first
  let guard = 0
  while (cur <= last && guard < MAX_SERIES_MONTHS) {
    const bar = bars.get(cur)
    const inc = bar?.inc ?? 0
    const em = bar?.em ?? 0
    const daf = bar?.daf ?? 0
    out.push({
      ym: cur,
      label: salYmLabel(cur),
      inc,
      em,
      daf,
      tot: inc + em + daf,
      prev: prevByMonth.get(cur) ?? 0,
    })
    cur = ymNext(cur)
    guard++
  }
  return { series: out, truncated }
}

/**
 * Righe del drill-down (solo importo > 0) per un mese `ym` e un tipo di click:
 * - `inc` / `em` / `daf`: mese(dataFatt) === ym && bucket corrispondente;
 * - `bar` (etichetta totale): mese(dataFatt) === ym && bucket qualsiasi
 *   (pagata || emessa || nessuno);
 * - `prev` (punto linea): riga NON pagata && mese(dataSaldo) === ym.
 * Ordinamento: `prev` per dataSaldo crescente, altrimenti dataFatt crescente.
 */
export function drillRows(
  rows: SalEconomicsRow[],
  kind: SalDrillKind,
  ym: string
): SalEconomicsRow[] {
  const filtered = rows.filter((row) => {
    if (num(row.importo) <= 0) return false
    if (kind === "prev") {
      return !salIsPagata(row.pagamento) && salYm(row.dataSaldo) === ym
    }
    if (salYm(row.dataFatt) !== ym) return false
    const bucket = classifySalRow(row)
    if (kind === "bar") return bucket !== null
    return bucket === kind
  })

  const sortKey =
    kind === "prev"
      ? (r: SalEconomicsRow) => r.dataSaldo ?? ""
      : (r: SalEconomicsRow) => r.dataFatt ?? ""
  return filtered.sort((a, b) => sortKey(a).localeCompare(sortKey(b)))
}

/**
 * Pill di stato di una riga del drill-down (stessa priorità del Prospetto):
 * 1. pagata → «Incassata» (verde);
 * 2. incasso scaduto (oggi > dataSaldo, non pagata) → «Fattura no incasso» (rosso);
 * 3. semaforo fatturazione warn/pre → «Scaduto» (rosso) / «Pre-warning» (ambra);
 * 4. stato 'emessa' → «Emessa – attesa incasso» (azzurro);
 * 5. default → «In programma» (neutro).
 */
export function drillPill(
  row: Pick<SalEconomicsRow, "stato" | "pagamento" | "dataFatt" | "ggSaldo">,
  todayIso: string
): SalDrillPill {
  if (salIsPagata(row.pagamento)) return { label: "Incassata", tone: "green" }

  if (salIncassoScaduto(row, todayIso)) {
    return { label: "Fattura no incasso", tone: "red" }
  }

  const alert = salAlertState(row, todayIso)
  if (alert === "warn") return { label: "Scaduto", tone: "red" }
  if (alert === "pre") return { label: "Pre-warning", tone: "amber" }

  if ((row.stato ?? "").trim() === "emessa") {
    return { label: "Emessa – attesa incasso", tone: "sky" }
  }
  return { label: "In programma", tone: "muted" }
}
