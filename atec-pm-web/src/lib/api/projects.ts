import {
  apiDelete,
  apiGet,
  apiPatch,
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
  ProjectLookupItem,
  ProjectSaveRequest,
} from "@/lib/api/types"

export interface FetchProjectsParams {
  page?: number
  pageSize?: number
  search?: string
  /**
   * Include anche le commesse chiuse (Completate/Annullate). Default `false`:
   * le liste mostrano solo il lavoro in corso (Bozza/Attiva/In pausa).
   */
  includeClosed?: boolean
  /**
   * Forza la presenza di questa commessa nel risultato anche se chiusa.
   * Serve al deep-link su una commessa chiusa con il filtro "solo aperte" attivo.
   */
  includeId?: number
}

/** I parametri comuni ai due elenchi, in querystring. */
function queryElenco(params: FetchProjectsParams): string {
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
  if (params.includeClosed) {
    query.set("includeClosed", "true")
  }
  if (!params.includeClosed && params.includeId) {
    query.set("includeId", String(params.includeId))
  }
  return query.toString()
}

/**
 * Le commesse per una **tendina**: id, codice, titolo, cliente, PM, stato — e basta.
 *
 * È questa che usano SAL, MoM, Chat, Milestones, Lavorazioni e Dashboard: aperta a tutti gli
 * autenticati, perché la commessa si sceglie da mezzo gestionale. `fetchProjects` invece porta
 * anche gli importi e sta dietro `nav.commesse`: usarla per riempire una combo vuol dire
 * spedire il valore di ogni commessa a chi sta solo scegliendo un nome.
 */
export async function fetchProjectsLookup(
  params: FetchProjectsParams = {}
): Promise<PagedResult<ProjectLookupItem>> {
  const response = await apiGet<ApiResponse<PagedResult<ProjectLookupItem>>>(
    `/api/projects/lookup?${queryElenco(params)}`
  )
  return unwrapApi(response)
}

/**
 * L'elenco commesse **della pagina Commesse**, importi compresi. Richiede `nav.commesse`.
 * Per riempire una tendina si usa `fetchProjectsLookup`.
 */
export async function fetchProjects(
  params: FetchProjectsParams = {}
): Promise<PagedResult<ProjectListItem>> {
  const response = await apiGet<ApiResponse<PagedResult<ProjectListItem>>>(
    `/api/projects?${queryElenco(params)}`
  )

  return unwrapApi(response)
}

/**
 * Codice commessa successivo proposto: `C{aaaammgg}.{progressivo del giorno}`
 * (es. `C20260731.001`, poi `.002`…). Il progressivo riparte ogni giorno.
 */
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

/**
 * Cambio di solo stato dalla colonna «Stato» della Dashboard (#88).
 * Serve il permesso «Opera su commesse sospese o chiuse»; CANCELLED è rifiutato dal server.
 */
export async function patchProjectStatus(
  projectId: number,
  status: string
): Promise<void> {
  const response = await apiPatch<ApiResponse<number>>(
    `/api/projects/${projectId}/status`,
    status
  )
  unwrapApi(response)
}

/**
 * Promozione di un'Altra Attività a commessa (#88, rivista con la #89): il dialog di
 * configurazione manda l'anagrafica completa; il server genera il codice nuovo
 * (progressivo di oggi) e conserva quello vecchio nelle note. Ritorna il codice assegnato.
 */
export async function promoteProjectToCommessa(
  projectId: number,
  request: ProjectSaveRequest
): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    `/api/projects/${projectId}/promote-to-commessa`,
    request
  )
  return unwrapApi(response)
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
