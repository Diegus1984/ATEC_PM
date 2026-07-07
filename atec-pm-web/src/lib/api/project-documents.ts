import {
  apiGet,
  apiPost,
  apiUpload,
  buildApiUrl,
  unwrapApi,
} from "@/lib/api/client"
import { getSession } from "@/lib/auth/session"
import type { ApiResponse, FileItem } from "@/lib/api/types"

function subPathQuery(subPath: string): string {
  return subPath ? `?subPath=${encodeURIComponent(subPath)}` : ""
}

/** Contenuto (file + cartelle) di una sotto-cartella della commessa. */
export async function fetchProjectFiles(
  projectId: number,
  subPath: string
): Promise<FileItem[]> {
  const response = await apiGet<ApiResponse<FileItem[]>>(
    `/api/projects/${projectId}/files${subPathQuery(subPath)}`
  )
  return unwrapApi(response)
}

/** Carica uno o più file nella sotto-cartella corrente. Ritorna il messaggio server. */
export async function uploadProjectFiles(
  projectId: number,
  subPath: string,
  files: File[]
): Promise<string> {
  if (files.length === 1) {
    const formData = new FormData()
    formData.append("file", files[0])
    const response = await apiUpload<ApiResponse<string>>(
      `/api/projects/${projectId}/upload${subPathQuery(subPath)}`,
      formData
    )
    return unwrapApi(response)
  }

  const formData = new FormData()
  for (const file of files) {
    formData.append("files", file)
  }
  const response = await apiUpload<ApiResponse<string>>(
    `/api/projects/${projectId}/upload-multiple${subPathQuery(subPath)}`,
    formData
  )
  return unwrapApi(response)
}

/** Crea la cartella base della commessa su disco (idempotente lato server). */
export async function createProjectBaseFolder(
  projectId: number
): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/projects/${projectId}/create-folder`
  )
  return unwrapApi(response)
}

export async function createProjectSubfolder(
  projectId: number,
  subPath: string,
  folderName: string
): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/projects/${projectId}/create-subfolder`,
    { subPath, folderName }
  )
  unwrapApi(response)
}

/** Rinomina file o cartella (`oldPath` relativo alla root commessa). */
export async function renameProjectItem(
  projectId: number,
  oldPath: string,
  newName: string
): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/projects/${projectId}/rename`,
    { oldPath, newName }
  )
  unwrapApi(response)
}

/** Sposta file o cartella nella cartella destinazione (relativa alla root). */
export async function moveProjectItem(
  projectId: number,
  sourcePath: string,
  destinationFolder: string
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/projects/${projectId}/move-item`,
    { sourcePath, destinationFolder }
  )
  unwrapApi(response)
}

export async function deleteProjectItem(
  projectId: number,
  itemPath: string
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/projects/${projectId}/delete-item`,
    { itemPath }
  )
  unwrapApi(response)
}

async function authedFetch(path: string): Promise<Response> {
  const token = getSession()?.token ?? null
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }
  const response = await fetch(buildApiUrl(path), { headers })
  if (!response.ok) {
    throw new Error("Anteprima non disponibile")
  }
  return response
}

/** Scarica il file come blob e ritorna un object URL (da revocare dopo l'uso). */
export async function fetchProjectFileBlobUrl(
  projectId: number,
  relativePath: string
): Promise<string> {
  const response = await authedFetch(
    `/api/projects/${projectId}/download?path=${encodeURIComponent(relativePath)}`
  )
  const blob = await response.blob()
  return URL.createObjectURL(blob)
}

/** Anteprima HTML server-side (Word .docx / Excel / CSV). */
export async function fetchProjectPreviewHtml(
  projectId: number,
  relativePath: string
): Promise<string> {
  const response = await authedFetch(
    `/api/projects/${projectId}/preview?path=${encodeURIComponent(relativePath)}`
  )
  return response.text()
}
