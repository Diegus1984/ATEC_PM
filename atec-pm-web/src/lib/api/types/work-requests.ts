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
  requestDate: string
  description: string
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
  projectId: number
  requestDate: string
  description: string
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
