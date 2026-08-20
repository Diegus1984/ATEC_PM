import {
  apiDelete,
  apiGet,
  apiPatch,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  BudgetVsActualData,
  ProjectCalcSheetDto,
  ProjectCalcSheetSaveRequest,
  ProjectOrderLineSaveRequest,
} from "@/lib/api/types"

/**
 * Preventivo vs Consuntivo di una commessa: confronto a 3 colonne
 * (preventivato dal costing / assegnato dalle fasi / consuntivo dal timesheet)
 * + materiali + conto economico. Endpoint gated PM/ADMIN (feature `data.budget`):
 * con ruolo insufficiente il server risponde 403 e `apiGet` lancia un ApiError.
 */
export async function fetchBudgetVsActual(
  projectId: number
): Promise<BudgetVsActualData> {
  const response = await apiGet<ApiResponse<BudgetVsActualData>>(
    `/api/projects/${projectId}/budget-vs-actual`
  )
  return unwrapApi(response)
}

/** Aggiorna l'Order Price (ricavo) della commessa. Body: decimal grezzo. */
export async function updateProjectRevenue(
  projectId: number,
  value: number
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/projects/${projectId}/revenue`,
    value
  )
  unwrapApi(response)
}

/** Aggiorna il costo trasferta a consuntivo della commessa. Body: decimal grezzo. */
export async function updateActualTravelCost(
  projectId: number,
  value: number
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/projects/${projectId}/budget-vs-actual/actual-travel-cost`,
    value
  )
  unwrapApi(response)
}

// `updateSaleTotal` è stata rimossa il 06/08/2026 (segnalazione #34): il «Totale Costi di
// Vendita» lo calcola il server dalle sezioni di costo e non si digita più. La rotta
// PATCH .../sale-total esiste ancora lato server ma risponde con un errore parlante, così
// un client vecchio rimasto in cache non scrive in silenzio un campo che nessuno legge.

/**
 * «Prezzo offerta finale» imputato a mano (segnalazione #35). `null` torna al valore
 * calcolato dalla Scheda Prezzi. È solo il numero da mostrare: non ricalcola niente a valle.
 */
export async function updateFinalPriceOverride(
  projectId: number,
  value: number | null
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/projects/${projectId}/budget-vs-actual/final-price-override`,
    value
  )
  unwrapApi(response)
}

/**
 * Conferma della finestra di calcolo: riscrive TUTTO il foglio in una PUT sola, con i
 * valori definitivi. Niente commit per campo — è quello che evita le due perdite di dati
 * viste sulle griglie inline del blocco 4. `rowVersion` = concorrenza sull'intero foglio.
 */
export async function saveProjectCalcSheet(
  projectId: number,
  calcKey: string,
  body: ProjectCalcSheetSaveRequest
): Promise<ProjectCalcSheetDto> {
  const response = await apiPut<ApiResponse<ProjectCalcSheetDto>>(
    `/api/projects/${projectId}/budget-vs-actual/calc/${calcKey}`,
    body
  )
  return unwrapApi(response)
}

const ORDER_LINES = (projectId: number) =>
  `/api/projects/${projectId}/budget-vs-actual/order-lines`

/** Aggiunge una riga ordine (in coda, o sotto `afterLineId`). Ritorna l'id nuovo. */
export async function createOrderLine(
  projectId: number,
  body: ProjectOrderLineSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(ORDER_LINES(projectId), body)
  return unwrapApi(response)
}

/** Modifica una riga ordine. `rowVersion` = concorrenza ottimistica. */
export async function updateOrderLine(
  projectId: number,
  lineId: number,
  body: ProjectOrderLineSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `${ORDER_LINES(projectId)}/${lineId}`,
    body
  )
  unwrapApi(response)
}

/** Elimina una riga ordine. Il server ne rimette una vuota se era l'ultima. */
export async function deleteOrderLine(
  projectId: number,
  lineId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `${ORDER_LINES(projectId)}/${lineId}`
  )
  unwrapApi(response)
}
