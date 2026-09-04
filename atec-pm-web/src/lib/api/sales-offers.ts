import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  SalesOffer,
  SalesOfferBulkCloseRequest,
  SalesOfferCreated,
  SalesOfferFollowupSaveRequest,
  SalesOfferListResponse,
  SalesOfferNextNumber,
  SalesOfferSaveRequest,
  SalesOfferSeller,
  SalesOfferType,
} from "@/lib/api/types"

const BASE = "/api/sales-offers"

/** Filtri dell'elenco. Tutti facoltativi: quelli vuoti non vengono inviati. */
export interface SalesOfferListParams {
  year?: number | null
  status?: string
  sellerCode?: string
  typeCode?: string
  customerId?: number | null
  /** `-1` = «non valutata» (la chance 0 è un valore vero, non il vuoto). */
  chance?: number | null
  search?: string
  sortBy?: string
  sortDir?: "asc" | "desc"
  page?: number
  pageSize?: number
}

function query(params: SalesOfferListParams): string {
  const q = new URLSearchParams()
  if (params.year) q.set("year", String(params.year))
  if (params.status) q.set("status", params.status)
  if (params.sellerCode) q.set("sellerCode", params.sellerCode)
  if (params.typeCode) q.set("typeCode", params.typeCode)
  if (params.customerId) q.set("customerId", String(params.customerId))
  if (params.chance !== null && params.chance !== undefined)
    q.set("chance", String(params.chance))
  if (params.search) q.set("search", params.search)
  if (params.sortBy) q.set("sortBy", params.sortBy)
  if (params.sortDir) q.set("sortDir", params.sortDir)
  if (params.page) q.set("page", String(params.page))
  if (params.pageSize) q.set("pageSize", String(params.pageSize))
  const s = q.toString()
  return s ? `?${s}` : ""
}

export async function fetchSalesOffers(
  params: SalesOfferListParams
): Promise<SalesOfferListResponse> {
  const response = await apiGet<ApiResponse<SalesOfferListResponse>>(
    `${BASE}${query(params)}`
  )
  return unwrapApi(response)
}

/** Scheda completa: la riga più gli appunti di contatto e il registro modifiche. */
export async function fetchSalesOffer(id: number): Promise<SalesOffer> {
  const response = await apiGet<ApiResponse<SalesOffer>>(`${BASE}/${id}`)
  return unwrapApi(response)
}

/** Il prossimo numero libero dell'anno, già composto se si passano tipo e sigla. */
export async function fetchNextOfferNumber(
  year: number,
  typeCode?: string,
  sellerCode?: string
): Promise<SalesOfferNextNumber> {
  const q = new URLSearchParams({ year: String(year) })
  if (typeCode) q.set("typeCode", typeCode)
  if (sellerCode) q.set("sellerCode", sellerCode)
  const response = await apiGet<ApiResponse<SalesOfferNextNumber>>(
    `${BASE}/next-number?${q.toString()}`
  )
  return unwrapApi(response)
}

/**
 * Crea l'offerta. Se il numero proposto è stato preso nel frattempo il server ne assegna
 * un altro e risponde `renumbered: true`: il chiamante lo dice all'utente.
 */
export async function createSalesOffer(
  request: SalesOfferSaveRequest
): Promise<SalesOfferCreated> {
  const response = await apiPost<ApiResponse<SalesOfferCreated>>(BASE, request)
  return unwrapApi(response)
}

export async function updateSalesOffer(
  id: number,
  request: SalesOfferSaveRequest
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(`${BASE}/${id}`, request)
  return unwrapApi(response)
}

export async function deleteSalesOffer(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(`${BASE}/${id}`)
  return unwrapApi(response)
}

/** Aggiunge un appunto di contatto e, se indicata, sposta il prossimo contatto. */
export async function addSalesOfferFollowup(
  id: number,
  request: SalesOfferFollowupSaveRequest
): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    `${BASE}/${id}/followup`,
    request
  )
  return unwrapApi(response)
}

/** Chiude in blocco le offerte ancora aperte degli anni passati. Ritorna quante. */
export async function bulkCloseSalesOffers(
  request: SalesOfferBulkCloseRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/bulk-close`,
    request
  )
  return unwrapApi(response)
}

export async function fetchSalesOfferTypes(
  includeInactive = false
): Promise<SalesOfferType[]> {
  const response = await apiGet<ApiResponse<SalesOfferType[]>>(
    `${BASE}/types?includeInactive=${includeInactive}`
  )
  return unwrapApi(response)
}

export async function fetchSalesOfferSellers(
  includeInactive = false
): Promise<SalesOfferSeller[]> {
  const response = await apiGet<ApiResponse<SalesOfferSeller[]>>(
    `${BASE}/sellers?includeInactive=${includeInactive}`
  )
  return unwrapApi(response)
}
