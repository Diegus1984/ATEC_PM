import { apiGet, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpControlReportRow,
  DdpControlSummaryEntry,
  DdpDeliveriesDay,
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

/** Contatori dei report di controllo cross-commessa (una entry per report). */
export async function fetchDdpControlSummary(): Promise<DdpControlSummaryEntry[]> {
  const response = await apiGet<ApiResponse<DdpControlSummaryEntry[]>>(
    "/api/ddp-manager/control-summary"
  )
  return unwrapApi(response)
}

/** Righe di un report di controllo (rit|ver|ro|do|io|dc) per tipo distinta. */
export async function fetchDdpControlReport(
  report: string,
  type: string
): Promise<DdpControlReportRow[]> {
  const response = await apiGet<ApiResponse<DdpControlReportRow[]>>(
    `/api/ddp-manager/control-report?report=${encodeURIComponent(report)}&type=${encodeURIComponent(type)}`
  )
  return unwrapApi(response)
}

/** Consegne previste per giorno su tutte le commesse (Analisi Consegne). */
export async function fetchDdpDeliveriesByDay(): Promise<DdpDeliveriesDay[]> {
  const response = await apiGet<ApiResponse<DdpDeliveriesDay[]>>(
    "/api/ddp-manager/deliveries-by-day"
  )
  return unwrapApi(response)
}
