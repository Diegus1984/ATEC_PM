import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  OfferPlanSupplier,
  PurchaseRfqDetail,
  PurchaseRfqEmailCandidate,
  PurchaseRfqListItem,
  PurchaseRfqOfferDto,
} from "@/lib/api/types"

export async function fetchPurchaseRfqs(
  status?: string
): Promise<PurchaseRfqListItem[]> {
  const query = status ? `?status=${encodeURIComponent(status)}` : ""
  const response = await apiGet<ApiResponse<PurchaseRfqListItem[]>>(
    `/api/purchase-rfqs${query}`
  )
  return unwrapApi(response)
}

export async function fetchPurchaseRfq(id: number): Promise<PurchaseRfqDetail> {
  const response = await apiGet<ApiResponse<PurchaseRfqDetail>>(
    `/api/purchase-rfqs/${id}`
  )
  return unwrapApi(response)
}

/**
 * Crea le RDO per un gruppo ATEC. Il server le SPEZZA per commessa (regola:
 * ogni commessa ha le sue RDO) e ritorna gli id creati, in ordine di commessa.
 */
export async function createPurchaseRfq(body: {
  atecCode: string
  description?: string
  notes?: string
  bomItemIds: number[]
  supplierIds?: number[]
}): Promise<number[]> {
  const response = await apiPost<ApiResponse<number[]>>("/api/purchase-rfqs", body)
  return unwrapApi(response)
}

/**
 * Piano fornitori per le righe distinta selezionate: chi può quotarle
 * (fornitore della riga + equivalenti dal mapping ATEC) e con quali articoli.
 */
export async function fetchOfferPlan(
  bomItemIds: number[]
): Promise<OfferPlanSupplier[]> {
  const response = await apiPost<ApiResponse<OfferPlanSupplier[]>>(
    "/api/purchase-rfqs/offer-plan",
    { bomItemIds }
  )
  return unwrapApi(response)
}

/**
 * Crea le richieste offerta (RDO automatiche per commessa × codice) con le
 * offerte dei fornitori scelti. Ritorna le offerte pronte per la mailto.
 */
export async function requestOffers(
  selections: { bomItemId: number; supplierIds: number[] }[]
): Promise<PurchaseRfqEmailCandidate[]> {
  const response = await apiPost<ApiResponse<PurchaseRfqEmailCandidate[]>>(
    "/api/purchase-rfqs/request-offers",
    { selections }
  )
  return unwrapApi(response)
}

/** Offerte in attesa di richiesta email, da raggruppare per fornitore (mailto). */
export async function fetchPurchaseRfqEmailCandidates(): Promise<
  PurchaseRfqEmailCandidate[]
> {
  const response = await apiGet<ApiResponse<PurchaseRfqEmailCandidate[]>>(
    "/api/purchase-rfqs/email-candidates"
  )
  return unwrapApi(response)
}

/**
 * Registra l'invio via mailto: timestamp sulle offerte contattate, RDO DRAFT → SENT.
 * (La mail la compone e la invia il client di posta dell'utente.)
 */
export async function markPurchaseRfqEmailed(offerIds: number[]): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/purchase-rfqs/mark-emailed",
    { offerIds }
  )
  unwrapApi(response)
}

export async function sendPurchaseRfq(id: number): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/purchase-rfqs/${id}/send`,
    {}
  )
  unwrapApi(response)
}

export async function savePurchaseRfqOffer(
  rfqId: number,
  offerId: number,
  body: {
    supplierId: number
    catalogItemId?: number | null
    unitPrice?: number | null
    validUntil?: string | null
    notes?: string
  }
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/purchase-rfqs/${rfqId}/offers/${offerId}`,
    body
  )
  unwrapApi(response)
}

export async function selectPurchaseRfqWinner(
  rfqId: number,
  offerId: number,
  targetStatus = "RO"
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/purchase-rfqs/${rfqId}/select-winner`,
    { offerId, targetStatus }
  )
  unwrapApi(response)
}

/**
 * Genera l'ORDINE FORNITORE direttamente in Danea (archivio Atec_PM):
 * articolo del vincitore, qtà = somma fabbisogni, prezzo = offerta vincente.
 * Ritorna il dettaglio aggiornato (con daneaOrderNum valorizzato).
 */
export async function createPurchaseRfqDaneaOrder(
  rfqId: number,
  expectedDate: string | null
): Promise<PurchaseRfqDetail> {
  const response = await apiPost<ApiResponse<PurchaseRfqDetail>>(
    `/api/purchase-rfqs/${rfqId}/create-danea-order`,
    { expectedDate }
  )
  return unwrapApi(response)
}

/**
 * Ordine Danea multi-RDO: più RDO chiuse dello stesso fornitore vincitore e
 * della stessa commessa → UN ordine multi-riga (una riga per RDO).
 * Ritorna il numero d'ordine Danea.
 */
export async function createPurchaseRfqDaneaOrderMulti(
  rfqIds: number[],
  expectedDate: string | null
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/purchase-rfqs/create-danea-order-multi",
    { rfqIds, expectedDate }
  )
  return unwrapApi(response)
}

export async function cancelPurchaseRfq(id: number): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/purchase-rfqs/${id}/cancel`,
    {}
  )
  unwrapApi(response)
}

export type { PurchaseRfqOfferDto }
