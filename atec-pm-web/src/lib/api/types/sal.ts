/** SAL: piano pagamenti, condizioni, prospetto — allineati a ATEC.PM.Shared/DTOs. */

export interface SalRow {
  id: number
  projectId: number
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  /** Stato fatturazione: '' | 'daEmettere' | 'emessa' */
  stato: string
  sortOrder: number
  rowVersion: number
  paidBy: number | null
  paidAt: string | null
  /** %IVA intera (default UI 22, mai lato server). */
  ivaPerc: number | null
  /** GG saldo fattura (data prevista saldo = dataFatt + ggSaldo). */
  ggSaldo: number | null
  /** N° fattura (solo cifre, stringa per zeri iniziali). */
  nFatt: string
  /** Causale Conto SAP (da anagrafica). */
  contoSap: string
  /** Stato pagamento (da anagrafica, semantica cablata su 'Pagata'). */
  pagamento: string
  /** Data incasso. */
  dataPagamento: string | null
  note: string
}

export interface SalHeader {
  projectId: number
  /** Legacy: testo libero su project_sal (non più editabile dal foglio). */
  cliente: string
  /** Ragione sociale dalla commessa/anagrafica clienti (sola lettura). */
  customerName: string
  valore: number | null
  rowVersion: number
  /** PO - Ordine cliente. */
  po: string
  /** Riferimento Offerta ATEC. */
  rifOfferta: string
}

export interface SalBundle {
  header: SalHeader
  rows: SalRow[]
}

export interface SalHeaderSaveRequest {
  /** Ignorato dal server: il cliente deriva dalla commessa. */
  cliente?: string
  valore: number | null
  rowVersion: number | null
  // Regola null-preserve server-side: il client web invia SEMPRE stringhe
  // esplicite (anche "") per po/rifOfferta — mai undefined/null.
  po: string
  rifOfferta: string
}

export interface SalRowSaveRequest {
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  stato: string
  rowVersion: number | null
  ivaPerc: number | null
  ggSaldo: number | null
  // Regola null-preserve server-side: il client web invia SEMPRE stringhe
  // esplicite (anche "") per nFatt/contoSap/pagamento/note — mai undefined/null.
  nFatt: string
  contoSap: string
  pagamento: string
  dataPagamento: string | null
  note: string
}

export interface SalReorderRequest {
  ids: number[]
}

export interface SalCondition {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
  /**
   * Colori riga OPZIONALI (hex #RRGGBB[AA]) — usati solo dagli Stati Pagamento;
   * le altre due anagrafiche SAL li lasciano sempre null. null = stato neutro
   * senza tinta (a differenza degli stati DDP dove i colori sono obbligatori).
   * Pura estetica: la semantica (lock, incasso, Cash Flow) resta cablata sulle
   * etichette «Pagata»/«Parzialmente Pagata».
   */
  colorBg: string | null
  colorFg: string | null
}

export interface SalConditionSaveRequest {
  label: string
  /** Colori opzionali (solo Stati Pagamento): null/assente = nessuna tinta. */
  colorBg?: string | null
  colorFg?: string | null
}

export interface SalProspettoRow {
  projectId: number
  code: string
  cliente: string
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  importo: number | null
  ord: number
  /** '' (in programma) | scaduta | pre-warning | incasso scaduto | emessa in attesa. */
  alert: "" | "warn" | "pre" | "incasso" | "attesa"
  /** GG saldo fattura. */
  ggSaldo: number | null
  /** Data prevista saldo (derivata: dataFatt + ggSaldo). */
  dataSaldo: string | null
  /** Stato fatturazione: '' | 'daEmettere' | 'emessa'. */
  stato: string
  /** Stato pagamento (da anagrafica). */
  pagamento: string
}

/** Stato del controllo periodico del prospetto SAL (ultima conferma registrata). */
export interface SalProspettoCheck {
  /** Data/ora ultimo controllo (null se mai fatto). */
  checkedAt: string | null
  /** Nome di chi ha confermato l'ultimo controllo. */
  checkedByName: string
  /** Giorni trascorsi dall'ultimo controllo. */
  days: number | null
  /** true = controllo dovuto (mai fatto o periodo di 15 gg scaduto). */
  due: boolean
  /** Prossima scadenza del controllo. */
  nextDue: string | null
}

/** Testata economica Cash Flow: una per ogni project_sal di commessa attiva (anche senza righe). */
export interface SalEconomicsHeader {
  projectId: number
  code: string
  cliente: string
  /** Importo ordine commessa. */
  valore: number | null
}

/** Riga economica SAL globale (base per Cash Flow / Analisi / drill-down, calcoli in SQL). */
export interface SalEconomicsRow {
  projectId: number
  code: string
  cliente: string
  /** Importo ordine commessa. */
  valore: number | null
  step: string
  perc: number | null
  /** valore × perc / 100. */
  importo: number | null
  ivaPerc: number | null
  /** importo × ivaPerc / 100 (0 se ivaPerc null). */
  iva: number | null
  /** importo + iva. */
  totIva: number | null
  condizione: string
  dataFatt: string | null
  ggSaldo: number | null
  /** Data prevista saldo (derivata: dataFatt + ggSaldo). */
  dataSaldo: string | null
  stato: string
  pagamento: string
}

/** Bundle economico globale SAL: testate (tutte le commesse attive) + righe (dettaglio step). */
export interface SalEconomics {
  headers: SalEconomicsHeader[]
  rows: SalEconomicsRow[]
}

export interface SalSummary {
  projectId: number
  code: string
  title: string
  total: number
  open: number
  warn: number
  pre: number
  /** Fatture con incasso scaduto (non pagate, data prevista saldo < oggi). */
  incasso: number
  /** Σ % SAL di tutte le righe (denominatore avanzamento incasso). */
  percTotal: number
  /** Σ % delle righe con Pagamento = «Pagata». */
  percPaid: number
}
