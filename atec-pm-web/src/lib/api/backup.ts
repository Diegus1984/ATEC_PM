import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  buildApiUrl,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  BackupDestination,
  BackupFileInfo,
  FullBackupEstimate,
  FullBackupJob,
  FullBackupPackage,
} from "@/lib/api/types"

/** Destinazione dei pacchetti completi (percorso, origine, utente share). */
export async function fetchBackupDestination(): Promise<BackupDestination> {
  const response = await apiGet<ApiResponse<BackupDestination>>(
    "/api/backup/full/destinazione"
  )
  return unwrapApi(response)
}

/**
 * Salva la destinazione dei pacchetti. Il server la PROVA prima di salvarla
 * (sessione SMB + scrittura di un file di prova): se non funziona rifiuta e
 * l'impostazione resta quella di prima. Percorso vuoto = torna a quella del server.
 * Ritorna anche il messaggio del server (dice cosa è successo davvero).
 */
export async function saveBackupDestination(body: {
  percorso: string
  shareUser?: string
  sharePassword?: string
}): Promise<{ destinazione: BackupDestination; messaggio: string }> {
  const response = await apiPut<ApiResponse<BackupDestination>>(
    "/api/backup/full/destinazione",
    body
  )
  return { destinazione: unwrapApi(response), messaggio: response.message ?? "" }
}

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
  await downloadFile(
    `/api/backup/download/${encodeURIComponent(fileName)}`,
    fileName,
    token
  )
}

// ── Backup completo: database + cartelle (documenti, foto, video) ──────────

export async function fetchFullBackupEstimate(): Promise<FullBackupEstimate> {
  const response = await apiGet<ApiResponse<FullBackupEstimate>>(
    "/api/backup/full/stima"
  )
  return unwrapApi(response)
}

export async function fetchFullBackupList(): Promise<FullBackupPackage[]> {
  const response = await apiGet<ApiResponse<FullBackupPackage[]>>(
    "/api/backup/full/list"
  )
  return unwrapApi(response)
}

export async function startFullBackup(): Promise<FullBackupJob> {
  const response = await apiPost<ApiResponse<FullBackupJob>>(
    "/api/backup/full/start",
    {}
  )
  return unwrapApi(response)
}

/** Operazione in corso, se c'è: permette di riagganciarsi all'avanzamento dopo un F5. */
export async function fetchFullBackupCurrentJob(): Promise<FullBackupJob | null> {
  const response = await apiGet<ApiResponse<FullBackupJob | null>>(
    "/api/backup/full/stato-corrente"
  )
  return unwrapApi(response)
}

export async function fetchFullBackupJob(jobId: string): Promise<FullBackupJob> {
  const response = await apiGet<ApiResponse<FullBackupJob>>(
    `/api/backup/full/stato/${encodeURIComponent(jobId)}`
  )
  return unwrapApi(response)
}

export async function restoreFullBackup(params: {
  fileName: string
  database: boolean
  file: boolean
}): Promise<FullBackupJob> {
  const query = `?database=${params.database}&file=${params.file}`
  const response = await apiPost<ApiResponse<FullBackupJob>>(
    `/api/backup/full/restore/${encodeURIComponent(params.fileName)}${query}`,
    {}
  )
  return unwrapApi(response)
}

/**
 * Carica sul server un pacchetto creato altrove (es. sul PC di sviluppo), così da
 * poterlo poi ripristinare da questa stessa pagina.
 */
export async function uploadFullBackup(
  file: File,
  token: string | null
): Promise<string> {
  const body = new FormData()
  body.append("file", file)

  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildApiUrl("/api/backup/full/upload"), {
    method: "POST",
    headers,
    body,
  })
  const payload = (await response.json()) as ApiResponse<string>
  if (!response.ok) {
    throw new Error(payload?.message ?? "Caricamento non riuscito")
  }
  return unwrapApi(payload)
}

export async function deleteFullBackup(fileName: string): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/backup/full/${encodeURIComponent(fileName)}`
  )
  unwrapApi(response)
}

export async function downloadFullBackup(
  fileName: string,
  token: string | null
): Promise<void> {
  await downloadFile(
    `/api/backup/full/download/${encodeURIComponent(fileName)}`,
    fileName,
    token
  )
}

async function downloadFile(
  path: string,
  fileName: string,
  token: string | null
): Promise<void> {
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }
  const response = await fetch(buildApiUrl(path), { headers })
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
