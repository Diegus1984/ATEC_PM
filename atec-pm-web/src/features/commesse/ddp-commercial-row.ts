import type { BomItemSaveRequest, DdpRowItem } from "@/lib/api/types"

/** Payload PUT per aggiornare una riga DDP commerciale (stato, destinazione o dialog modifica). */
export function ddpCommercialRowToSaveRequest(
  projectId: number,
  row: DdpRowItem,
  itemStatus: string,
  quantity?: number,
  destination?: string,
  destinationSpec?: string
): BomItemSaveRequest {
  return {
    id: row.id,
    projectId,
    catalogItemId: null,
    partNumber: row.partNumber,
    description: row.description,
    unit: row.unit || "PZ",
    quantity: quantity ?? row.quantity,
    unitCost: row.unitCost,
    supplierId: null,
    manufacturer: row.manufacturer ?? "",
    itemStatus,
    requestedBy: row.requestedBy ?? "",
    daneaRef: row.daneaRef ?? "",
    dateNeeded: row.dateNeeded,
    destination: destination ?? row.destination ?? "",
    destinationSpec: destinationSpec ?? row.destinationSpec ?? "",
    notes: row.notes ?? "",
    ddpType: "COMMERCIAL",
    expectedUpdatedAt: row.updatedAt ?? null,
  }
}
