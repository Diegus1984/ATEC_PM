import { useQuery } from "@tanstack/react-query"

import { fetchProjectHoursPendingCount } from "@/lib/api/project-hours"
import { canAccessFeature } from "@/lib/auth/permissions"

export const ORE_COMMESSA_PENDING_QUERY_KEY = ["ore-commessa", "pending-count"] as const

/**
 * Badge «Ore Commessa» del menu (#109): quante PERSONE hanno scaricato ore che il PM non
 * ha ancora dichiarato di aver guardato, su tutte le commesse. Gemello di
 * `useTravelBadge`, con l'altro perimetro: qui contano tutte le ore, non solo quelle di
 * cantiere.
 */
export function useOreCommessaBadge(): number {
  const query = useQuery({
    queryKey: ORE_COMMESSA_PENDING_QUERY_KEY,
    queryFn: fetchProjectHoursPendingCount,
    enabled: canAccessFeature("nav.ore_commessa"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
  })

  return query.data ?? 0
}
