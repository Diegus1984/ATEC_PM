/** Segnalazioni (bug e migliorie) — allineati a ATEC.PM.Shared/DTOs/BugReports_DTOs.cs */

export type BugKind = "BUG" | "IMPROVEMENT"
export type BugSeverity = "LOW" | "MEDIUM" | "HIGH"
export type BugStatus = "OPEN" | "IN_PROGRESS" | "RESOLVED" | "REJECTED"

export interface BugAttachment {
  id: number
  bugId: number
  fileName: string
  contentType: string
  sizeBytes: number
  isImage: boolean
  isReply?: boolean
  /** Chi ha caricato il file: solo lui (o chi gestisce) può toglierlo. NULL = utenza rimossa. */
  createdById: number | null
  createdByName?: string
  createdAt: string
}

export interface BugReport {
  id: number
  kind: BugKind
  title: string
  description: string
  area: string
  severity: BugSeverity
  status: BugStatus
  adminNote: string
  context?: string | null
  fixedInBuild?: string | null
  archivedAt?: string | null
  createdById: number | null
  createdByName: string
  createdAt: string
  updatedAt: string | null
  resolvedAt: string | null
  rowVersion: number
  /** true = l'ha aperta l'utente collegato (può modificarla o eliminarla). */
  isMine: boolean
  attachments: BugAttachment[]
}

export interface BugReportSaveRequest {
  kind: BugKind
  title: string
  description: string
  area: string
  severity: BugSeverity
  context?: string | null
  rowVersion?: number | null
}

/** Cambio stato + risposta: solo ADMIN. */
export interface BugReportStatusRequest {
  status: BugStatus
  adminNote: string
  rowVersion?: number | null
}

export interface BugReportCounts {
  all: number
  mine: number
  open: number
  inProgress: number
  resolved: number
  rejected: number
  archived: number
}

/** Payload dell'evento SignalR BugReportsChanged (gruppo bugs-all). */
export interface BugReportChange {
  action: string
  bugId: number
}
