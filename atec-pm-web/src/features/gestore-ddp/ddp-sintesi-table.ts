import type { DdpRowItem } from "@/lib/api/types"
import { euro } from "@/lib/format"

export const COMMERCIAL_SINTESI_HEADERS = [
  "#",
  // «Rich.» / «Data» → «Inserito da» / «Data inserimento» (segnalazione #61): stessi nomi
  // e stesso ordine delle DDP Excel e delle due griglie di commessa. Lo scambio riguarda
  // solo le posizioni 1 e 2, quindi DUE_DATE_CELL_INDEX resta valido.
  "Inserito da",
  "Data inserimento",
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
  "Inserito da",
  "Data inserimento",
  "Codice",
  "Descrizione",
  "Qtà",
  "Materiale",
  "Trattamento",
  "Fornitore",
  "Stato",
  "Rif. Danea",
  // «Necessario» → «Data Richiesta» (segnalazione #58): stesso nome della DDP Officina di
  // commessa e della Inbox. Vale anche per stampe ed export, che escono da qui.
  "Data Richiesta",
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

/**
 * In ritardo = consegna prevista PRIMA di oggi (una prevista oggi non è in ritardo).
 * `today` arriva dal modello: un solo orologio per KPI, celle e stampe.
 */
export function isOverdueDate(value: string | null, today: string): boolean {
  if (!value) return false
  return value.slice(0, 10) < today
}

/**
 * Indice della colonna «Data prev.» / «Data Richiesta» (uguale nei due layout): è la cella che
 * va colorata di rosso quando la consegna è in ritardo. Il ritardo si segnala con una
 * **classe sulla cella**, non con un prefisso nel testo — altrimenti finisce negli export.
 */
export const DUE_DATE_CELL_INDEX = 11

export function ddpRowToSintesiCells(
  row: DdpRowItem,
  officina: boolean,
  statoLabel: (key: string) => string,
  /** Formato date: 4 cifre per stampe ed export (default), gg/mm/aa a video. */
  formatDate: (value: string | null) => string = fmtDate
): string[] {
  const dateCol = formatDate(row.dateNeeded)

  if (officina) {
    return [
      String(row.rowNumber),
      row.requestedBy,
      formatDate(row.createdAt),
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
    row.requestedBy,
    formatDate(row.createdAt),
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
