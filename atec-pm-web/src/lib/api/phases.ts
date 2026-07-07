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

export async function linkPhaseToSection(
  phaseId: number,
  sectionId: number
): Promise<void> {
  await patchPhaseTemplateField(phaseId, {
    field: "cost_section_template_id",
    value: String(sectionId),
  })
}

export async function unlinkPhaseFromSection(phaseId: number): Promise<void> {
  await patchPhaseTemplateField(phaseId, {
    field: "cost_section_template_id",
    value: null,
  })
}

export async function reorderSectionPhases(
  sectionId: number,
  phases: PhaseTemplateDto[]
): Promise<void> {
  const sectionPhases = phases
    .filter((phase) => phase.costSectionTemplateId === sectionId)
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder)

  for (let index = 0; index < sectionPhases.length; index++) {
    const newSort = index + 1
    const phase = sectionPhases[index]
    if (phase.sortOrder !== newSort) {
      await patchPhaseTemplateField(phase.id, {
        field: "sort_order",
        value: String(newSort),
      })
    }
  }
}
