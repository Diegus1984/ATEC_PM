/** Gestione Risorse: planner, ferie, presenze — allineati a ATEC.PM.Shared/DTOs. */

// ── Gestione Risorse (Planner / Ferie) ──────────────────────────
// Allineato a ATEC.PM.Shared/DTOs/Resources_DTOs.cs (serializzazione camelCase).
// Le date arrivano come ISO datetime ("2026-06-30T00:00:00"); lato client si usa la
// porzione yyyy-MM-dd (date "pure", nessun fuso). Tipi: OP | FLEX | FERIE.
export type ResTipo = "OP" | "FLEX" | "FERIE"

export interface ResAssignmentDto {
  id: number
  employeeId: number
  employeeName: string
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId: number | null
  projectCode: string | null
  projectTitle: string | null
  serviceId: number | null
  serviceCod: string | null
  otherActivityId: number | null
  otherActivityDesc: string | null
  descrizione: string | null
  hasConflict: boolean
  updatedBy: number | null
  updatedByName: string | null
  updatedAt: string | null
  giorni: number
}

export interface ResAssignmentCreateRequest {
  employeeIds: number[]
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId?: number | null
  serviceId?: number | null
  otherActivityId?: number | null
  descrizione?: string | null
}

export interface ResAssignmentUpdateRequest {
  employeeId: number
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId?: number | null
  serviceId?: number | null
  otherActivityId?: number | null
  descrizione?: string | null
  /** Versione (updated_at) vista all'apertura: il server risponde 409 se cambiata. */
  expectedUpdatedAt?: string | null
}

export interface ResAssignmentChange {
  action: string // create | update | delete
  ids: number[]
}

/** SignalR "PresenceChanged": chi ha almeno un client Gantt connesso in questo momento. */
export interface PresenceSnapshot {
  onlineEmployeeIds: number[]
}

export interface ResServiceDto {
  id: number
  cod: string
  cliente: string | null
  isActive: boolean
  display: string
}

export interface ResServiceSaveRequest {
  cod: string
  cliente?: string | null
}

export interface ResOtherActivityDto {
  id: number
  descrizione: string
  isActive: boolean
}

export interface ResOtherActivitySaveRequest {
  descrizione: string
}

// ── Digest email (riepilogo modifiche piano risorse) ────────────
export interface PlanChangeLine {
  assignmentId: number
  kind: string // new | changed | deleted
  attivita: string
  periodo: string
  note: string | null
  autoreNome: string | null
}
