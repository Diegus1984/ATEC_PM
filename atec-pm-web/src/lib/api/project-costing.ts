import { apiDelete, apiGet, apiPatch, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  AvailableCostTemplatesDto,
  EmployeeCostLookup,
  ProjectCostResourceSaveRequest,
  ProjectCostingData,
  ProjectMaterialItemSaveRequest,
  ProjectPricingDto,
} from "@/lib/api/types"

/**
 * Layer API costing PREVENTIVO di commessa (`/api/projects/{id}/costing/*`).
 * Speculare a `quote-costing.ts` ma puntato al ProjectCostingController.
 * Tutti gli endpoint richiedono la feature `data.costs` (PM/ADMIN).
 *
 * NB: rispetto al preventivo-offerta il controller commessa NON espone
 * varianti/clone/catalogo/tabella distribuzione prezzo/batch — qui esponiamo
 * solo ciò che serve a compilare il preventivo a mano e alimentare il
 * confronto Prev vs Consuntivo.
 */
function base(projectId: number): string {
  return `/api/projects/${projectId}/costing`
}

export async function fetchProjectCosting(projectId: number): Promise<ProjectCostingData> {
  const response = await apiGet<ApiResponse<ProjectCostingData>>(base(projectId))
  return unwrapApi(response)
}

/** Semina l'albero preventivo dai template di default (`is_default_project=1`). */
export async function initProjectCosting(projectId: number): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(`${base(projectId)}/init`)
  unwrapApi(response)
}

export async function fetchProjectAvailableTemplates(
  projectId: number
): Promise<AvailableCostTemplatesDto> {
  const response = await apiGet<ApiResponse<AvailableCostTemplatesDto>>(
    `${base(projectId)}/available-templates`
  )
  return unwrapApi(response)
}

/**
 * Risorse proponibili per la sezione: wildcard di reparto in testa, poi le persone,
 * in ordine alfabetico italiano. Il server toglie i nomi già usati nella sezione —
 * `excludeResourceId` è la riga che si sta modificando, che deve restare selezionabile.
 */
export async function fetchProjectSectionEmployees(
  projectId: number,
  sectionId: number,
  excludeResourceId?: number | null
): Promise<EmployeeCostLookup[]> {
  const query =
    excludeResourceId != null ? `?excludeResourceId=${excludeResourceId}` : ""
  const response = await apiGet<ApiResponse<EmployeeCostLookup[]>>(
    `${base(projectId)}/sections/${sectionId}/employees${query}`
  )
  return unwrapApi(response)
}

export async function setProjectSectionDepartments(
  projectId: number,
  sectionId: number,
  departmentIds: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(projectId)}/sections/${sectionId}/departments`,
    { departmentIds }
  )
  unwrapApi(response)
}

// ── Sezioni costo ──────────────────────────────────────────

export interface AddProjectCostSectionInput {
  templateId: number | null
  name: string
  sectionType: string
  groupName: string
  sortOrder: number
  isEnabled: boolean
}

export async function addProjectCostSection(
  projectId: number,
  input: AddProjectCostSectionInput
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(projectId)}/sections`, input)
  return unwrapApi(response)
}

export async function updateProjectCostSectionField(
  projectId: number,
  id: number,
  field: string,
  value: string | null
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `${base(projectId)}/sections/${id}/field`,
    { field, value }
  )
  unwrapApi(response)
}

export async function deleteProjectCostSection(projectId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${base(projectId)}/sections/${id}`)
  unwrapApi(response)
}

// ── Risorse ────────────────────────────────────────────────

export async function addProjectResource(
  projectId: number,
  req: ProjectCostResourceSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(projectId)}/resources`, req)
  return unwrapApi(response)
}

export async function updateProjectResource(
  projectId: number,
  id: number,
  req: ProjectCostResourceSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`${base(projectId)}/resources/${id}`, req)
  unwrapApi(response)
}

export async function deleteProjectResource(projectId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${base(projectId)}/resources/${id}`)
  unwrapApi(response)
}

// ── Sezioni materiali ──────────────────────────────────────

export interface AddProjectMaterialSectionInput {
  categoryId: number | null
  name: string
  markupValue: number
  commissionMarkup: number
  sortOrder: number
  isEnabled: boolean
}

export async function addProjectMaterialSection(
  projectId: number,
  input: AddProjectMaterialSectionInput
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(projectId)}/material-sections`,
    input
  )
  return unwrapApi(response)
}

export async function updateProjectMaterialSectionField(
  projectId: number,
  id: number,
  field: string,
  value: string | null
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `${base(projectId)}/material-sections/${id}/field`,
    { field, value }
  )
  unwrapApi(response)
}

export async function deleteProjectMaterialSection(projectId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${base(projectId)}/material-sections/${id}`
  )
  unwrapApi(response)
}

// ── Righe materiali ────────────────────────────────────────

export async function addProjectMaterialItem(
  projectId: number,
  req: ProjectMaterialItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(projectId)}/material-items`, req)
  return unwrapApi(response)
}

export async function updateProjectMaterialItem(
  projectId: number,
  id: number,
  req: ProjectMaterialItemSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(projectId)}/material-items/${id}`,
    req
  )
  unwrapApi(response)
}

export async function deleteProjectMaterialItem(projectId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${base(projectId)}/material-items/${id}`
  )
  unwrapApi(response)
}

// ── Scheda prezzi ──────────────────────────────────────────

export async function updateProjectPricing(
  projectId: number,
  dto: ProjectPricingDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`${base(projectId)}/pricing`, dto)
  unwrapApi(response)
}
