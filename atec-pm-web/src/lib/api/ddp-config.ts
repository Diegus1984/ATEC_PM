import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpAggregation,
  DdpAggregationSaveRequest,
  DdpDestinationItem,
  DdpDestinationSaveRequest,
  DdpStatusItem,
  DdpStatusSaveRequest,
  DdpStatusTransitionItem,
  DdpTreatmentItem,
  DdpTreatmentSaveRequest,
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

/** Matrice degli avanzamenti di stato (v7): righe governate della finestra opzioni. */
export async function fetchDdpStatusTransitions(): Promise<
  DdpStatusTransitionItem[]
> {
  const response = await apiGet<ApiResponse<DdpStatusTransitionItem[]>>(
    "/api/ddp-statuses/transitions"
  )
  return unwrapApi(response)
}

/** Salvataggio integrale della matrice transizioni (editor in Conf. DDP). */
export async function saveDdpStatusTransitions(
  rows: DdpStatusTransitionItem[]
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(
    "/api/ddp-statuses/transitions",
    rows
  )
  return unwrapApi(response)
}

/**
 * Indice della matrice per stato corrente (chiavi UPPER) del tipo di distinta
 * indicato ("COMMERCIAL" | "OFFICINA"). La chiave "INIZIO" è la finestra di
 * partenza delle righe senza stato. Uno stato assente dall'indice non è
 * governato → la finestra opzioni resta completa.
 */
export function buildDdpTransitionMap(
  rows: DdpStatusTransitionItem[],
  ddpType: "COMMERCIAL" | "OFFICINA"
): Record<string, string[]> {
  const map: Record<string, string[]> = {}
  for (const row of rows) {
    if (row.ddpType.toUpperCase() !== ddpType) continue
    map[row.fromKey.toUpperCase()] = row.toKeys.map((k) => k.toUpperCase())
  }
  return map
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

export async function fetchDdpTreatments(): Promise<DdpTreatmentItem[]> {
  const response = await apiGet<ApiResponse<DdpTreatmentItem[]>>(
    "/api/ddp-treatments"
  )
  return unwrapApi(response)
}

/**
 * Trattamenti selezionabili in DDP Officina.
 * Usa lo stesso endpoint di Config. DDP (GetAll) e filtra gli attivi in client:
 * così non dipendiamo da /active (che in alcuni ambienti torna vuoto/errore
 * mentre l'elenco in Config è popolato correttamente).
 */
export async function fetchActiveDdpTreatments(): Promise<DdpTreatmentItem[]> {
  const all = await fetchDdpTreatments()
  return all.filter((item) => {
    const active =
      item.isActive ??
      (item as unknown as { IsActive?: boolean }).IsActive ??
      true
    return active !== false
  })
}

export async function createDdpTreatment(
  request: DdpTreatmentSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/ddp-treatments",
    request
  )
  return unwrapApi(response)
}

export async function updateDdpTreatment(
  id: number,
  request: DdpTreatmentSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/ddp-treatments/${id}`,
    { ...request, id }
  )
  return unwrapApi(response)
}

export async function deleteDdpTreatment(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/ddp-treatments/${id}`
  )
  return unwrapApi(response)
}
