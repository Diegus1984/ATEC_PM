/** Gestione Lavorazioni (richieste di lavorazione) — allineati a ATEC.PM.Shared/DTOs. */

export interface Rfq {
  supplier: string
  date: string
  ok: boolean
}

export interface WorkRequest {
  id: number
  projectId: number
  projectName: string
  projectCode: string
  /** Cliente della commessa (riga a sé nella colonna "Commessa"). */
  customerName: string
  requestDate: string
  description: string
  /** Righe manuali di Lavorazioni Officine (#83): sulle righe da distinta stanno sulla riga DDP. */
  partNumber: string
  quantity: number
  quantityProduced: number
  material: string
  treatment: string
  destination: string
  destinationSpec: string
  type: string
  priority: number | null
  availabilityDate: string
  notes: string
  isUltraCritical: boolean
  isDelivered: boolean
  deliveredAt: number | null
  isStaging: boolean
  rfqs: Rfq[]
  poSupplier: string
  poNumber: string
  poDate: string
  hasTreatment: boolean
  treatmentDate: string
  treatmentNotes: string
  isTreatmentConfirmed: boolean
  treatmentConfirmedAt: number | null
  rowVersion: number
  createdAt: number
  /** Riga DDP Officina che ha generato la lavorazione (null = inserita a mano).
   *  Descrizione, data disponibilità e trattamento seguono la riga DDP. */
  ddpOfficinaItemId: number | null
}

export interface WorkRequestSaveRequest {
  id?: number
  /** 0 = riga manuale senza commessa (#83). */
  projectId: number
  requestDate: string
  description: string
  partNumber?: string
  quantity?: number
  quantityProduced?: number
  material?: string
  treatment?: string
  destination?: string
  destinationSpec?: string
  type: string
  priority: number | null
  availabilityDate: string
  notes: string
  isUltraCritical: boolean
  isDelivered: boolean
  deliveredAt?: number | null
  isStaging: boolean
  rfqs: Rfq[]
  poSupplier: string
  poNumber: string
  poDate: string
  hasTreatment: boolean
  treatmentDate: string
  treatmentNotes: string
  isTreatmentConfirmed: boolean
  treatmentConfirmedAt?: number | null
  // Concurrency token: se valorizzato, il server rifiuta con CONFLITTO se la riga è cambiata
  rowVersion?: number | null
}

// Payload dell'evento SignalR "WorkRequestsChanged" (hub /hubs/project)
export interface WorkRequestsChange {
  action: string
  projectId: number | null
}

/** Provenienza di una riga di «Lavorazioni Officine» (#83). */
export type WorkshopRowSource = "DDP" | "MANUAL"

/**
 * Riga della pagina «Lavorazioni Officine». Le righe `DDP` sono la riga di distinta vista da
 * qui: tutto in sola lettura tranne `requestDate`, `notes` e `isUltraCritical`, che questa
 * pagina possiede e che nella DDP non si toccano. Le righe `MANUAL` sono battute a mano,
 * non hanno stato DDP e possono non avere commessa.
 */
export interface WorkshopRow {
  source: WorkshopRowSource
  /** Id nella tabella d'origine: unico solo a parità di `source`. */
  id: number
  projectId: number | null
  projectCode: string
  projectTitle: string
  customerName: string
  partNumber: string
  description: string
  quantity: number
  quantityProduced: number
  material: string
  treatment: string
  destination: string
  destinationSpec: string
  /** 'Internal' | 'External' — decide la vista in cui la riga compare. */
  workType: string
  /** Stato DDP (DC, DO, PAR, MIT…); vuoto sulle righe manuali. */
  itemStatus: string
  requestDate: string
  daysLate: number | null
  supplierName: string
  daneaRef: string
  orderDate: string
  /** «Consegnato il»: su questa data si ordinano e si filtrano le esterne. */
  deliveredAt: string
  notes: string
  isUltraCritical: boolean
  /** Concorrenza delle righe DDP. */
  updatedAt: string | null
  /** Concorrenza delle righe manuali. */
  rowVersion: number
}
