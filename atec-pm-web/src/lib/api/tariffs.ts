import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  TariffOptionDto,
  TariffOptionSaveRequest,
} from "@/lib/api/types"

/**
 * Anagrafica tariffe (`tariff_options`). La lettura è libera — serve ai picker di ogni
 * livello — mentre aggiunta, modifica ed eliminazione sono riservate alla feature
 * `nav.config_sezioni`, la stessa che protegge la Configurazione sezioni.
 */
const BASE = "/api/tariff-options"

export async function fetchTariffOptions(
  type?: string
): Promise<TariffOptionDto[]> {
  const response = await apiGet<ApiResponse<TariffOptionDto[]>>(
    type ? `${BASE}?type=${encodeURIComponent(type)}` : BASE
  )
  return unwrapApi(response)
}

export async function addTariffOption(
  body: TariffOptionSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(BASE, body)
  return unwrapApi(response)
}

/**
 * Cambia l'importo di una tariffa (il tipo non si tocca). Non riscrive la storia: il
 * valore viene copiato nella riga quando lo si sceglie, quindi qui si sposta solo quello
 * che verrà proposto d'ora in avanti.
 */
export async function updateTariffOption(
  id: number,
  body: TariffOptionSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`${BASE}/${id}`, body)
  unwrapApi(response)
}

/** Il server rifiuta l'eliminazione se il valore è già usato in una commessa o offerta. */
export async function deleteTariffOption(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${BASE}/${id}`)
  unwrapApi(response)
}
