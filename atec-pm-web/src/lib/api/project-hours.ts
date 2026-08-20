import { apiGet, apiPatch, apiPost, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  ProjectHourRow,
  ProjectHoursSummary,
} from "@/lib/api/types"

/**
 * Pagina «Ore Commessa» (segnalazione #39). Endpoint riservati al PM dalla feature
 * `nav.ore_commessa`: un tecnico non li vede nemmeno chiamandoli a mano.
 */
const base = (projectId: number) => `/api/projects/${projectId}/hours`

/** Tutte le imputazioni della commessa, comprese quelle già su Extra Lavoro. */
export async function fetchProjectHours(
  projectId: number
): Promise<ProjectHourRow[]> {
  const response = await apiGet<ApiResponse<ProjectHourRow[]>>(base(projectId))
  return unwrapApi(response)
}

/**
 * Sposta righe su «Extra Lavoro»: da quel momento non pesano sui costi della commessa,
 * finché non le si rimette dentro dall'interruttore della pagina Extra Lavoro.
 */
export async function moveToExtraWork(
  projectId: number,
  entryIds: number[]
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(projectId)}/extra-work`,
    { entryIds }
  )
  return unwrapApi(response)
}

/** Riporta le righe nella contabilità normale della commessa. */
export async function backToProjectHours(
  projectId: number,
  entryIds: number[]
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${base(projectId)}/extra-work/back`,
    { entryIds }
  )
  return unwrapApi(response)
}

/** L'interruttore «queste ore contano nella commessa» sulla singola riga di Extra Lavoro. */
export async function setExtraWorkCounts(
  projectId: number,
  entryId: number,
  counts: boolean
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `${base(projectId)}/extra-work/${entryId}/counts`,
    counts
  )
  unwrapApi(response)
}

// ── Pagina cross-commessa (#109) ───────────────────────────────────────────

/** Le commesse con ore scaricate: una card per commessa. */
export async function fetchProjectHoursSummary(
  includeClosed: boolean
): Promise<ProjectHoursSummary[]> {
  const response = await apiGet<ApiResponse<ProjectHoursSummary[]>>(
    `/api/project-hours/summary?includeClosed=${includeClosed}`
  )
  return unwrapApi(response)
}

/**
 * «Verifica effettuata» (#109): il PM dichiara di aver guardato le ore arrivate finora
 * su questa commessa. Spegne il rosso della card e il pallino del menu.
 */
export async function verifyProjectHours(projectId: number): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(`${base(projectId)}/verify`, {})
  if (!response.success) throw new Error(response.message || "Verifica non riuscita")
  return response.message || "Ore verificate"
}

/** Persone con ore da verificare, su tutte le commesse: il pallino del menu. */
export async function fetchProjectHoursPendingCount(): Promise<number> {
  const response = await apiGet<ApiResponse<number>>("/api/project-hours/pending-count")
  return unwrapApi(response)
}
