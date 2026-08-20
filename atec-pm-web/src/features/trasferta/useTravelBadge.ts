import { useQuery } from "@tanstack/react-query"

import { fetchTravelPendingCount } from "@/lib/api/travel"
import { canAccessFeature } from "@/lib/auth/permissions"

export const TRAVEL_PENDING_QUERY_KEY = ["travel", "pending-count"] as const

/**
 * Badge «Trasferta» del menu (#102): quante PERSONE hanno scaricato ore di cantiere che
 * il PM non ha ancora dichiarato di aver guardato. Chi ha scaricato su tre commesse conta
 * una volta sola: la domanda è «quante persone devo ricontrollare», non «quante righe».
 *
 * Si aggiorna al minuto come gli altri badge. Nessun hub dedicato: la chiave è figlia di
 * `["travel"]`, quindi l'invalidazione della pagina Trasferta — anche quella che arriva
 * dal realtime `TravelChanged` — rinfresca già il contatore.
 */
export function useTravelBadge(): number {
  const query = useQuery({
    queryKey: TRAVEL_PENDING_QUERY_KEY,
    queryFn: fetchTravelPendingCount,
    enabled: canAccessFeature("nav.trasferta"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
  })

  return query.data ?? 0
}
