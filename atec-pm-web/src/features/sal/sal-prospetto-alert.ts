// Le segnalazioni del Prospetto SAL: rango e etichetta di ogni alert. Senza React, così le
// usano anche i moduli puri (export Excel) e i loro test.

import type { SalProspettoRow } from "@/lib/api/types"

/** Ordine di gravità della segnalazione (per ordinamento colonna). */
export const salProspettoAlertRank = (a: string): number =>
  a === "incasso" ? 0 : a === "warn" ? 1 : a === "pre" ? 2 : a === "attesa" ? 3 : 4

export function salProspettoAlertLabel(a: SalProspettoRow["alert"] | string): string {
  switch (a) {
    case "incasso":
      return "Fattura no incasso"
    case "warn":
      return "Scaduto"
    case "pre":
      return "Pre-warning"
    case "attesa":
      return "Emessa – attesa incasso"
    default:
      return "In programma"
  }
}
