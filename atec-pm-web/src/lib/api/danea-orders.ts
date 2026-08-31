import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, DaneaOrderView } from "@/lib/api/types"

/** Ordine fornitore Danea (Atec_PM) per il popup di rendering. */
export async function fetchDaneaOrder(idDoc: number): Promise<DaneaOrderView> {
  const response = await apiGet<ApiResponse<DaneaOrderView>>(
    `/api/danea-orders/${idDoc}`
  )
  return unwrapApi(response)
}

/**
 * Ricerca per numero d'ordine (Rif. Danea scritto a mano, es. «123/26»):
 * il server guarda prima l'archivio attuale e poi il vecchio (migrazione).
 */
export async function fetchDaneaOrderByRef(rif: string): Promise<DaneaOrderView> {
  const response = await apiGet<ApiResponse<DaneaOrderView>>(
    `/api/danea-orders/by-ref?rif=${encodeURIComponent(rif)}`
  )
  return unwrapApi(response)
}
