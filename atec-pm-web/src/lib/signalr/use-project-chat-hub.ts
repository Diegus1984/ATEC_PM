import * as React from "react"

import type { ChatChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) e richiama `onChange` quando un
 * altro utente modifica le chat della commessa `projectId`.
 * Riusa lo stesso hub di `useProjectHub` ma ascolta l'evento `ChatChanged`.
 */
export function useProjectChatHub(
  projectId: number | null,
  onChange: (change: ChatChange) => void
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

    connection.on("ChatChanged", (change: ChatChange) => {
      if (change.projectId !== projectId) return
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 300)
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
