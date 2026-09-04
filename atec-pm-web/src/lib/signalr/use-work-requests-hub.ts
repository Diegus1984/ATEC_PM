import type { WorkRequestsChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche alle Lavorazioni
 * e richiama `onChange` quando un altro utente crea/modifica/elimina una lavorazione.
 *
 * - Senza `projectId` → gruppo globale `workrequests-all` (Pannello Lavorazioni).
 * - Con `projectId` → gruppo `project-{id}` (griglia nel dettaglio commessa).
 * - `enabled: false` → nessuna connessione.
 * - Eventi debounced per coalescere i burst di autosave.
 * - Best-effort: se l'hub è spento la pagina resta usabile con il refetch manuale.
 */
export function useWorkRequestsHub(
  enabled: boolean,
  onChange: (change: WorkRequestsChange) => void,
  projectId?: number
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [projectId],
    subscribe: (on) =>
      on("WorkRequestsChanged", (change: WorkRequestsChange) => handlerRef.current(change)),
    join: (connection) =>
      projectId && projectId > 0
        ? connection.invoke("JoinProject", projectId)
        : connection.invoke("JoinWorkRequests"),
  })
}
