import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/** Cosa è cambiato: serve solo a distinguere i log, la pagina ricarica comunque tutto. */
export interface CostSectionsChange {
  action: string
}

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `cost-sections-all`, per la
 * Configurazione sezioni di costo: gruppi, sezioni, reparti collegati, anagrafica delle fasi
 * e tariffe.
 *
 * È una pagina che si lavora in due — uno assegna le fasi alle sezioni, l'altro sistema i
 * reparti — e ogni modifica cambia l'albero sotto gli occhi dell'altro. Senza avviso il
 * secondo trascina fasi su una sezione che nel frattempo è stata rinominata o spenta, e se ne
 * accorge solo ricaricando.
 *
 * Debounce come gli altri hub: un riordino manda una PATCH per riga, e senza attesa
 * sarebbero N ricariche invece di una. Best-effort: se l'hub è spento resta «Aggiorna».
 */
export function useCostSectionsHub(
  enabled: boolean,
  onChange: (change: CostSectionsChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) =>
      on("CostSectionsChanged", (change: CostSectionsChange) => handlerRef.current(change)),
    join: (connection) => connection.invoke("JoinCostSections"),
  })
}
