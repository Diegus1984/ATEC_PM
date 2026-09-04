import type { ProjectChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `projects-all`, per
 * l'ANAGRAFICA commesse: quando un collega ne crea, modifica o elimina una, gli elenchi
 * aperti (albero, Milestones, SAL, Lavorazioni, Check list, MoM) si ricaricano da soli.
 * Eventi debounced; best-effort: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useProjectsHub(
  enabled: boolean,
  onChange: (change: ProjectChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) =>
      on("ProjectsChanged", (change: ProjectChange) => handlerRef.current(change)),
    join: (connection) => connection.invoke("JoinProjects"),
  })
}
