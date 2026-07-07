import {
  apiDelete,
  apiGet,
  apiGetBlob,
  apiPatch,
  apiPost,
  apiPut,
  buildApiUrl,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  FieldUpdateRequest,
  PagedResult,
  QuoteConvertDto,
  QuoteDto,
  QuoteItemSaveDto,
  QuoteSaveDto,
  QuoteStatsDto,
  QuoteStatusChangeDto,
} from "@/lib/api/types"

export interface FetchQuotesParams {
  page?: number
  /** 0 (default server) = nessuna paginazione: ritorna tutti i preventivi. */
  pageSize?: number
  search?: string
  status?: string
  customerId?: number
  quoteType?: string
  // Filtri per colonna (server)
  quoteNumber?: string
  customerName?: string
  title?: string
}

/**
 * Lista preventivi (paginata server-side). Esclude le revisioni "superseded"
 * a meno che non si filtri esplicitamente per status. Fedele a QuotesController.GetAll.
 */
export async function fetchQuotes(
  params: FetchQuotesParams = {}
): Promise<PagedResult<QuoteDto>> {
  const query = new URLSearchParams({ page: String(params.page ?? 1) })
  if (params.pageSize !== undefined) {
    query.set("pageSize", String(params.pageSize))
  }
  const setIf = (key: string, value?: string) => {
    const trimmed = value?.trim()
    if (trimmed) {
      query.set(key, trimmed)
    }
  }
  setIf("search", params.search)
  setIf("status", params.status)
  setIf("quoteType", params.quoteType)
  setIf("quoteNumber", params.quoteNumber)
  setIf("customerName", params.customerName)
  setIf("title", params.title)
  if (params.customerId) {
    query.set("customerId", String(params.customerId))
  }

  const response = await apiGet<ApiResponse<PagedResult<QuoteDto>>>(
    `/api/quotes?${query.toString()}`
  )
  return unwrapApi(response)
}

/** Carica tutte le versioni di una o più famiglie di revisione (master + superseded + revisioni). */
export async function fetchQuoteChains(masterIds: number[]): Promise<QuoteDto[]> {
  const query = masterIds.length ? `?masterIds=${masterIds.join(",")}` : ""
  const response = await apiGet<ApiResponse<QuoteDto[]>>(
    `/api/quotes/chains${query}`
  )
  return unwrapApi(response)
}

/** Singolo preventivo con le sue righe (items). */
export async function fetchQuote(id: number): Promise<QuoteDto> {
  const response = await apiGet<ApiResponse<QuoteDto>>(`/api/quotes/${id}`)
  return unwrapApi(response)
}

/** Crea preventivo (numero auto, status=draft; se IMPIANTO inizializza il costing). Ritorna l'id. */
export async function createQuote(request: QuoteSaveDto): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/quotes", request)
  return unwrapApi(response)
}

/** Aggiorna l'intestazione del preventivo e ricalcola i totali. */
export async function updateQuote(
  id: number,
  request: QuoteSaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`/api/quotes/${id}`, request)
  unwrapApi(response)
}

/** Elimina preventivo (consentito solo in stato draft lato server). */
export async function deleteQuote(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`/api/quotes/${id}`)
  unwrapApi(response)
}

// ── Righe (items) ──────────────────────────────────────────

/** Aggiunge una riga al preventivo. Ritorna l'id della riga. */
export async function addQuoteItem(
  quoteId: number,
  request: QuoteItemSaveDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${quoteId}/items`,
    request
  )
  return unwrapApi(response)
}

/** Aggiunge un prodotto catalogo con TUTTE le sue varianti (parent qty=0, figli qty=1). Ritorna l'id del parent. */
export async function addQuoteProduct(
  quoteId: number,
  productId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${quoteId}/items/product/${productId}`
  )
  return unwrapApi(response)
}

