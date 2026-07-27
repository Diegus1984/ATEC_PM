import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, Deadline } from "@/lib/api/types"

/**
 * Recupera l'elenco unificato di tutte le scadenze aperte (SAL, PROJECT, CHECKLIST, MOM, DDP).
 */
export async function fetchDeadlines(): Promise<Deadline[]> {
  const response = await apiGet<ApiResponse<Deadline[]>>("/api/deadlines")
  return unwrapApi(response)
}
