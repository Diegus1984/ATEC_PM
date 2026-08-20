import * as React from "react"

import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Trasferta in ambiente condiviso: il piano lo compila il PM ma lo guardano in tanti, e
 * la griglia riga-persona è editabile da più postazioni. Chi ha aperto la pagina ricarica
 * quando qualcun altro tocca step o righe.
 *
 * `projectId` null = pagina cross-commessa (gruppo globale `projects-all`), così anche
 * l'elenco delle card si riallinea. Eventi debounced (400 ms), best-effort come gli altri
 * hub: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useTravelHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number | null
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

    connection.on("TravelChanged", () => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(), 400)
    })

    const rejoin = async () => {
      try {
        if (projectId && projectId > 0) {
          await connection.invoke("JoinProject", projectId)
        } else {
          await connection.invoke("JoinProjects")
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
