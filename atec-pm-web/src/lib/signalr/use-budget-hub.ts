import * as React from "react"

import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Bilancio commessa in ambiente condiviso: l'ordine cliente e il Totale Vendita sono
 * dati che più persone toccano. Chi ha aperto il tab «Preventivo vs Consuntivo» di una
 * commessa (gruppo `project-{id}`) ricarica quando qualcun altro li modifica.
 * Eventi debounced (400 ms); best-effort: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useBudgetHub(
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

    connection.on("BudgetChanged", () => {
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

/**
 * Stessa cosa per la pagina /bilancio cross-commessa: gruppo globale `projects-all`,
 * così l'elenco delle card si riallinea appena cambia l'economia di una commessa qualsiasi.
 */
export function useGlobalBudgetHub(enabled: boolean, onChange: () => void): void {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  React.useEffect(() => {
    if (!enabled) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("BudgetChanged", () => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(), 400)
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
