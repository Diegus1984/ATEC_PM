import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DepartmentDto,
  DepartmentLookupDto,
  DepartmentSaveRequest,
} from "@/lib/api/types"

/**
 * I reparti per una **lista di spunta**: sigla, nome, attivo, ordine. Aperta a tutti.
 * `fetchDepartments` invece porta costo orario e ricarico e richiede `nav.config_sezioni`.
 */
export async function fetchDepartmentsLookup(): Promise<DepartmentLookupDto[]> {
  const response = await apiGet<ApiResponse<DepartmentLookupDto[]>>(
    "/api/departments/lookup"
  )
  return unwrapApi(response)
}

/** I reparti con costo orario e ricarico (Configurazione sezioni). Richiede `nav.config_sezioni`. */
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
