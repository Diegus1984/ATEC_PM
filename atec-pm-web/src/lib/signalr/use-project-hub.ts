import type { DdpChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

type ProjectHubScope = "all" | number | null

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) e richiama `onDdpChange` quando un
 * altro utente modifica una distinta DDP. Riproduce `ProjectHubClient` del WPF:
 * - scope `"all"` → `JoinAll` (Gestore DDP: tutte le commesse)
 * - scope `number` → `JoinProject` (Sintesi: una sola commessa)
 * - scope `null` → nessuna connessione
 *
 * Gli eventi sono debounced per coalescere i burst. Real-time best-effort: se l'hub è
 * spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useProjectHub(
  scope: ProjectHubScope,
  onDdpChange: (change: DdpChange) => void,
  /** Evento RDO Acquisti (`PurchaseRfqChanged`, solo gruppo "all"): refetch della lista RDO. */
  onPurchaseRfqChange?: () => void
): void {
  const handlerRef = useLatestRef(onDdpChange)
  const rfqHandlerRef = useLatestRef(onPurchaseRfqChange)
  useHubSubscription({
    hub: "project",
    enabled: scope != null,
    deps: [scope],
    subscribe: (on) => {
      on("DdpChanged", (change: DdpChange) => handlerRef.current(change))
      on("PurchaseRfqChanged", () => rfqHandlerRef.current?.())
    },
    join: (connection) =>
      scope === "all" ? connection.invoke("JoinAll") : connection.invoke("JoinProject", scope),
  })
}
