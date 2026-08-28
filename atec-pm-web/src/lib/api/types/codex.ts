/** Codex: articoli, composizioni, ricodifica, import — allineati a ATEC.PM.Shared/DTOs. */

export interface CompositionTreeNode {
  compositionId: number
  codexId: number
  catalogId: number | null
  codice: string
  descr: string
  source: string // "codex" | "catalog"
  quantity: number
  children: CompositionTreeNode[]
}

/** Figlio diretto (primo livello) della composizione di un articolo Codex. */
export interface CompositionChildItem {
  id: number
  parentCodexId: number
  childCodexId: number | null
  childCatalogId: number | null
  childCodice: string
  childDescr: string
  sortOrder: number
  quantity: number
  source: string // "codex" | "catalog"
}

export interface AddCompositionRequest {
  parentCodexId: number
  childCodexId: number | null
  childCatalogId: number | null
  quantity: number
}

/** Notifica real-time (SignalR `CompositionChanged`, hub `/hubs/codex`) di modifica composizione Codex. */
export interface CompositionChange {
  parentCodexId: number
  action: string // create | delete
  compositionId: number
}

export interface CodexImportItem {
  code: string
  quantity: number
}

export interface CodexImportPreviewResult {
  code: string
  quantity: number
  descr: string | null
  id: number | null
  source: string | null // "codex" | "catalog"
  isValid: boolean
  error: string | null
}

export interface CodexImportCommitRequest {
  parentId: number
  items: CodexImportItem[]
  replaceExisting: boolean
}

// ── Codex ────────────────────────────────────────────────
export interface CodexListItem {
  id: number
  codice: string
  /** Nuova codifica (ricodifica manuale): "" = riga non ancora ricodificata. */
  codiceNuovo: string
  codeForn: string
  fornitore: string
  prezzoForn: number
  iva: string
  produttore: string
  data: string
  descr: string
  note: string
  categoria: string
  barcode: string
  tipologia: string
  extra1: string
  extra2: string
  extra3: string
  codeProd: string
  spec: string
  oper: number
  um: string
  ubicazione: string
  codexforn: string
  /**
   * #135 — grezzo commerciale da cui si ricava questo particolare a disegno.
   * `refCommercialeCodice` vuoto = nessuna derivazione. L'id serve alla DELETE.
   */
  refCommercialeId: number | null
  refCommercialeCodice: string
  refCommercialeDescr: string
}

export interface CodexRecodeStats {
  total: number
  done: number
}

/** Riga dell'anteprima di assegnazione massiva: vecchio codice → nuovo PRENOTATO. */
export interface CodexBulkReserveItem {
  id: number
  codice: string
  descr: string
  newCode: string
  reservationId: number
}

export interface CodexBulkReserveResult {
  items: CodexBulkReserveItem[]
  skipped: number
}

export interface CodexSyncStatus {
  isSyncing: boolean
  lastSync: string | null
  totalRows: number
  lastError: string | null
}

export interface CodexPrefix {
  codice: string
  descrizione: string
}

export interface CodexReservationResult {
  codice: string
  reservationId: number
}

export interface CodexGeneratedCode {
  codice: string
  id: number
}

export interface AddCodexReferenceRequest {
  sourceCodexId: number
  refCodexId: number
  /** "201" (commerciale) o "401" (materia prima). */
  refType: string
}

/**
 * Derivazione di un particolare a disegno: da quale articolo commerciale si ricava (#135).
 * Il codice arriva già col punto dal server.
 */
export interface CodexItemReference {
  id: number
  sourceCodexId: number
  refCodexId: number
  /** "201" (commerciale) o "401" (materia prima, famiglia ritirata). */
  refType: string
  refCodice: string
  refDescr: string
}
