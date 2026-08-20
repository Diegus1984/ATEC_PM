import { apiGet, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DashboardFoldersResponse,
  DashboardSettings,
} from "@/lib/api/types"

/** Cartelle della pagina d'ingresso (blocco 7): in dashboard + escluse + limite. */
export async function fetchDashboardFolders(
  includeClosed: boolean
): Promise<DashboardFoldersResponse> {
  const response = await apiGet<ApiResponse<DashboardFoldersResponse>>(
    `/api/dashboard/folders?includeClosed=${includeClosed}`
  )
  return unwrapApi(response)
}

/** Spunta «In dashboard»: scelta condivisa fra tutti, non preferenza personale. */
export async function setProjectInDashboard(
  projectId: number,
  inDashboard: boolean
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/dashboard/folders/${projectId}`,
    { inDashboard }
  )
  return unwrapApi(response)
}

export async function saveDashboardSettings(
  settings: DashboardSettings
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(
    "/api/dashboard/settings",
    settings
  )
  return unwrapApi(response)
}
