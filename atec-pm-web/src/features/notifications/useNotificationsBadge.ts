import { useEffect, useRef, useState } from "react"
import { useQuery } from "@tanstack/react-query"

import { fetchNotificationBadge } from "@/lib/api/notifications"

const INTERVAL_BASE = 30_000
const INTERVAL_MEDIUM = 60_000
const INTERVAL_SLOW = 120_000
const THRESHOLD_MEDIUM = 5
const THRESHOLD_SLOW = 10

export const NOTIFICATIONS_BADGE_KEY = ["notifications-badge"] as const

/**
 * Badge notifiche con polling adattivo, fedele a `NotificationPollingService` (WPF):
 * 30s di base, rallenta a 60s dopo 5 cicli senza nuove notifiche e a 120s dopo 10;
 * torna subito a 30s appena il conteggio sale. react-query mette in pausa il polling
 * quando la tab perde il focus (`refetchIntervalInBackground: false`) e rifà il fetch
 * al ritorno del focus.
 */
export function useNotificationsBadge() {
  const [intervalMs, setIntervalMs] = useState(INTERVAL_BASE)
  const emptyCycles = useRef(0)
  const lastCount = useRef(0)

  const query = useQuery({
    queryKey: NOTIFICATIONS_BADGE_KEY,
    queryFn: fetchNotificationBadge,
    refetchInterval: intervalMs,
    refetchIntervalInBackground: false,
  })

  const updatedAt = query.dataUpdatedAt
  useEffect(() => {
    if (query.data === undefined) {
      return
    }
    const count = query.data
    if (count > lastCount.current) {
      // Nuove notifiche: accelera al ritmo base.
      emptyCycles.current = 0
      setIntervalMs(INTERVAL_BASE)
    } else {
      // Nessuna novità: rallenta progressivamente.
      emptyCycles.current += 1
      const next =
        emptyCycles.current >= THRESHOLD_SLOW
          ? INTERVAL_SLOW
          : emptyCycles.current >= THRESHOLD_MEDIUM
            ? INTERVAL_MEDIUM
            : INTERVAL_BASE
      setIntervalMs((prev) => (prev === next ? prev : next))
    }
    lastCount.current = count
    // `updatedAt` cambia a ogni fetch riuscito → un "ciclo" per tick di polling.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [updatedAt])

  return { unreadCount: query.data ?? 0 }
}
