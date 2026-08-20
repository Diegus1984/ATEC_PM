import type { BomItemSaveRequest, DdpRowItem } from "@/lib/api/types"

/** Campi modificabili inline/da dialog; quelli assenti restano al valore della riga. */
export interface DdpCommercialRowOverrides {
  itemStatus?: string
  quantity?: number
  /** Presente = la PUT aggiorna il costo unitario (`updateUnitCost: true`); assente = resta quello a DB. */
  unitCost?: number
  destination?: string
  destinationSpec?: string
  daneaRef?: string
  /** Presente (anche `null` = data rimossa) = la PUT aggiorna la data prevista. */
  dateNeeded?: string | null
  notes?: string
  /** «Inserito da» (#61): presente = la PUT riscrive l'autore della riga. */
  requestedBy?: string
  /** Presente = la PUT aggiorna il fornitore (`updateSupplier: true`); `id: null` = nessun fornitore. */
  supplier?: { id: number | null }
}

/** Payload PUT per aggiornare una riga DDP commerciale (edit inline o dialog modifica). */
export function ddpCommercialRowToSaveRequest(
  projectId: number,
  row: DdpRowItem,
  overrides: DdpCommercialRowOverrides = {}
): BomItemSaveRequest {
  return {
    id: row.id,
    projectId,
    catalogItemId: row.catalogItemId ?? null,
    partNumber: row.partNumber,
    description: row.description,
    unit: row.unit || "PZ",
    quantity: overrides.quantity ?? row.quantity,
    unitCost: overrides.unitCost ?? row.unitCost,
    supplierId: overrides.supplier ? overrides.supplier.id : (row.supplierId ?? null),
    // Il server tocca supplier_id solo se richiesto esplicitamente: gli altri edit non lo azzerano.
    updateSupplier: overrides.supplier !== undefined,
    // Stessa regola per il costo: senza override il campo viaggia solo come eco della riga.
    updateUnitCost: overrides.unitCost !== undefined,
    manufacturer: row.manufacturer ?? "",
    itemStatus: overrides.itemStatus ?? row.itemStatus,
    requestedBy: overrides.requestedBy ?? row.requestedBy ?? "",
    daneaRef: overrides.daneaRef ?? row.daneaRef ?? "",
    dateNeeded:
      overrides.dateNeeded !== undefined ? overrides.dateNeeded : row.dateNeeded,
    destination: overrides.destination ?? row.destination ?? "",
    destinationSpec: overrides.destinationSpec ?? row.destinationSpec ?? "",
    notes: overrides.notes ?? row.notes ?? "",
    ddpType: "COMMERCIAL",
    atecCode: row.atecCode ?? "",
    expectedUpdatedAt: row.updatedAt ?? null,
  }
}
