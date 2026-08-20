import * as React from "react"

import type { ProjectChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `projects-all`, per
 * l'ANAGRAFICA commesse: quando un collega ne crea, modifica o elimina una, gli elenchi
 * aperti (albero, Milestones, SAL, Lavorazioni, Check list, MoM) si ricaricano da soli.
 * Eventi debounced (400 ms); best-effort: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useProjectsHub(
  enabled: boolean,
  onChange: (change: ProjectChange) => void
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

    connection.on("ProjectsChanged", (change: ProjectChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 400)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinProjects")
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
