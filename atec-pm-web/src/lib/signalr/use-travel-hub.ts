import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Trasferta in ambiente condiviso: il piano lo compila il PM ma lo guardano in tanti, e
 * la griglia riga-persona è editabile da più postazioni. Chi ha aperto la pagina ricarica
 * quando qualcun altro tocca step o righe.
 *
 * `projectId` null = pagina cross-commessa (gruppo globale `projects-all`), così anche
 * l'elenco delle card si riallinea. Eventi debounced, best-effort come gli altri
 * hub: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useTravelHub(
  enabled: boolean,
  onChange: () => void,
  projectId: number | null
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [projectId],
    subscribe: (on) => on("TravelChanged", () => handlerRef.current()),
    join: (connection) =>
      projectId && projectId > 0
        ? connection.invoke("JoinProject", projectId)
        : connection.invoke("JoinProjects"),
  })
}
