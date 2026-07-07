import { apiDelete, apiGet, apiPatch, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  CostSectionGroupDto,
  CostSectionGroupSaveRequest,
  CostSectionTemplateDto,
  CostSectionTemplateSaveRequest,
  DepartmentDto,
  FieldUpdateRequest,
} from "@/lib/api/types"

export async function fetchCostSectionGroups(): Promise<CostSectionGroupDto[]> {
  const response = await apiGet<ApiResponse<CostSectionGroupDto[]>>(
    "/api/cost-sections/groups"
  )
  return unwrapApi(response)
}

export async function createCostSectionGroup(
  request: CostSectionGroupSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/cost-sections/groups",
    request
  )
  return unwrapApi(response)
}

export async function updateCostSectionGroup(
  id: number,
  request: CostSectionGroupSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/cost-sections/groups/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteCostSectionGroup(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/cost-sections/groups/${id}`
  )
  unwrapApi(response)
}

export async function fetchCostSectionTemplates(): Promise<
  CostSectionTemplateDto[]
> {
  const response = await apiGet<ApiResponse<CostSectionTemplateDto[]>>(
    "/api/cost-sections/templates"
  )
  return unwrapApi(response)
}

export async function createCostSectionTemplate(
  request: CostSectionTemplateSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/cost-sections/templates",
    request
  )
  return unwrapApi(response)
}

export async function updateCostSectionTemplate(
  id: number,
  request: CostSectionTemplateSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/cost-sections/templates/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteCostSectionTemplate(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/cost-sections/templates/${id}`
  )
  unwrapApi(response)
}

export async function patchCostSectionGroupField(
  id: number,
  request: FieldUpdateRequest
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `/api/cost-sections/groups/${id}/field`,
    request
  )
  unwrapApi(response)
}

export async function patchCostSectionTemplateField(
  id: number,
  request: FieldUpdateRequest
): Promise<void> {
  const response = await apiPatch<ApiResponse<string>>(
    `/api/cost-sections/templates/${id}/field`,
    request
  )
  unwrapApi(response)
}

export async function updateSectionDepartments(
  sectionId: number,
  departmentIds: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/cost-sections/templates/${sectionId}/departments`,
    { departmentIds }
  )
  unwrapApi(response)
}

export async function fetchDepartments(): Promise<DepartmentDto[]> {
  const response = await apiGet<ApiResponse<DepartmentDto[]>>("/api/departments")
  return unwrapApi(response)
}
