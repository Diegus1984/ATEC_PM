// ── Foglio SAL: colonne, payload di riga e navigazione da tastiera ─────────

import type { SalRow, SalRowSaveRequest } from "@/lib/api/types"

/** Larghezze colonne foglio SAL (header multiriga + celle allineate). */
export const SAL_COL = {
  num: "w-14",
  iva: "w-24",
  ivaPerc: "w-20 min-w-[4.5rem]",
  totIva: "w-24",
  dataSaldo: "w-24",
  ggSaldo: "w-20 min-w-[5rem]",
  step: "min-w-[10rem]",
  // 10 cifre (es. 2026000193) a text-sm ≈ 82px + padding cella/input: sotto i 8rem il numero si taglia.
  nFatt: "w-32",
  contoSap: "w-32",
  perc: "w-20 min-w-[4.5rem]",
  condizione: "w-32",
  importo: "w-24",
  dataFatt: "w-28",
  stato: "w-28",
  pagamento: "w-32",
  dataIncasso: "w-28",
  note: "min-w-[10rem]",
  actions: "w-12",
} as const

export type SalColId = keyof typeof SAL_COL

/** Ordine canonico delle colonne del foglio (header, footer, colSpan riga nuova). */
export const SAL_COL_ORDER: SalColId[] = [
  "num", "iva", "ivaPerc", "totIva", "dataSaldo", "ggSaldo", "step", "nFatt",
  "contoSap", "perc", "condizione", "importo", "dataFatt", "stato",
  "pagamento", "dataIncasso", "note", "actions",
]

/** Colonne visibili solo ai ruoli con dati economici. */
export const SAL_ECONOMICS_COLS = new Set<SalColId>(["iva", "totIva", "importo"])

/** Voci del menu «Colonne» (# e azioni restano sempre visibili). */
export const SAL_HIDEABLE_COLS: { id: SalColId; label: string }[] = [
  { id: "iva", label: "IVA" },
  { id: "ivaPerc", label: "% IVA" },
  { id: "totIva", label: "Tot. + IVA" },
  { id: "dataSaldo", label: "Data Prev. Saldo" },
  { id: "ggSaldo", label: "GG. Saldo" },
  { id: "step", label: "Step SAL" },
  { id: "nFatt", label: "N° Fattura" },
  { id: "contoSap", label: "Conto SAP" },
  { id: "perc", label: "% SAL" },
  { id: "condizione", label: "Condizioni Pagamento" },
  { id: "importo", label: "Importo Fattura" },
  { id: "dataFatt", label: "Ipotesi Fatturazione" },
  { id: "stato", label: "Stato Fatturazione" },
  { id: "pagamento", label: "Pagamento" },
  { id: "dataIncasso", label: "Data Incasso" },
  { id: "note", label: "Note" },
]

export const SAL_VISIBILITY_STORAGE_KEY = "table-visibility-sal-fatturazione-v1"

export type DropHint = { id: number; after: boolean }

/**
 * Payload di una riga SAL nuova: default v10 (%IVA = 22, %SAL = 0) + stringhe
 * SEMPRE esplicite (contratto null-preserve: mai undefined/null sui campi testo).
 */
export function emptyRowRequest(step = ""): SalRowSaveRequest {
  return {
    step,
    perc: 0,
    condizione: "",
    dataFatt: null,
    stato: "",
    ivaPerc: 22,
    ggSaldo: 0,
    nFatt: "",
    contoSap: "",
    pagamento: "",
    dataPagamento: null,
    note: "",
    rowVersion: null,
  }
}

/**
 * Riga esistente + patch → payload SEMPRE completo (contratto null-preserve: le
 * stringhe viaggiano esplicite, anche vuote) con rowVersion per la concorrenza.
 */
export function salRowPayload(
  row: SalRow,
  fields: Partial<SalRowSaveRequest>
): SalRowSaveRequest {
  return {
    step: row.step,
    perc: row.perc,
    condizione: row.condizione ?? "",
    dataFatt: row.dataFatt,
    stato: row.stato,
    ivaPerc: row.ivaPerc,
    ggSaldo: row.ggSaldo,
    nFatt: row.nFatt ?? "",
    contoSap: row.contoSap ?? "",
    pagamento: row.pagamento ?? "",
    dataPagamento: row.dataPagamento,
    note: row.note ?? "",
    rowVersion: row.rowVersion,
    ...fields,
  }
}

/**
 * Enter = riempimento verticale (pattern Excel v10): sposta il focus sullo STESSO
 * campo della riga successiva della stessa tabella; il blur che ne consegue
 * committa il valore corrente. Se non c'è una riga sotto (o è bloccata), fa solo
 * blur (= commit). La ricerca è scoping-ata sulla <table> per non saltare tra
 * commesse diverse nella vista «Tutte le commesse».
 */
export function focusNextRowField(el: HTMLElement, col: string, rowIndex: number): void {
  const table = el.closest("table")
  const next = table?.querySelector<HTMLElement>(
    `[data-sal-col="${col}"][data-sal-row="${rowIndex + 1}"]`
  )
  if (next && !(next as HTMLInputElement | HTMLTextAreaElement).disabled) {
    next.focus()
    if (next instanceof HTMLInputElement) next.select()
  } else {
    el.blur()
  }
}
