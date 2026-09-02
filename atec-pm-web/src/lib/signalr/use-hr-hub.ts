import * as React from "react"

import type { HrChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `hr-all`, e richiama
 * `onChange` quando qualcosa del modulo presenze cambia: import da Ecos (anche quello
 * automatico delle 12 ore), rettifiche, solleciti, causali, richieste di assenza,
 * mappatura, credenziali.
 *
 * - `enabled: false` → nessuna connessione.
 * - Eventi debounced (400 ms) per coalescere i burst (un import ne manda a raffica).
 *   `import-progress` NON è debounced: è la barra di avanzamento, deve muoversi subito.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'aggiornamento manuale
 *   e il refetch-on-focus di react-query.
 */
export function useHrHub(enabled: boolean, onChange: (change: HrChange) => void): void {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  React.useEffect(() => {
    if (!enabled) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("HrChanged", (change: HrChange) => {
      if (change.action === "import-progress") {
        handlerRef.current(change)
        return
      }
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinHr")
      } catch {
        /* best-effort */
      }
    }

    connection.onreconnected(() => {
      void rejoin()
      // Dopo una disconnessione si può essere persi qualcosa: si rilegge tutto.
      handlerRef.current({ action: "reconnected", employeeId: null, date: null })
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
