import { describe, expect, it } from "vitest"

import { dash, euro, fmt2, formatCodexCode, formatSize, parseDecimal, percent } from "@/lib/format"

// Le regole di casa sugli importi (memoria «web_importi_euro»): ogni importo passa da `euro()`.
describe("euro", () => {
  it("due decimali, virgola, punto delle migliaia SEMPRE, simbolo in coda", () => {
    expect(euro(6.07)).toBe("6,07 €")
    expect(euro(265.96)).toBe("265,96 €")
    // it-IT «vero» ometterebbe il punto sotto i 10.000: qui è voluto che ci sia.
    expect(euro(4000)).toBe("4.000,00 €")
    expect(euro(1234567.891)).toBe("1.234.567,89 €")
    expect(euro(0)).toBe("0,00 €")
  })

  it("negativi col segno davanti, nullish e NaN col trattino", () => {
    expect(euro(-1500.5)).toBe("-1.500,50 €")
    expect(euro(null)).toBe("—")
    expect(euro(undefined)).toBe("—")
    expect(euro(Number.NaN)).toBe("—")
  })
})

describe("percent", () => {
  it("due decimali, virgola, simbolo attaccato; non calcolabile ≠ 0%", () => {
    expect(percent(18.42)).toBe("18,42%")
    expect(percent(-3.1)).toBe("-3,10%")
    expect(percent(100)).toBe("100,00%")
    expect(percent(null)).toBe("—")
    expect(percent(Number.NaN)).toBe("—")
  })
})

describe("parseDecimal", () => {
  it("con la virgola i punti sono migliaia, senza virgola il punto è decimale", () => {
    expect(parseDecimal("1.234,50")).toBe(1234.5)
    expect(parseDecimal("1234.5")).toBe(1234.5)
    expect(parseDecimal("12,5")).toBe(12.5)
    expect(parseDecimal("1.234.567,89")).toBe(1234567.89)
  })

  it("ignora spazi e simbolo €, e non esplode sul vuoto o sul testo", () => {
    expect(parseDecimal(" 4.000,00 € ")).toBe(4000)
    expect(parseDecimal("")).toBe(0)
    expect(parseDecimal(null)).toBe(0)
    expect(parseDecimal("abc")).toBe(0)
  })

  it("va e torna con euro()", () => {
    for (const v of [0, 6.07, 4000, 265.96, 1234567.89]) {
      expect(parseDecimal(euro(v))).toBe(v)
    }
  })
})

describe("formatCodexCode", () => {
  it("un solo punto prima delle ultime tre cifre, qualunque sia l'ingresso", () => {
    expect(formatCodexCode("101170426001")).toBe("101170426.001")
    expect(formatCodexCode("101.170.426.001")).toBe("101170426.001")
    expect(formatCodexCode("101170426.001")).toBe("101170426.001")
    expect(formatCodexCode(" 101170426001 ")).toBe("101170426.001")
  })

  it("codici corti e vuoti restano come sono", () => {
    expect(formatCodexCode("123")).toBe("123")
    expect(formatCodexCode("")).toBe("")
    expect(formatCodexCode(null)).toBe("")
  })
})

describe("fmt2, dash, formatSize", () => {
  it("fmt2 è all'italiana con due decimali", () => {
    expect(fmt2(0.5)).toBe("0,50")
    expect(fmt2(12345.5)).toBe("12.345,50")
  })

  it("dash mette il trattino solo su vuoto o spazi", () => {
    expect(dash("testo")).toBe("testo")
    expect(dash("   ")).toBe("—")
    expect(dash(null)).toBe("—")
  })

  it("formatSize sceglie l'unità", () => {
    expect(formatSize(512)).toBe("512 B")
    expect(formatSize(2048)).toBe("2.0 KB")
    expect(formatSize(3 * 1024 * 1024)).toBe("3.0 MB")
  })
})
