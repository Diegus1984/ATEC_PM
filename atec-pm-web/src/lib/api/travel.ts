import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  EmployeeCostLookup,
  ProjectCalcSheetDto,
  ProjectCalcSheetSaveRequest,
  TravelCalcKind,
  TravelPlanDto,
  TravelProjectSummaryDto,
  TravelApplyCostsRequest,
  TravelRowSaveRequest,
  TravelStepSaveRequest,
} from "@/lib/api/types"

const base = (projectId: number) => `/api/projects/${projectId}/travel`

/** Trasferta completa di una commessa: step, righe, riepilogo e totali. */
export async function fetchTravelPlan(projectId: number): Promise<TravelPlanDto> {
  const response = await apiGet<ApiResponse<TravelPlanDto>>(base(projectId))
  return unwrapApi(response)
}

/** Card della pagina: una per commessa, con i 4 KPI. */
export async function fetchTravelSummary(
  includeClosed: boolean
): Promise<TravelProjectSummaryDto[]> {
  const response = await apiGet<ApiResponse<TravelProjectSummaryDto[]>>(
    `/api/travel/summary?includeClosed=${includeClosed}`
  )
  return unwrapApi(response)
}

/** Nominativi con il costo orario di reparto: scegliendo la persona la tariffa si precompila. */
export async function fetchTravelPeople(): Promise<EmployeeCostLookup[]> {
  const response = await apiGet<ApiResponse<EmployeeCostLookup[]>>("/api/travel/people")
  return unwrapApi(response)
}

/**
 * Rilegge il Timesheet e rifà le righe di cantiere (#37/#52). Restituisce il messaggio da
 * mostrare («N giornate di cantiere su M fasi…»). Le righe scritte a mano non si toccano.
 */
export async function rebuildTravelFromTimesheet(projectId: number): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    `${base(projectId)}/rebuild-from-timesheet`,
    {}
  )
  if (!response.success) throw new Error(response.message || "Aggiornamento non riuscito")
  return response.message || "Trasferta aggiornata dal Timesheet"
}

/**
 * «Verifica effettuata» (#102): il PM dichiara di aver guardato lo scarico ore di
 * cantiere arrivato finora. Spegne il rosso della card e il pallino del menu, fino
 * al prossimo scarico. Non tocca nessun dato di trasferta.
 */
export async function verifyTravelScarico(projectId: number): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(`${base(projectId)}/verify`, {})
  if (!response.success) throw new Error(response.message || "Verifica non riuscita")
  return response.message || "Scarico ore verificato"
}

/** Persone con ore di cantiere da verificare, su tutte le commesse: il pallino del menu. */
export async function fetchTravelPendingCount(): Promise<number> {
  const response = await apiGet<ApiResponse<number>>("/api/travel/pending-count")
  return unwrapApi(response)
}

// ── Step ────────────────────────────────────────────────────────

export async function createTravelStep(
  projectId: number,
  body: TravelStepSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${base(projectId)}/steps`, body)
  return unwrapApi(response)
}

export async function updateTravelStep(
  projectId: number,
  stepId: number,
  body: TravelStepSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${base(projectId)}/steps/${stepId}`,
    body
  )
  unwrapApi(response)
}

export async function deleteTravelStep(
  projectId: number,
  stepId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `${base(projectId)}/steps/${stepId}`
  )
  unwrapApi(response)
}

export async function reorderTravelSteps(
  projectId: number,
  ids: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${base(projectId)}/steps/reorder`,
    { ids }
  )
  unwrapApi(response)
}

// ── Righe-persona ───────────────────────────────────────────────

export async function createTravelRow(
  projectId: number,
  stepId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(projectId)}/steps/${stepId}/rows`,
    {}
  )
  return unwrapApi(response)
}

/**
 * Salva TUTTI i campi editabili della riga in una volta sola, all'uscita dalla riga.
 * Non fare un salvataggio per campo: nel blocco 4 il secondo e il terzo commit trovavano
 * il primo ancora in volo e venivano scartati in silenzio.
 */
export async function updateTravelRow(
  projectId: number,
  rowId: number,
  body: TravelRowSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${base(projectId)}/rows/${rowId}`,
    body
  )
  unwrapApi(response)
}

export async function deleteTravelRow(
  projectId: number,
  rowId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `${base(projectId)}/rows/${rowId}`
  )
  unwrapApi(response)
}

export async function reorderTravelRows(
  projectId: number,
  stepId: number,
  ids: number[]
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${base(projectId)}/steps/${stepId}/rows/reorder`,
    { ids }
  )
  unwrapApi(response)
}

/**
 * Aggrega costi (#67): applica i campi Alloggio/Vitto e Altri costi a più righe
 * selezionate. Restituisce quante righe sono state aggiornate.
 */
export async function applyTravelCosts(
  projectId: number,
  body: TravelApplyCostsRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `${base(projectId)}/rows/apply-costs`,
    body
  )
  return unwrapApi(response)
}

// ── Le 4 calcolatrici ───────────────────────────────────────────

export async function fetchTravelRowCalc(
  projectId: number,
  rowId: number,
  kind: TravelCalcKind
): Promise<ProjectCalcSheetDto> {
  const response = await apiGet<ApiResponse<ProjectCalcSheetDto>>(
    `${base(projectId)}/rows/${rowId}/calc/${kind}`
  )
  return unwrapApi(response)
}

/** Conferma: il server salva il dettaglio E scrive il totale nella colonna della riga. */
export async function saveTravelRowCalc(
  projectId: number,
  rowId: number,
  kind: TravelCalcKind,
  body: ProjectCalcSheetSaveRequest
): Promise<ProjectCalcSheetDto> {
  const response = await apiPut<ApiResponse<ProjectCalcSheetDto>>(
    `${base(projectId)}/rows/${rowId}/calc/${kind}`,
    body
  )
  return unwrapApi(response)
}
