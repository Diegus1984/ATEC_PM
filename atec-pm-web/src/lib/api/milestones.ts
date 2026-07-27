import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  Milestone,
  MilestoneSaveRequest,
  MilestoneSummary,
} from "@/lib/api/types"

/** Milestone di una commessa, ordinate per sort_order. */
export async function fetchMilestones(projectId: number): Promise<Milestone[]> {
  const response = await apiGet<ApiResponse<Milestone[]>>(
    `/api/milestones?projectId=${projectId}`
  )
  return unwrapApi(response)
}

/** Riepilogo (conteggi di stato) delle milestone attive per ogni commessa che ne ha almeno una.
 *  Leggero e aggregato: nutre la sidebar PM globale senza caricare tutte le milestone. */
export async function fetchMilestonesSummary(): Promise<MilestoneSummary[]> {
  const response = await apiGet<ApiResponse<MilestoneSummary[]>>(
    `/api/milestones/summary`
  )
  return unwrapApi(response)
}

export async function createMilestone(
  projectId: number,
  request: MilestoneSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/milestones?projectId=${projectId}`,
    request
  )
  return unwrapApi(response)
}

export async function updateMilestone(
  id: number,
  request: MilestoneSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/milestones/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteMilestone(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/milestones/${id}`
  )
  return unwrapApi(response)
}

/** Riordino: la posizione degli id nell'array diventa il nuovo sort_order. */
export async function reorderMilestones(
  projectId: number,
  ids: number[]
): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/milestones/reorder?projectId=${projectId}`,
    { ids }
  )
  return unwrapApi(response)
}

/** Precarico: copia (snapshot) le voci di catalogo scelte come milestone della commessa. */
export async function seedMilestonesFromCatalog(
  projectId: number,
  catalogIds: number[]
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/milestones/project/${projectId}/seed-from-catalog`,
    { catalogIds }
  )
  return unwrapApi(response)
}

export const preloadMilestonesFromCatalog = seedMilestonesFromCatalog

export interface ActivityCatalogItem {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

/** Recupera le attività attive dall'anagrafica catalogo per il precarico/selezione milestone. */
export async function fetchActiveActivityCatalog(): Promise<ActivityCatalogItem[]> {
  const response = await apiGet<ApiResponse<ActivityCatalogItem[]>>(
    `/api/activity-catalog/active`
  )
  return unwrapApi(response)
}

