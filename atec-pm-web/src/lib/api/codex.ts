import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  AddCodexReferenceRequest,
  ApiResponse,
  CodexBulkReserveItem,
  CodexBulkReserveResult,
  CodexGeneratedCode,
  CodexListItem,
  CodexPrefix,
  CodexRecodeStats,
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
  /** Stato ricodifica: "missing" = senza codice nuovo, "done" = con codice nuovo. */
  newCodeState?: "missing" | "done"
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
  if (params.newCodeState) {
    query.set("newCodeState", params.newCodeState)
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

// ── NUOVA CODIFICA (ricodifica manuale) ─────────────────

/**
 * Assegna (o rimuove, con newCode vuoto) il codice nuovo di una riga Codex.
 * `reservationId` = prenotazione ottenuta da reserveCodexNewCode: il server la
 * libera al salvataggio.
 */
export async function updateCodexNewCode(
  id: number,
  newCode: string,
  reservationId?: number | null
): Promise<string> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/codex/${id}/new-code`,
    { newCode, reservationId: reservationId ?? null }
  )
  return unwrapApi(response)
}

/**
 * PRENOTA il prossimo codice della famiglia (regola Codex: famiglia + data odierna
 * ggMMaa + progressivo del giorno), come il generatore: più operatori in parallelo
 * non ricevono mai lo stesso codice. Va liberata col salvataggio o con
 * releaseCodexReservation se l'operatore annulla.
 */
export async function reserveCodexNewCode(
  family: string
): Promise<CodexReservationResult> {
  const response = await apiPost<ApiResponse<CodexReservationResult>>(
    "/api/codex/new-code/reserve",
    { family }
  )
  return unwrapApi(response)
}

/**
 * Assegnazione MASSIVA, fase 1: prenota N codici della famiglia per le righe
 * selezionate senza codice nuovo e ritorna l'anteprima vecchio→nuovo+descrizione
 * da confermare (commit) o annullare (release).
 */
export async function bulkReserveCodexNewCodes(
  ids: number[],
  family: string
): Promise<CodexBulkReserveResult> {
  const response = await apiPost<ApiResponse<CodexBulkReserveResult>>(
    "/api/codex/new-code/bulk-reserve",
    { ids, family }
  )
  return unwrapApi(response)
}

/** Fase 2: il pulsante «Assegna» del form scrive i codici prenotati sulle righe. */
export async function bulkCommitCodexNewCodes(
  items: CodexBulkReserveItem[]
): Promise<{ assigned: number; skipped: number }> {
  const response = await apiPost<
    ApiResponse<{ assigned: number; skipped: number }>
  >("/api/codex/new-code/bulk-commit", { items })
  return unwrapApi(response)
}

/** Annulla del form: libera in blocco le prenotazioni dell'anteprima. */
export async function bulkReleaseCodexReservations(
  reservationIds: number[]
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/codex/new-code/bulk-release",
    { reservationIds }
  )
  unwrapApi(response)
}

/** Reset massivo: le righe selezionate tornano "non ricodificate". */
export async function bulkRemoveCodexNewCodes(ids: number[]): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/codex/new-code/bulk-remove",
    { ids }
  )
  return unwrapApi(response)
}

/** Avanzamento ricodifica della famiglia vecchia indicata (default 201xxx). */
export async function fetchCodexRecodeStats(
  prefix = "201"
): Promise<CodexRecodeStats> {
  const response = await apiGet<ApiResponse<CodexRecodeStats>>(
    `/api/codex/recode-stats?prefix=${encodeURIComponent(prefix)}`
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
