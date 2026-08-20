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

/**
 * Una fase agganciata a UNA sezione di costo. Una fase ne ha N: «Call Cliente» sta sotto
 * Program Manager e sotto Progettazione insieme, e in commessa nasce una volta per sezione.
 * Ordine e «default» sono del legame, non della fase. Vedi PIANO-FASI-MULTISEZIONE.md.
 */
export interface PhaseTemplateSectionLink {
  sectionId: number
  sectionName: string
  sectionType: string
  groupName: string
  sortOrder: number
  isDefault: boolean
}

export interface PhaseTemplateDto {
  id: number
  name: string
  /** Compatibilità: la PRIMA di `sections`. Il codice nuovo legge `sections`. */
  costSectionTemplateId: number | null
  costSectionName: string
  sortOrder: number
  /** Vero se la fase nasce da sola in almeno una delle sue sezioni. */
  isDefault: boolean
  sections: PhaseTemplateSectionLink[]
}

export interface PhaseTemplateSaveRequest {
  name: string
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
  /**
   * Fase spenta (#51): fuori dall'elenco del Bilancio e dalla tendina del Timesheet.
   * Le ore già imputate restano e continuano a contare nei costi.
   */
  isOff: boolean
}

/** Una fase da importare in commessa, nella sezione indicata. */
export interface BulkPhaseItem {
  templateId: number
  sectionId: number | null
}

export interface BulkPhaseRequest {
  projectId: number
  /** Compatibilità con il vecchio contratto: usare `items`. */
  templateIds: number[]
  items: BulkPhaseItem[]
}

export interface LocalPhaseRequest {
  projectId: number
  costSectionTemplateId: number | null
  name: string
  departmentId: number | null
}
