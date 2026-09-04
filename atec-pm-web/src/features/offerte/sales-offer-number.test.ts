import { describe, expect, it } from "vitest"

import { chanceLabel, composeOfferNumber, pad3 } from "./sales-offer-number"

describe("numero composto dell'offerta", () => {
  it("è tipo + progressivo a 3 cifre + anno + sigla", () => {
    expect(composeOfferNumber("S", 97, 2026, "EC")).toBe("S097-2026-EC")
    expect(composeOfferNumber("R", 1, 2022, "FF")).toBe("R001-2022-FF")
    expect(composeOfferNumber("AMU", 164, 2026, "GS")).toBe("AMU164-2026-GS")
  })

  it("regge i pezzi mancanti: una bozza ha solo numero e anno", () => {
    expect(composeOfferNumber(null, 164, 2026, null)).toBe("164-2026-")
    expect(composeOfferNumber(undefined, undefined, undefined, undefined)).toBe("--")
  })

  it("non tronca i progressivi oltre le tre cifre", () => {
    expect(pad3(1234)).toBe("1234")
    expect(pad3(7)).toBe("007")
    expect(pad3(null)).toBe("")
  })
})

describe("chance", () => {
  it("distingue «non valutata» da zero", () => {
    // Sono due cose diverse: «non l'ho ancora stimata» non è «non la prendiamo».
    expect(chanceLabel(null)).toBe("Non valutata")
    expect(chanceLabel(0)).toBe("0%")
    expect(chanceLabel(70)).toBe("70%")
  })
})
