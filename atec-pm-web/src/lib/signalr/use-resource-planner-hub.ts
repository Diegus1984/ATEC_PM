import * as React from "react"

import type { PresenceSnapshot, ResAssignmentChange } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

const EMPTY_SET: ReadonlySet<number> = new Set()

/**
 * Sottoscrive l'hub risorse (`/hubs/resource-planner`) e richiama `onChange` quando
 * un altro utente crea/modifica/elimina un'allocazione. Riproduce `PlannerHubService`
 * del Blazor: broadcast `AssignmentsChanged` (debounced 300ms) + presenza online
 * (`PresenceChanged`, pallino verde/rosso accanto al nome nel Gantt).
 *
 * `connRef` riceve il ConnectionId corrente: la pagina lo passa come `?conn=` alle
 * mutazioni così il server non rinotifica l'autore (che ha già aggiornato la vista).
 * Real-time best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna.
 *
 * Ritorna l'insieme dei dipendenti con almeno un client connesso in questo momento.
 */
export function useResourcePlannerHub(
  onChange: (change: ResAssignmentChange) => void,
  connRef: React.MutableRefObject<string | null>
): ReadonlySet<number> {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  const [online, setOnline] = React.useState<ReadonlySet<number>>(EMPTY_SET)

  React.useEffect(() => {
    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("resource-planner")

    connection.on("AssignmentsChanged", (change: ResAssignmentChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 300)
    })

    connection.on("PresenceChanged", (snap: PresenceSnapshot) => {
      setOnline(new Set(snap.onlineEmployeeIds))
    })

    // Snapshot esplicito dopo la connessione (evita la corsa tra "start()" risolto
    // e il primo broadcast); ripetuto anche dopo un riconnect automatico.
    const fetchSnapshot = async () => {
      try {
        const ids = await connection.invoke<number[]>("GetOnlineEmployeeIds")
        if (!disposed) setOnline(new Set(ids))
      } catch {
        /* best-effort */
      }
    }

    connection.onreconnected((id) => {
      connRef.current = id ?? null
      void fetchSnapshot()
    })
    connection.onclose(() => {
      connRef.current = null
      setOnline(EMPTY_SET)
    })

    void (async () => {
      const ok = await startHub(connection)
      if (!ok || disposed) return
      connRef.current = connection.connectionId
      await fetchSnapshot()
    })()

    return () => {
      disposed = true
      if (debounce) clearTimeout(debounce)
      connRef.current = null
      void stopHub(connection)
    }
    // connRef è stabile (useRef del chiamante): effetto montato una sola volta.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return online
}
