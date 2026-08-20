/** Timesheet (ore lavorate) — allineati a ATEC.PM.Shared/DTOs. */

export interface TimesheetEntryDto {
  id: number
  employeeId: number
  projectPhaseId: number
  workDate: string
  hours: number
  entryType: string
  notes: string
  phaseDisplay: string
}

export interface TimesheetSaveRequest {
  id: number
  employeeId: number
  projectPhaseId: number
  workDate: string
  hours: number
  entryType: string
  notes: string
}

export interface TimesheetPhaseOption {
  phaseId: number
  display: string
  /** Sezione di costo della fase: vuota se la fase non è collegata a nessuna. */
  costSectionName: string
  /** IN_SEDE / DA_CLIENTE. */
  costSectionType: string
  costSectionGroup: string
  /** Colore del gruppo dall'anagrafica sezioni (#105), es. `#2563EB`. */
  costSectionGroupColor: string
  costSectionSort: number
}

export interface TimesheetProjectOption {
  projectId: number
  display: string
}
