import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, EmployeeSaveRequest } from "@/lib/api/types"

export async function fetchEmployee(id: number): Promise<EmployeeSaveRequest> {
  const response = await apiGet<ApiResponse<EmployeeSaveRequest>>(
    `/api/employees/${id}`
  )
  return unwrapApi(response)
}

/** Crea il dipendente e ritorna il nuovo id. */
export async function createEmployee(
  request: EmployeeSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/employees", request)
  return unwrapApi(response)
}

export async function updateEmployee(
  id: number,
  request: EmployeeSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/employees/${id}`,
    request
  )
  unwrapApi(response)
}

/** Cessazione (soft delete: status → TERMINATED). */
export async function deleteEmployee(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/employees/${id}`)
  unwrapApi(response)
}
