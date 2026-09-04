import { describe, expect, it } from "vitest"

import {
  compareProjectCodes,
  isCommessaCode,
  partitionCommesseAltreAttivita,
  projectCodeDate,
} from "@/lib/project-code"
import { wildcardMatch } from "@/lib/wildcard"

// Segnalazione #79: una sola regola ovunque per «commessa vera» vs «altra attività».
describe("projectCodeDate / isCommessaCode", () => {
  it("legge la data dai due formati in uso", () => {
    expect(projectCodeDate("C240318_146")).toBe("20240318") // vecchio gestionale
    expect(projectCodeDate("C20260731.001")).toBe("20260731") // formato attuale
    expect(projectCodeDate("C260814_900_1")).toBe("20260814")
    expect(projectCodeDate("C260814_TESTO")).toBe("20260814")
  })

  it("i codici a testo libero non sono commesse", () => {
    for (const code of ["INTERNA", "SERVICE _ SANGRATO", "C2603", "c260814_1", ""]) {
      expect(projectCodeDate(code)).toBe("")
      expect(isCommessaCode(code)).toBe(false)
    }
    expect(isCommessaCode("C20260731.001")).toBe(true)
  })

  it("nove cifre non sono una data", () => {
    expect(projectCodeDate("C202607311")).toBe("")
  })
})

describe("ordinamento e partizione", () => {
  it("le commesse per data crescente, a pari data per codice", () => {
    const codes = ["C20260731.002", "C240318_146", "C20260731.001", "C260814_900"]
    expect([...codes].sort(compareProjectCodes)).toEqual([
      "C240318_146",
      "C20260731.001",
      "C20260731.002",
      "C260814_900",
    ])
  })

  it("partiziona: commesse per data, altre attività in alfabetico", () => {
    const items = ["SERVICE _ SANGRATO", "C260814_900", "INTERNA", "C240318_146"]
    const { commesse, altreAttivita } = partitionCommesseAltreAttivita(items, (x) => x)
    expect(commesse).toEqual(["C240318_146", "C260814_900"])
    expect(altreAttivita).toEqual(["INTERNA", "SERVICE _ SANGRATO"])
  })
})

describe("wildcardMatch (port di WildcardMatcher)", () => {
  it("le quattro forme e il pattern vuoto", () => {
    expect(wildcardMatch("Gruppo Pompa", "")).toBe(true)
    expect(wildcardMatch("Gruppo Pompa", "pompa")).toBe(true) // testo semplice = contiene
    expect(wildcardMatch("Gruppo Pompa", "*pompa")).toBe(true) // finisce con
    expect(wildcardMatch("Gruppo Pompa", "gruppo*")).toBe(true) // inizia con
    expect(wildcardMatch("Gruppo Pompa", "*po*")).toBe(true) // contiene
    expect(wildcardMatch("Gruppo Pompa", "*gruppo")).toBe(false)
    expect(wildcardMatch("Gruppo Pompa", "pompa*")).toBe(false)
    expect(wildcardMatch(null, "x")).toBe(false)
  })
})
