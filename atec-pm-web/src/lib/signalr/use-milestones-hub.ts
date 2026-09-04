import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche alle milestone di una commessa
 * e richiama `onChange` quando un altro utente crea/modifica/elimina/riordina una milestone.
 * Gruppo `project-{id}` (stesso di chat/DDP/checklist). Eventi debounced. Best-effort.
 */
export function useMilestonesHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled: enabled && projectId > 0,
    deps: [projectId],
    subscribe: (on) => on("MilestonesChanged", () => handlerRef.current()),
    join: (connection) => connection.invoke("JoinProject", projectId),
  })
}
