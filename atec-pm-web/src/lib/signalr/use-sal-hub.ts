import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per le modifiche al modulo SAL
 * e richiama `onChange` quando un altro utente crea/modifica/elimina/riordina/precarica dati SAL.
 * Gruppo `project-{id}`. Eventi debounced. Best-effort.
 */
export function useSalHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled: enabled && projectId > 0,
    deps: [projectId],
    subscribe: (on) => on("SalChanged", () => handlerRef.current()),
    join: (connection) => connection.invoke("JoinProject", projectId),
  })
}

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) per gli aggiornamenti SAL globali
 * e richiama `onChange` quando una commessa qualsiasi subisce modifiche SAL (GlobalSalChanged).
 * Nessun gruppo: il server lo manda a tutte le connessioni (`Clients.All`).
 */
export function useGlobalSalHub(
  enabled: boolean,
  onChange: () => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) => on("GlobalSalChanged", () => handlerRef.current()),
  })
}
