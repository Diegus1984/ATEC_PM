import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  SalBundle,
  SalHeaderSaveRequest,
  SalRowSaveRequest,
  SalCondition,
  SalConditionSaveRequest,
  SalEconomics,
  SalProspettoRow,
  SalSapAcconti,
  SalSapAccontiSaveRequest,
  SalSummary,
} from "@/lib/api/types"

/** Recupera il bundle SAL (header + righe) per una commessa. */
export async function fetchSal(projectId: number): Promise<SalBundle> {
  const response = await apiGet<ApiResponse<SalBundle>>(
    `/api/sal?projectId=${projectId}`
  )
  return unwrapApi(response)
}

/** Salva/Aggiorna l'header SAL (cliente, valore). */
export async function saveSalHeader(
  projectId: number,
  request: SalHeaderSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/header?projectId=${projectId}`,
    request
  )
  return unwrapApi(response)
}

/** Crea una nuova riga/step SAL. */
export async function createSalRow(
  projectId: number,
  request: SalRowSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/sal/rows?projectId=${projectId}`,
    request
  )
  return unwrapApi(response)
}

/** Aggiorna una riga/step SAL esistente. */
export async function updateSalRow(
  id: number,
  request: SalRowSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/rows/${id}`,
    request
  )
  return unwrapApi(response)
}

/** Elimina una riga/step SAL (con check di concorrenza su rowVersion). */
export async function deleteSalRow(
  id: number,
  rowVersion: number
): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/rows/${id}?rowVersion=${rowVersion}`
  )
  return unwrapApi(response)
}

/** Riordina le righe SAL per una commessa. */
export async function reorderSalRows(
  projectId: number,
  ids: number[]
): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/sal/rows/reorder?projectId=${projectId}`,
    { ids }
  )
  return unwrapApi(response)
}

/** Precarica il template a 6 step standard. */
export async function seedSalTemplate(projectId: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/sal/project/${projectId}/seed-template`
  )
  return unwrapApi(response)
}

/** Recupera le condizioni di pagamento. */
export async function fetchSalConditions(activeOnly = false): Promise<SalCondition[]> {
  const url = activeOnly ? "/api/sal/conditions/active" : "/api/sal/conditions"
  const response = await apiGet<ApiResponse<SalCondition[]>>(url)
  return unwrapApi(response)
}

/** Crea una nuova condizione di pagamento. */
export async function createSalCondition(
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/sal/conditions",
    request
  )
  return unwrapApi(response)
}

/** Rinomina una condizione di pagamento. */
export async function updateSalCondition(
  id: number,
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/conditions/${id}`,
    request
  )
  return unwrapApi(response)
}

/** Attiva/Disattiva una condizione di pagamento. */
export async function toggleActiveSalCondition(
  id: number,
  active: boolean
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/conditions/${id}/toggle-active?active=${active}`
  )
  return unwrapApi(response)
}

/** Elimina una condizione di pagamento. */
export async function deleteSalCondition(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/conditions/${id}`
  )
  return unwrapApi(response)
}

/** Riordina le condizioni di pagamento. */
export async function reorderSalConditions(ids: number[]): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/conditions/reorder",
    { ids }
  )
  return unwrapApi(response)
}

/** Ripristina le condizioni di pagamento standard. */
export async function resetSalConditions(): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/conditions/reset"
  )
  return unwrapApi(response)
}

// ------------------------------------------------------------------
// Anagrafica Causali Conto SAP (/api/sal/sap-causali) — stesso shape
// e stesso CRUD delle condizioni di pagamento.
// ------------------------------------------------------------------

/** Recupera tutte le causali Conto SAP. */
export async function fetchSapCausali(): Promise<SalCondition[]> {
  const response = await apiGet<ApiResponse<SalCondition[]>>(
    "/api/sal/sap-causali"
  )
  return unwrapApi(response)
}

/** Recupera le sole causali Conto SAP attive. */
export async function fetchSapCausaliActive(): Promise<SalCondition[]> {
  const response = await apiGet<ApiResponse<SalCondition[]>>(
    "/api/sal/sap-causali/active"
  )
  return unwrapApi(response)
}

/** Crea una nuova causale Conto SAP. */
export async function createSapCausale(label: string): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/sal/sap-causali",
    { label }
  )
  return unwrapApi(response)
}

/** Rinomina una causale Conto SAP. */
export async function updateSapCausale(
  id: number,
  label: string
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/sap-causali/${id}`,
    { label }
  )
  return unwrapApi(response)
}

