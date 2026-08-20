import { apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpControlReportRow,
  DdpControlSummaryEntry,
  DdpDeliveriesDay,
  DdpProjectDetail,
  DdpProjectSummary,
  DdpUpdatedItem,
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

/**
 * Elenco delle DDP aggiornate negli ultimi N giorni **da altri** e non ancora aperte
 * da chi chiede (#113, #114). È la sorgente della card «DDP Commesse» in Dashboard.
 */
export async function fetchDdpUpdatedList(days: number = 7): Promise<DdpUpdatedItem[]> {
  const response = await apiGet<ApiResponse<DdpUpdatedItem[]>>(
    `/api/ddp-manager/updated-list?days=${days}`
  )
  return unwrapApi(response)
}

/**
 * Presa visione della DDP di una commessa (#114): chi la apre se la toglie dal proprio
 * elenco in Dashboard. Personale, e revocata da sé se un collega la tocca ancora.
 */
export async function markDdpSeen(projectId: number, type: string): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/ddp-manager/${projectId}/seen?type=${encodeURIComponent(type)}`
  )
  return unwrapApi(response)
}
