import { apiGet, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  BilancioSettings,
  BilancioSummaryResponse,
} from "@/lib/api/types"

const BASE = "/api/bilancio"

/**
 * Riepilogo cross-commessa della pagina Bilancio: una riga per commessa con i due KPI di
 * redditività a consuntivo, più la soglia sotto la quale la card diventa rossa.
 * Endpoint gated dalla feature `nav.bilancio`.
 *
 * `includeClosed` fa entrare anche le COMPLETATE (di default restano fuori, come in ogni
 * elenco commesse); ANNULLATE e BOZZE restano sempre escluse.
 */
export async function fetchBilancioSummary(
  includeClosed: boolean
): Promise<BilancioSummaryResponse> {
  const response = await apiGet<ApiResponse<BilancioSummaryResponse>>(
    `${BASE}/summary?includeClosed=${includeClosed}`
  )
  return unwrapApi(response)
}

/** Salva la soglia di redditività (solo ADMIN). */
export async function saveBilancioSettings(
  settings: BilancioSettings
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(`${BASE}/settings`, settings)
  unwrapApi(response)
}
