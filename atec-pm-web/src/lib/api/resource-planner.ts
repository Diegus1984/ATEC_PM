import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  LookupItem,
  ResAssignmentCreateRequest,
  ResAssignmentDto,
  ResAssignmentUpdateRequest,
  ResOtherActivityDto,
  ResOtherActivitySaveRequest,
  ResServiceDto,
  ResServiceSaveRequest,
} from "@/lib/api/types"

// Layer API del modulo Gestione Risorse (`/api/resource-planner`).
// Le mutazioni accettano un `conn` (ConnectionId SignalR del chiamante): il server
// lo usa per NON rinotificare l'autore, che ha già aggiornato la propria vista.

const BASE = "/api/resource-planner"

function withConn(path: string, conn?: string | null): string {
  if (!conn) return path
  const sep = path.includes("?") ? "&" : "?"
  return `${path}${sep}conn=${encodeURIComponent(conn)}`
}

// ── Allocazioni ─────────────────────────────────────────────────

export async function fetchAssignments(
  from?: string,
  to?: string
): Promise<ResAssignmentDto[]> {
  const qs = from && to ? `?from=${from}&to=${to}` : ""
  const response = await apiGet<ApiResponse<ResAssignmentDto[]>>(
    `${BASE}/assignments${qs}`
  )
  return unwrapApi(response)
}

export async function createAssignments(
  request: ResAssignmentCreateRequest,
  conn?: string | null
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    withConn(`${BASE}/assignments`, conn),
    request
  )
  return unwrapApi(response)
}

export async function updateAssignment(
  id: number,
  request: ResAssignmentUpdateRequest,
  conn?: string | null
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    withConn(`${BASE}/assignments/${id}`, conn),
    request
  )
  return unwrapApi(response)
}

export async function deleteAssignment(
  id: number,
  conn?: string | null
): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    withConn(`${BASE}/assignments/${id}`, conn)
  )
  return unwrapApi(response)
}

// ── Lookup (combo) ──────────────────────────────────────────────

export async function fetchResourceLookups(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    `${BASE}/lookups/resources`
  )
  return unwrapApi(response)
}

export async function fetchProjectLookups(): Promise<LookupItem[]> {
  const response = await apiGet<ApiResponse<LookupItem[]>>(
    `${BASE}/lookups/projects`
  )
  return unwrapApi(response)
}

// ── Anagrafica Service (Syncorgest) ─────────────────────────────

export async function fetchServices(): Promise<ResServiceDto[]> {
  const response = await apiGet<ApiResponse<ResServiceDto[]>>(`${BASE}/services`)
  return unwrapApi(response)
}

export async function createService(
  request: ResServiceSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/services`,
    request
  )
  return unwrapApi(response)
}

export async function updateService(
  id: number,
  request: ResServiceSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `${BASE}/services/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteService(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(`${BASE}/services/${id}`)
  return unwrapApi(response)
}

// ── Anagrafica Altre attività ───────────────────────────────────

export async function fetchOthers(): Promise<ResOtherActivityDto[]> {
  const response = await apiGet<ApiResponse<ResOtherActivityDto[]>>(
    `${BASE}/others`
  )
  return unwrapApi(response)
}

export async function createOther(
  request: ResOtherActivitySaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${BASE}/others`, request)
  return unwrapApi(response)
}

export async function updateOther(
  id: number,
  request: ResOtherActivitySaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `${BASE}/others/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteOther(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(`${BASE}/others/${id}`)
  return unwrapApi(response)
}
