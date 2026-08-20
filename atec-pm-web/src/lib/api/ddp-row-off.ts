import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type { ApiResponse } from "@/lib/api/types"

/**
 * «Righe spente» della Sintesi DDP: righe escluse dalle stampe di Avanzamento e righe
 * già gestite in Dati Mancanti. Sono un dato di commessa CONDIVISO (scelta di Diego,
 * 06/08/2026): quello che spegne un utente lo vedono spento anche gli altri.
 * La chiave è `(commessa, tipo DDP, sezione, id riga)`.
 */
export interface DdpRowOffItem {
  sectionKey: string
  itemId: number
}

export async function fetchDdpRowOff(
  projectId: number,
  ddpType: string
): Promise<DdpRowOffItem[]> {
  const response = await apiGet<ApiResponse<DdpRowOffItem[]>>(
    `/api/ddp-row-off/${projectId}/${ddpType}`
  )
  return unwrapApi(response)
}

export async function setDdpRowOff(
  projectId: number,
  ddpType: string,
  sectionKey: string,
  itemId: number,
  off: boolean
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/ddp-row-off/${projectId}/${ddpType}/${sectionKey}/${itemId}`,
    { off }
  )
  unwrapApi(response)
}

/** «Tutte accese» / «Tutte spente» su una sezione. */
export async function setDdpRowOffBulk(
  projectId: number,
  ddpType: string,
  sectionKey: string,
  itemIds: number[],
  off: boolean
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/ddp-row-off/${projectId}/${ddpType}/${sectionKey}/bulk`,
    { itemIds, off }
  )
  unwrapApi(response)
}

/** Riaccende tutto: una sola sezione se `sectionKey` è valorizzata, altrimenti la DDP intera. */
export async function resetDdpRowOff(
  projectId: number,
  ddpType: string,
  sectionKey?: string
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/ddp-row-off/${projectId}/${ddpType}/reset`,
    { sectionKey: sectionKey ?? null }
  )
  unwrapApi(response)
}
