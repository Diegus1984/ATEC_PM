import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, CashFlowData } from "@/lib/api/types"

export interface CashFlowInitRequest {
  paymentAmount: number
  monthCount: number
  startDate?: string | null
}

export interface CashFlowCategorySaveRequest {
  name: string
  totalAmount: number
  notes: string
}

export interface CashFlowDataSaveRequest {
  dataType: string
  refId: number
  monthNumber: number
  numValue: number
  dateValue?: string | null
}

export async function fetchCashFlow(projectId: number): Promise<CashFlowData> {
  const r = await apiGet<ApiResponse<CashFlowData>>(
    `/api/projects/${projectId}/cashflow`
  )
  return unwrapApi(r)
}

export async function initCashFlow(
  projectId: number,
  req: CashFlowInitRequest
): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>(
    `/api/projects/${projectId}/cashflow/init`,
    req
  )
  unwrapApi(r)
}

export async function updateCashFlowHeader(
  projectId: number,
  req: CashFlowInitRequest
): Promise<void> {
  const r = await apiPut<ApiResponse<boolean>>(
    `/api/projects/${projectId}/cashflow/header`,
    req
  )
  unwrapApi(r)
}

export async function saveCashFlowData(
  projectId: number,
  req: CashFlowDataSaveRequest
): Promise<void> {
  const r = await apiPut<ApiResponse<boolean>>(
    `/api/projects/${projectId}/cashflow/data`,
    req
  )
  unwrapApi(r)
}

export async function addCashFlowCategory(
  projectId: number,
  req: CashFlowCategorySaveRequest
): Promise<number> {
  const r = await apiPost<ApiResponse<number>>(
    `/api/projects/${projectId}/cashflow/categories`,
    req
  )
  return unwrapApi(r)
}

export async function updateCashFlowCategory(
  projectId: number,
  catId: number,
  req: CashFlowCategorySaveRequest
): Promise<void> {
  const r = await apiPut<ApiResponse<boolean>>(
    `/api/projects/${projectId}/cashflow/categories/${catId}`,
    req
  )
  unwrapApi(r)
}

export async function deleteCashFlowCategory(
  projectId: number,
  catId: number
): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(
    `/api/projects/${projectId}/cashflow/categories/${catId}`
  )
  unwrapApi(r)
}
