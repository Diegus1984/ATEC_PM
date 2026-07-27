/** Fasi di commessa e assegnazioni — allineati a ATEC.PM.Shared/DTOs. */

export interface PhaseGanttItem {
  phaseId: number
  phaseName: string
  departmentCode: string
  status: string
  progressPct: number
  budgetHours: number
  hoursWorked: number
  startDate: string | null
  endDate: string | null
  sortOrder: number
}

export interface PhaseTemplateDto {
  id: number
  name: string
  category: string
  costSectionTemplateId: number | null
  costSectionName: string
  sortOrder: number
  isDefault: boolean
}

export interface PhaseTemplateSaveRequest {
  name: string
  category: string
  costSectionTemplateId: number | null
  sortOrder: number
  isDefault: boolean
}

// ── Fasi di commessa + assegnazioni (GET /api/phases/project/{id}) ──────────
export interface PhaseAssignmentDto {
  id: number
  projectPhaseId: number
  employeeId: number
  employeeName: string
  assignRole: string
  plannedHours: number
  hoursWorked: number
}

export interface PhaseListItem {
  id: number
  name: string
  category: string
  budgetHours: number
  budgetCost: number
  status: string
  progressPct: number
  sortOrder: number
  hoursWorked: number
  assignments: PhaseAssignmentDto[]
  phaseTemplateId: number
  customName: string
  costSectionName: string
  costSectionTemplateId: number | null
  isLocal: boolean
}

export interface BulkPhaseRequest {
  projectId: number
  templateIds: number[]
}

export interface LocalPhaseRequest {
  projectId: number
  costSectionTemplateId: number | null
  name: string
  departmentId: number | null
}
