/** MoM (verbali e action item) — allineati a ATEC.PM.Shared/DTOs. */

export interface MoMListItem {
  id: number
  tipo: string
  projectId: number | null
  projectCode: string | null
  projectTitle?: string | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
  rev: number
  itemsCount: number
  openCount: number
  p1Count: number
  p2Count: number
  p3Count: number
  periodStart: string | null
  periodEnd: string | null
}

export interface MoMActionItem {
  id: number
  momId: number
  attivita: string
  descrizione: string | null
  azione: string | null
  priorita: number
  status: string // OPEN | STANDBY | CLOSED
  isCritical: boolean
  resp1Id: number | null
  resp1Name: string | null
  resp2Id: number | null
  resp2Name: string | null
  resp3Id: number | null
  resp3Name: string | null
  dataCheck: string | null
  dataClose: string | null
  sortOrder: number
  rowVersion: number
  responsibleNames: string[]
  responsibleIds: number[]
}

/** Riga dello storico revisioni (cambio data riunione confermato). */
export interface MoMRevision {
  rev: number
  meetingDate: string | null
  prevDate: string | null
  createdAt: string
}

export interface MoMDetail {
  id: number
  tipo: string
  projectId: number | null
  projectCode: string | null
  projectTitle: string | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
  rev: number
  items: MoMActionItem[]
  revisions: MoMRevision[]
}

export interface MoMSaveRequest {
  tipo: string
  projectId: number | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
}

export interface MoMActionItemSaveRequest {
  attivita: string
  descrizione: string | null
  azione: string | null
  priorita: number
  status: string
  isCritical: boolean
  resp1Id: number | null
  resp2Id: number | null
  resp3Id: number | null
  responsibleIds: number[]
  dataCheck: string | null
  dataClose: string | null
  /** Token concorrenza ottimistica: il server rifiuta se la riga è cambiata. */
  rowVersion?: number | null
}

/** Nota di acquisizione rapida (vista «Note MoM», staging personale). */
export interface MoMNote {
  id: number
  note: string
  targetMomId: number | null
}

export interface MoMNoteSaveRequest {
  note: string
  targetMomId: number | null
}

/** Payload dell'evento SignalR MoMChanged (gruppo mom-all). */
export interface MoMChange {
  momId: number
  action: string
}

export interface MoMProjectLookup {
  id: number
  code: string
  title: string
  display: string
}
