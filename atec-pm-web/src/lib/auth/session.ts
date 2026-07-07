import {
  apiGet,
  apiPost,
  setApiBaseUrl,
  setApiTokenProvider,
  unwrapApi,
} from "@/lib/api/client"
import { clearAuthFeatures, loadAuthFeatures } from "@/lib/auth/permissions"
import { resolveStoredApiBase } from "@/lib/auth/server-url"
import type {
  ApiResponse,
  LoginRequest,
  LoginResponse,
  SessionStatusDto,
} from "@/lib/api/types"

const TOKEN_KEY = "atec_pm_token"
const USER_KEY = "atec_pm_user"

export interface AuthUser {
  employeeId: number
  fullName: string
  userRole: string
  mustChangePassword: boolean
}

export interface AuthSession {
  token: string
  user: AuthUser
}

function readStoredSession(): AuthSession | null {
  const token = localStorage.getItem(TOKEN_KEY)
  const rawUser = localStorage.getItem(USER_KEY)
  if (!token || !rawUser) {
    return null
  }

  try {
    const user = JSON.parse(rawUser) as AuthUser
    return { token, user }
  } catch {
    return null
  }
}

let session: AuthSession | null = readStoredSession()

setApiBaseUrl(resolveStoredApiBase())
setApiTokenProvider(() => session?.token ?? null)

export function getSession(): AuthSession | null {
  return session
}

export function isAuthenticated(): boolean {
  return session !== null
}

/** Login completato: token presente e password iniziale già cambiata. */
export function isLoginComplete(): boolean {
  return session !== null && !session.user.mustChangePassword
}

export function markPasswordChanged(): void {
  if (!session) {
    return
  }

  saveSession({
    ...session,
    user: {
      ...session.user,
      mustChangePassword: false,
    },
  })
}

export function clearSession() {
  session = null
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
  // Server e username restano in localStorage (come ATEC Risorse al logout).
  clearAuthFeatures()
}

export function saveSession(next: AuthSession) {
  session = next
  localStorage.setItem(TOKEN_KEY, next.token)
  localStorage.setItem(USER_KEY, JSON.stringify(next.user))
}

export async function login(request: LoginRequest): Promise<AuthSession> {
  const response = await apiPost<ApiResponse<LoginResponse>>(
    "/api/auth/login",
    request
  )
  const data = unwrapApi(response)

  const nextSession: AuthSession = {
    token: data.token,
    user: {
      employeeId: data.employeeId,
      fullName: data.fullName,
      userRole: data.userRole,
      mustChangePassword: data.mustChangePassword,
    },
  }

  saveSession(nextSession)

  try {
    await loadAuthFeatures()
  } catch {
    // Permessi opzionali: menu fallback permissivo
  }

  return nextSession
}

export async function checkSessionActive(): Promise<boolean> {
  if (!session) {
    return false
  }

  try {
    const response = await apiGet<ApiResponse<SessionStatusDto>>("/api/auth/session")
    const status = unwrapApi(response)
    return status.isActive
  } catch {
    return false
  }
}

export function onSessionExpired(handler: () => void): () => void {
  const listener = () => handler()
  window.addEventListener("atec:session-expired", listener)
  return () => window.removeEventListener("atec:session-expired", listener)
}
