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

// ── Sincronizzazione con ATEC Risorse (VPS) ─────────────────────
// Allineati a ATEC.PM.Shared/DTOs (RisorseSync*), sotto `/api/resource-planner/sync`.
// Le date-ora (`lastRun`, `runUtc`, `serverUtc`) arrivano come ISO datetime UTC.

/** Impostazioni del collegamento al VPS. In lettura `password` non è mai valorizzata:
 *  `hasPassword` dice se ce n'è una salvata; in scrittura vuota/omessa = non cambiarla. */
export interface RisorseSyncSettingsDto {
  enabled: boolean
  baseUrl: string
  username: string
  password?: string | null
  hasPassword: boolean
  lastRun?: string | null
  lastEsito?: string | null
  lastError?: string | null
}

/** Esito di un giro di sincronizzazione (una riga della tabella «Ultimi giri»). */
export interface RisorseSyncLogEntry {
  runUtc: string
  innesco: string
  esito: string
  durataMs: number
  dettaglio?: string | null
}

/** Stato del servizio di sincronizzazione, riletto a intervalli dalla pagina. */
export interface RisorseSyncStatusDto {
  enabled: boolean
  configured: boolean
  hubConnected: boolean
  inCorso: boolean
  lastRun?: string | null
  lastEsito?: string | null
  lastError?: string | null
  ultimiGiri: RisorseSyncLogEntry[]
}

/** Risposta della prova di collegamento: conteggi e versione letti dal VPS. */
export interface SyncStatusDto {
  serverUtc: string
  employees: number
  projects: number
  departments: number
  assignments: number
  version: string
}
