import { describe, expect, it } from "vitest"

import { cashFlowTotals, classifySalRow, salChiuso, salYm, salYmLabel, salYmTitle } from "@/features/sal/sal-economics"
import type { SalEconomics, SalEconomicsHeader, SalEconomicsRow } from "@/lib/api/types"

// Regole v10 del Cash Flow SAL e del «SAL chiuso» (#134): scritte una volta sola, qui provate.
function riga(over: Partial<SalEconomicsRow>): SalEconomicsRow {
  return { projectId: 1, perc: 0, importo: 0, iva: 0, totIva: 0, stato: "", pagamento: "", ...over } as SalEconomicsRow
}

describe("salChiuso (#134)", () => {
  it("chiuso quando le percentuali pagate raggiungono il totale, con la tolleranza dei decimali", () => {
    expect(salChiuso(100, 100)).toBe(true)
    expect(salChiuso(100, 99.9999999)).toBe(true) // ultimo bit dopo il giro JSON
    expect(salChiuso(100, 99.99)).toBe(false)
    expect(salChiuso(100, 0)).toBe(false)
  })

  it("senza righe SAL non c'è niente da chiudere", () => {
    expect(salChiuso(0, 0)).toBe(false)
    expect(salChiuso(null, null)).toBe(false)
  })
})

describe("classifySalRow (bucket v10, mutuamente esclusivi)", () => {
  it("Pagata → incassate, emessa → emesse, tutto vuoto → da fatturare, il resto fuori", () => {
    expect(classifySalRow({ stato: "emessa", pagamento: "Pagata" })).toBe("inc")
    expect(classifySalRow({ stato: "", pagamento: "PAGATA" })).toBe("inc")
    expect(classifySalRow({ stato: "emessa", pagamento: "" })).toBe("em")
    expect(classifySalRow({ stato: "", pagamento: "" })).toBe("daf")
    expect(classifySalRow({ stato: "daEmettere", pagamento: "" })).toBeNull()
    expect(classifySalRow({ stato: "", pagamento: "Parzialmente Pagata" })).toBeNull()
  })
})

describe("cashFlowTotals", () => {
  it("i bucket sommano importo e totIva; Avere = Emesse + da Fatturare", () => {
    const data: SalEconomics = {
      headers: [{ projectId: 1, valore: 1000 }, { projectId: 2, valore: 500 }] as SalEconomicsHeader[],
      rows: [
        riga({ projectId: 1, perc: 50, importo: 500, iva: 110, totIva: 610, stato: "emessa", pagamento: "Pagata" }),
        riga({ projectId: 1, perc: 50, importo: 500, iva: 110, totIva: 610, stato: "emessa", pagamento: "" }),
        riga({ projectId: 2, perc: 100, importo: 500, iva: 110, totIva: 610, stato: "", pagamento: "" }),
      ],
    }
    const t = cashFlowTotals(data)
    expect(t.ordini).toEqual({ netto: 1500, conIva: 1500 + 330 })
    expect(t.incassate).toEqual({ netto: 500, conIva: 610 })
    expect(t.emesse).toEqual({ netto: 500, conIva: 610 })
    expect(t.daFatturare).toEqual({ netto: 500, conIva: 610 })
    expect(t.avere).toEqual({ netto: 1000, conIva: 1220 })
    expect(t.totaleEmesso).toEqual({ netto: 1000, conIva: 1220 })
    expect(t.ordiniEsclusi).toBe(0)
  })

  it("una commessa col SAL chiuso esce dal Totale Ordini (netto E IVA) ma resta nelle Incassate", () => {
    const data: SalEconomics = {
      headers: [{ projectId: 1, valore: 1000 }, { projectId: 2, valore: 500 }] as SalEconomicsHeader[],
      rows: [
        riga({ projectId: 1, perc: 100, importo: 1000, iva: 220, totIva: 1220, stato: "emessa", pagamento: "Pagata" }),
        riga({ projectId: 2, perc: 100, importo: 500, iva: 110, totIva: 610, stato: "emessa", pagamento: "" }),
      ],
    }
    const t = cashFlowTotals(data)
    expect(t.ordini).toEqual({ netto: 500, conIva: 610 })
    expect(t.ordiniEsclusi).toBe(1)
    expect(t.incassate).toEqual({ netto: 1000, conIva: 1220 }) // più grande degli ordini: voluto
  })
})

describe("mesi", () => {
  it("salYm / salYmLabel / salYmTitle", () => {
    expect(salYm("2026-01-15")).toBe("2026-01")
    expect(salYm(null)).toBeNull()
    expect(salYmLabel("2026-01")).toBe("gen 26")
    expect(salYmTitle("2026-12")).toBe("dicembre 2026")
    expect(salYmLabel("boh")).toBe("boh")
  })
})
