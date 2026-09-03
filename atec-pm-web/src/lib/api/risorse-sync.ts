import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  RisorseSyncLogEntry,
  RisorseSyncSaluteDto,
  RisorseSyncSettingsDto,
  RisorseSyncStatusDto,
  SyncStatusDto,
} from "@/lib/api/types"

// Layer API della sincronizzazione con ATEC Risorse (VPS): impostazioni,
// prova di collegamento, stato e giro manuale. Sotto `/api/resource-planner`
// come il digest e il resto del planner.

const BASE = "/api/resource-planner/sync"

export async function fetchSyncSettings(): Promise<RisorseSyncSettingsDto> {
  const response = await apiGet<ApiResponse<RisorseSyncSettingsDto>>(`${BASE}/settings`)
  return unwrapApi(response)
}

/** Salva le impostazioni. `password` vuota o assente = il server tiene quella salvata. */
export async function saveSyncSettings(
  settings: RisorseSyncSettingsDto
): Promise<boolean> {
  const response = await apiPut<ApiResponse<boolean>>(`${BASE}/settings`, settings)
  return unwrapApi(response)
}

/** Prova il collegamento col VPS: se non risponde arriva un ApiError col messaggio. */
export async function testSync(): Promise<SyncStatusDto> {
  const response = await apiPost<ApiResponse<SyncStatusDto>>(`${BASE}/test`)
  return unwrapApi(response)
}

export async function fetchSyncStatus(): Promise<RisorseSyncStatusDto> {
  const response = await apiGet<ApiResponse<RisorseSyncStatusDto>>(`${BASE}/status`)
  return unwrapApi(response)
}

/** La salute del collegamento per l'avviso nel planner (#147): chiave della pagina, non admin. */
export async function fetchSyncSalute(): Promise<RisorseSyncSaluteDto> {
  const response = await apiGet<ApiResponse<RisorseSyncSaluteDto>>(`${BASE}/salute`)
  return unwrapApi(response)
}

/** Lancia subito un giro di sincronizzazione e ne restituisce l'esito. */
export async function runSyncNow(): Promise<RisorseSyncLogEntry> {
  const response = await apiPost<ApiResponse<RisorseSyncLogEntry>>(`${BASE}/run-now`)
  return unwrapApi(response)
}
