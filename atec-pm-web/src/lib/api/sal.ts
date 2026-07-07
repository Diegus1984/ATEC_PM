import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  SalBundle,
  SalHeaderSaveRequest,
  SalRowSaveRequest,
  SalCondition,
  SalConditionSaveRequest,
  SalProspettoRow,
  SalSummary,
} from "@/lib/api/types"

/** Recupera il bundle SAL (header + righe) per una commessa. */
export async function fetchSal(projectId: number): Promise<SalBundle> {
  const response = await apiGet<ApiResponse<SalBundle>>(
    `/api/sal?projectId=${projectId}`
  )
  return unwrapApi(response)
}

/** Salva/Aggiorna l'header SAL (cliente, valore). */
export async function saveSalHeader(
  projectId: number,
  request: SalHeaderSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/header?projectId=${projectId}`,
    request
  )
  return unwrapApi(response)
}

/** Crea una nuova riga/step SAL. */
export async function createSalRow(
  projectId: number,
  request: SalRowSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/sal/rows?projectId=${projectId}`,
    request
  )
  return unwrapApi(response)
}

/** Aggiorna una riga/step SAL esistente. */
export async function updateSalRow(
  id: number,
  request: SalRowSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/rows/${id}`,
    request
  )
  return unwrapApi(response)
}

/** Elimina una riga/step SAL. */
export async function deleteSalRow(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/rows/${id}`
  )
  return unwrapApi(response)
}

/** Riordina le righe SAL per una commessa. */
export async function reorderSalRows(
  projectId: number,
  ids: number[]
): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/sal/rows/reorder?projectId=${projectId}`,
    { ids }
  )
  return unwrapApi(response)
}

/** Precarica il template a 6 step standard. */
export async function seedSalTemplate(projectId: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/sal/project/${projectId}/seed-template`
  )
  return unwrapApi(response)
}

/** Recupera le condizioni di pagamento. */
export async function fetchSalConditions(activeOnly = false): Promise<SalCondition[]> {
  const url = activeOnly ? "/api/sal/conditions/active" : "/api/sal/conditions"
  const response = await apiGet<ApiResponse<SalCondition[]>>(url)
  return unwrapApi(response)
}

/** Crea una nuova condizione di pagamento. */
export async function createSalCondition(
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/sal/conditions",
    request
  )
  return unwrapApi(response)
}

/** Rinomina una condizione di pagamento. */
export async function updateSalCondition(
  id: number,
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/conditions/${id}`,
    request
  )
  return unwrapApi(response)
}

/** Attiva/Disattiva una condizione di pagamento. */
export async function toggleActiveSalCondition(
  id: number,
  active: boolean
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/conditions/${id}/toggle-active?active=${active}`
  )
  return unwrapApi(response)
}

/** Elimina una condizione di pagamento. */
export async function deleteSalCondition(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/conditions/${id}`
  )
  return unwrapApi(response)
}

/** Riordina le condizioni di pagamento. */
export async function reorderSalConditions(ids: number[]): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/conditions/reorder",
    { ids }
  )
  return unwrapApi(response)
}

/** Ripristina le condizioni di pagamento standard. */
export async function resetSalConditions(): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/conditions/reset"
  )
  return unwrapApi(response)
}

/** Recupera il prospetto globale delle ipotesi di fatturazione aperte. */
export async function fetchSalProspetto(): Promise<SalProspettoRow[]> {
  const response = await apiGet<ApiResponse<SalProspettoRow[]>>(
    "/api/sal/prospetto"
  )
  return unwrapApi(response)
}

export async function fetchSalSummary(): Promise<SalSummary[]> {
  const response = await apiGet<ApiResponse<SalSummary[]>>("/api/sal/summary")
  return unwrapApi(response)
}
