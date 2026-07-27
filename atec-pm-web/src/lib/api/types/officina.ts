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
  unitCost: number
  totalCost: number
  material: string
  treatment: string
  supplierId: number | null
  supplierName: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  /** Data In Ordine (auto al passaggio IO; editabile). */
  orderDate: string | null
  destination: string
  destinationSpec: string
  notes: string
  /** «Comanda il padre»: id della riga padre se importata dalla composizione Codex. */
  parentOfficinaItemId: number | null
  /** Quantità unitaria di composizione (per 1 padre); il server riallinea i figli al cambio Qtà del padre. */
  compositionQty: number | null
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
  unitCost: number
  material: string
  treatment: string
  supplierId?: number | null
  supplierName: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  orderDate: string | null
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
