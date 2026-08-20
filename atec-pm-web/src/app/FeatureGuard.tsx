import type { ReactNode } from "react"
import { Link } from "react-router-dom"
import { ShieldAlert } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import { firstAccessibleNavPath } from "@/config/navigation"
import { canAccessFeature } from "@/lib/auth/permissions"

/**
 * Cancello di rotta: la pagina si apre solo se la persona ha la chiave di quella pagina.
 * Prima esisteva solo il filtro del menu laterale, quindi bastava scrivere l'indirizzo a
 * mano — o avere il preferito salvato — per aprire pagine che non si dovevano vedere.
 *
 * Chiave non registrata in `auth_features`, oppure permessi non ancora arrivati: NEGA,
 * esattamente come `canAccessFeature` da quando il fallback è stato invertito. Chi non ha
 * proprio nessuna funzione non arriva però fin qui: `AppShell` gli mette al posto del
 * contenuto l'avviso «Nessun permesso assegnato», che spiega la causa invece di limitarsi
 * a sbarrare la strada. Il cancello nasconde la pagina, NON protegge i dati: quello
 * tocca all'API.
 *
 * ⚠️ **Rotta SENZA chiave: si nega anche quella.** Le sotto-pagine prendono la chiave da
 * `ROUTE_FEATURE_KEYS` (`lib/auth/route-features.ts`), e finché qui bastava `featureKey &&`
 * un indirizzo dimenticato in quell'elenco restava aperto **in silenzio**: nessun errore,
 * nessun segno, la pagina semplicemente si apriva a chiunque. Era l'ultimo pezzo di
 * «se non lo so, lascio passare» rimasto in piedi dopo l'inversione della Fase A, ed è lo
 * stesso difetto per cui Chat, DDP e Documenti sono rimasti aperti per mesi. Ora una rotta
 * non censita dà «Accesso negato» a tutti — chiassoso, e quindi corretto subito.
 */
export function FeatureGuard({
  featureKey,
  children,
}: {
  featureKey?: string
  children: ReactNode
}) {
  if (!featureKey || !canAccessFeature(featureKey)) {
    return <AccessDenied />
  }
  return <>{children}</>
}

function AccessDenied() {
  // Chi non vede la Dashboard (ruoli di reparto) torna alla sua prima pagina visibile.
  const home = canAccessFeature("nav.dashboard")
    ? "/"
    : (firstAccessibleNavPath(canAccessFeature) ?? "/")

  return (
    <Empty className="p-10">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <ShieldAlert />
        </EmptyMedia>
        <EmptyTitle>Accesso negato</EmptyTitle>
        <EmptyDescription>
          Non hai il permesso per questa pagina. Se ti serve, chiedi a un amministratore
          di abilitartela.
        </EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button asChild variant="outline" size="sm">
          <Link to={home}>Torna indietro</Link>
        </Button>
      </EmptyContent>
    </Empty>
  )
}
