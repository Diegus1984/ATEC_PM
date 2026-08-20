import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { fetchDdpUpdatedList, markDdpSeen } from "@/lib/api/ddp-manager"
import type { DdpUpdatedItem } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"

export const DDP_UPDATED_QUERY_KEY = ["ddp-manager", "updated-list"] as const

/**
 * Elenco delle DDP aggiornate dai colleghi negli ultimi N giorni e non ancora aperte
 * da chi guarda (#113, #114). Alimenta la card «DDP Commesse» della sezione
 * «Gestione Controlli» in Dashboard.
 *
 * Polling ogni 60 secondi, solo se l'utente ha accesso a `nav.gestore_ddp`: l'endpoint
 * risponde 403 a chi non ce l'ha, e sarebbe un 403 al minuto.
 */
export function useDdpUpdatedList(days: number = 7): DdpUpdatedItem[] {
  const query = useQuery({
    queryKey: [...DDP_UPDATED_QUERY_KEY, days],
    queryFn: () => fetchDdpUpdatedList(days),
    enabled: canAccessFeature("nav.gestore_ddp"),
    refetchInterval: 60000,
    refetchIntervalInBackground: false,
    staleTime: 0,
  })

  return query.data ?? []
}

/**
 * Registra la presa visione della DDP aperta (#114): la voce sparisce dall'elenco in
 * Dashboard finché un collega non la tocca di nuovo.
 *
 * Si segna **una volta per (commessa, tipo)** a ogni apertura della pagina, non a ogni
 * render: la chiamata parte da un effetto con guardia, altrimenti ogni refetch del
 * Gestore rimetterebbe l'ora avanti mentre la pagina resta lì aperta.
 */
export function useMarkDdpSeen(projectId: number, type: string) {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: () => markDdpSeen(projectId, type),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DDP_UPDATED_QUERY_KEY })
    },
  })

  // `mutate` cambia identità a ogni render di react-query: tenerlo in un ref evita che
  // l'effetto riparta per quel motivo (e con esso la scrittura sul database).
  const mutateRef = React.useRef(mutation.mutate)
  mutateRef.current = mutation.mutate

  React.useEffect(() => {
    if (!Number.isFinite(projectId) || projectId <= 0) return
    if (!canAccessFeature("nav.gestore_ddp")) return
    mutateRef.current()
  }, [projectId, type])
}
