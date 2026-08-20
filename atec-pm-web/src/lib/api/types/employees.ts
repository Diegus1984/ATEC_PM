/** Utenti, dipendenti e reparti — allineati a ATEC.PM.Shared/DTOs. */

export interface UserListItem {
  id: number
  fullName: string
  email: string
  userRole: string
  status: string
  hasCredentials: boolean
  username: string
  departmentCodes: string[]
  competenceCodes: string[]
}

export interface EmployeeDepartmentItem {
  id: number
  departmentId: number
  departmentCode: string
  departmentName: string
  isResponsible: boolean
  isPrimary: boolean
}

export interface EmployeeCompetenceItem {
  id: number
  departmentId: number
  departmentCode: string
  departmentName: string
  notes: string
}

export interface UserDetailDto {
  id: number
  fullName: string
  userRole: string
  username: string
  departments: EmployeeDepartmentItem[]
  competences: EmployeeCompetenceItem[]
}

export interface EmployeeSaveRequest {
  id: number
  firstName: string
  lastName: string
  email: string
  empType: string
  supplierId: number | null
  status: string
}

export interface DeptSummary {
  departmentCode: string
  departmentName: string
  costingHours: number
  assignedHours: number
  hoursWorked: number
  budgetHours: number
  totalPhases: number
  completedPhases: number
}

export interface DepartmentSaveRequest {
  id: number
  code: string
  name: string
  hourlyCost: number
  defaultMarkup: number
  sortOrder: number
  isActive: boolean
}

export interface DepartmentDto {
  id: number
  code: string
  name: string
  hourlyCost: number
  defaultMarkup: number
  sortOrder: number
  isActive: boolean
}

/**
 * Il reparto come lo mostra una **lista di spunta** (`GET /api/departments/lookup`): sigla,
 * nome, attivo, ordine. Niente costo orario, niente ricarico — ed è per questo che l'endpoint
 * può restare aperto a tutti gli autenticati.
 */
export interface DepartmentLookupDto {
  id: number
  code: string
  name: string
  sortOrder: number
  isActive: boolean
}
