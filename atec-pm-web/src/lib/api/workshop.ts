import { apiGet, apiPatch, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, WorkshopRow } from "@/lib/api/types"

/**
 * Righe di «Lavorazioni Officine» (#83): tutte e quattro le viste in un colpo solo.
 * Filtrare per vista lato client tiene i contatori veri senza quattro giri di rete.
 */
export async function fetchWorkshopRows(
  projectId?: number | null
): Promise<WorkshopRow[]> {
  const qs =
    projectId != null && projectId > 0
      ? `?projectId=${encodeURIComponent(String(projectId))}`
      : ""
  const r = await apiGet<ApiResponse<WorkshopRow[]>>(`/api/work-requests/officina${qs}`)
  return unwrapApi(r)
}

/**
 * I soli tre campi che questa pagina possiede su una riga di distinta.
 * `expectedUpdatedAt` è il token di concorrenza della riga officina: se qualcuno l'ha
 * toccata nel frattempo il server risponde 409 invece di sovrascriverla.
 */
export async function patchWorkshopField(
  itemId: number,
  field: "request_date" | "notes" | "is_ultra_critical",
  value: string | boolean | null,
  expectedUpdatedAt?: string | null
): Promise<void> {
  const r = await apiPatch<ApiResponse<boolean>>(
    `/api/work-requests/officina/${itemId}/field`,
    {
      field,
      value: value === null || value === undefined ? null : String(value),
      expectedUpdatedAt: expectedUpdatedAt ?? null,
    }
  )
  unwrapApi(r)
}