/** Aggiunge una variante locale (non da catalogo) a un item parent. Ritorna l'id della variante. */
export async function addQuoteLocalVariant(
  quoteId: number,
  parentItemId: number,
  request: QuoteItemSaveDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${quoteId}/items/${parentItemId}/variant`,
    request
  )
  return unwrapApi(response)
}

/** Aggiorna una riga e ricalcola i totali. */
export async function updateQuoteItem(
  quoteId: number,
  itemId: number,
  request: QuoteItemSaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/quotes/${quoteId}/items/${itemId}`,
    request
  )
  unwrapApi(response)
}

export async function deleteQuoteItem(
  quoteId: number,
  itemId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/quotes/${quoteId}/items/${itemId}`
  )
  unwrapApi(response)
}

/** Clona una riga (parent + tutte le varianti figlie). Ritorna l'id del nuovo parent. */
export async function cloneQuoteItem(
  quoteId: number,
  itemId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${quoteId}/items/${itemId}/clone`
  )
  return unwrapApi(response)
}

/** Riordina le righe del preventivo (sort_order) secondo l'ordine degli id passati. */
export async function reorderQuoteItems(
  quoteId: number,
  itemIds: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/quotes/${quoteId}/items/reorder`,
    itemIds
  )
  unwrapApi(response)
}

/** Aggiorna un singolo campo della riga (PATCH field) e ricalcola i totali. */
export async function updateQuoteItemField(
  quoteId: number,
  itemId: number,
  field: string,
  value: string | null
): Promise<void> {
  const body: FieldUpdateRequest = { field, value }
  const response = await apiPatch<ApiResponse<string>>(
    `/api/quotes/${quoteId}/items/${itemId}/field`,
    body
  )
  unwrapApi(response)
}

/** Aggiorna un singolo campo di intestazione del preventivo (PATCH field). */
export async function updateQuoteField(
  id: number,
  field: string,
  value: string | null
): Promise<void> {
  const body: FieldUpdateRequest = { field, value }
  const response = await apiPatch<ApiResponse<string>>(
    `/api/quotes/${id}/field`,
    body
  )
  unwrapApi(response)
}

/** Ricarica i prodotti con flag auto_include dal catalogo (svuota e reinserisce). */
export async function reloadAutoIncludes(quoteId: number): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/quotes/${quoteId}/reload-auto-includes`
  )
  unwrapApi(response)
}

// ── Stato / revisioni / conversione ────────────────────────

/** Cambia stato del preventivo (registra log + setta sent_at/accepted_at/converted_at). */
export async function changeQuoteStatus(
  id: number,
  request: QuoteStatusChangeDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/quotes/${id}/status`,
    request
  )
  unwrapApi(response)
}

/** Crea una revisione (il master diventa superseded, nuova revisione draft). Ritorna l'id della revisione. */
export async function createQuoteRevision(id: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${id}/revision`
  )
  return unwrapApi(response)
}

/** Duplica il preventivo come copia indipendente (parent_quote_id=NULL). Ritorna l'id della copia. */
export async function duplicateQuote(id: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${id}/duplicate`
  )
  return unwrapApi(response)
}

/** Converte un preventivo IMPIANTO in commessa (crea cartella, copia costing/materiali/fasi). Ritorna l'id della commessa. */
export async function convertQuote(
  id: number,
  request: QuoteConvertDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/quotes/${id}/convert`,
    request
  )
  return unwrapApi(response)
}

// ── PDF / statistiche ──────────────────────────────────────

/**
 * URL (assoluto) del PDF del preventivo. L'endpoint richiede Bearer: per il
 * download autenticato vedi la pagina dettaglio (fetch + blob). Esposto qui per comodità.
 */
export function quotePdfUrl(id: number): string {
  return buildApiUrl(`/api/quotes/${id}/pdf`)
}

/** Scarica il PDF del preventivo come Blob (GET autenticato) per anteprima o salvataggio. */
export async function fetchQuotePdf(id: number): Promise<Blob> {
  return apiGetBlob(`/api/quotes/${id}/pdf`)
}

export async function fetchQuoteStats(): Promise<QuoteStatsDto> {
  const response = await apiGet<ApiResponse<QuoteStatsDto>>("/api/quotes/stats")
  return unwrapApi(response)
}
