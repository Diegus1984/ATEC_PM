import type { ConfirmOptions } from "@/components/shared/confirm"
import type { DdpStatusItem } from "@/lib/api/types"

import { DDP_STATUS_CANCELLED } from "./ddp-constants"

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

/** Conferma annullamento riga DDP (non eliminazione fisica). */
export async function confirmDdpRowAnnul(
  confirm: ConfirmFn,
  statusMap: Map<string, DdpStatusItem>,
  rowLabel: string
): Promise<boolean> {
  const cancelledLabel =
    statusMap.get(DDP_STATUS_CANCELLED)?.label ?? "Annullato"

  return confirm({
    title: "Annullare la riga?",
    description: `La riga "${rowLabel}" verrà impostata in stato ${cancelledLabel}.\n\nNon verrà eliminata dalla distinta.`,
    confirmLabel: cancelledLabel,
    destructive: true,
  })
}

export { DDP_STATUS_CANCELLED }
