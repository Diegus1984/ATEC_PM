import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  CatalogItemListItem,
  CatalogItemSaveRequest,
  PagedResult,
} from "@/lib/api/types"

export interface FetchCatalogParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDir?: "asc" | "desc"
  /** Filtri per colonna (chiave = parametro server: code/description/supplier/manufacturer/category). */
  filters?: Record<string, string>
}

/**
 * Articoli di catalogo paginati server-side (ricerca multi-colonna con regole
 * jolly abc/abc*\/*abc/*abc*, ordinamento per colonna). Usato dalla pagina
 * Catalogo: evita di scaricare tutto il catalogo client-side.
 */
export async function fetchCatalogItems(
  params: FetchCatalogParams = {}
): Promise<PagedResult<CatalogItemListItem>> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 50
  const search = params.search?.trim()

  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })
  if (search) {
    query.set("search", search)
  }
  if (params.sortBy) {
    query.set("sortBy", params.sortBy)
    query.set("sortDir", params.sortDir ?? "asc")
  }
  if (params.filters) {
    for (const [key, value] of Object.entries(params.filters)) {
      const trimmed = value.trim()
      if (trimmed) {
        query.set(key, trimmed)
      }
    }
  }

  const response = await apiGet<ApiResponse<PagedResult<CatalogItemListItem>>>(
    `/api/catalog?${query.toString()}`
  )
  return unwrapApi(response)
}

export async function fetchCatalogItem(
  id: number
): Promise<CatalogItemSaveRequest> {
  const response = await apiGet<ApiResponse<CatalogItemSaveRequest>>(
    `/api/catalog/${id}`
  )
  return unwrapApi(response)
}

export async function createCatalogItem(
  request: CatalogItemSaveRequest
): Promise<void> {
  const response = await apiPost<ApiResponse<string>>("/api/catalog", request)
  unwrapApi(response)
}

export async function updateCatalogItem(
  id: number,
  request: CatalogItemSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/catalog/${id}`,
    request
  )
  unwrapApi(response)
}

/** Soft delete (is_active=0). Fallisce se l'articolo è usato in una composizione. */
export async function deleteCatalogItem(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`/api/catalog/${id}`)
  unwrapApi(response)
}
