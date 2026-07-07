import * as React from "react"

import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche alle milestone di una commessa
 * e richiama `onChange` quando un altro utente crea/modifica/elimina/riordina una milestone.
 * Gruppo `project-{id}` (stesso di chat/DDP/checklist). Eventi debounced (400 ms). Best-effort.
 */
export function useMilestonesHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number
): void {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  React.useEffect(() => {
    if (!enabled || !projectId || projectId <= 0) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("MilestonesChanged", () => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(), 400)
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
  }, [enabled, projectId])
}
