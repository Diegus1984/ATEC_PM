import * as React from "react"

import type { DocumentsChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) e richiama `onChange` quando un
 * altro utente modifica i documenti (file/cartelle) della commessa `projectId`.
 * Riusa lo stesso hub di `useProjectHub` ma ascolta l'evento `DocumentsChanged`.
 *
 * - `projectId` numerico → `JoinProject(id)`; `null` → nessuna connessione.
 * - Eventi debounced (400 ms) per coalescere i burst (es. upload multiplo).
 * - Si ri-aggancia dopo un reconnect automatico.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useProjectDocumentsHub(
  projectId: number | null,
  onChange: (change: DocumentsChange) => void
): void {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  React.useEffect(() => {
    if (projectId == null || projectId <= 0) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("DocumentsChanged", (change: DocumentsChange) => {
      if (change.projectId !== projectId) return
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinProject", projectId)
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
  }, [projectId])
}
