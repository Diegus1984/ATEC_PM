import { useQuery } from "@tanstack/react-query"

import { fetchDeadlines } from "@/lib/api/deadlines"
import { canAccessFeature } from "@/lib/auth/permissions"

export const DEADLINES_QUERY_KEY = ["deadlines"] as const

const mapTypeToMenuId = (type: string): string | null => {
  switch (type) {
    case "MOM":
      return "mom"
    case "CHECKLIST":
      return "checklist"
    // 🪤 DDP non sta più qui (#118, 24/08/2026): il pallino di «Gestore DDP» conta le DDP
    // «da verificare» — quelle toccate di recente e non ancora aperte, la stessa lista
    // della card in Dashboard — non il materiale con data entro 7 giorni. Erano due cose
    // diverse sullo stesso pallino. Anche il materiale in scadenza resta in `pendingCount`.
    // 🪤 SAL e SAL_INCASSO NON stanno più qui (#117, 24/08/2026). Il pallino di
    // «SAL / Fatturazione» si conta dagli alert del prospetto — la stessa sorgente delle
    // viste «Warning Fatturazione» e «Warning incasso fattura» — non dalle scadenze, che
    // usano una soglia fissa a 7 giorni e davano un numero diverso da quello scritto
    // dentro la pagina (9 contro 6). Vedi `useSalWarnings` e `AppShell`.
    // Restano invece dentro `pendingCount`: nella campanella «Scadenze» ci vanno eccome.
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
    checklist: 0,
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
