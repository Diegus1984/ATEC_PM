import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"

import {
  fetchDdpRowOff,
  resetDdpRowOff,
  setDdpRowOff,
  setDdpRowOffBulk,
} from "@/lib/api/ddp-row-off"
import { notifyError } from "@/lib/toast"

export interface DdpRowOffApi {
  /** true = riga spenta (esclusa dalle stampe / nascosta in Dati Mancanti). */
  isOff: (sectionKey: string, rowId: number) => boolean
  toggle: (sectionKey: string, rowId: number, off: boolean) => void
  setAll: (sectionKey: string, rowIds: number[], off: boolean) => void
  reset: (sectionKey?: string) => Promise<void>
  offCount: (sectionKey: string) => number
  /** Filtro pronto per le stampe: tiene solo le righe accese. */
  onlyOn: <T extends { id: number }>(sectionKey: string, rows: T[]) => T[]
  /** Rilettura da chiamare quando arriva l'evento realtime della commessa. */
  refetch: () => void
}

/**
 * Righe spente di una DDP, condivise fra utenti (tabella `ddp_row_off`).
 * Si aggiorna dopo le proprie scritture e, tramite `refetch`, quando un altro utente
 * spegne o riaccende righe (evento SignalR della commessa).
 */
export function useDdpRowOff(projectId: number, ddpType: string): DdpRowOffApi {
  const queryClient = useQueryClient()
  const queryKey = React.useMemo(
    () => ["ddp-row-off", projectId, ddpType],
    [projectId, ddpType]
  )

  const query = useQuery({
    queryKey,
    queryFn: () => fetchDdpRowOff(projectId, ddpType),
    enabled: Number.isFinite(projectId),
    staleTime: 0,
  })

  const offSet = React.useMemo(() => {
    const set = new Set<string>()
    for (const item of query.data ?? []) set.add(`${item.sectionKey}|${item.itemId}`)
    return set
  }, [query.data])

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
  }, [queryClient, queryKey])

  const isOff = React.useCallback(
    (sectionKey: string, rowId: number) => offSet.has(`${sectionKey}|${rowId}`),
    [offSet]
  )

  const toggle = React.useCallback(
    (sectionKey: string, rowId: number, off: boolean) => {
      void (async () => {
        try {
          await setDdpRowOff(projectId, ddpType, sectionKey, rowId, off)
          invalidate()
        } catch (error) {
          notifyError(error)
        }
      })()
    },
    [projectId, ddpType, invalidate]
  )

  const setAll = React.useCallback(
    (sectionKey: string, rowIds: number[], off: boolean) => {
      if (rowIds.length === 0) return
      void (async () => {
        try {
          await setDdpRowOffBulk(projectId, ddpType, sectionKey, rowIds, off)
          invalidate()
        } catch (error) {
          notifyError(error)
        }
      })()
    },
    [projectId, ddpType, invalidate]
  )

  const reset = React.useCallback(
    async (sectionKey?: string) => {
      try {
        await resetDdpRowOff(projectId, ddpType, sectionKey)
        invalidate()
      } catch (error) {
        notifyError(error)
      }
    },
    [projectId, ddpType, invalidate]
  )

  const offCount = React.useCallback(
    (sectionKey: string) => {
      const prefix = `${sectionKey}|`
      let count = 0
      for (const key of offSet) if (key.startsWith(prefix)) count++
      return count
    },
    [offSet]
  )

  const onlyOn = React.useCallback(
    <T extends { id: number }>(sectionKey: string, rows: T[]) =>
      rows.filter((row) => !offSet.has(`${sectionKey}|${row.id}`)),
    [offSet]
  )

  return { isOff, toggle, setAll, reset, offCount, onlyOn, refetch: invalidate }
}
