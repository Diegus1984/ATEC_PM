// ── DDP Officina: conversioni, etichette e costruzione righe (nessun React) ──

import type { OfficinaItem, OfficinaItemSaveRequest } from "@/lib/api/types"

export const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  createdAt: "Data",
  requestedBy: "Rich.",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  quantityProduced: "Prodotti",
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
  material: "Materiale",
  treatment: "Trattamento",
  supplierName: "Fornitore",
  itemStatus: "Stato",
  daneaRef: "N° Ordine",
  dateNeeded: "Necessario",
  orderDate: "Data ordine",
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
    unitCost: item.unitCost,
    material: item.material,
    treatment: item.treatment,
    supplierId: item.supplierId,
    supplierName: item.supplierName,
    itemStatus: item.itemStatus,
    requestedBy: item.requestedBy,
    daneaRef: item.daneaRef,
    dateNeeded: item.dateNeeded,
    orderDate: item.orderDate,
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
  const set = new Set<number>()
  for (const item of list) {
    if (item.parentOfficinaItemId != null) set.add(item.parentOfficinaItemId)
  }
  return set
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
  const parents = list.filter((it) => it.parentOfficinaItemId == null)
  const children = list.filter((it) => it.parentOfficinaItemId != null)

  const childrenMap: Record<number, OfficinaItem[]> = {}
  for (const child of children) {
    const pid = child.parentOfficinaItemId!
    if (!childrenMap[pid]) childrenMap[pid] = []
    childrenMap[pid].push(child)
  }
  for (const pid of Object.keys(childrenMap)) {
    childrenMap[Number(pid)].sort((a, b) =>
      (a.partNumber || "").localeCompare(b.partNumber || "", undefined, {
        numeric: true,
        sensitivity: "base",
      })
    )
  }

  // Lookup separato dei figli: serve al rollup dei costi anche dopo lo svuotamento
  // di childrenMap fatto durante l'ordinamento.
  const childrenByParent: Record<number, OfficinaItem[]> = {}
  for (const child of children) {
    const pid = child.parentOfficinaItemId!
    if (!childrenByParent[pid]) childrenByParent[pid] = []
    childrenByParent[pid].push(child)
  }

  const sortedList: OfficinaItem[] = []
  for (const parent of parents) {
    sortedList.push(parent)
    const pChildren = childrenMap[parent.id]
    if (pChildren) {
      sortedList.push(...pChildren)
      delete childrenMap[parent.id]
    }
  }
  for (const orphans of Object.values(childrenMap)) sortedList.push(...orphans)

  let parentCount = 0
  return sortedList.map((item) => {
    let displayIndex = "•"
    if (item.parentOfficinaItemId == null) {
      parentCount++
      displayIndex = String(parentCount)
    }

    let unitCost = item.unitCost
    if (parentIdsWithChildren.has(item.id)) {
      unitCost = (childrenByParent[item.id] ?? []).reduce(
        (sum, child) => sum + child.unitCost * (child.compositionQty ?? 1),
        0
      )
    }

    return {
      ...item,
      rowNumber: displayIndex,
      unitCost,
      totalCost: unitCost * item.quantity,
    }
  })
}
