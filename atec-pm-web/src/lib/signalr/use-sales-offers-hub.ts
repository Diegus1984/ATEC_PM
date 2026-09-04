import type { SalesOffersChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `sales-offers-all`, per le
 * modifiche al Registro Offerte.
 *
 * <p>Serve più che altrove: il registro è un elenco solo, ci lavorano più venditori insieme,
 * e il numero che uno sta per usare può essere stato preso da un altro trenta secondi fa.
 * Senza avviso te ne accorgi al salvataggio.</p>
 */
export function useSalesOffersHub(
  enabled: boolean,
  onChange: (change: SalesOffersChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    // Gruppo unico e globale: non dipende da niente.
    deps: [],
    subscribe: (on) =>
      on("SalesOffersChanged", (change: SalesOffersChange) =>
        handlerRef.current(change)
      ),
    join: (connection) => connection.invoke("JoinSalesOffers"),
  })
}
