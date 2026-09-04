import { describe, expect, it } from "vitest"

import { computeDist, distKey, type DistRow } from "@/features/preventivi/costing-distribution"

// Distribuzione di contingency e margine sulle righe del preventivo (fedele a CostingTreeControl).
function riga(over: Partial<DistRow> & { sale: number }): DistRow {
  return {
    rowType: "R",
    sectionId: 1,
    itemId: 0,
    name: "riga",
    contingencyPct: 0,
    marginPct: 0,
    contingencyPinned: false,
    marginPinned: false,
    isShadowed: false,
    ...over,
  }
}

describe("computeDist", () => {
  it("pesi proporzionali alla vendita, importi = peso × pool", () => {
    const out = computeDist([riga({ sectionId: 1, sale: 100 }), riga({ sectionId: 2, sale: 300 })], 40, 80)
    expect(out.map((r) => r.contingencyPct)).toEqual([0.25, 0.75])
    expect(out.map((r) => r.marginPct)).toEqual([0.25, 0.75])
    expect(out.map((r) => r.contingencyAmount)).toEqual([10, 30])
    expect(out.map((r) => r.marginAmount)).toEqual([20, 60])
    expect(out.map((r) => r.sectionTotal)).toEqual([130, 390])
  })

  it("una riga pinnata tiene la sua percentuale, il resto si spartisce quello che avanza", () => {
    const out = computeDist(
      [
        riga({ sectionId: 1, sale: 100, contingencyPct: 0.5, contingencyPinned: true }),
        riga({ sectionId: 2, sale: 300 }),
        riga({ sectionId: 3, sale: 100 }),
      ],
      100,
      0
    )
    expect(out[0].contingencyPct).toBe(0.5)
    expect(out[1].contingencyPct).toBe(0.375) // 0,5 × 300/400
    expect(out[2].contingencyPct).toBe(0.125)
    expect(out.map((r) => r.contingencyAmount)).toEqual([50, 37.5, 12.5])
  })

  it("una riga ombreggiata vale zero e la sua vendita si spalma sulle visibili", () => {
    const out = computeDist(
      [riga({ sectionId: 1, sale: 100 }), riga({ sectionId: 2, sale: 300 }), riga({ sectionId: 3, sale: 200, isShadowed: true })],
      0,
      0
    )
    const ombra = out[2]
    expect([ombra.contingencyPct, ombra.marginPct, ombra.sectionTotal, ombra.shadowedAmount]).toEqual([0, 0, 0, 0])
    expect(out.map((r) => r.shadowedAmount)).toEqual([50, 150, 0]) // 200 × 100/400, 200 × 300/400
    expect(out.map((r) => r.sectionTotal)).toEqual([150, 450, 0])
  })

  it("vendite a zero: la riga resta fuori dai pesi; senza vendite si divide in parti uguali", () => {
    const conZero = computeDist([riga({ sectionId: 1, sale: 0 }), riga({ sectionId: 2, sale: 50 })], 10, 10)
    expect(conZero[0].contingencyPct).toBe(0)
    expect(conZero[1].contingencyPct).toBe(1)

    const tutteZero = computeDist([riga({ sectionId: 1, sale: 0 }), riga({ sectionId: 2, sale: 0 })], 10, 10)
    expect(tutteZero.map((r) => r.contingencyPct)).toEqual([0, 0]) // sale 0 → saltate, niente da pesare
  })

  it("non tocca le righe in ingresso e la chiave è stabile", () => {
    const input = [riga({ sectionId: 1, sale: 100 })]
    computeDist(input, 10, 10)
    expect(input[0].contingencyPct).toBe(0)
    expect(distKey({ rowType: "M", sectionId: 7, itemId: 42 })).toBe("M_7_42")
  })
})
