import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  NotificationBadge,
  NotificationListItem,
} from "@/lib/api/types"

/** GET /api/notifications/badge — conteggio non lette dell'utente corrente. */
export async function fetchNotificationBadge(): Promise<number> {
  const response = await apiGet<ApiResponse<NotificationBadge>>(
    "/api/notifications/badge"
  )
  return unwrapApi(response).unreadCount
}

/** GET /api/notifications?unreadOnly=&limit= — lista notifiche dell'utente. */
export async function fetchNotifications(
  unreadOnly = false,
  limit = 50
): Promise<NotificationListItem[]> {
  const response = await apiGet<ApiResponse<NotificationListItem[]>>(
    `/api/notifications?unreadOnly=${unreadOnly}&limit=${limit}`
  )
  return unwrapApi(response)
}

/** PUT /api/notifications/{id}/read — segna letta (id = recipient id). */
export async function markNotificationRead(id: number): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/notifications/${id}/read`,
    {}
  )
  unwrapApi(response)
}

/** PUT /api/notifications/read-all — segna tutte lette. */
export async function markAllNotificationsRead(): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    "/api/notifications/read-all",
    {}
  )
  unwrapApi(response)
}

/** DELETE /api/notifications/{id} — rimuove la notifica per l'utente. */
export async function deleteNotification(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/notifications/${id}`
  )
  unwrapApi(response)
}

/**
 * POST /api/notifications/check-pending — genera le notifiche pendenti
 * (commesse attive / assegnazioni fasi) al login. Fire-and-forget, come il WPF.
 * Ritorna il numero di notifiche generate.
 */
export async function checkPendingNotifications(): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/notifications/check-pending",
    {}
  )
  return unwrapApi(response)
}
