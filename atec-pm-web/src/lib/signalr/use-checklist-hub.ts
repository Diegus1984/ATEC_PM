import * as React from "react"

import type { ChecklistChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche alle attività Check list
 * e richiama `onChange` quando un altro utente crea/modifica/elimina un'attività.
 *
 * - Senza `projectId` → gruppo globale `checklist-all` (pagina PM aggregata).
 * - Con `projectId` → gruppo `project-{id}` (tab Check list nel dettaglio commessa).
 * - `enabled: false` → nessuna connessione.
 * - Eventi debounced (400 ms) per coalescere i burst di autosave.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useChecklistHub(
  enabled: boolean,
  onChange: (change: ChecklistChange) => void,
  projectId?: number
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

    connection.on("ChecklistChanged", (change: ChecklistChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        if (projectId && projectId > 0) {
          await connection.invoke("JoinProject", projectId)
        } else {
          await connection.invoke("JoinCheckList")
        }
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
  }, [enabled, projectId])
}
