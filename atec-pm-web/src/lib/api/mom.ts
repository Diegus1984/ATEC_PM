import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  LookupItem,
  MoMActionItemSaveRequest,
  MoMDetail,
  MoMListItem,
  MoMNote,
  MoMNoteSaveRequest,
  MoMProjectLookup,
  MoMSaveRequest,
} from "@/lib/api/types"

export async function fetchMoMList(projectId?: number): Promise<MoMListItem[]> {
  const query = projectId !== undefined ? `?projectId=${projectId}` : ""
  const response = await apiGet<ApiResponse<MoMListItem[]>>(
    `/api/mom/list${query}`
  )
  return unwrapApi(response)
}

export async function fetchMoMDetail(id: number): Promise<MoMDetail> {
  const response = await apiGet<ApiResponse<MoMDetail>>(`/api/mom/${id}`)
  return unwrapApi(response)
}

/** Crea il verbale e ritorna il nuovo id. */
export async function createMoM(request: MoMSaveRequest): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/mom", request)
  return unwrapApi(response)
}

/**
 * Aggiorna l'intestazione e ritorna la revisione corrente: il server incrementa
 * la revisione quando una data riunione già impostata viene cambiata (regola v9).
 */
export async function updateMoM(
  id: number,
  request: MoMSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(`/api/mom/${id}`, request)
  return unwrapApi(response)
}

export async function deleteMoM(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/mom/${id}`)
  unwrapApi(response)
}

// ── Action items ────────────────────────────────────────

export async function addMoMItem(
  momId: number,
  request: MoMActionItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/mom/${momId}/items`,
    request
  )
  return unwrapApi(response)
}

export async function updateMoMItem(
  itemId: number,
  request: MoMActionItemSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/mom/items/${itemId}`,
    request
  )
  unwrapApi(response)
}

export async function deleteMoMItem(itemId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/mom/items/${itemId}`
  )
  unwrapApi(response)
}

/** Ordine manuale del foglio: tutti gli id riga nell'ordine voluto. */
export async function reorderMoMItems(
  momId: number,
  itemIds: number[]
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/mom/${momId}/items/reorder`,
    { itemIds }
  )
  unwrapApi(response)
}

// ── Note MoM (acquisizione rapida, staging personale) ──────

export async function fetchMoMNotes(): Promise<MoMNote[]> {
  const response = await apiGet<ApiResponse<MoMNote[]>>("/api/mom/notes")
  return unwrapApi(response)
}

export async function addMoMNote(request: MoMNoteSaveRequest): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/mom/notes", request)
  return unwrapApi(response)
}

export async function updateMoMNote(
  id: number,
  request: MoMNoteSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/mom/notes/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteMoMNote(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/mom/notes/${id}`)
  unwrapApi(response)
}

/** Assegna la nota alla MoM di destinazione; ritorna l'id della MoM. */
export async function assignMoMNote(id: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/mom/notes/${id}/assign`,
    {}
  )
  return unwrapApi(response)
}

// ── Lookup ──────────────────────────────────────────────

export async function fetchMoMProjects(): Promise<MoMProjectLookup[]> {
  const response = await apiGet<ApiResponse<MoMProjectLookup[]>>(
    "/api/mom/lookups/projects"
  )
  return unwrapApi(response)
}

export async function fetchMoMEmployees(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/mom/lookups/employees"
  )
  return unwrapApi(response)
}

/**
 * Wildcard reparto ([PM] Generico, …): usate solo per il pre-assegnamento da filtro
 * reparto. Aggiunte al pool responsabili così un id wildcard salvato resta risolvibile.
 */
export async function fetchMoMWildcards(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/mom/lookups/wildcards"
  )
  return unwrapApi(response)
}
