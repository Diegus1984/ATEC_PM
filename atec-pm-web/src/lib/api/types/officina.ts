/** DDP Officina (distinta meccanica) — allineati a ATEC.PM.Shared/DTOs. */

export interface OfficinaItem {
  id: number
  projectId: number
  partNumber: string
  description: string
  /** Solo client (non arriva dal server): numero progressivo per i padri, lettera a/b/c per i figli di composizione. */
  rowNumber: number | string
  quantity: number
  /** Pezzi già prodotti / costruiti (0 … quantity). */
  quantityProduced: number
  /** Ore di lavorazione (officine interne): × tariffa oraria = costo unitario. Null = non imputate. */
  workHours: number | null
  /** Tariffa oraria con cui è stato fatto il conto (#87). Null = costo scritto a mano. */
  hourlyRate: number | null
  unitCost: number
  totalCost: number
  material: string
  treatment: string
  supplierId: number | null
  supplierName: string
  itemStatus: string
  /**
   * Natura della lavorazione: "Internal" (officina ATEC) / "External" (fornitore) /
   * "Print3D" (stampa 3D, #87) / "" non classificata. Stessi valori del Tipo delle
   * Lavorazioni. Serve al Bilancio, che scompone la voce «Lavorazioni Officine» in interne
   * (stampa 3D compresa) ed esterne.
   */
  workType: string
  requestedBy: string
  daneaRef: string
  /** #142 — codice del 201 di derivazione (col punto). Vuoto/assente = il 101 non ha grezzo. */
  grezzoCodice?: string
  /** #142 — Rif. Danea della riga grezzo in DDP Commerciale ("" = non ancora ordinato). */
  grezzoDaneaRef?: string
  /** #142 — IDDoc dell'ordine Danea del grezzo (null = ordine non generato da ATEC PM). */
  grezzoDaneaOrderIdDoc?: number | null
  dateNeeded: string | null
  /** Data In Ordine (auto al passaggio IO; editabile). */
  orderDate: string | null
  /** «Consegnato il» (#82): editabile a mano; auto al primo passaggio CON/COS/DISP se vuota. */
  deliveredAt?: string | null
  destination: string
  destinationSpec: string
  notes: string
  /** «Comanda il padre»: id della riga padre se importata dalla composizione Codex. */
  parentOfficinaItemId: number | null
  /** Quantità unitaria di composizione (per 1 padre); il server riallinea i figli al cambio Qtà del padre. */
  compositionQty: number | null
  createdById?: number | null
  createdByName?: string
  createdAt: string | null
  updatedAt: string | null
}

/** Riga inbox Officina (cross-commessa) per il Responsabile. */
export interface OfficinaInboxItem extends OfficinaItem {
  projectCode: string
  projectTitle: string
  customerName: string
  workRequestId: number | null
  /** Priorità WR (0=P0 … 2=P2); null se senza lavorazione. */
  wrPriority: number | null
  isUltraCritical: boolean
  /** Giorni di ritardo (positivo = in ritardo); null se senza dateNeeded. */
  daysLate: number | null
}

export interface OfficinaItemSaveRequest {
  id: number
  projectId: number
  partNumber: string
  description: string
  quantity: number
  quantityProduced: number
  /** Ore di lavorazione (officine interne). Null = non imputate. */
  workHours?: number | null
  /** Tariffa oraria scelta (#87). Omessa = il server lascia quella già scritta sulla riga. */
  hourlyRate?: number | null
  unitCost: number
  material: string
  treatment: string
  supplierId?: number | null
  supplierName: string
  itemStatus: string
  /** Omesso = il server lascia invariata la classificazione interna/esterna esistente. */
  workType?: string
  requestedBy: string
  createdByName?: string
  createdAt?: string | null
  daneaRef: string
  dateNeeded: string | null
  orderDate: string | null
  /** «Consegnato il» (#82). */
  deliveredAt?: string | null
  destination: string
  destinationSpec: string
  notes: string
  expectedUpdatedAt?: string | null
  parentOfficinaItemId?: number | null
  updateCodexPrice?: boolean
}

/** Import in distinta officina della composizione Codex di un codice padre (figli diretti). */
export interface OfficinaImportCompositionRequest {
  codexParentId: number
  requestedBy: string
}

export interface OfficinaImportCompositionResult {
  added: number // nuove righe inserite
  updated: number // righe esistenti con quantità sommata
  skipped: number // figli non importabili (articoli da Catalogo)
  parentQuantity: number // moltiplicatore applicato (Qtà del padre in distinta)
}
