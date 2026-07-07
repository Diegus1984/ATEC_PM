import {
  ApiError,
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  BomItemSaveRequest,
  DdpRowItem,
} from "@/lib/api/types"

/**
 * Tutte le righe DDP di una commessa, per tipo distinta.
 * - COMMERCIAL → `/api/projects/{id}/ddp?type=COMMERCIAL` (bom_items)
 * - OFFICINA   → `/api/projects/{id}/ddp-officina` (ddp_officina_items)
 *
 * Le righe sono ordinate per id dal server; `rowNumber` (non è colonna DB) viene
 * assegnato per posizione, come fa il client WPF.
 */
export async function fetchDdpRows(
  projectId: number,
  type: string
): Promise<DdpRowItem[]> {
  const officina = type.toUpperCase() === "OFFICINA"
  const path = officina
    ? `/api/projects/${projectId}/ddp-officina`
    : `/api/projects/${projectId}/ddp?type=COMMERCIAL`
  const response = await apiGet<ApiResponse<DdpRowItem[]>>(path)
  const rows = unwrapApi(response)
  rows.forEach((row, index) => {
    row.rowNumber = index + 1
    row.ddpType = row.ddpType ?? (officina ? "OFFICINA" : "COMMERCIAL")
    if (officina) {
      row.unit = row.unit ?? ""
      row.manufacturer = row.manufacturer ?? ""
      row.material = row.material ?? ""
      row.treatment = row.treatment ?? ""
      row.totalCost = row.quantity * row.unitCost
    } else {
      row.unit = row.unit ?? ""
      row.manufacturer = row.manufacturer ?? ""
    }
  })
  return rows
}

/** Crea una riga DDP commerciale (`bom_items`) e ritorna il nuovo id. */
export async function createDdpRow(
  projectId: number,
  request: BomItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/projects/${projectId}/ddp`,
    request
  )
  return unwrapApi(response)
}

/**
 * Aggiorna una riga DDP commerciale. Ritorna il nuovo `updated_at` (token di
 * concorrenza). In caso di conflitto ottimistico il server risponde 409 e
 * `apiPut` lancia un `ApiError` (status 409) col messaggio del server.
 * NB: il server aggiorna solo quantità, stato, rif. Danea, data, destinazione e note.
 */
export async function updateDdpRow(
  projectId: number,
  itemId: number,
  request: BomItemSaveRequest
): Promise<string | null> {
  const response = await apiPut<ApiResponse<string | null>>(
    `/api/projects/${projectId}/ddp/${itemId}`,
    request
  )
  // Il server serializza con WhenWritingNull: quando il nuovo updated_at è null la
  // proprietà `data` è omessa. Non usiamo unwrapApi (che lancerebbe su data assente):
  // qui un updated_at nullo è un esito valido.
  if (!response.success) {
    throw new ApiError(response.message || "Operazione non riuscita", 400)
  }
  return response.data ?? null
}

export async function deleteDdpRow(
  projectId: number,
  itemId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/projects/${projectId}/ddp/${itemId}`
  )
  unwrapApi(response)
}
