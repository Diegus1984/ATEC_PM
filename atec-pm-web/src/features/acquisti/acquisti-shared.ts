// ── Inbox Acquisti: predicati di dominio, conteggi e raggruppamenti ────────

import type {
  AcquistiInboxItem,
  DdpStatusItem,
  PurchaseRfqDetail,
  PurchaseRfqListItem,
} from "@/lib/api/types"

export const VISIBLE_STATUSES = new Set(["VER", "CHEK", "RO", "DO", "IO"])
const TO_BUY_STATUSES = new Set(["VER", "CHEK", "DO"])

/** Stato riga normalizzato (maiuscolo, mai null): base di tutti i predicati di dominio. */
export function statusOf(item: { itemStatus?: string | null }): string {
  return (item.itemStatus ?? "").toUpperCase()
}

export function isVisible(item: AcquistiInboxItem): boolean {
  return VISIBLE_STATUSES.has(statusOf(item))
}

export function isToBuy(item: AcquistiInboxItem): boolean {
  return TO_BUY_STATUSES.has(statusOf(item))
}

export function normalizeAtec(code: string | undefined | null): string {
  return (code ?? "").replace(/\./g, "").trim()
}

/**
 * PREDICATO UNICO «per questa riga esiste già un ordine Danea?».
 * Marcatori equivalenti, ne basta uno: stato IO, IDDoc dell'ordine (0 = sentinella
 * del claim server, quindi `!= null`), Rif. Danea valorizzato. Unico punto da
 * aggiornare se il server aggiunge marcatori — prima la stessa domanda era scritta
 * con tre combinazioni diverse (griglia, footer RDO, gruppi ordine) e potevano divergere.
 */
export function rowHasDaneaOrder(item: {
  itemStatus?: string | null
  daneaRef?: string | null
  daneaOrderIdDoc?: number | null
}): boolean {
  return statusOf(item) === "IO" || item.daneaOrderIdDoc != null || !!item.daneaRef?.trim()
}

/** Riferimenti dell'ordine Danea di una RDO, presi dalla RDO stessa o dalle sue righe. */
export function rfqDaneaOrder(detail: PurchaseRfqDetail): {
  num: string | number | null
  idDoc: number | null
  exists: boolean
} {
  const num = detail.daneaOrderNum ?? detail.items.find((i) => i.daneaRef)?.daneaRef ?? null
  const idDoc =
    detail.daneaOrderIdDoc ?? detail.items.find((i) => i.daneaOrderIdDoc)?.daneaOrderIdDoc ?? null
  return { num, idDoc, exists: num != null || idDoc != null || detail.items.some(rowHasDaneaOrder) }
}

/** Chiave di ordinamento della colonna «Prossimo Passo»: stessi rami (e stesso
 *  ordine) del renderer della cella, così posizione ed etichetta non divergono. */
export function getSmartActionSortKey(item: AcquistiInboxItem): string {
  if (rowHasDaneaOrder(item)) return `3_IO_${item.daneaRef || ""}`
  if (item.inActiveRfq || statusOf(item) === "RO")
    return `2_RDO_${String(item.activeRfqId || 0).padStart(6, "0")}`
  if (statusOf(item) === "DO") return "1_DO"
  return `4_OTHER_${statusOf(item)}`
}

/**
 * Ordine «naturale» dell'Inbox: commessa, poi Prossimo Passo. È lo stesso ordine
 * che `buildProjectGroups` produce riga per riga, ma su una lista piatta: serve a
 * `useDeferredItemOrder` per sapere come rimettere tutto in fila su «Aggiorna».
 */
export function sortAcquistiByProjectAndAction(
  items: AcquistiInboxItem[]
): AcquistiInboxItem[] {
  const keyed = items.map((it) => ({ it, key: getSmartActionSortKey(it) }))
  keyed.sort(
    (a, b) =>
      a.it.projectCode.localeCompare(b.it.projectCode, "it") ||
      a.key.localeCompare(b.key)
  )
  return keyed.map((k) => k.it)
}

export interface StatusCountItem {
  key: string
  value: string
  label: string
  count: number
  colorBg?: string
  colorFg?: string
  sortOrder: number
}

/** Conteggio righe per stato arricchito da Conf. DDP e ordinato: usato sia dalla
 *  barra filtri di pagina sia dal combo di colonna (prima duplicato in due punti). */
