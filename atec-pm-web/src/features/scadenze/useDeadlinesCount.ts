import { useQuery } from "@tanstack/react-query"

import { fetchDeadlines } from "@/lib/api/deadlines"

export const DEADLINES_QUERY_KEY = ["deadlines"] as const

const mapTypeToMenuId = (type: string): string | null => {
  switch (type) {
    case "MOM":
      return "mom"
    case "DDP":
      return "gestore-ddp"
    case "CHECKLIST":
      return "checklist"
    case "SAL":
      return "sal"
    case "SAL_INCASSO":
      return "sal-prospetto"
    default:
      return null
  }
}

export function useDeadlinesCount() {
  const query = useQuery({
    queryKey: DEADLINES_QUERY_KEY,
    queryFn: fetchDeadlines,
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
    "sal-prospetto": 0,
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
