/** DDP commerciale: righe, stati, destinazioni, controllo — allineati a ATEC.PM.Shared/DTOs. */

export interface DdpProjectSummary {
  projectId: number
  code: string
  customerName: string
  ddpType: string // COMMERCIAL | OFFICINA
  totalRows: number
  totalValue: number
  datedCount: number
  overdueCount: number
  deliveryStart: string | null
  deliveryEnd: string | null
  lastInsertedAt: string | null
  statusCounts?: DdpStatusCount[]
}

export interface DdpStatusCount {
  statusKey: string
  count: number
}

export interface DdpProjectDetail {
  projectId: number
  code: string
  customerName: string
  totalRows: number
  totalValue: number
  datedCount: number
  overdueCount: number
  deliveryStart: string | null
  deliveryEnd: string | null
  statusCounts: DdpStatusCount[]
}

/** Contatori C/O di un report di controllo cross-commessa (hub "Report di Controllo"). */
export interface DdpControlSummaryEntry {
  report: string
  commercialCount: number
  officinaCount: number
}

/** Riga di un report di controllo: riga DDP completa + riferimenti commessa. */
export interface DdpControlReportRow {
  projectId: number
  projectCode: string
  customerName: string
  ddpType: string
  id: number
  rowNumber: number | string
  partNumber: string
  description: string
  unit: string
  quantity: number
  unitCost: number
  supplierName: string
  manufacturer: string
  material: string
  treatment: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  createdAt: string | null
  destination: string
  destinationSpec: string
  notes: string
  parentOfficinaItemId?: number | null
  compositionQty?: number | null
}

/** Consegne previste in un giorno su tutte le commesse (Analisi Consegne). */
export interface DdpDeliveriesDay {
  day: string
  commercialCount: number
  commercialValue: number
  officinaCount: number
  officinaValue: number
}

/** Feedback Acquisti (aggregato su tutte le commesse): una riga per stato dell'aggregazione A6. */
export interface DdpFeedbackAcquistiGroup {
  projectId: number
  code: string
  customerName: string
  ddpType: string // "COMMERCIAL" | "OFFICINA"
  rows: DdpFeedbackAcquistiRow[]
}

export interface DdpFeedbackAcquistiRow {
  statusKey: string
  count: number
  note: string
  hidden: boolean
}

/** Feedback Magazzino (aggregato su tutte le commesse): righe reali negli stati dell'aggregazione A7. */
export interface DdpFeedbackMagazzinoGroup {
  projectId: number
  code: string
  customerName: string
  ddpType: string
  rows: DdpFeedbackMagazzinoRow[]
}

export interface DdpFeedbackMagazzinoRow {
  itemId: number
  requestedBy: string
  description: string
  quantity: number
  unit: string
  material: string
  treatment: string
  supplierName: string
  manufacturer: string
  itemStatus: string
  daneaRef: string
  destination: string
  destinationSpec: string
  notes: string
  hidden: boolean
}

/**
 * Riga DDP di una commessa (distinta commerciale `bom_items` o officina `ddp_officina_items`).
 * Shape unificato: le righe officina hanno gli stessi nomi proprietà (PartNumber, SupplierName…)
 * più `material`/`treatment`; i campi assenti restano ai default. Usato dalla Sintesi DDP.
 */
export interface DdpRowItem {
  id: number
  projectId: number
  rowNumber: number | string
  catalogItemId?: number | null
  partNumber: string
  description: string
  unit: string
  quantity: number
  unitCost: number
  totalCost: number
  supplierId?: number | null
  supplierName: string
  manufacturer: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  /** IDDoc dell'ordine Danea generato da ATEC PM (null = mai ordinata via RDO): abilita il popup ordine. */
  daneaOrderIdDoc?: number | null
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  ddpType?: string
  /** Snapshot codice ATEC (nuova codifica); vuoto = senza mapping. */
  atecCode?: string
  createdAt: string | null
  updatedAt: string | null
  material?: string
  treatment?: string
  parentOfficinaItemId?: number | null
  compositionQty?: number | null
}

/** Richiesta crea/modifica riga DDP commerciale (`bom_items`).
 *  `expectedUpdatedAt` = token concorrenza ottimistica (null = nessun controllo). */
export interface BomItemSaveRequest {
  id: number
  projectId: number
  catalogItemId: number | null
  partNumber: string
  description: string
  unit: string
  quantity: number
  unitCost: number
  supplierId: number | null
  manufacturer: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  ddpType: string
  atecCode?: string
  /** Il server aggiorna `supplier_id` solo se true (evita che i client che non gestiscono il campo lo azzerino). */
  updateSupplier?: boolean
  /** true = aggiorna anche snapshot catalogo (codice, costo, manufacturer, atec…). */
  updateCatalogSnapshot?: boolean
  /** Il server scrive `unit_cost` solo se true: gli edit inline rimandano il costo invariato. */
  updateUnitCost?: boolean
  expectedUpdatedAt: string | null
}

/** Notifica real-time (SignalR `DdpChanged`) di modifica distinta di una commessa. */
export interface DdpChange {
  projectId: number
  action: string // create | update | delete
  itemId: number
  ddpType: string // COMMERCIAL | OFFICINA
}

export interface DdpDestinationItem {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

export interface DdpDestinationSaveRequest {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

export interface DdpTreatmentItem {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

export interface DdpTreatmentSaveRequest {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

export interface DdpStatusItem {
  id: number
  statusKey: string
  label: string
  colorBg: string
  colorFg: string
  sortOrder: number
  isActive: boolean
}

export interface DdpStatusSaveRequest {
  id: number
  label: string
  colorBg: string
  colorFg: string
  sortOrder: number
  isActive: boolean
}

/**
 * Riga della matrice degli avanzamenti di stato DDP (v7), per tipo di distinta
 * (`ddpType` COMMERCIAL | OFFICINA — la commerciale non contempla DC): stato corrente →
 * stati selezionabili nella finestra opzioni. `fromKey` speciale "INIZIO" = finestra di
 * partenza delle righe senza stato. `toKeys` vuota = stato terminale (ANN, SOST).
 * Una coppia (tipo, stato) ASSENTE dalla matrice non è governata → finestra completa.
 */
export interface DdpStatusTransitionItem {
  ddpType: string
  fromKey: string
  toKeys: string[]
}

export interface DdpAggregation {
  id: number
  code: string
  name: string
  description: string
  kind: string
  sortOrder: number
  isActive: boolean
  statusKeys: string[]
}

export interface DdpAggregationSaveRequest {
  id: number
  name: string
  description: string
  statusKeys: string[]
}
