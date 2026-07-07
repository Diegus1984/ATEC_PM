import { apiGet, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpProjectDetail,
  DdpProjectSummary,
} from "@/lib/api/types"

/** Riepilogo DDP aggregato per commessa e tipo (COMMERCIAL/OFFICINA). */
export async function fetchDdpSummary(): Promise<DdpProjectSummary[]> {
  const response = await apiGet<ApiResponse<DdpProjectSummary[]>>(
    "/api/ddp-manager/summary"
  )
  return unwrapApi(response)
}

/** Sintesi di una commessa: KPI + ripartizione per stato. */
export async function fetchDdpDetail(
  projectId: number,
  type: string
): Promise<DdpProjectDetail> {
  const response = await apiGet<ApiResponse<DdpProjectDetail>>(
    `/api/ddp-manager/${projectId}?type=${encodeURIComponent(type)}`
  )
  return unwrapApi(response)
}
