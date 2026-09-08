// La tabella del Prospetto SAL pronta per l'export Excel (#150). Logica pura: colonne
// tipizzate + righe coi valori dei DTO; il formato (numeri, date, percentuali, filtri) lo
// mette il server. Le percentuali partono in punti (18,373443646) e gli importi interi: non
// si arrotonda qui, per non avere due regole.

import type { ExcelCell, ExcelColumn, ExcelTable } from "@/lib/api/export"
import type { SalProspettoRow } from "@/lib/api/types"

import { salProspettoAlertLabel } from "./sal-prospetto-alert"

/** Stesse etichette e stesso ordine della stampa e della griglia. */
export function buildProspettoExcelTable(
  rows: SalProspettoRow[],
  canSeeEconomics: boolean,
  foglio = "Prospetto SAL"
): ExcelTable {
  const colonne: ExcelColumn[] = [
    { etichetta: "Segnalazione", tipo: "testo" },
    { etichetta: "Commessa", tipo: "testo" },
    { etichetta: "Cliente", tipo: "testo" },
    { etichetta: "Scad.", tipo: "intero" },
    { etichetta: "Step SAL", tipo: "testo" },
    { etichetta: "%", tipo: "percento" },
    { etichetta: "Condizione", tipo: "testo" },
  ]
  // Gli importi li vede solo chi ha `sal.economics` (il server li manda già a null agli
  // altri, #91): senza la funzione la colonna non esiste proprio.
  if (canSeeEconomics) colonne.push({ etichetta: "Importo", tipo: "euro" })
  colonne.push(
    { etichetta: "Ipotesi Fatturazione", tipo: "data" },
    { etichetta: "Data Prevista Saldo", tipo: "data" }
  )

  const righe = rows.map((r) => {
    const cells: ExcelCell[] = [
      salProspettoAlertLabel(r.alert),
      r.code,
      r.cliente || "",
      r.ord,
      r.step || "",
      r.perc,
      r.condizione || "",
    ]
    if (canSeeEconomics) cells.push(r.importo)
    cells.push(r.dataFatt, r.dataSaldo)
    return cells
  })

  return { foglio, colonne, righe }
}