export function buildStatusCounts(
  items: AcquistiInboxItem[],
  statusMap: Map<string, DdpStatusItem>
): StatusCountItem[] {
  const counts = new Map<string, number>()
  for (const row of items) {
    const key = row.itemStatus ?? ""
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return [...counts.entries()]
    .map(([key, count]) => {
      const def = statusMap.get(key)
      return {
        key,
        value: key,
        label: def?.label || key || "Senza stato",
        count,
        colorBg: def?.colorBg,
        colorFg: def?.colorFg,
        sortOrder: def?.sortOrder ?? Number.MAX_SAFE_INTEGER,
      }
    })
    .sort((a, b) => a.sortOrder - b.sortOrder || a.label.localeCompare(b.label, "it"))
}

export const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  // Stessi nomi delle DDP di commessa (segnalazione #61): sono le stesse righe viste
  // da qui, non possono chiamarsi in due modi.
  createdAt: "Data inserimento",
  requestedBy: "Inserito da",
  atecCode: "Cod. ATEC",
  partNumber: "Codice",
  description: "Descrizione",
  rfqAction: "RDO (oggetto)",
  quantity: "Qtà",
  unit: "UM",
  supplierName: "Fornitore",
  manufacturer: "Produttore",
  itemStatus: "Stato",
  smartAction: "Prossimo Passo",
  daneaRef: "Rif. Danea",
  dateNeeded: "Data Prevista",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  notes: "Note",
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
}

export interface GroupByProject {
  projectId: number
  projectCode: string
  projectTitle: string
  customerName: string
  items: AcquistiInboxItem[]
  totalQty: number
  totalEstCost: number
  rfqs: PurchaseRfqListItem[]
}

/** Gruppo ordine Danea: RDO chiuse con vincitore, stesso fornitore + stessa
 *  commessa, non ancora ordinate (regola: 1 ordine = 1 fornitore + 1 commessa). */
export interface OrderSupplierGroup {
  key: string
  supplierId: number
  supplierName: string
  projectId: number
  projectCode: string
  rfqs: PurchaseRfqListItem[]
  total: number
}

/** Costruisce i gruppi ordinabili da una lista di RDO (già limitata a una commessa). */
export function buildOrderGroups(rfqs: PurchaseRfqListItem[]): OrderSupplierGroup[] {
  const map = new Map<string, OrderSupplierGroup>()
  for (const r of rfqs) {
    // daneaOrderIdDoc != null copre anche la sentinella 0 del claim server
    // (ordine in corso da un altro utente, o fallito a metà): non riproporla.
    if (r.status !== "CLOSED" || r.daneaOrderNum != null || r.daneaOrderIdDoc != null) continue
    if (r.winnerSupplierId == null || r.winnerUnitPrice == null || r.projectId == null) continue
    const key = `${r.winnerSupplierId}|${r.projectId}`
    let g = map.get(key)
    if (!g) {
      g = {
        key,
        supplierId: r.winnerSupplierId,
        supplierName: r.winnerSupplierName,
        projectId: r.projectId,
        projectCode: r.projectCode,
        rfqs: [],
        total: 0,
      }
      map.set(key, g)
    }
    g.rfqs.push(r)
    g.total += r.totalQuantity * r.winnerUnitPrice
  }
  return [...map.values()].sort((a, b) => a.supplierName.localeCompare(b.supplierName, "it"))
}

/** Righe raggruppate per commessa, ordinate per codice; dentro ogni gruppo le
 *  righe seguono l'ordine della colonna «Prossimo Passo». */
export function buildProjectGroups(
  items: AcquistiInboxItem[],
  rfqsByProject: Map<number, PurchaseRfqListItem[]>,
  /** `keepItemOrder`: le righe arrivano già nell'ordine voluto (ordine congelato). */
  opts?: { keepItemOrder?: boolean }
): GroupByProject[] {
  const map = new Map<number, GroupByProject>()
  for (const item of items) {
    let g = map.get(item.projectId)
    if (!g) {
      g = {
        projectId: item.projectId,
        projectCode: item.projectCode,
        projectTitle: item.projectTitle || "",
        customerName: item.customerName || "",
        items: [],
        totalQty: 0,
        totalEstCost: 0,
        rfqs: rfqsByProject.get(item.projectId) ?? [],
      }
      map.set(item.projectId, g)
    }
    g.items.push(item)
    g.totalQty += item.quantity
    g.totalEstCost += (item.unitCost || 0) * item.quantity
  }
  const groups = [...map.values()].sort((a, b) =>
    a.projectCode.localeCompare(b.projectCode, "it")
  )
  if (opts?.keepItemOrder !== true) {
    for (const g of groups) {
      // Chiave calcolata una volta per riga, non a ogni confronto del sort.
      const keyed = g.items.map((it) => ({ it, key: getSmartActionSortKey(it) }))
      keyed.sort((a, b) => a.key.localeCompare(b.key))
      g.items = keyed.map((k) => k.it)
    }
  }
  return groups
}
