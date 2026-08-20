import {
  apiDelete,
  apiGet,
  apiGetBlob,
  apiPost,
  apiPut,
  apiUpload,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  BugReport,
  BugReportCounts,
  BugReportSaveRequest,
  BugReportStatusRequest,
} from "@/lib/api/types"

/** Elenco completo: tutti vedono tutte le segnalazioni, le proprie hanno `isMine`. */
export async function fetchBugReports(params?: {
  archived?: boolean
}): Promise<BugReport[]> {
  const query = params?.archived ? "?archived=true" : ""
  const response = await apiGet<ApiResponse<BugReport[]>>(
    `/api/bug-reports${query}`
  )
  return unwrapApi(response)
}

export async function fetchBugReportCounts(): Promise<BugReportCounts> {
  const response = await apiGet<ApiResponse<BugReportCounts>>(
    "/api/bug-reports/counts"
  )
  return unwrapApi(response)
}

export async function createBugReport(
  request: BugReportSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/bug-reports", request)
  return unwrapApi(response)
}

export async function updateBugReport(
  id: number,
  request: BugReportSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(`/api/bug-reports/${id}`, request)
  unwrapApi(response)
}

/** Cambio stato + nota di risposta: il server lo consente solo agli ADMIN. */
export async function updateBugReportStatus(
  id: number,
  request: BugReportStatusRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/bug-reports/${id}/status`,
    request
  )
  unwrapApi(response)
}

export async function archiveBugReport(id: number): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/bug-reports/${id}/archive`
  )
  unwrapApi(response)
}

export async function unarchiveBugReport(id: number): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/bug-reports/${id}/unarchive`
  )
  unwrapApi(response)
}

export async function deleteBugReport(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/bug-reports/${id}`)
  unwrapApi(response)
}

// ── Allegati ────────────────────────────────────────────

export async function uploadBugAttachment(
  bugId: number,
  file: File,
  isReply = false
): Promise<number> {
  const formData = new FormData()
  formData.append("file", file)
  const query = isReply ? "?isReply=true" : ""
  const response = await apiUpload<ApiResponse<number>>(
    `/api/bug-reports/${bugId}/attachments${query}`,
    formData
  )
  return unwrapApi(response)
}

export async function deleteBugAttachment(attachmentId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/bug-reports/attachments/${attachmentId}`
  )
  unwrapApi(response)
}

/** Gli allegati passano da un endpoint autenticato: servono come Blob, non come URL diretta. */
export async function fetchBugAttachmentBlob(attachmentId: number): Promise<Blob> {
  return apiGetBlob(`/api/bug-reports/attachments/${attachmentId}`)
}
