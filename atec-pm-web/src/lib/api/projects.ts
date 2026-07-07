import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  FileTreeItem,
  LookupItem,
  PagedResult,
  ProjectDashboardData,
  ProjectListItem,
  ProjectSaveRequest,
} from "@/lib/api/types"

export interface FetchProjectsParams {
  page?: number
  pageSize?: number
  search?: string
}

export async function fetchProjects(
  params: FetchProjectsParams = {}
): Promise<PagedResult<ProjectListItem>> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 50
  const search = params.search?.trim()

  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })

  if (search) {
    query.set("search", search)
  }

  const response = await apiGet<ApiResponse<PagedResult<ProjectListItem>>>(
    `/api/projects?${query.toString()}`
  )

  return unwrapApi(response)
}

/** Codice commessa successivo proposto (es. `AT2026001`). */
export async function fetchProjectNextCode(): Promise<string> {
  const response = await apiGet<ApiResponse<string>>("/api/projects/next-code")
  return unwrapApi(response)
}

/** Singola commessa per la modifica (shape `ProjectSaveRequest`). */
export async function fetchProject(
  projectId: number
): Promise<ProjectSaveRequest> {
  const response = await apiGet<ApiResponse<ProjectSaveRequest>>(
    `/api/projects/${projectId}`
  )
  return unwrapApi(response)
}

/** Crea la commessa e ritorna il nuovo id. */
export async function createProject(
  request: ProjectSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/projects", request)
  return unwrapApi(response)
}

export async function updateProject(
  projectId: number,
  request: ProjectSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/projects/${projectId}`,
    request
  )
  unwrapApi(response)
}

/** Eliminazione logica (soft delete: stato → CANCELLED). */
export async function deleteProject(projectId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/projects/${projectId}`
  )
  unwrapApi(response)
}

/** Eliminazione definitiva (ADMIN): rimuove dati e cartelle su disco. */
export async function hardDeleteProject(projectId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/projects/${projectId}/hard`
  )
  unwrapApi(response)
}

/** Clienti attivi per la tendina del dialog commessa. */
export async function fetchCustomerLookup(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/lookup/customers"
  )
  return unwrapApi(response)
}

/** Project Manager (dipendenti attivi con ruolo PM) per la tendina. */
export async function fetchPmLookup(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    "/api/lookup/employees?role=PM"
  )
  return unwrapApi(response)
}

export async function fetchProjectDashboard(
  projectId: number
): Promise<ProjectDashboardData> {
  const response = await apiGet<ApiResponse<ProjectDashboardData>>(
    `/api/projects/${projectId}/dashboard`
  )
  return unwrapApi(response)
}

export async function fetchProjectFileTree(
  projectId: number
): Promise<FileTreeItem[]> {
  const response = await apiGet<ApiResponse<FileTreeItem[]>>(
    `/api/projects/${projectId}/file-tree`
  )
  return unwrapApi(response)
}

export async function downloadProjectFile(
  projectId: number,
  relativePath: string,
  token: string | null
): Promise<void> {
  const base = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? ""
  const url = `${base}/api/projects/${projectId}/download?path=${encodeURIComponent(relativePath)}`
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(url, { headers })
  if (!response.ok) {
    throw new Error("Download non riuscito")
  }

  const blob = await response.blob()
  const fileName = relativePath.split(/[/\\]/).pop() ?? "file"
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}
