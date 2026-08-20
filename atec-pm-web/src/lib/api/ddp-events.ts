import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, DdpItemEvent } from "@/lib/api/types"

/** Distinta di appartenenza della riga. */
export type DdpItemKind = "COMMERCIAL" | "OFFICINA"

/**
 * Cronistoria di una riga di distinta: tutti i passaggi di stato, dal più recente.
 * Da qui si legge «consegnato il», «ordinato il», chi ha fatto cosa e quando.
 */
export async function fetchDdpItemEvents(
  kind: DdpItemKind,
  itemId: number
): Promise<DdpItemEvent[]> {
  const response = await apiGet<ApiResponse<DdpItemEvent[]>>(
    `/api/ddp-events/${kind}/${itemId}`
  )
  return unwrapApi(response)
}
