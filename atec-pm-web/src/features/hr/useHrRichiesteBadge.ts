import { useQuery } from "@tanstack/react-query"

import { fetchHrAbsences } from "@/lib/api/hr"
import { canWriteFeature } from "@/lib/auth/permissions"

export const HR_DA_APPROVARE_QUERY_KEY = ["hr-absences", "da-approvare"] as const

/**
 * Badge della voce «Ferie e permessi» (08/09/2026): quante richieste dell'anno sono in
 * attesa. Lo stesso numero del contatore in pagina, così menu e pagina dicono la stessa
 * cosa. Solo per chi approva (scrittura sulla chiave): agli altri il numero non serve.
 *
 * La chiave è figlia di `["hr-absences"]`: ogni invalidazione della pagina Richieste e
 * del real-time HR rinfresca anche il contatore; in più si rilegge al minuto come gli altri.
 */
export function useHrRichiesteBadge(): number {
  const query = useQuery({
    queryKey: HR_DA_APPROVARE_QUERY_KEY,
    queryFn: async () => {
      const richieste = await fetchHrAbsences({ year: new Date().getFullYear() })
      return richieste.filter((r) => r.status === "PENDING").length
    },
    enabled: canWriteFeature("nav.hr_richieste"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
  })

  return query.data ?? 0
}
