import type { ChecklistChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche alle attività Check list
 * e richiama `onChange` quando un altro utente crea/modifica/elimina un'attività.
 *
 * - Senza `projectId` → gruppo globale `checklist-all` (pagina PM aggregata).
 * - Con `projectId` → gruppo `project-{id}` (tab Check list nel dettaglio commessa).
 * - `enabled: false` → nessuna connessione.
 * - Eventi debounced per coalescere i burst di autosave.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useChecklistHub(
  enabled: boolean,
  onChange: (change: ChecklistChange) => void,
  projectId?: number
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [projectId],
    subscribe: (on) =>
      on("ChecklistChanged", (change: ChecklistChange) => handlerRef.current(change)),
    join: (connection) =>
      projectId && projectId > 0
        ? connection.invoke("JoinProject", projectId)
        : connection.invoke("JoinCheckList"),
  })
}
