// ── DDP Officina: conversioni, etichette e costruzione righe (nessun React) ──

import type { OfficinaItem, OfficinaItemSaveRequest } from "@/lib/api/types"

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
