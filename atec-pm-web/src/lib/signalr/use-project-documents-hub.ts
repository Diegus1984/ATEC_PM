import type { DocumentsChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) e richiama `onChange` quando un
 * altro utente modifica i documenti (file/cartelle) della commessa `projectId`.
 * Riusa lo stesso hub di `useProjectHub` ma ascolta l'evento `DocumentsChanged`.
 *
 * - `projectId` numerico → `JoinProject(id)`; `null` → nessuna connessione.
 * - Eventi debounced per coalescere i burst (es. upload multiplo); quelli di altre
 *   commesse vengono scartati prima del debounce.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useProjectDocumentsHub(
  projectId: number | null,
  onChange: (change: DocumentsChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled: projectId != null && projectId > 0,
    deps: [projectId],
    subscribe: (on) =>
      on("DocumentsChanged", (change: DocumentsChange) => handlerRef.current(change), {
        when: (change) => change.projectId === projectId,
      }),
    join: (connection) => connection.invoke("JoinProject", projectId),
  })
}
