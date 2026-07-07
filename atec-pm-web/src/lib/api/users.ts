import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  EmployeeCompetenceItem,
  EmployeeDepartmentItem,
  UserDetailDto,
  UserListItem,
} from "@/lib/api/types"

export async function fetchUsers(
  includeTerminated = false
): Promise<UserListItem[]> {
  const query = includeTerminated ? "?includeTerminated=true" : ""
  const response = await apiGet<ApiResponse<UserListItem[]>>(
    `/api/users${query}`
  )
  return unwrapApi(response)
}

export async function fetchUserDetail(id: number): Promise<UserDetailDto> {
  const response = await apiGet<ApiResponse<UserDetailDto>>(`/api/users/${id}`)
  return unwrapApi(response)
}

export async function setUserRole(
  employeeId: number,
  userRole: string
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>("/api/users/role", {
    employeeId,
    userRole,
  })
  unwrapApi(response)
}

export async function setUserStatus(
  employeeId: number,
  isActive: boolean
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>("/api/users/status", {
    employeeId,
    isActive,
  })
  unwrapApi(response)
}

/** Reimposta username+password alla forma iniziale (iniziale.cognome). Ritorna il login. */
export async function resetUserPassword(employeeId: number): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    "/api/users/reset-password",
    { employeeId }
  )
  return unwrapApi(response)
}

export async function saveUserDepartments(
  employeeId: number,
  departments: EmployeeDepartmentItem[]
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>("/api/users/departments", {
    employeeId,
    departments,
  })
  unwrapApi(response)
}

export async function saveUserCompetences(
  employeeId: number,
  competences: EmployeeCompetenceItem[]
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>("/api/users/competences", {
    employeeId,
    competences,
  })
  unwrapApi(response)
}

/** Imposta username + password per un dipendente (solo ADMIN). */
export async function setUserCredentials(
  employeeId: number,
  username: string,
  password: string
): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    "/api/auth/set-credentials",
    { employeeId, username, password }
  )
  unwrapApi(response)
}
