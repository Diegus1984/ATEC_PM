import { apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, EmailSettingsDto, TestEmailRequest } from "@/lib/api/types"

// Configurazione applicativa ADMIN-only (`/api/settings`). Oggi: solo SMTP per il digest email.

export async function fetchEmailSettings(): Promise<EmailSettingsDto> {
  const response = await apiGet<ApiResponse<EmailSettingsDto>>("/api/settings/email")
  return unwrapApi(response)
}

export async function saveEmailSettings(settings: EmailSettingsDto): Promise<string> {
  const response = await apiPost<ApiResponse<string>>("/api/settings/email", settings)
  return unwrapApi(response)
}

export async function sendTestEmail(request: TestEmailRequest): Promise<string> {
  const response = await apiPost<ApiResponse<string>>("/api/settings/email/test", request)
  return unwrapApi(response)
}
