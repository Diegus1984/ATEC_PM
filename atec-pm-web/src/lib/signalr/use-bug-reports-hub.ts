import type { BugReportChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo globale `bugs-all`, per le
 * segnalazioni: la lista si aggiorna da sola quando un collega ne apre una o quando
 * l'ADMIN cambia stato. Best-effort: se l'hub è spento resta l'Aggiorna manuale.
 */
export function useBugReportsHub(
  enabled: boolean,
  onChange: (change: BugReportChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) =>
      on("BugReportsChanged", (change: BugReportChange) => handlerRef.current(change)),
    join: (connection) => connection.invoke("JoinBugReports"),
  })
}
