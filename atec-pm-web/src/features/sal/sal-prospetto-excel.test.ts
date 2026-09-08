import { describe, expect, it } from "vitest"

import { buildProspettoExcelTable } from "@/features/sal/sal-prospetto-excel"
import type { SalProspettoRow } from "@/lib/api/types"

// #150: il CSV scriveva i numeri col punto e Excel italiano li leggeva come migliaia. Ora
// la tabella porta i VALORI (numeri, ISO) con il tipo di colonna: il formato lo fa il server.
function riga(over: Partial<SalProspettoRow>): SalProspettoRow {
  return {
    projectId: 1,
    code: "C260415_203",
    cliente: "SOLE ODERZO S.r.l.",
    step: "Step 5 agosto 2026",
    perc: 18.373443646,
    condizione: "Ric.Fatt.FM",
    dataFatt: "2026-08-05T00:00:00",
    importo: 26205.1240001075,
    ord: 1,
    alert: "incasso",
    ggSaldo: 26,
    dataSaldo: "2026-08-31T00:00:00",
    stato: "emessa",
    pagamento: "",
    ...over,
  }
}

describe("buildProspettoExcelTable (#150)", () => {
  it("colonne tipizzate: percentuale, importo in euro, date; stesse etichette della stampa", () => {
    const t = buildProspettoExcelTable([riga({})], true)

    expect(t.foglio).toBe("Prospetto SAL")
    expect(t.colonne.map((c) => c.etichetta)).toEqual([
      "Segnalazione", "Commessa", "Cliente", "Scad.", "Step SAL", "%", "Condizione",
      "Importo", "Ipotesi Fatturazione", "Data Prevista Saldo",
    ])
    expect(t.colonne.map((c) => c.tipo)).toEqual([
      "testo", "testo", "testo", "intero", "testo", "percento", "testo", "euro", "data", "data",
    ])
  })

  it("i valori partono grezzi: percentuale a 10 decimali in punti, importo intero, date ISO", () => {
    const t = buildProspettoExcelTable([riga({})], true)

    expect(t.righe).toHaveLength(1)
    expect(t.righe[0]).toEqual([
      "Fattura no incasso", "C260415_203", "SOLE ODERZO S.r.l.", 1, "Step 5 agosto 2026",
      18.373443646, "Ric.Fatt.FM", 26205.1240001075, "2026-08-05T00:00:00", "2026-08-31T00:00:00",
    ])
  })

  it("senza `sal.economics` la colonna Importo non esiste, né in testa né nelle righe", () => {
    const t = buildProspettoExcelTable([riga({ importo: null })], false)

    expect(t.colonne.map((c) => c.etichetta)).not.toContain("Importo")
    expect(t.righe[0]).toHaveLength(9)
    expect(t.righe[0][7]).toBe("2026-08-05T00:00:00")
  })

  it("mancanze → celle vuote (null), mai «—» o stringhe finte", () => {
    const t = buildProspettoExcelTable(
      [riga({ perc: null, importo: null, dataFatt: null, dataSaldo: null, cliente: "", step: "", condizione: "", alert: "" })],
      true
    )

    expect(t.righe[0]).toEqual(["In programma", "C260415_203", "", 1, "", null, "", null, null, null])
  })

  it("le etichette di segnalazione sono quelle della griglia", () => {
    const alerts: SalProspettoRow["alert"][] = ["incasso", "warn", "pre", "attesa", ""]
    const t = buildProspettoExcelTable(alerts.map((alert) => riga({ alert })), false)

    expect(t.righe.map((r) => r[0])).toEqual([
      "Fattura no incasso", "Scaduto", "Pre-warning", "Emessa – attesa incasso", "In programma",
    ])
  })

  it("il nome del foglio segue la vista (Warning Fatturazione / incasso)", () => {
    expect(buildProspettoExcelTable([], false, "Warning incasso").foglio).toBe("Warning incasso")
    expect(buildProspettoExcelTable([], false).righe).toEqual([])
  })
})
