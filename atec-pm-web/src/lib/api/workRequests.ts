import { apiDelete, apiGet, apiPatch, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  WorkRequest,
  WorkRequestSaveRequest,
} from "@/lib/api/types"

export async function fetchWorkRequests(projectId: number): Promise<WorkRequest[]> {
  const response = await apiGet<ApiResponse<WorkRequest[]>>(
    `/api/work-requests/project/${projectId}`
  )
  return unwrapApi(response)
}

export async function fetchPriorityWorkRequests(
  scopes: string,
  levels: string
): Promise<WorkRequest[]> {
  const response = await apiGet<ApiResponse<WorkRequest[]>>(
    `/api/work-requests/priorities?scopes=${encodeURIComponent(scopes)}&levels=${encodeURIComponent(levels)}`
  )
  return unwrapApi(response)
}

export async function createWorkRequest(
  request: WorkRequestSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/work-requests",
    request
  )
  return unwrapApi(response)
}

export async function updateWorkRequest(
  id: number,
  request: WorkRequestSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/work-requests/${id}`,
    request
  )
  unwrapApi(response)
}

export async function patchWorkRequestField(
  id: number,
  field: string,
  value: unknown
): Promise<void> {
  // Il contratto server (FieldUpdateRequest.Value) è string|null: booleani e numeri
  // vanno serializzati come stringa, altrimenti il model binding risponde 400.
  const response = await apiPatch<ApiResponse<boolean>>(`/api/work-requests/${id}/field`, {
    field,
    value: value === null || value === undefined ? null : String(value),
  })
  unwrapApi(response)
}

export async function deleteWorkRequest(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/work-requests/${id}`
  )
  unwrapApi(response)
}
