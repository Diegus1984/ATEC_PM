import * as React from "react"

import type { DdpChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

type ProjectHubScope = "all" | number | null

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) e richiama `onDdpChange` quando un
 * altro utente modifica una distinta DDP. Riproduce `ProjectHubClient` del WPF:
 * - scope `"all"` → `JoinAll` (Gestore DDP: tutte le commesse)
 * - scope `number` → `JoinProject` (Sintesi: una sola commessa)
 * - scope `null` → nessuna connessione
 *
 * Gli eventi sono debounced (400 ms) per coalescere i burst, e la sottoscrizione si
 * ri-aggancia dopo un reconnect automatico. Real-time best-effort: se l'hub è spento
 * la pagina resta usabile con l'Aggiorna manuale.
 */
export function useProjectHub(
  scope: ProjectHubScope,
  onDdpChange: (change: DdpChange) => void,
  /** Evento RDO Acquisti (`PurchaseRfqChanged`, solo gruppo "all"): refetch della lista RDO. */
  onPurchaseRfqChange?: () => void
): void {
  const handlerRef = React.useRef(onDdpChange)
  React.useEffect(() => {
    handlerRef.current = onDdpChange
  }, [onDdpChange])

  const rfqHandlerRef = React.useRef(onPurchaseRfqChange)
  React.useEffect(() => {
    rfqHandlerRef.current = onPurchaseRfqChange
  }, [onPurchaseRfqChange])

  React.useEffect(() => {
    if (scope == null) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    let rfqDebounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("DdpChanged", (change: DdpChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    connection.on("PurchaseRfqChanged", () => {
      if (!rfqHandlerRef.current) return
      if (rfqDebounce) clearTimeout(rfqDebounce)
      rfqDebounce = setTimeout(() => rfqHandlerRef.current?.(), 400)
    })

    const rejoin = async () => {
      try {
        if (scope === "all") await connection.invoke("JoinAll")
        else await connection.invoke("JoinProject", scope)
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
      if (rfqDebounce) clearTimeout(rfqDebounce)
      void stopHub(connection)
    }
  }, [scope])
}
