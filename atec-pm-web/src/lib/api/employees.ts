import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  EmployeeSaveRequest,
  LookupItem,
} from "@/lib/api/types"

/**
 * Tendina «solo utenti reali» (niente ADMIN, cessati o wildcard reparto), aperta a
 * chiunque sia loggato. La chat DEVE usare questa e non il lookup di MoM, che sta
 * dietro `nav.mom`/`project.mom`: chi ha la chat ma non i verbali vedrebbe l'elenco
 * partecipanti vuoto (#121).
 */
export async function fetchRealEmployees(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/employees/real"
  )
  return unwrapApi(response)
}

/**
 * Dipendenti con obbligo di timbratura (esclusi forfettari / esenti da cartellino presenze).
 */
export async function fetchPunchingEmployees(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/employees/real?mustPunch=true"
  )
  return unwrapApi(response)
}

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
