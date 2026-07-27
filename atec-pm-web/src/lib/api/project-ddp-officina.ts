import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  OfficinaImportCompositionRequest,
  OfficinaImportCompositionResult,
  OfficinaItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"

export async function fetchOfficinaItems(
  projectId: number
): Promise<OfficinaItem[]> {
  const r = await apiGet<ApiResponse<OfficinaItem[]>>(
    `/api/projects/${projectId}/ddp-officina`
  )
  return unwrapApi(r)
}

export async function addOfficinaItem(
  projectId: number,
  req: OfficinaItemSaveRequest
): Promise<number> {
  const r = await apiPost<ApiResponse<number>>(
    `/api/projects/${projectId}/ddp-officina`,
    req
  )
  return unwrapApi(r)
}

export async function updateOfficinaItem(
  projectId: number,
  itemId: number,
  req: OfficinaItemSaveRequest
): Promise<void> {
  const r = await apiPut<ApiResponse<number>>(
    `/api/projects/${projectId}/ddp-officina/${itemId}`,
    req
  )
  unwrapApi(r)
}

/**
 * Importa i figli diretti della composizione Codex di un codice padre nella
 * distinta officina (dedup per codice: le righe già presenti sommano le quantità).
 */
export async function importOfficinaComposition(
  projectId: number,
  req: OfficinaImportCompositionRequest
): Promise<OfficinaImportCompositionResult> {
  const r = await apiPost<ApiResponse<OfficinaImportCompositionResult>>(
    `/api/projects/${projectId}/ddp-officina/import-composition`,
    req
  )
  return unwrapApi(r)
}

export async function deleteOfficinaItem(
  projectId: number,
  itemId: number
): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(
    `/api/projects/${projectId}/ddp-officina/${itemId}`
  )
  unwrapApi(r)
}
