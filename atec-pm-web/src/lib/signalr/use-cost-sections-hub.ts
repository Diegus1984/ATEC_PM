import * as React from "react"

import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

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
 * Debounce 400 ms come gli altri hub: un riordino manda una PATCH per riga, e senza attesa
 * sarebbero N ricariche invece di una. Best-effort: se l'hub è spento resta «Aggiorna».
 */
export function useCostSectionsHub(
  enabled: boolean,
  onChange: (change: CostSectionsChange) => void
): void {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  React.useEffect(() => {
    if (!enabled) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("CostSectionsChanged", (change: CostSectionsChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinCostSections")
      } catch {
        /* best-effort */
      }
    }

    connection.onreconnected(() => {
      void rejoin()
    })

    void (async () => {
      const ok = await startHub(connection)
      if (!ok || disposed) return
      await rejoin()
    })()

    return () => {
      disposed = true
      if (debounce) clearTimeout(debounce)
      void stopHub(connection)
    }
  }, [enabled])
}
