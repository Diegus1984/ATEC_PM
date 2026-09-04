import * as React from "react"

import type { PresenceSnapshot, ResAssignmentChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

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
  const handlerRef = useLatestRef(onChange)
  const [online, setOnline] = React.useState<ReadonlySet<number>>(EMPTY_SET)

  useHubSubscription({
    hub: "resource-planner",
    deps: [],
    subscribe: (on) => {
      on("AssignmentsChanged", (change: ResAssignmentChange) => handlerRef.current(change), {
        debounceMs: 300,
      })
      on(
        "PresenceChanged",
        (snap: PresenceSnapshot) => setOnline(new Set(snap.onlineEmployeeIds)),
        { debounceMs: 0 }
      )
    },
    // Snapshot esplicito dopo la connessione (evita la corsa tra "start()" risolto
    // e il primo broadcast); ripetuto anche dopo un riconnect automatico.
    join: async (connection, isActive) => {
      const ids = await connection.invoke<number[]>("GetOnlineEmployeeIds")
      if (isActive()) setOnline(new Set(ids))
    },
    onConnected: ({ connectionId }) => {
      connRef.current = connectionId
    },
    onClosed: () => {
      connRef.current = null
      setOnline(EMPTY_SET)
    },
  })

  return online
}
