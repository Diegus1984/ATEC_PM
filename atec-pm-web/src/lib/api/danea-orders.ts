import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, DaneaOrderView } from "@/lib/api/types"

/** Ordine fornitore Danea (Atec_PM) per il popup di rendering. */
export async function fetchDaneaOrder(idDoc: number): Promise<DaneaOrderView> {
  const response = await apiGet<ApiResponse<DaneaOrderView>>(
    `/api/danea-orders/${idDoc}`
  )
  return unwrapApi(response)
}
