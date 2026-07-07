import * as React from "react"

import type { CompositionChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub Codex (`/hubs/codex`) e richiama `onChange` quando un altro utente
 * modifica una composizione (aggiunta/rimozione componente). Le composizioni sono globali
 * (non per-commessa) → l'hub non ha gruppi: si ascolta e basta.
 *
 * Ritorna un ref con il `connectionId` corrente: la pagina lo passa come `?conn=` alle
 * mutazioni così l'autore NON riceve la propria notifica (self-exclusion, niente reload extra).
 * Gli eventi sono debounced (400 ms) per coalescere i burst (es. drop con quantità N).
 * Real-time best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useCodexHub(
  onChange: (change: CompositionChange) => void
): React.RefObject<string | null> {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  const connectionIdRef = React.useRef<string | null>(null)

  React.useEffect(() => {
    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("codex")

    connection.on("CompositionChanged", (change: CompositionChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    connection.onreconnected((id) => {
      connectionIdRef.current = id ?? connection.connectionId
    })
    connection.onclose(() => {
      connectionIdRef.current = null
    })

    void (async () => {
      const ok = await startHub(connection)
      if (!ok || disposed) return
      connectionIdRef.current = connection.connectionId
    })()

    return () => {
      disposed = true
      if (debounce) clearTimeout(debounce)
      connectionIdRef.current = null
      void stopHub(connection)
    }
  }, [])

  return connectionIdRef
}
