import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  ChecklistAssignRequest,
  ChecklistBoard,
  ChecklistGroupSaveRequest,
  ChecklistInboxItem,
  ChecklistInboxSaveRequest,
  ChecklistItem,
  ChecklistItemSaveRequest,
  ChecklistProjectLookup,
} from "@/lib/api/types"

// ── Board (pagina PM aggregata) e attività per commessa ──

export async function fetchChecklistBoard(): Promise<ChecklistBoard> {
  const response = await apiGet<ApiResponse<ChecklistBoard>>("/api/checklist/board")
  return unwrapApi(response)
}

export async function fetchProjectChecklist(
  projectId: number
): Promise<ChecklistItem[]> {
  const response = await apiGet<ApiResponse<ChecklistItem[]>>(
    `/api/checklist/project/${projectId}`
  )
  return unwrapApi(response)
}

// ── Attività (item) ─────────────────────────────────────

export async function addChecklistItem(
  request: ChecklistItemSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/checklist/items",
    request
  )
  return unwrapApi(response)
}

export async function updateChecklistItem(
  itemId: number,
  request: ChecklistItemSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/checklist/items/${itemId}`,
    request
  )
  unwrapApi(response)
}

export async function deleteChecklistItem(itemId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/checklist/items/${itemId}`
  )
  unwrapApi(response)
}

// ── Gruppi generici (DIREZIONE, VARIE, custom) ──────────

export async function addChecklistGroup(
  request: ChecklistGroupSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/checklist/groups",
    request
  )
  return unwrapApi(response)
}

export async function updateChecklistGroup(
  groupId: number,
  request: ChecklistGroupSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/checklist/groups/${groupId}`,
    request
  )
  unwrapApi(response)
}

export async function deleteChecklistGroup(groupId: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/checklist/groups/${groupId}`
  )
  unwrapApi(response)
}

// ── Inbox "Fissa attività" (staging personale) ──────────

export async function fetchChecklistInbox(): Promise<ChecklistInboxItem[]> {
  const response = await apiGet<ApiResponse<ChecklistInboxItem[]>>(
    "/api/checklist/inbox"
  )
  return unwrapApi(response)
}

export async function addChecklistInbox(
  request: ChecklistInboxSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/checklist/inbox",
    request
  )
  return unwrapApi(response)
}

export async function updateChecklistInbox(
  id: number,
  request: ChecklistInboxSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/checklist/inbox/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteChecklistInbox(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/checklist/inbox/${id}`
  )
  unwrapApi(response)
}

/** Assegna la nota a una commessa o a un gruppo; ritorna l'id della nuova attività. */
export async function assignChecklistInbox(
  id: number,
  request: ChecklistAssignRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/checklist/inbox/${id}/assign`,
    request
  )
  return unwrapApi(response)
}

// ── Lookup ──────────────────────────────────────────────

export async function fetchChecklistProjects(): Promise<
  ChecklistProjectLookup[]
> {
  const response = await apiGet<ApiResponse<ChecklistProjectLookup[]>>(
    "/api/checklist/lookups/projects"
  )
  return unwrapApi(response)
}
