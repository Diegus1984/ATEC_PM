import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpAggregation,
  DdpAggregationSaveRequest,
  DdpDestinationItem,
  DdpDestinationSaveRequest,
  DdpStatusItem,
  DdpStatusSaveRequest,
} from "@/lib/api/types"

export async function fetchDdpDestinations(): Promise<DdpDestinationItem[]> {
  const response = await apiGet<ApiResponse<DdpDestinationItem[]>>(
    "/api/ddp-destinations"
  )
  return unwrapApi(response)
}

/** Solo le destinazioni attive: alimenta le combo di selezione sulle righe DDP. */
export async function fetchActiveDdpDestinations(): Promise<
  DdpDestinationItem[]
> {
  const response = await apiGet<ApiResponse<DdpDestinationItem[]>>(
    "/api/ddp-destinations/active"
  )
  return unwrapApi(response)
}

export async function createDdpDestination(
  request: DdpDestinationSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/ddp-destinations",
    request
  )
  return unwrapApi(response)
}

export async function updateDdpDestination(
  id: number,
  request: DdpDestinationSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/ddp-destinations/${id}`,
    { ...request, id }
  )
  return unwrapApi(response)
}

export async function deleteDdpDestination(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/ddp-destinations/${id}`
  )
  return unwrapApi(response)
}

export async function fetchDdpStatuses(): Promise<DdpStatusItem[]> {
  const response = await apiGet<ApiResponse<DdpStatusItem[]>>("/api/ddp-statuses")
  return unwrapApi(response)
}

export async function createDdpStatus(
  request: DdpStatusSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/ddp-statuses", request)
  return unwrapApi(response)
}

export async function updateDdpStatus(
  id: number,
  request: DdpStatusSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/ddp-statuses/${id}`,
    { ...request, id }
  )
  return unwrapApi(response)
}

export async function fetchDdpAggregations(): Promise<DdpAggregation[]> {
  const response = await apiGet<ApiResponse<DdpAggregation[]>>(
    "/api/ddp-aggregations"
  )
  return unwrapApi(response)
}

export async function updateDdpAggregation(
  id: number,
  request: DdpAggregationSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/ddp-aggregations/${id}`,
    { ...request, id }
  )
  return unwrapApi(response)
}
