import { apiGet, unwrapApi } from "@/lib/api/client"
import type { AcquistiInboxItem, ApiResponse } from "@/lib/api/types"

/** Inbox DDP commerciale cross-commessa (Acquisti). */
export async function fetchAcquistiInbox(
  projectId?: number
): Promise<AcquistiInboxItem[]> {
  const query =
    projectId != null ? `?projectId=${encodeURIComponent(String(projectId))}` : ""
  const response = await apiGet<ApiResponse<AcquistiInboxItem[]>>(
    `/api/ddp-commercial/inbox${query}`
  )
  return unwrapApi(response)
}
