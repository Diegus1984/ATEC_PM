// ── Trasferta a righe della sezione di preventivo (segnalazione #33) ───────
//
// Perché un file a parte e non dentro `project-costing.ts`: qui non si compila il
// preventivo «classico» (risorse, materiali, prezzi) ma la tabella-trasferta della
// sezione, che ha un modello suo — righe con calcolatrici e concorrenza per riga.
// Gli endpoint stanno comunque sotto `/api/projects/{id}/costing/*`, come da contratto.
//
// Differenza di modello rispetto alla Gestione Trasferta (`travel.ts`): lì il costo di
// riga è UN numero solo, qui è spaccato in due — `markableCost` (alloggio, vitto, auto,
// treno/aereo), che prende il K di ricarico della commessa, e `allowanceCost`, che il K
// non lo prende MAI (decisione D33-A).

import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  ProjectCalcSheetDto,
  ProjectCalcSheetSaveRequest,
} from "@/lib/api/types"

/**
 * Le tre calcolatrici della riga. Sono le stesse della Trasferta MENO «ore»: le ore del
 * preventivo stanno già nella griglia «Risorse pianificate», qui si contano solo le spese.
 */
export const COST_TRAVEL_CALC_KINDS = ["vitto", "indennita", "auto"] as const
export type CostTravelCalcKind = (typeof COST_TRAVEL_CALC_KINDS)[number]

/** Riga della tabella trasferta di una sezione di costo (`project_cost_travel_rows`). */
export interface ProjectCostTravelRow {
  id: number
  sectionId: number
  /** Risorsa pianificata collegata; null quando il nominativo è testo libero. */
  resourceId: number | null
  /** Il nome resta scritto sulla riga: si legge anche se la risorsa viene eliminata. */
  personName: string
  nights: number | null
  nightPrice: number | null
  /** Derivata dal server: Notti × Prezzo. null (→ «—») se manca uno dei due. */
  lodgingCost: number | null
  /** Totale della calcolatrice `preventivo.vitto:{id}`. */
  mealCost: number | null
  /** Totale della calcolatrice `preventivo.indennita:{id}`. Non prende mai il K. */
  allowanceCost: number | null
  /** Totale della calcolatrice `preventivo.auto:{id}`. */
  carCost: number | null
  transportCost: number | null
  /** Alloggio + vitto + auto + treno/aereo: la parte soggetta al K di trasferta. */
  markableCost: number
  /** Costo secco della riga = ricaricabili + indennità (senza K). */
  travelCost: number
  sortOrder: number
  rowVersion: number
}

/**
 * Solo i campi che si digitano nella griglia: vitto, indennità e auto arrivano dalle
 * calcolatrici (PUT dedicata) e alloggio è derivato, quindi non si mandano da qui.
 */
export interface ProjectCostTravelRowSaveRequest {
  resourceId: number | null
  personName: string
  nights: number | null
  nightPrice: number | null
  transportCost: number | null
  sortOrder: number
  /** Concorrenza ottimistica: null solo alla creazione. */
  rowVersion: number | null
}

function base(projectId: number): string {
  return `/api/projects/${projectId}/costing`
}

export async function fetchCostTravelRows(
  projectId: number,
  sectionId: number
): Promise<ProjectCostTravelRow[]> {
  const response = await apiGet<ApiResponse<ProjectCostTravelRow[]>>(
    `${base(projectId)}/sections/${sectionId}/travel-rows`
  )
  return unwrapApi(response)
}

export async function createCostTravelRow(
  projectId: number,
  sectionId: number,
  body: ProjectCostTravelRowSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(projectId)}/sections/${sectionId}/travel-rows`,
    body
  )
  return unwrapApi(response)
}

/**
 * Salva TUTTI i campi digitabili della riga in una volta sola, all'uscita dalla riga.
 * Mai un salvataggio per campo: nella griglia della Trasferta il secondo e il terzo
 * commit trovavano il primo ancora in volo e venivano scartati in silenzio.
 */
export async function updateCostTravelRow(
  projectId: number,
  rowId: number,
  body: ProjectCostTravelRowSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${base(projectId)}/travel-rows/${rowId}`,
    body
  )
  unwrapApi(response)
}

export async function deleteCostTravelRow(
  projectId: number,
  rowId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `${base(projectId)}/travel-rows/${rowId}`
  )
  unwrapApi(response)
}

/** Dettaglio di una calcolatrice (foglio `preventivo.{kind}:{rowId}`). */
export async function fetchCostTravelCalc(
  projectId: number,
  rowId: number,
  kind: CostTravelCalcKind
): Promise<ProjectCalcSheetDto> {
  const response = await apiGet<ApiResponse<ProjectCalcSheetDto>>(
    `${base(projectId)}/travel-rows/${rowId}/calc/${kind}`
  )
  return unwrapApi(response)
}

/**
 * Conferma della calcolatrice: il server salva il dettaglio E scrive il totale nella
 * colonna della riga. Confermare un foglio vuoto riporta la colonna a NULL, cioè «—»:
 * è voluto, «—» vuol dire «non calcolato», non «vale zero».
 */
export async function saveCostTravelCalc(
  projectId: number,
  rowId: number,
  kind: CostTravelCalcKind,
  body: ProjectCalcSheetSaveRequest
): Promise<ProjectCalcSheetDto> {
  const response = await apiPut<ApiResponse<ProjectCalcSheetDto>>(
    `${base(projectId)}/travel-rows/${rowId}/calc/${kind}`,
    body
  )
  return unwrapApi(response)
}
