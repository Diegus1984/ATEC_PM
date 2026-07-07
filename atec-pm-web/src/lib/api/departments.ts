import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DepartmentDto,
  DepartmentSaveRequest,
} from "@/lib/api/types"

export async function fetchDepartments(): Promise<DepartmentDto[]> {
  const response = await apiGet<ApiResponse<DepartmentDto[]>>("/api/departments")
  return unwrapApi(response)
}

export async function createDepartment(
  request: DepartmentSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/departments", request)
  return unwrapApi(response)
}

export async function updateDepartment(
  id: number,
  request: DepartmentSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`/api/departments/${id}`, {
    ...request,
    id,
  })
  unwrapApi(response)
}

export async function deleteDepartment(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`/api/departments/${id}`)
  unwrapApi(response)
}
