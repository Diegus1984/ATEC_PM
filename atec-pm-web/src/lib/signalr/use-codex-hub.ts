import * as React from "react"

import type { CompositionChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub Codex (`/hubs/codex`) e richiama `onChange` quando un altro utente
 * modifica una composizione (aggiunta/rimozione componente). Le composizioni sono globali
 * (non per-commessa) → l'hub non ha gruppi: si ascolta e basta.
 *
 * Ritorna un ref con il `connectionId` corrente: la pagina lo passa come `?conn=` alle
 * mutazioni così l'autore NON riceve la propria notifica (self-exclusion, niente reload extra).
 * Gli eventi sono debounced per coalescere i burst (es. drop con quantità N).
 * Real-time best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useCodexHub(
  onChange: (change: CompositionChange) => void
): React.RefObject<string | null> {
  const handlerRef = useLatestRef(onChange)
  const connectionIdRef = React.useRef<string | null>(null)
  useHubSubscription({
    hub: "codex",
    deps: [],
    subscribe: (on) =>
      on("CompositionChanged", (change: CompositionChange) => handlerRef.current(change)),
    onConnected: ({ connectionId }) => {
      connectionIdRef.current = connectionId
    },
    onClosed: () => {
      connectionIdRef.current = null
    },
  })
  return connectionIdRef
}
