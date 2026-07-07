import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  AddCodexReferenceRequest,
  ApiResponse,
  CodexGeneratedCode,
  CodexListItem,
  CodexPrefix,
  CodexReservationResult,
  CodexSyncStatus,
  PagedResult,
} from "@/lib/api/types"

export interface FetchCodexParams {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDir?: "asc" | "desc"
  /** Filtra per primo carattere del codice (OR di più prefissi). Es. ["1","2","3","4"]. */
  codicePrefixes?: string[]
  /** Filtri per colonna (chiave = parametro server, es. codice/descr/fornitore/…). */
  filters?: Record<string, string>
}

/** Lista articoli Codex (paginata server-side, ricerca multi-colonna, ordinamento). */
export async function fetchCodex(
  params: FetchCodexParams = {}
): Promise<PagedResult<CodexListItem>> {
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
  if (params.codicePrefixes && params.codicePrefixes.length > 0) {
    query.set("codicePrefixes", params.codicePrefixes.join(","))
  }
  if (params.filters) {
    for (const [key, value] of Object.entries(params.filters)) {
      const trimmed = value.trim()
      if (trimmed) {
        query.set(key, trimmed)
      }
    }
  }

  const response = await apiGet<ApiResponse<PagedResult<CodexListItem>>>(
    `/api/codex?${query.toString()}`
  )
  return unwrapApi(response)
}

/**
 * Carica TUTTI gli articoli Codex che soddisfano i parametri, sfogliando le pagine
 * (200/pagina). Pensato per insiemi già ristretti server-side (es.
 * `codicePrefixes: ["501"]` → solo i compositi 501): senza il filtro per prefisso
 * l'elenco completo (18k+ righe) sfora il tetto di pagine e i codici alti
 * (5xx/6xx/7xx), ordinati per ultimi alfabeticamente, resterebbero fuori.
 */
export async function fetchAllCodex(
  params: FetchCodexParams = {}
): Promise<CodexListItem[]> {
  const all: CodexListItem[] = []
  for (let page = 1; page <= 60; page++) {
    const result = await fetchCodex({ ...params, page, pageSize: 200 })
    all.push(...result.items)
    if (!result.hasMore) {
      break
    }
  }
  return all
}

// ── SYNC ────────────────────────────────────────────────

export async function fetchCodexSyncStatus(): Promise<CodexSyncStatus> {
  const response = await apiGet<ApiResponse<CodexSyncStatus>>(
    "/api/codex/sync-status"
  )
  return unwrapApi(response)
}

/** Avvia la sincronizzazione col Codex remoto (operazione asincrona lato server). */
export async function startCodexSync(): Promise<string> {
  const response = await apiPost<ApiResponse<string>>("/api/codex/sync")
  return unwrapApi(response)
}

// ── GENERAZIONE CON PRENOTAZIONE ────────────────────────

export async function fetchCodexPrefixes(): Promise<CodexPrefix[]> {
  const response = await apiGet<ApiResponse<CodexPrefix[]>>("/api/codex/prefixes")
  return unwrapApi(response)
}

/** Prenota il prossimo codice per il prefisso (da rilasciare o confermare). */
export async function reserveCodexCode(
  prefisso: string
): Promise<CodexReservationResult> {
  const response = await apiPost<ApiResponse<CodexReservationResult>>(
    "/api/codex/reserve",
    { prefisso }
  )
  return unwrapApi(response)
}

export async function releaseCodexReservation(
  reservationId: number
): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/codex/release/${reservationId}`
  )
  unwrapApi(response)
}

/** Conferma la prenotazione: crea l'articolo con la descrizione e ritorna il codice. */
export async function confirmCodexReservation(
  reservationId: number,
  descrizione: string
): Promise<CodexGeneratedCode> {
  const response = await apiPost<ApiResponse<CodexGeneratedCode>>(
    "/api/codex/confirm",
    { reservationId, descrizione }
  )
  return unwrapApi(response)
}

// ── MODIFICA / ELIMINA ──────────────────────────────────

export async function updateCodexDescription(
  id: number,
  descrizione: string
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(`/api/codex/${id}`, {
    descrizione,
  })
  unwrapApi(response)
}

export async function deleteCodex(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/codex/${id}`)
  unwrapApi(response)
}

// ── RIFERIMENTI 101 → 201/401 ───────────────────────────

export async function addCodexReference(
  request: AddCodexReferenceRequest
): Promise<void> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/codex/references",
    request
  )
  unwrapApi(response)
}
