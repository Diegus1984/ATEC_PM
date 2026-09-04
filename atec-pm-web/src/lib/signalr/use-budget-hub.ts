import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Bilancio commessa in ambiente condiviso: l'ordine cliente e il Totale Vendita sono
 * dati che più persone toccano. Chi ha aperto il tab «Preventivo vs Consuntivo» di una
 * commessa (gruppo `project-{id}`) ricarica quando qualcun altro li modifica.
 * Eventi debounced; best-effort: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useBudgetHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled: enabled && projectId > 0,
    deps: [projectId],
    subscribe: (on) => on("BudgetChanged", () => handlerRef.current()),
    join: (connection) => connection.invoke("JoinProject", projectId),
  })
}

/**
 * Stessa cosa per la pagina /bilancio cross-commessa: gruppo globale `projects-all`,
 * così l'elenco delle card si riallinea appena cambia l'economia di una commessa qualsiasi.
 */
export function useGlobalBudgetHub(enabled: boolean, onChange: () => void): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) => on("BudgetChanged", () => handlerRef.current()),
    join: (connection) => connection.invoke("JoinProjects"),
  })
}
