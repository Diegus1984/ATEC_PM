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
  /** Filtri per colonna (chiave = parametro server: code/description/supplier/manufacturer/category/atecCode). */
  filters?: Record<string, string>
  /** Stato mapping codice ATEC: missing/done/orphans (Extra1 senza match Codex). */
  atecState?: "missing" | "done" | "orphans"
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
  if (params.atecState) {
    query.set("atecState", params.atecState)
  }

  const response = await apiGet<ApiResponse<PagedResult<CatalogItemListItem>>>(
    `/api/catalog?${query.toString()}`
  )
  return unwrapApi(response)
}

export interface CatalogFilterMeta {
  suppliers: string[]
  manufacturers: string[]
  categories: string[]
  subcategories: string[]
}

/** Valori distinti per i filtri a tendina delle colonne (fornitore/produttore/categoria/sottocategoria). */
export async function fetchCatalogFilterMeta(): Promise<CatalogFilterMeta> {
  const response = await apiGet<ApiResponse<CatalogFilterMeta>>(
    "/api/catalog/filter-meta"
  )
  return unwrapApi(response)
}

// ── MAPPING DANEA ↔ CODICE ATEC (Extra1) ────────────────

/** Articoli Danea associati al codice nuovo della riga Codex. */
export async function fetchCatalogByCodex(
  codexItemId: number
): Promise<CatalogItemListItem[]> {
  const response = await apiGet<ApiResponse<CatalogItemListItem[]>>(
    `/api/catalog-mapping/by-codex/${codexItemId}`
  )
  return unwrapApi(response)
}

/** Alternative fornitore per un codice ATEC (stringa). */
export async function fetchCatalogByAtec(
  atecCode: string
): Promise<CatalogItemListItem[]> {
  const code = encodeURIComponent(atecCode.replace(/\./g, "").trim())
  if (!code) return []
  const response = await apiGet<ApiResponse<CatalogItemListItem[]>>(
    `/api/catalog-mapping/by-atec/${code}`
  )
  return unwrapApi(response)
}

/** Extra1 valorizzati che non matchano nessun codice_nuovo Codex (refusi). */
export async function fetchCatalogMappingOrphans(params: {
  page?: number
  pageSize?: number
  search?: string
} = {}): Promise<PagedResult<CatalogItemListItem>> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 50
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })
  const search = params.search?.trim()
  if (search) query.set("search", search)
  const response = await apiGet<ApiResponse<PagedResult<CatalogItemListItem>>>(
    `/api/catalog-mapping/orphans?${query.toString()}`
  )
  return unwrapApi(response)
}

export interface CatalogMappingAssignResult {
  assigned: boolean
  /** L'articolo è già associato a un altro codice: serve conferma → retry con force. */
  requiresForce: boolean
  currentAtecCode: string
}

/**
 * Associa l'articolo Danea al codice nuovo della riga Codex (scrive Extra1 su
 * Danea, poi lo specchio locale). Con force=true sovrascrive un'associazione
 * esistente (riassegnazione confermata dall'operatore).
 */
export async function assignCatalogMapping(
  catalogItemId: number,
  codexItemId: number,
  force = false
): Promise<CatalogMappingAssignResult> {
  const response = await apiPost<ApiResponse<CatalogMappingAssignResult>>(
    "/api/catalog-mapping/assign",
    { catalogItemId, codexItemId, force }
  )
  return unwrapApi(response)
}

/**
 * Associa partendo da una riga della distinta commerciale (Inbox Acquisti):
 * l'articolo Danea lo risolve il server (link catalogo o match esatto sul
 * codice), che a successo aggiorna anche snapshot/link della riga.
 */
export async function assignCatalogMappingFromBom(
  bomItemId: number,
  codexItemId: number,
  force = false
): Promise<CatalogMappingAssignResult> {
  const response = await apiPost<ApiResponse<CatalogMappingAssignResult>>(
    "/api/catalog-mapping/assign-from-bom",
    { bomItemId, codexItemId, force }
  )
  return unwrapApi(response)
}

/** Sgancia l'articolo (svuota Extra1 su Danea + specchio locale). */
export async function unassignCatalogMapping(
  catalogItemId: number
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/catalog-mapping/unassign",
    { catalogItemId }
  )
  unwrapApi(response)
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
