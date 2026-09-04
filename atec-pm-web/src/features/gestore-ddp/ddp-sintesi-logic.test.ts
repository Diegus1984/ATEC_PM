import { describe, expect, it } from "vitest"

import {
  aggOf,
  barWidthPercent,
  buildRipartizioneBars,
  isNonDefDest,
  mancantiFooterText,
  normDest,
  sectionKeyForStatus,
  type DdpStateSets,
} from "@/features/gestore-ddp/ddp-sintesi-logic"
import type { DdpStatusItem } from "@/lib/api/types"

// Le regole pure della Sintesi DDP (port delle Build* del WPF).
describe("destinazioni", () => {
  it("normDest: maiuscolo, spazi compattati, vuoto → NON DEFINITA", () => {
    expect(normDest("Gruppo  Pompa")).toBe("GRUPPO POMPA")
    expect(normDest("  gruppo pompa ")).toBe("GRUPPO POMPA")
    expect(normDest("")).toBe("NON DEFINITA")
    expect(normDest(null)).toBe("NON DEFINITA")
  })

  it("isNonDefDest intercetta le varianti", () => {
    expect(isNonDefDest("NON DEFINITA")).toBe(true)
    expect(isNonDefDest("Non destinata")).toBe(true)
    expect(isNonDefDest("NONDEF")).toBe(true)
    expect(isNonDefDest("GRUPPO POMPA")).toBe(false)
  })
})

describe("aggregazioni", () => {
  it("aggOf: configurata vince, mancante O vuota → fallback cablato", () => {
    const sets = new Map<string, Set<string>>([
      ["A2", new Set(["DISP"])],
      ["A3", new Set()],
    ])
    expect([...aggOf(sets, "A2", ["X"])]).toEqual(["DISP"])
    expect([...aggOf(sets, "A3", ["X", "Y"])]).toEqual(["X", "Y"])
    expect([...aggOf(sets, "A9", ["Z"])]).toEqual(["Z"])
  })

  it("sectionKeyForStatus: stati monovalenti prima delle aggregazioni, ASS mai in Magazzino", () => {
    const sets = {
      parziale: new Set(["PAR"]),
      stop: new Set(["SOSP", "ANN"]),
      delivered: new Set(["DISP", "CON", "COS", "ASS"]),
    } as unknown as DdpStateSets
    expect(sectionKeyForStatus("VER", sets)).toBe("ver")
    expect(sectionKeyForStatus("IO", sets)).toBe("tab")
    expect(sectionKeyForStatus("ASS", sets)).toBe("ass") // non «del», anche se sta in delivered
    expect(sectionKeyForStatus("PAR", sets)).toBe("par")
    expect(sectionKeyForStatus("SOSP", sets)).toBe("stop")
    expect(sectionKeyForStatus("CON", sets)).toBe("del")
    expect(sectionKeyForStatus("BOH", sets)).toBeUndefined()
  })
})

describe("buildRipartizioneBars", () => {
  const def = (statusKey: string, label: string) =>
    ({ id: 1, statusKey, label, colorBg: null, colorFg: null }) as unknown as DdpStatusItem
  const defs = new Map<string, DdpStatusItem>([
    ["IO", def("IO", "In ordine")],
    ["DISP", def("DISP", "Disponibile")],
  ])

  it("ordina per conteggio, poi per chiave; frazioni sul totale", () => {
    const bars = buildRipartizioneBars(
      [
        { statusKey: "IO", count: 1 },
        { statusKey: "DISP", count: 3 },
        { statusKey: "ANN", count: 1 },
      ],
      defs
    )
    expect(bars.map((b) => b.key)).toEqual(["DISP", "ANN", "IO"])
    expect(bars.map((b) => b.label)).toEqual(["Disponibile", "ANN", "In ordine"]) // senza anagrafica: la chiave
    expect(bars.map((b) => b.fraction)).toEqual([0.6, 0.2, 0.2])
    expect(bars[0].pct).toContain("60")
  })

  it("le righe senza causale diventano ND con etichetta parlante", () => {
    const [bar] = buildRipartizioneBars([{ statusKey: "", count: 2 }], defs)
    expect(bar.key).toBe("ND")
    expect(bar.label).toBe("Stato non valorizzato")
    expect(bar.fraction).toBe(1)
  })

  it("con totale zero nessuna divisione per zero", () => {
    expect(buildRipartizioneBars([{ statusKey: "IO", count: 0 }], defs)[0].fraction).toBe(0)
  })
})

describe("piccoli aiuti", () => {
  it("barWidthPercent: minimo 3% se c'è almeno una riga, 0 se nessuna", () => {
    expect(barWidthPercent(0.005, 1)).toBe(3)
    expect(barWidthPercent(0.5, 10)).toBe(50)
    expect(barWidthPercent(0.5, 0)).toBe(0)
  })

  it("mancantiFooterText: una formulazione sola, con la riga spenta al singolare", () => {
    const counts = { withMissing: 4, analyzed: 20, excluded: 3 }
    expect(mancantiFooterText(18, 0, counts)).toBe(
      "18 righe visualizzate · 4 con almeno un dato mancante su 20 analizzate (3 escluse per stato)."
    )
    expect(mancantiFooterText(17, 1, counts)).toContain("· 1 riga spenta ·")
    expect(mancantiFooterText(16, 2, counts)).toContain("· 2 righe spente ·")
  })
})
