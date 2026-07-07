import { apiDelete, apiGet, apiPatch, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  AvailableCostTemplatesDto,
  BatchDistributionRequest,
  EmployeeCostLookup,
  PricingDistributionRow,
  ProjectCostResourceSaveRequest,
  ProjectCostingData,
  ProjectMaterialItemSaveRequest,
  ProjectPricingDto,
  RebalanceRequest,
  SectionDistributionDto,
} from "@/lib/api/types"

/**
 * Layer API costing preventivo IMPIANTO (`/api/quotes/{id}/costing/*`).
 * Tutti gli endpoint richiedono la feature `data.costs` (PM/ADMIN).
 * Fedele a QuoteCostingController + ProjectCosting_DTOs.cs.
 */
function base(quoteId: number): string {
  return `/api/quotes/${quoteId}/costing`
}

export async function fetchCosting(quoteId: number): Promise<ProjectCostingData> {
  const response = await apiGet<ApiResponse<ProjectCostingData>>(base(quoteId))
  return unwrapApi(response)
}

export async function fetchAvailableTemplates(
  quoteId: number
): Promise<AvailableCostTemplatesDto> {
  const response = await apiGet<ApiResponse<AvailableCostTemplatesDto>>(
    `${base(quoteId)}/available-templates`
  )
  return unwrapApi(response)
}

export async function fetchSectionEmployees(
  quoteId: number,
  sectionId: number
): Promise<EmployeeCostLookup[]> {
  const response = await apiGet<ApiResponse<EmployeeCostLookup[]>>(
    `${base(quoteId)}/sections/${sectionId}/employees`
  )
  return unwrapApi(response)
}

export async function setSectionDepartments(
  quoteId: number,
  sectionId: number,
  departmentIds: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/sections/${sectionId}/departments`,
    { departmentIds }
  )
  unwrapApi(response)
}

// ── Sezioni costo ──────────────────────────────────────────

export interface AddCostSectionInput {
  templateId: number | null
  name: string
  sectionType: string
  groupName: string
  sortOrder: number
  isEnabled: boolean
}

export async function addCostSection(
  quoteId: number,
  input: AddCostSectionInput
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(quoteId)}/sections`, input)
  return unwrapApi(response)
}

export async function updateCostSectionField(
  quoteId: number,
  id: number,
  field: string,
  value: string | null
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `${base(quoteId)}/sections/${id}/field`,
    { field, value }
  )
  unwrapApi(response)
}

export async function deleteCostSection(quoteId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${base(quoteId)}/sections/${id}`)
  unwrapApi(response)
}

// ── Risorse ────────────────────────────────────────────────

export async function addResource(
  quoteId: number,
  req: ProjectCostResourceSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(quoteId)}/resources`, req)
  return unwrapApi(response)
}

export async function updateResource(
  quoteId: number,
  id: number,
  req: ProjectCostResourceSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`${base(quoteId)}/resources/${id}`, req)
  unwrapApi(response)
}

export async function deleteResource(quoteId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${base(quoteId)}/resources/${id}`)
  unwrapApi(response)
}

// ── Sezioni materiali ──────────────────────────────────────

export interface AddMaterialSectionInput {
  categoryId: number | null
  name: string
  markupValue: number
  commissionMarkup: number
  sortOrder: number
  isEnabled: boolean
}

export async function addMaterialSection(
  quoteId: number,
  input: AddMaterialSectionInput
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(quoteId)}/material-sections`,
    input
  )
  return unwrapApi(response)
}

export async function updateMaterialSectionField(
  quoteId: number,
  id: number,
  field: string,
  value: string | null
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `${base(quoteId)}/material-sections/${id}/field`,
    { field, value }
  )
  unwrapApi(response)
}

export async function deleteMaterialSection(quoteId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${base(quoteId)}/material-sections/${id}`
  )
  unwrapApi(response)
}

// ── Righe materiali ────────────────────────────────────────

export async function addMaterialItem(
  quoteId: number,
  req: ProjectMaterialItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(quoteId)}/material-items`, req)
  return unwrapApi(response)
}

export async function addMaterialVariant(
  quoteId: number,
  parentId: number,
  req: ProjectMaterialItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(quoteId)}/material-items/${parentId}/variant`,
    req
  )
  return unwrapApi(response)
}

export async function updateMaterialItem(
  quoteId: number,
  id: number,
  req: ProjectMaterialItemSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}`,
    req
  )
  unwrapApi(response)
}

export async function deleteMaterialItem(quoteId: number, id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}`
  )
  unwrapApi(response)
}

export async function toggleMaterialItemActive(
  quoteId: number,
  id: number,
  isActive: boolean
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}/toggle-active`,
    { isActive }
  )
  unwrapApi(response)
}

export async function cloneMaterialItem(quoteId: number, id: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(quoteId)}/material-items/${id}/clone`
  )
  return unwrapApi(response)
}

export async function refreshMaterialFromCatalog(quoteId: number, id: number): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}/refresh-from-catalog`
  )
  unwrapApi(response)
}

export async function pushMaterialToCatalog(quoteId: number, id: number): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}/push-to-catalog`
  )
  unwrapApi(response)
}

// ── Pricing + distribuzione ────────────────────────────────

export async function updatePricing(quoteId: number, dto: ProjectPricingDto): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(`${base(quoteId)}/pricing`, dto)
  unwrapApi(response)
}

export async function fetchPricingDistribution(
  quoteId: number
): Promise<PricingDistributionRow[]> {
  const response = await apiGet<ApiResponse<PricingDistributionRow[]>>(
    `${base(quoteId)}/pricing-distribution`
  )
  return unwrapApi(response)
}

export async function generatePricingDistribution(quoteId: number): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `${base(quoteId)}/pricing-distribution/generate`
  )
  unwrapApi(response)
}

export async function updatePricingDistribution(
  quoteId: number,
  id: number,
  row: PricingDistributionRow
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/pricing-distribution/${id}`,
    row
  )
  unwrapApi(response)
}

export async function rebalancePricingDistribution(
  quoteId: number,
  req: RebalanceRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/pricing-distribution/rebalance`,
    req
  )
  unwrapApi(response)
}

export async function updateSectionDistribution(
  quoteId: number,
  id: number,
  dto: SectionDistributionDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/sections/${id}/distribution`,
    dto
  )
  unwrapApi(response)
}

export async function updateMaterialItemDistribution(
  quoteId: number,
  id: number,
  dto: SectionDistributionDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/material-items/${id}/distribution`,
    dto
  )
  unwrapApi(response)
}

export async function saveDistributionsBatch(
  quoteId: number,
  req: BatchDistributionRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${base(quoteId)}/distributions/batch`,
    req
  )
  unwrapApi(response)
}
