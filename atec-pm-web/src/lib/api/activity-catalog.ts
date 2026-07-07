import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ActivityCatalogItem,
  ActivityCatalogSaveRequest,
  ApiResponse,
} from "@/lib/api/types"

/** Tutte le voci del catalogo attività (attive + disattivate), ordinate per sort_order. */
export async function fetchActivityCatalog(): Promise<ActivityCatalogItem[]> {
  const response = await apiGet<ApiResponse<ActivityCatalogItem[]>>(
    "/api/activity-catalog"
  )
  return unwrapApi(response)
}

/** Solo le voci attive: alimenta il precarico "Attività da precaricare" alla creazione commessa. */
export async function fetchActiveActivityCatalog(): Promise<
  ActivityCatalogItem[]
> {
  const response = await apiGet<ApiResponse<ActivityCatalogItem[]>>(
    "/api/activity-catalog/active"
  )
  return unwrapApi(response)
}

export async function createActivityCatalog(
  request: ActivityCatalogSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/activity-catalog",
    request
  )
  return unwrapApi(response)
}

export async function updateActivityCatalog(
  id: number,
  request: ActivityCatalogSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/activity-catalog/${id}`,
    { ...request, id }
  )
  return unwrapApi(response)
}

export async function deleteActivityCatalog(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/activity-catalog/${id}`
  )
  return unwrapApi(response)
}

/** Riordino: la posizione degli id nell'array diventa il nuovo sort_order 1..N. */
export async function reorderActivityCatalog(ids: number[]): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/activity-catalog/reorder",
    { ids }
  )
  return unwrapApi(response)
}

/** Ripristino distruttivo all'elenco standard (le commesse già create non vengono toccate). */
export async function resetActivityCatalog(): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/activity-catalog/reset",
    {}
  )
  return unwrapApi(response)
}
