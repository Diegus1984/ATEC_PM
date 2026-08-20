/** Check list / Attività — allineati a ATEC.PM.Shared/DTOs. */

// ── Check list / Attività ───────────────────────────────
// Priorità 0–3 (come nel prototipo): 0=Critica · 1=Alta · 2=Media · 3=Bassa.

/** Stato attività, allineato al MoM: aperta · in standby · chiusa (gestita). */
export type ChecklistStatus = "OPEN" | "STANDBY" | "CLOSED"

export interface ChecklistItem {
  id: number
  projectId: number | null
  groupId: number | null
  description: string
  priority: number
  dueDate: string | null // ISO date
  isCritical: boolean
  status: ChecklistStatus
  dataClose: string | null // ISO date, valorizzata quando status=CLOSED
  sortOrder: number
  rowVersion: number
  createdAt: string | null // ISO datetime, sola lettura
}

export interface ChecklistGroup {
  id: number
  name: string
  sortOrder: number
  rowVersion: number
  items: ChecklistItem[]
}

export interface ChecklistProject {
  projectId: number
  code: string
  title: string
  /** Cliente della commessa (riga a sé nella colonna "Commessa / Gruppo"). */
  customerName: string
  display: string
  items: ChecklistItem[]
}

export interface ChecklistBoard {
  projects: ChecklistProject[]
  groups: ChecklistGroup[]
}

export interface ChecklistItemSaveRequest {
  projectId: number | null
  groupId: number | null
  description: string
  priority: number
  dueDate: string | null
  isCritical: boolean
  status?: ChecklistStatus
  rowVersion?: number | null
}

export interface ChecklistGroupSaveRequest {
  name: string
  rowVersion?: number | null
}

export interface ChecklistInboxItem {
  id: number
  text: string
  sortOrder: number
}

export interface ChecklistInboxSaveRequest {
  text: string
}

export interface ChecklistAssignRequest {
  projectId: number | null
  groupId: number | null
}

export interface ChecklistProjectLookup {
  id: number
  code: string
  title: string
  display: string
}

/** Payload dell'evento SignalR ChecklistChanged (gruppo checklist-all + project-{id}). */
export interface ChecklistChange {
  action: string
  projectId: number | null
}
