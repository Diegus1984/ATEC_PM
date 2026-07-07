import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DigestStatusDto,
  NotifyPendingDto,
  NotifySendResultDto,
  PlanDigestSettingsDto,
  SelectivePreviewDto,
  SendSelectedRequest,
} from "@/lib/api/types"

// Layer API del digest email (riepilogo modifiche piano risorse), sotto
// `/api/resource-planner` come il resto del modulo.

const BASE = "/api/resource-planner"

export async function fetchNotifyPending(): Promise<NotifyPendingDto> {
  const response = await apiGet<ApiResponse<NotifyPendingDto>>(`${BASE}/notify/pending`)
  return unwrapApi(response)
}

export async function fetchSelectivePreview(): Promise<SelectivePreviewDto> {
  const response = await apiGet<ApiResponse<SelectivePreviewDto>>(
    `${BASE}/digest/preview-selective`
  )
  return unwrapApi(response)
}

export async function sendSelected(
  request: SendSelectedRequest
): Promise<NotifySendResultDto> {
  const response = await apiPost<ApiResponse<NotifySendResultDto>>(
    `${BASE}/digest/send-selected`,
    request
  )
  return unwrapApi(response)
}

export async function runDigestNow(): Promise<NotifySendResultDto> {
  const response = await apiPost<ApiResponse<NotifySendResultDto>>(
    `${BASE}/digest/run-now`
  )
  return unwrapApi(response)
}

export async function fetchDigestSettings(): Promise<PlanDigestSettingsDto> {
  const response = await apiGet<ApiResponse<PlanDigestSettingsDto>>(
    `${BASE}/digest/settings`
  )
  return unwrapApi(response)
}

export async function saveDigestSettings(
  settings: PlanDigestSettingsDto
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${BASE}/digest/settings`,
    settings
  )
  return unwrapApi(response)
}

export async function fetchDigestStatus(): Promise<DigestStatusDto> {
  const response = await apiGet<ApiResponse<DigestStatusDto>>(`${BASE}/digest/status`)
  return unwrapApi(response)
}
