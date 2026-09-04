import * as React from "react"
import type { HubConnection } from "@microsoft/signalr"

import { createHubConnection, startHub, stopHub, type HubName } from "@/lib/signalr/hubs"

/**
 * Attesa standard prima di richiamare la pagina: coalesce i burst (autosave, riordini
 * che mandano una PATCH per riga, import a raffica) in una ricarica sola.
 */
export const HUB_DEBOUNCE_MS = 400

export interface HubOnOptions<TArgs extends unknown[]> {
  /** Attesa in ms prima di richiamare `handler` con l'ULTIMO payload; `0` = subito. Default `HUB_DEBOUNCE_MS`. */
  debounceMs?: number
  /** Filtro valutato PRIMA del debounce: un evento scartato non tocca il timer (es. un'altra commessa). */
  when?: (...args: TArgs) => boolean
  /** Eventi che passano subito anche con il debounce acceso (es. la barra di avanzamento di un import). */
  immediate?: (...args: TArgs) => boolean
}

/** Registra un evento dell'hub (`connection.on`) con debounce per evento. */
export type HubOn = <TArgs extends unknown[] = []>(
  event: string,
  handler: (...args: TArgs) => void,
  options?: HubOnOptions<TArgs>
) => void

export interface HubConnectedInfo {
  connectionId: string | null
  /** `true` dopo una riconnessione automatica: l'id è nuovo e i gruppi vengono ripresi da `join`. */
  reconnected: boolean
}

export interface HubSubscriptionOptions {
  hub: HubName
  /** `false` → nessuna connessione aperta (la pagina non mostra quei dati). Default `true`. */
  enabled?: boolean
  /** Valori che, cambiando, chiudono e riaprono la connessione (la commessa, lo scope del gruppo…). */
  deps: React.DependencyList
  /** Registra gli eventi ascoltati; eseguito una volta per connessione. */
  subscribe: (on: HubOn, connection: HubConnection) => void
  /**
   * Eseguito dopo lo start E dopo ogni riconnessione automatica: tipicamente `JoinProject`
   * o `JoinXxx`, perché il server non ricorda i gruppi di una connessione caduta.
   * Best-effort: un errore viene ignorato. `isActive()` è `false` se nel frattempo l'effetto
   * è stato smontato (utile prima di un `setState` dopo un `invoke`).
   */
  join?: (connection: HubConnection, isActive: () => boolean) => Promise<unknown>
  /** Connessione (ri)stabilita: qui si legge il `connectionId` (self-exclusion delle mutazioni). */
  onConnected?: (info: HubConnectedInfo) => void
  /**
   * Connessione finita, per smontaggio o perché il server non risponde più: riportare lo
   * stato a «scollegato». Una volta sola per connessione; può arrivare anche senza che
   * `onConnected` sia mai stato chiamato (start fallito).
   */
  onClosed?: () => void
}

/**
 * Lo scheletro unico di tutti gli hook SignalR del client: apre la connessione, registra
 * gli eventi con debounce, entra nel gruppo dopo lo start e dopo ogni riconnessione,
 * chiude tutto allo smontaggio. Real-time best-effort: se l'hub è spento la pagina resta
 * usabile con l'Aggiorna manuale (e con `staleTime: 0` il refetch al focus fa il resto).
 *
 * Le opzioni si leggono sempre nell'ultima versione renderizzata (ref), quindi i chiamanti
 * non devono memoizzare nulla: solo `deps` (più `hub` ed `enabled`) decide quando la
 * connessione va rifatta.
 *
 * Ritorna il ref alla connessione corrente (`null` quando non c'è), per chi deve invocare
 * metodi dell'hub, es. `ChatTyping`.
 */
export function useHubSubscription(
  options: HubSubscriptionOptions
): React.RefObject<HubConnection | null> {
  const optionsRef = React.useRef(options)
  React.useEffect(() => {
    optionsRef.current = options
  })

  const connectionRef = React.useRef<HubConnection | null>(null)
  const enabled = options.enabled ?? true

  React.useEffect(() => {
    if (!enabled) return

    let disposed = false
    const timers = new Map<string, ReturnType<typeof setTimeout>>()
    const connection = createHubConnection(options.hub)
    connectionRef.current = connection

    const on = <TArgs extends unknown[]>(
      event: string,
      handler: (...args: TArgs) => void,
      opts?: HubOnOptions<TArgs>
    ): void => {
      const debounceMs = opts?.debounceMs ?? HUB_DEBOUNCE_MS
      connection.on(event, (...raw: unknown[]) => {
        if (disposed) return
        const args = raw as TArgs
        if (opts?.when && !opts.when(...args)) return
        if (debounceMs <= 0 || opts?.immediate?.(...args)) {
          handler(...args)
          return
        }
        const pending = timers.get(event)
        if (pending) clearTimeout(pending)
        timers.set(
          event,
          setTimeout(() => {
            timers.delete(event)
            handler(...args)
          }, debounceMs)
        )
      })
    }

    const isActive = () => !disposed
    const runJoin = async () => {
      const join = optionsRef.current.join
      if (!join) return
      try {
        await join(connection, isActive)
      } catch {
        /* best-effort */
      }
    }

    optionsRef.current.subscribe(on, connection)

    connection.onreconnected((connectionId) => {
      if (disposed) return
      optionsRef.current.onConnected?.({
        connectionId: connectionId ?? connection.connectionId ?? null,
        reconnected: true,
      })
      void runJoin()
    })
    connection.onclose(() => {
      // Dopo il cleanup lo ha già detto il cleanup stesso: una volta sola.
      if (disposed) return
      optionsRef.current.onClosed?.()
    })

    void (async () => {
      const ok = await startHub(connection)
      if (!ok || disposed) return
      optionsRef.current.onConnected?.({
        connectionId: connection.connectionId ?? null,
        reconnected: false,
      })
      await runJoin()
    })()

    return () => {
      disposed = true
      timers.forEach((timer) => clearTimeout(timer))
      timers.clear()
      connectionRef.current = null
      optionsRef.current.onClosed?.()
      void stopHub(connection)
    }
    // `deps` è la lista del chiamante (gruppo/commessa); il resto delle opzioni passa dal ref.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, options.hub, ...options.deps])

  return connectionRef
}

/**
 * Ultima versione di una callback, senza che un `onChange` nuovo a ogni render rimetta in
 * piedi la connessione. Aggiornato dopo il commit, come facevano gli hook prima di questo.
 */
export function useLatestRef<T>(value: T): React.RefObject<T> {
  const ref = React.useRef(value)
  React.useEffect(() => {
    ref.current = value
  }, [value])
  return ref
}
