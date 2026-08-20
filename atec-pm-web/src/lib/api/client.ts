import { recordLastError } from "@/lib/api/last-error"
import type { ApiResponse } from "@/lib/api/types"

const buildTimeBase = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? ""

/** Base URL runtime (impostata al login o al ripristino sessione). */
let runtimeBase: string | null = null

export function setApiBaseUrl(url: string): void {
  runtimeBase = url.replace(/\/$/, "")
}

export function getApiBaseUrl(): string {
  if (buildTimeBase) {
    return buildTimeBase
  }
  if (runtimeBase !== null) {
    return runtimeBase
  }
  return ""
}

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = "ApiError"
    this.status = status
  }
}

type TokenProvider = () => string | null

let tokenProvider: TokenProvider = () => null

export function setApiTokenProvider(provider: TokenProvider) {
  tokenProvider = provider
}

function buildUrl(path: string): string {
  if (path.startsWith("http://") || path.startsWith("https://")) {
    return path
  }

  const normalizedPath = path.startsWith("/") ? path : `/${path}`
  return `${getApiBaseUrl()}${normalizedPath}`
}

function parseJson<T>(text: string): T | null {
  if (!text) {
    return null
  }

  try {
    return JSON.parse(text) as T
  } catch {
    return null
  }
}

async function handleResponse<T>(
  response: Response,
  method = "GET",
  path = ""
): Promise<T> {
  const text = await response.text()

  if (response.status === 401 && tokenProvider()) {
    window.dispatchEvent(new CustomEvent("atec:session-expired"))
    throw new ApiError("Sessione scaduta", 401)
  }

  if (!response.ok) {
    const payload = parseJson<ApiResponse<unknown>>(text)
    const errorMsg = payload?.message || response.statusText
    recordLastError({
      method,
      url: path,
      status: response.status,
      message: errorMsg,
    })
    throw new ApiError(errorMsg, response.status)
  }

  const payload = parseJson<T>(text)
  if (payload === null) {
    throw new ApiError("Risposta non valida dal server", response.status)
  }

  return payload
}

export async function apiGet<T>(path: string): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "GET",
    headers,
  })

  return handleResponse<T>(response, "GET", path)
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers({
    "Content-Type": "application/json",
  })
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "POST",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  return handleResponse<T>(response, "POST", path)
}

export async function apiPatch<T>(path: string, body?: unknown): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers({
    "Content-Type": "application/json",
  })
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "PATCH",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  return handleResponse<T>(response, "PATCH", path)
}

export async function apiPut<T>(path: string, body?: unknown): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers({
    "Content-Type": "application/json",
  })
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "PUT",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  return handleResponse<T>(response, "PUT", path)
}

export async function apiDelete<T>(path: string): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "DELETE",
    headers,
  })

  return handleResponse<T>(response, "DELETE", path)
}

export async function apiUpload<T>(path: string, formData: FormData): Promise<T> {
  const token = tokenProvider()
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), {
    method: "POST",
    headers,
    body: formData,
  })

  return handleResponse<T>(response, "POST", path)
}

/** GET autenticato che ritorna un Blob (PDF, Excel, immagini): per anteprima/download. */
export async function apiGetBlob(path: string): Promise<Blob> {
  const token = tokenProvider()
  const headers = new Headers()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(buildUrl(path), { method: "GET", headers })

  if (response.status === 401 && tokenProvider()) {
    window.dispatchEvent(new CustomEvent("atec:session-expired"))
    throw new ApiError("Sessione scaduta", 401)
  }
  if (!response.ok) {
    throw new ApiError(response.statusText || "Download non riuscito", response.status)
  }

  return response.blob()
}

export function buildApiUrl(path: string): string {
  return buildUrl(path)
}

/**
 * Estrae `data` da ApiResponse se `success`, altrimenti lancia.
 *
 * Il server serializza con WhenWritingNull: quando `data` è null la proprietà
 * sparisce dal JSON. Gli endpoint «void» (es. PUT /api/permessi/combo) rispondono
 * `{ success: true, message: "…" }` senza `data` — è un successo, non un errore.
 * Prima `data === undefined` faceva scattare un falso «Operazione non riuscita»
 * / toast «Permesso non modificato» anche se il DB era già aggiornato.
 */
export function unwrapApi<T>(response: ApiResponse<T>): T {
  if (!response.success) {
    throw new ApiError(response.message || "Operazione non riuscita", 400)
  }

  return response.data as T
}
