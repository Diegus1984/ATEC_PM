import {
  apiDelete,
  apiGet,
  apiPost,
  buildApiUrl,
  unwrapApi,
} from "@/lib/api/client"
import type { ApiResponse, BackupFileInfo } from "@/lib/api/types"

export async function fetchBackupList(): Promise<BackupFileInfo[]> {
  const response = await apiGet<ApiResponse<BackupFileInfo[]>>("/api/backup/list")
  return unwrapApi(response)
}

export async function runBackupNow(): Promise<string> {
  const response = await apiPost<ApiResponse<string>>("/api/backup/now", {})
  return unwrapApi(response)
}

export async function restoreBackup(fileName: string): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/backup/restore/${encodeURIComponent(fileName)}`,
    {}
  )
  return unwrapApi(response)
}

export async function deleteBackup(fileName: string): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/backup/${encodeURIComponent(fileName)}`
  )
  unwrapApi(response)
}

export async function downloadBackup(
  fileName: string,
  token: string | null
): Promise<void> {
  const url = buildApiUrl(`/api/backup/download/${encodeURIComponent(fileName)}`)
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }
  const response = await fetch(url, { headers })
  if (!response.ok) {
    throw new Error("Download non riuscito")
  }
  const blob = await response.blob()
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}
