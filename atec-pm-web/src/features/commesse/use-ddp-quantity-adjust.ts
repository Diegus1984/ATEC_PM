import * as React from "react"

import type { ConfirmOptions } from "@/components/shared/confirm"
import type { DdpStatusItem } from "@/lib/api/types"

import { DDP_STATUS_CANCELLED } from "./ddp-constants"

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

export interface DdpQuantityRow {
  quantity: number
  itemStatus: string
  partNumber?: string | null
  description?: string | null
}

export function useDdpQuantityAdjust<T extends DdpQuantityRow>({
  confirm,
  statusMap,
  isPending,
  excludedSet,
  onApply,
}: {
  confirm: ConfirmFn
  statusMap: Map<string, DdpStatusItem>
  isPending: boolean
  /** Stati «esclusi da totale/conteggi» (aggregazione A9): su queste righe la quantità è bloccata. */
  excludedSet?: Set<string>
  onApply: (item: T, patch: { quantity?: number; itemStatus?: string }) => void
}) {
  return React.useCallback(
    async (item: T, delta: -1 | 1) => {
      if (isPending) {
        return
      }

      // Riga in stato escluso (A9): per cambiare la quantità serve prima ripristinare uno stato attivo.
      if (excludedSet?.has(item.itemStatus)) {
        return
      }

      if (delta > 0) {
        onApply(item, { quantity: item.quantity + delta })
        return
      }

      if (item.quantity > 1) {
        onApply(item, { quantity: item.quantity - 1 })
        return
      }

      if (item.itemStatus === DDP_STATUS_CANCELLED) {
        return
      }

      const cancelledLabel =
        statusMap.get(DDP_STATUS_CANCELLED)?.label ?? "Annullato"
      const rowLabel =
        item.partNumber?.trim() ||
        item.description?.trim() ||
        "questa riga"

      const ok = await confirm({
        title: "Annullare la riga?",
        description: `La quantità è già 1.\n\nVuoi impostare "${rowLabel}" in stato ${cancelledLabel}?`,
        confirmLabel: cancelledLabel,
        destructive: true,
      })

      if (ok) {
        onApply(item, { itemStatus: DDP_STATUS_CANCELLED })
      }
    },
    [confirm, statusMap, isPending, excludedSet, onApply]
  )
}
