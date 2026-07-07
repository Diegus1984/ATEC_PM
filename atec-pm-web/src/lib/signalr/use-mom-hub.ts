import * as React from "react"

import type { MoMChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) sul gruppo globale MoM (`JoinMoM`)
 * e richiama `onChange` quando un altro utente modifica un verbale (header, righe,
 * riordino). Le MoM di tipo RIUNIONE non hanno commessa, quindi il gruppo è globale.
 *
 * - `enabled: false` → nessuna connessione (pagina che non mostra MoM).
 * - Eventi debounced (400 ms) per coalescere i burst di autosave.
 * - Si ri-aggancia dopo un reconnect automatico.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useMoMHub(
  enabled: boolean,
  onChange: (change: MoMChange) => void
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

    connection.on("MoMChanged", (change: MoMChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinMoM")
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
