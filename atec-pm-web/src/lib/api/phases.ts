import { apiDelete, apiGet, apiPatch, apiPost, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  BulkPhaseRequest,
  FieldUpdateRequest,
  LocalPhaseRequest,
  LookupItem,
  PhaseAssignmentDto,
  PhaseListItem,
  PhaseTemplateDto,
  PhaseTemplateSaveRequest,
} from "@/lib/api/types"

// ── Fasi di commessa + assegnazioni tecnici ────────────────────────────────

export async function fetchProjectPhases(
  projectId: number
): Promise<PhaseListItem[]> {
  const response = await apiGet<ApiResponse<PhaseListItem[]>>(
    `/api/phases/project/${projectId}`
  )
  return unwrapApi(response)
}

/** Tecnici eleggibili per il reparto della fase (per «aggiungi tecnico»). */
export async function fetchEmployeesByPhase(
  phaseId: number
): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    `/api/employees/by-phase/${phaseId}`
  )
  return unwrapApi(response)
}

export async function addPhaseAssignment(
  phaseId: number,
  request: { employeeId: number; assignRole: string; plannedHours: number }
): Promise<number> {
  const body: Partial<PhaseAssignmentDto> = {
    projectPhaseId: phaseId,
    employeeId: request.employeeId,
    assignRole: request.assignRole,
    plannedHours: request.plannedHours,
  }
  const response = await apiPost<ApiResponse<number>>(
    `/api/phases/${phaseId}/assignments`,
    body
  )
  return unwrapApi(response)
}

export async function updateAssignmentHours(
  assignmentId: number,
  plannedHours: number
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/phases/assignments/${assignmentId}/hours`,
    { plannedHours }
  )
  unwrapApi(response)
}

export async function deletePhaseAssignment(
  assignmentId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/phases/assignments/${assignmentId}`
  )
  unwrapApi(response)
}

/** Importa fasi da template globali nella commessa. */
export async function bulkCreatePhases(
  request: BulkPhaseRequest
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    "/api/phases/bulk",
    request
  )
  unwrapApi(response)
}

/** Crea una fase locale (solo per questa commessa). Ritorna il nuovo id. */
export async function createLocalPhase(
  request: LocalPhaseRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/phases/local",
    request
  )
  return unwrapApi(response)
}

/**
 * Spegne o riaccende una fase di commessa (segnalazione #51).
 * Spenta = sparisce dall'elenco del Bilancio e dalla tendina del Timesheet, ma le ore già
 * imputate **continuano a contare** nei costi: nasconde, non esclude. Sempre reversibile.
 */
export async function setProjectPhaseOff(
  phaseId: number,
  isOff: boolean
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/phases/${phaseId}/off`,
    isOff
  )
  unwrapApi(response)
}

/**
 * Chiude o riapre una fase (#106).
 *
 * «Avanzamento Commessa» conta le fasi in `COMPLETED`, ma nessuna schermata del
 * client scriveva mai quello stato: la percentuale era ferma a 0% su qualunque
 * commessa. Da qui il PM la chiude a mano, che è l'unico modo per sapere se una
 * fase è finita — le ore lavorate non lo dicono.
 */
export async function setProjectPhaseStatus(
  phaseId: number,
  status: "NOT_STARTED" | "IN_PROGRESS" | "COMPLETED"
): Promise<void> {
  const body: FieldUpdateRequest = { field: "status", value: status }
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/phases/${phaseId}/field`,
    body
  )
  unwrapApi(response)
}

/** Rimuove una fase dalla commessa (solo se senza ore registrate). */
export async function deleteProjectPhase(phaseId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/phases/${phaseId}`
  )
  unwrapApi(response)
}

export async function fetchPhaseTemplates(): Promise<PhaseTemplateDto[]> {
  const response = await apiGet<ApiResponse<PhaseTemplateDto[]>>(
    "/api/phases/templates"
  )
  return unwrapApi(response)
}

export async function createPhaseTemplate(
  request: PhaseTemplateSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/phases/templates",
    request
  )
  return unwrapApi(response)
}

export async function patchPhaseTemplateField(
  id: number,
  request: FieldUpdateRequest
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/phases/templates/${id}/field`,
    request
  )
  unwrapApi(response)
}

export async function deletePhaseTemplate(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/phases/templates/${id}`
  )
  unwrapApi(response)
}

// ── Legami fase ↔ sezione di costo ─────────────────────────────────────────
// Una fase dell'anagrafica sta su PIÙ sezioni e in commessa nasce una volta per sezione:
// agganciare non è più «spostare». Ordine e «nasce da sola» sono del legame, non della fase.
// Vedi PIANO-FASI-MULTISEZIONE.md.

/** Aggancia la fase a una sezione in più (non la toglie da quelle in cui è già). */
export async function addPhaseToSection(
  phaseId: number,
  sectionId: number,
  isDefault = false
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/phases/templates/${phaseId}/sections`,
    { sectionId, isDefault }
  )
  unwrapApi(response)
}

/** Toglie la fase da UNA sezione. Le commesse già create non cambiano. */
export async function removePhaseFromSection(
  phaseId: number,
  sectionId: number
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/phases/templates/${phaseId}/sections/${sectionId}`
  )
  unwrapApi(response)
}

export async function updatePhaseSectionLink(
  phaseId: number,
  sectionId: number,
  patch: { sortOrder?: number; isDefault?: boolean }
): Promise<void> {
  const response = await apiPatch<ApiResponse<boolean>>(
    `/api/phases/templates/${phaseId}/sections/${sectionId}`,
    patch
  )
  unwrapApi(response)
}

/** Riscrive l'ordine delle fasi DENTRO una sezione (le altre sezioni non si toccano). */
export async function reorderSectionPhases(
  sectionId: number,
  orderedPhaseIds: number[]
): Promise<void> {
  for (let index = 0; index < orderedPhaseIds.length; index++) {
    await updatePhaseSectionLink(orderedPhaseIds[index], sectionId, {
      sortOrder: index + 1,
    })
  }
}
