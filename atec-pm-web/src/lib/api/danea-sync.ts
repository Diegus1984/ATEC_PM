import { apiGet, apiPost } from "@/lib/api/client"

// Sync manuale col Danea configurato (DaneaSync:EftFilePath = archivio Atec_PM).
// NB: questi endpoint NON usano la busta ApiResponse (payload piatto).

export interface DaneaSyncStatus {
  isSyncing: boolean
  lastSync: string | null
  lastError: string | null
  progress: string | null
  suppliers: number
  customers: number
  articles: number
}

export async function fetchDaneaSyncStatus(): Promise<DaneaSyncStatus> {
  return apiGet<DaneaSyncStatus>("/api/danea-sync/status")
}

/** Avvia il sync (asincrono lato server); 409 se già in corso. */
export async function runDaneaSync(): Promise<void> {
  await apiPost<{ message: string }>("/api/danea-sync/run")
}
