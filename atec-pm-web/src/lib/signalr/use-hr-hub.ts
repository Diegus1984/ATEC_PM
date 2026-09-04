import type { HrChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `hr-all`, e richiama
 * `onChange` quando qualcosa del modulo presenze cambia: import da Ecos (anche quello
 * automatico delle 12 ore), rettifiche, solleciti, causali, richieste di assenza,
 * mappatura, credenziali.
 *
 * - `enabled: false` → nessuna connessione.
 * - Eventi debounced per coalescere i burst (un import ne manda a raffica).
 *   `import-progress` NON è debounced: è la barra di avanzamento, deve muoversi subito.
 * - Dopo una disconnessione si può essere persi qualcosa: arriva un `reconnected` e la
 *   pagina rilegge tutto.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'aggiornamento manuale
 *   e il refetch-on-focus di react-query.
 */
export function useHrHub(enabled: boolean, onChange: (change: HrChange) => void): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) =>
      on("HrChanged", (change: HrChange) => handlerRef.current(change), {
        immediate: (change) => change.action === "import-progress",
      }),
    join: (connection) => connection.invoke("JoinHr"),
    onConnected: ({ reconnected }) => {
      if (reconnected) handlerRef.current({ action: "reconnected", employeeId: null, date: null })
    },
  })
}
