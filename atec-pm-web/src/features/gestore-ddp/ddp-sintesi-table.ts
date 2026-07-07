import type { DdpRowItem } from "@/lib/api/types"
import { euro } from "@/lib/format"

export const COMMERCIAL_SINTESI_HEADERS = [
  "#",
  "Data",
  "Rich.",
  "Codice",
  "Descrizione",
  "Qtà",
  "UM",
  "Fornitore",
  "Produttore",
  "Stato",
  "Rif. Danea",
  "Data prev.",
  "Destinazione",
  "Specifica",
  "Note",
  "Costo un.",
  "Totale",
] as const

export const OFFICINA_SINTESI_HEADERS = [
  "#",
  "Data",
  "Rich.",
  "Codice",
  "Descrizione",
  "Qtà",
  "Materiale",
  "Trattamento",
  "Fornitore",
  "Stato",
  "Rif. Danea",
  "Necessario",
  "Destinazione",
  "Specifica",
  "Note",
  "Costo un.",
  "Totale",
] as const

export function sintesiTableHeaders(officina: boolean): readonly string[] {
  return officina ? OFFICINA_SINTESI_HEADERS : COMMERCIAL_SINTESI_HEADERS
}

function fmtDate(value: string | null): string {
  if (!value) return ""
  const [year, month, day] = value.slice(0, 10).split("-")
  return year && month && day ? `${day}/${month}/${year}` : ""
}

export function fmtQty(value: number): string {
  return value.toLocaleString("it-IT", { maximumFractionDigits: 2 })
}

export function isOverdueDate(value: string | null): boolean {
  if (!value) return false
  const today = new Date()
  const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(
    2,
    "0"
  )}-${String(today.getDate()).padStart(2, "0")}`
  return value.slice(0, 10) < todayIso
}

export function ddpRowToSintesiCells(
  row: DdpRowItem,
  officina: boolean,
  statoLabel: (key: string) => string,
  options?: { markOverdue?: boolean }
): string[] {
  const dateText = fmtDate(row.dateNeeded)
  const dateCol =
    options?.markOverdue && isOverdueDate(row.dateNeeded)
      ? `⚠ ${dateText}`
      : dateText

  if (officina) {
    return [
      String(row.rowNumber),
      fmtDate(row.createdAt),
      row.requestedBy,
      row.partNumber,
      row.description,
      fmtQty(row.quantity),
      row.material ?? "",
      row.treatment ?? "",
      row.supplierName,
      statoLabel(row.itemStatus),
      row.daneaRef,
      dateCol,
      row.destination,
      row.destinationSpec ?? "",
      row.notes,
      euro(row.unitCost),
      euro(row.totalCost),
    ]
  }

  return [
    String(row.rowNumber),
    fmtDate(row.createdAt),
    row.requestedBy,
    row.partNumber,
    row.description,
    fmtQty(row.quantity),
    row.unit,
    row.supplierName,
    row.manufacturer,
    statoLabel(row.itemStatus),
    row.daneaRef,
    dateCol,
    row.destination,
    row.destinationSpec ?? "",
    row.notes,
    euro(row.unitCost),
    euro(row.totalCost),
  ]
}
