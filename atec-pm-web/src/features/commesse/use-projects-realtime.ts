import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"

import type { ProjectChange } from "@/lib/api/types"
import { useProjectsHub } from "@/lib/signalr/use-projects-hub"

/**
 * Elenchi che mostrano le commesse: alla creazione/modifica/eliminazione di una commessa
 * da parte di un collega vanno tutti ricaricati. Sono chiavi react-query di primo livello:
 * l'invalidazione per prefisso porta con sé le varianti (filtri, ricerca, pagine).
 *
 * Se nasce un nuovo elenco di commesse, si aggiunge qui la sua chiave.
 */
const PROJECT_LIST_QUERY_KEYS: readonly (readonly unknown[])[] = [
  ["projects-tree"], // albero commesse (infinite query)
  ["projects"], // Pannello Lavorazioni
  ["pm-sal-projects"], // SAL
  ["pm-milestones-projects"], // Milestones
  ["mom-projects"], // Verbali MoM
  ["checklist", "lookups", "projects"], // Check list (barra laterale + combo)
  ["checklist", "board"], // Check list (tabelle per commessa)
  ["sal-summary"], // sidebar SAL
  // Cash Flow / Analisi SAL (#126): il perimetro è l'insieme delle commesse ATTIVE,
  // quindi un cambio di stato commessa deve rinfrescare anche i totali economici
  // (le mutazioni SAL vere passano già da GlobalSalChanged).
  ["sal", "economics"],
  ["milestones-summary"], // sidebar Milestones
  ["dashboard"], // riquadro commesse in home
  ["dashboard-folders"], // cartelle della home (anche la spunta «In dashboard» passa di qui)
]

/**
 * Real-time dell'anagrafica commesse: si monta UNA volta nella shell, così ogni pagina
 * con un elenco di commesse resta allineata senza doversi sottoscrivere da sé. Una
 * commessa eliminata da un collega sparisce dagli elenchi in ~1s.
 *
 * `onChange` opzionale: serve a chi deve reagire oltre al refresh (es. la pagina Commesse
 * chiude il dettaglio se la commessa aperta è stata eliminata).
 */
export function useProjectsRealtime(onChange?: (change: ProjectChange) => void): void {
  const queryClient = useQueryClient()
  const onChangeRef = React.useRef(onChange)
  React.useEffect(() => {
    onChangeRef.current = onChange
  }, [onChange])

  const handle = React.useCallback(
    (change: ProjectChange) => {
      for (const key of PROJECT_LIST_QUERY_KEYS) {
        void queryClient.invalidateQueries({ queryKey: key as unknown[] })
      }
      onChangeRef.current?.(change)
    },
    [queryClient]
  )

  useProjectsHub(true, handle)
}
