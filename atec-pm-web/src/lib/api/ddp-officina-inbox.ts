import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, OfficinaInboxItem } from "@/lib/api/types"

/** Inbox DDP Officina cross-commessa (Responsabile Officina). */
export async function fetchOfficinaInbox(
  projectId?: number | null
): Promise<OfficinaInboxItem[]> {
  const qs =
    projectId != null && projectId > 0
      ? `?projectId=${encodeURIComponent(String(projectId))}`
      : ""
  const r = await apiGet<ApiResponse<OfficinaInboxItem[]>>(
    `/api/ddp-officina/inbox${qs}`
  )
  return unwrapApi(r)
}
