import { useQuery } from "@tanstack/react-query"

import { fetchDeadlines } from "@/lib/api/deadlines"
import { canAccessFeature } from "@/lib/auth/permissions"

export const DEADLINES_QUERY_KEY = ["deadlines"] as const

const mapTypeToMenuId = (type: string): string | null => {
  switch (type) {
    case "MOM":
      return "mom"
    case "DDP":
      return "gestore-ddp"
    case "CHECKLIST":
      return "checklist"
    // Il Prospetto SAL non è più una voce di menu a sé (03/08/2026): anche gli
    // incassi scaduti si contano sulla voce «SAL / Fatturazione», che lo contiene.
    case "SAL":
    case "SAL_INCASSO":
      return "sal"
    default:
      return null
  }
}

export function useDeadlinesCount() {
  // Il contatore gira nella shell per tutti, ma /api/deadlines ora richiede il livello
  // di «Scadenze»: senza permesso non si interroga proprio, altrimenti sarebbe un 403
  // ogni minuto.
  const query = useQuery({
    queryKey: DEADLINES_QUERY_KEY,
    queryFn: fetchDeadlines,
    enabled: canAccessFeature("nav.scadenze"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
  })

  const pendingDeadlines = query.data
    ? query.data.filter((d) => d.days <= 7)
    : []

  const pendingCount = pendingDeadlines.length

  const sectionCounts: Record<string, number> = {
    mom: 0,
    "gestore-ddp": 0,
    checklist: 0,
    sal: 0,
  }

  pendingDeadlines.forEach((d) => {
    const menuId = mapTypeToMenuId(d.type)
    if (menuId && menuId in sectionCounts) {
      sectionCounts[menuId]++
    }
  })

  return {
    pendingCount,
    sectionCounts,
    isLoading: query.isLoading,
  }
}
