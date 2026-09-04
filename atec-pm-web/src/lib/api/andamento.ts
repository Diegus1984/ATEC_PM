import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, AndamentoOfferte } from "@/lib/api/types"

/**
 * L'andamento delle offerte di un anno: KPI, serie mensili, raggruppamenti, chance,
 * portafoglio ponderato e le aperte più grandi.
 *
 * Gli importi sono coperti da `data.revenue`: chi non vede il fatturato riceve i conteggi
 * e le percentuali con gli euro azzerati — il server li toglie, non il client.
 */
export async function fetchAndamentoOfferte(params: {
  year?: number
  sellerCode?: string
  category?: string
}): Promise<AndamentoOfferte> {
  const q = new URLSearchParams()
  if (params.year) q.set("year", String(params.year))
  if (params.sellerCode) q.set("sellerCode", params.sellerCode)
  if (params.category) q.set("category", params.category)
  const response = await apiGet<ApiResponse<AndamentoOfferte>>(
    `/api/andamento/offerte${q.toString() ? `?${q.toString()}` : ""}`
  )
  return unwrapApi(response)
}
