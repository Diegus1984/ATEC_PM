/** Acquisti: inbox, RDO e ordini Danea — allineati a ATEC.PM.Shared/DTOs. */

import type { DdpRowItem } from "./ddp"

/** Riga inbox Acquisti cross-commessa. */
export interface AcquistiInboxItem extends DdpRowItem {
  projectCode: string
  projectTitle: string
  customerName: string
  daysLate: number | null
  /** true = riga già in una RDO non annullata (esclusa dai gruppi «pronti per la gara»). */
  inActiveRfq: boolean
  /** RDO viva che contiene la riga (link «in gara — RDO #x» nel pannello). */
  activeRfqId: number | null
  activeRfqStatus: string
}

export interface PurchaseRfqListItem {
  id: number
  atecCode: string
  description: string
  status: string
  notes: string
  createdBy: number | null
  createdByName: string
  createdAt: string | null
  sentAt: string | null
  closedAt: string | null
  updatedAt: string | null
  itemCount: number
  totalQuantity: number
  offerCount: number
  /** Numero dell'ordine fornitore creato in Danea (null = non ancora generato). */
  daneaOrderNum: number | null
  /** IDDoc dell'ordine Danea: chiave per il popup di rendering (GET /api/danea-orders/{idDoc}). */
  daneaOrderIdDoc: number | null
  /** Vincitore e commessa (RDO mono-commessa): chiavi del pannello «Ordini da generare». */
  winnerSupplierId: number | null
  winnerSupplierName: string
  winnerUnitPrice: number | null
  projectId: number | null
  projectCode: string
}

export interface PurchaseRfqItemDto {
  id: number
  rfqId: number
  bomItemId: number
  projectId: number
  projectCode: string
  quantity: number
  partNumber: string
  description: string
  itemStatus: string
  unitCost?: number
  dateNeeded?: string | null
  daneaRef?: string
  daneaOrderIdDoc?: number | null
}

export interface PurchaseRfqOfferDto {
  id: number
  rfqId: number
  supplierId: number
  supplierName: string
  supplierEmail: string
  catalogItemId: number | null
  catalogCode: string
  unitPrice: number | null
  validUntil: string | null
  notes: string
  emailSentAt: string | null
  isWinner: boolean
}

export interface PurchaseRfqDetail extends PurchaseRfqListItem {
  items: PurchaseRfqItemDto[]
  offers: PurchaseRfqOfferDto[]
}

/** Fornitore interpellabile per le righe selezionate (piano richiesta offerta). */
export interface OfferPlanSupplier {
  supplierId: number
  supplierName: string
  supplierEmail: string
  items: OfferPlanItem[]
}

export interface OfferPlanItem {
  bomItemId: number
  projectId: number
  projectCode: string
  catalogItemId: number | null
  /** Codice articolo Danea di QUESTO fornitore (dalla riga o dal mapping ATEC). */
  articleCode: string
  articleDescription: string
  quantity: number
  listCost: number | null
  /** true = fornitore già indicato sulla riga; false = alternativa dal mapping ATEC. */
  isRowSupplier: boolean
}

/** Offerta in attesa di richiesta email (RDO aperta × fornitore non contattato). */
export interface PurchaseRfqEmailCandidate {
  offerId: number
  rfqId: number
  atecCode: string
  rfqDescription: string
  supplierId: number
  supplierName: string
  supplierEmail: string
  /** Codice articolo Danea del fornitore (vuoto se offerta senza articolo). */
  catalogCode: string
  catalogDescription: string
  quantity: number
  projectId: number | null
  projectCode: string
}

/** Ordine fornitore Danea (Atec_PM) reso dal server per il popup «come su Danea». */
export interface DaneaOrderView {
  idDoc: number
  num: number
  date: string | null
  descDoc: string
  orderStatus: string
  warehouse: string
  expectedDate: string | null
  internalNote: string
  supplierName: string
  supplierAddress: string
  supplierZip: string
  supplierCity: string
  supplierProvince: string
  supplierCountry: string
  supplierVat: string
  totNet: number
  totVat: number
  totDoc: number
  rows: DaneaOrderRowView[]
  vatSummary: DaneaOrderVatView[]
}

export interface DaneaOrderRowView {
  code: string
  supplierCode: string
  description: string
  quantity: number
  unit: string
  unitPrice: number
  vatCode: string
  netAmount: number
  grossAmount: number
}

export interface DaneaOrderVatView {
  vatCode: string
  netAmount: number
  vatAmount: number
}
