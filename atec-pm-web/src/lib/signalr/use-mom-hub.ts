import type { MoMChange } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`) sul gruppo globale MoM (`JoinMoM`)
 * e richiama `onChange` quando un altro utente modifica un verbale (header, righe,
 * riordino). Le MoM di tipo RIUNIONE non hanno commessa, quindi il gruppo è globale.
 *
 * - `enabled: false` → nessuna connessione (pagina che non mostra MoM).
 * - Eventi debounced per coalescere i burst di autosave.
 * - Best-effort: se l'hub è spento la pagina resta usabile con l'Aggiorna manuale.
 */
export function useMoMHub(
  enabled: boolean,
  onChange: (change: MoMChange) => void
): void {
  const handlerRef = useLatestRef(onChange)
  useHubSubscription({
    hub: "project",
    enabled,
    deps: [],
    subscribe: (on) => on("MoMChanged", (change: MoMChange) => handlerRef.current(change)),
    join: (connection) => connection.invoke("JoinMoM"),
  })
}
