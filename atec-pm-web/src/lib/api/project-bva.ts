import { apiGet, apiPatch, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, BudgetVsActualData } from "@/lib/api/types"

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
