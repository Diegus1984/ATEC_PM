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
}

export interface TimesheetProjectOption {
  projectId: number
  display: string
}