/** Attiva/Disattiva una causale Conto SAP. */
export async function toggleSapCausale(
  id: number,
  active: boolean
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/sap-causali/${id}/toggle-active?active=${active}`
  )
  return unwrapApi(response)
}

/** Elimina una causale Conto SAP. */
export async function deleteSapCausale(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/sap-causali/${id}`
  )
  return unwrapApi(response)
}

/** Riordina le causali Conto SAP. */
export async function reorderSapCausali(ids: number[]): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/sap-causali/reorder",
    { ids }
  )
  return unwrapApi(response)
}

/** Ripristina le causali Conto SAP standard (Acconto, Ricavo). */
export async function resetSapCausali(): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/sap-causali/reset"
  )
  return unwrapApi(response)
}

// ------------------------------------------------------------------
// Anagrafica Stati Pagamento (/api/sal/payment-states) — le voci
// 'Pagata' e 'Parzialmente Pagata' sono di sistema (no rename/delete).
// ------------------------------------------------------------------

/** Recupera tutti gli stati pagamento. */
export async function fetchPaymentStates(): Promise<SalCondition[]> {
  const response = await apiGet<ApiResponse<SalCondition[]>>(
    "/api/sal/payment-states"
  )
  return unwrapApi(response)
}

/** Recupera i soli stati pagamento attivi. */
export async function fetchPaymentStatesActive(): Promise<SalCondition[]> {
  const response = await apiGet<ApiResponse<SalCondition[]>>(
    "/api/sal/payment-states/active"
  )
  return unwrapApi(response)
}

/** Crea un nuovo stato pagamento (colori opzionali). */
export async function createPaymentState(
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/sal/payment-states",
    request
  )
  return unwrapApi(response)
}

/**
 * Aggiorna uno stato pagamento: PUT full-replace di etichetta + colori
 * (colorBg/colorFg null = nessuna tinta). Il rename delle voci di sistema è
 * bloccato dal server, ma i loro COLORI restano modificabili (stessa label).
 */
export async function updatePaymentState(
  id: number,
  request: SalConditionSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/payment-states/${id}`,
    request
  )
  return unwrapApi(response)
}

/** Attiva/Disattiva uno stato pagamento. */
export async function togglePaymentState(
  id: number,
  active: boolean
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/sal/payment-states/${id}/toggle-active?active=${active}`
  )
  return unwrapApi(response)
}

/** Elimina uno stato pagamento (voci di sistema escluse). */
export async function deletePaymentState(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/sal/payment-states/${id}`
  )
  return unwrapApi(response)
}

/** Riordina gli stati pagamento. */
export async function reorderPaymentStates(ids: number[]): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/payment-states/reorder",
    { ids }
  )
  return unwrapApi(response)
}

/** Ripristina gli stati pagamento standard (Pagata, Parzialmente Pagata). */
export async function resetPaymentStates(): Promise<boolean> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/sal/payment-states/reset"
  )
  return unwrapApi(response)
}

/** Recupera il prospetto globale delle ipotesi di fatturazione aperte. */
export async function fetchSalProspetto(): Promise<SalProspettoRow[]> {
  const response = await apiGet<ApiResponse<SalProspettoRow[]>>(
    "/api/sal/prospetto"
  )
  return unwrapApi(response)
}

export async function fetchSalSummary(): Promise<SalSummary[]> {
  const response = await apiGet<ApiResponse<SalSummary[]>>("/api/sal/summary")
  return unwrapApi(response)
}

/** Dati economici SAL globali (Cash Flow / Analisi) — solo PM/ADMIN (403 altrimenti). */
export async function fetchSalEconomics(): Promise<SalEconomics> {
  const response = await apiGet<ApiResponse<SalEconomics>>(
    "/api/sal/economics"
  )
  return unwrapApi(response)
}

/** #131 — le tre tabelle di «SAL / SAP Acconti» (solo chi ha `sal.economics`: 403 altrimenti). */
export async function fetchSalSapAcconti(): Promise<SalSapAcconti> {
  const response = await apiGet<ApiResponse<SalSapAcconti>>(
    "/api/sal/sap-acconti"
  )
  return unwrapApi(response)
}

/** Salva i totali del conto SAP scritti a mano; torna la nuova `rowVersion`. */
export async function saveSalSapAcconti(
  request: SalSapAccontiSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    "/api/sal/sap-acconti",
    request
  )
  return unwrapApi(response)
}
