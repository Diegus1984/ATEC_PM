// ── DDP Officina: conversioni, etichette e costruzione righe (nessun React) ──

import type { OfficinaItem, OfficinaItemSaveRequest } from "@/lib/api/types"
import { buildCompositionRows, collectParentIds } from "./ddp-composition-rows"

export const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  // Segnalazione #61: stessi nomi e stesso ordine delle DDP Excel — dopo la «#»
  // vengono «Inserito da» e «Data inserimento». «Creata da» è l'autore registrato
  // dal server, che resta anche se «Inserito da» viene corretto a mano.
  requestedBy: "Inserito da",
  createdAt: "Data inserimento",
  createdByName: "Creata da",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  quantityProduced: "Prodotti",
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
  material: "Materiale",
  treatment: "Trattamento",
  workType: "Tipo",
  supplierName: "Fornitore",
  itemStatus: "Stato",
  dateNeeded: "Data Richiesta",
  daneaRef: "Rif. Danea",
  orderDate: "Data ordine",
  deliveredAt: "Consegnato il",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  notes: "Note",
}

/** Stato del check «aggiorna prezzo Codex» nel dialogo di modifica (articolo con prezzo 0 nel Codex). */
export type CodexPriceInfo = {
  checked: boolean
  showCheckbox: boolean
  codexId: number | null
}

export function toForm(item: OfficinaItem): OfficinaItemSaveRequest {
  return {
    id: item.id,
    projectId: item.projectId,
    partNumber: item.partNumber,
    description: item.description,
    quantity: item.quantity,
    quantityProduced: item.quantityProduced ?? 0,
    workHours: item.workHours ?? null,
    hourlyRate: item.hourlyRate ?? null,
    unitCost: item.unitCost,
    material: item.material,
    treatment: item.treatment,
    supplierId: item.supplierId,
    supplierName: item.supplierName,
    itemStatus: item.itemStatus,
    workType: item.workType ?? "",
    requestedBy: item.requestedBy,
    createdByName: item.createdByName,
    createdAt: item.createdAt,
    daneaRef: item.daneaRef,
    dateNeeded: item.dateNeeded,
    orderDate: item.orderDate,
    deliveredAt: item.deliveredAt ?? null,
    destination: item.destination,
    destinationSpec: item.destinationSpec ?? "",
    notes: item.notes,
    expectedUpdatedAt: item.updatedAt,
    parentOfficinaItemId: item.parentOfficinaItemId,
  }
}

/** Riga di griglia: numero visualizzato + costi (il padre somma i suoi componenti). */
export type OfficinaRow = OfficinaItem & {
  rowNumber: string
  totalCost: number
}

/** Padri che hanno almeno un componente importato dalla composizione. */
export function collectParentIdsWithChildren(list: OfficinaItem[]): Set<number> {
  return collectParentIds(list, (item) => item.parentOfficinaItemId)
}

/**
 * Ordina la distinta padre→componenti (i figli per codice, gli orfani in coda) e
 * calcola i costi: il costo unitario di un padre con componenti è la somma dei
 * figli × la loro quantità di composizione.
 */
export function buildOfficinaRows(
  list: OfficinaItem[],
  parentIdsWithChildren: Set<number>
): OfficinaRow[] {
  // La meccanica sta in `ddp-composition-rows`, condivisa con la DDP Commerciale (#119):
  // qui si passa solo il campo padre di questa tabella. In officina il costo non è mai
  // nullo (niente filtro prezzi su questa griglia), quindi i null si richiudono a 0.
  return buildCompositionRows(
    list,
    (item) => item.parentOfficinaItemId,
    parentIdsWithChildren,
    (item) => item.unitCost
  ).map((row) => ({
    ...row,
    unitCost: row.unitCost ?? 0,
    totalCost: row.totalCost ?? 0,
  }))
}
