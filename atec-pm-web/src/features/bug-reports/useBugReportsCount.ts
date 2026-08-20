import { useQuery } from "@tanstack/react-query"

import { fetchBugReportCounts } from "@/lib/api/bug-reports"
import { canAccessFeature } from "@/lib/auth/permissions"

export const BUG_REPORTS_COUNTS_QUERY_KEY = ["bug-reports", "counts"] as const

/**
 * Badge «Segnalazioni» del menu: segnalazioni ancora da chiudere (aperte + in lavorazione).
 * Le risolte e le rifiutate non contano, altrimenti il numero crescerebbe per sempre.
 * Gira nella shell per chi vede la voce di menu; si aggiorna al minuto come le scadenze
 * (niente hub dedicato: la chiave è figlia di `["bug-reports"]`, quindi l'invalidazione
 * della pagina Segnalazioni — anche quella realtime — rinfresca già il contatore).
 */
export function useBugReportsCount(): number {
  const query = useQuery({
    queryKey: BUG_REPORTS_COUNTS_QUERY_KEY,
    queryFn: fetchBugReportCounts,
    enabled: canAccessFeature("nav.bug_reports"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
  })

  if (!query.data) return 0
  return query.data.open + query.data.inProgress
}
