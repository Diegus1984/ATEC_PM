const SERVER_KEY = "atec_pm_server"
const USERNAME_KEY = "atec_pm_username"
const DEV_DEFAULT_SERVER = "http://localhost:5150"

/** Vite dev server — SPA e API su porte diverse (come :5265 in ATEC Risorse). */
export function isDevHost(): boolean {
  if (typeof window === "undefined") {
    return false
  }
  return window.location.origin.includes(":5173")
}

export function shouldShowServerField(): boolean {
  return isDevHost()
}

export function resolveServerUrl(stored: string): string {
  const trimmed = stored.trim().replace(/\/$/, "")
  if (trimmed) {
    return trimmed
  }

  if (isDevHost()) {
    return DEV_DEFAULT_SERVER
  }

  if (typeof window !== "undefined") {
    return window.location.origin
  }

  return ""
}

export function getStoredServerUrl(): string {
  try {
    return localStorage.getItem(SERVER_KEY) ?? ""
  } catch {
    return ""
  }
}

export function saveServerUrl(url: string): void {
  try {
    localStorage.setItem(SERVER_KEY, url.trim().replace(/\/$/, ""))
  } catch {
    // Best-effort: non bloccare il login se lo storage non è disponibile.
  }
}

export function getStoredUsername(): string {
  try {
    return localStorage.getItem(USERNAME_KEY) ?? ""
  } catch {
    return ""
  }
}

export function saveUsername(username: string): void {
  try {
    localStorage.setItem(USERNAME_KEY, username.trim())
  } catch {
    // Best-effort.
  }
}

/** Base URL REST/SignalR al login: vuoto in prod (same-origin), assoluto in dev. */
export function resolveLoginApiBase(serverFieldValue: string): string {
  if (!shouldShowServerField()) {
    return ""
  }
  return resolveServerUrl(serverFieldValue)
}

/** Ripristina la base URL dopo refresh (sessione già autenticata). */
export function resolveStoredApiBase(): string {
  const buildTime = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? ""
  if (buildTime) {
    return buildTime
  }

  if (!shouldShowServerField()) {
    return ""
  }

  const stored = getStoredServerUrl()
  if (stored) {
    return resolveServerUrl(stored)
  }

  return ""
}

/** Valore da persistere in localStorage dopo login riuscito. */
export function serverUrlToPersist(apiBase: string): string {
  if (apiBase) {
    return apiBase
  }
  if (typeof window !== "undefined") {
    return window.location.origin
  }
  return ""
}
