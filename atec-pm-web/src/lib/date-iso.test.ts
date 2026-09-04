import { describe, expect, it } from "vitest"

import {
  dateToIso,
  formatDateFull,
  formatDateOrDash,
  formatDateShort,
  isoToDate,
  toDateOnly,
} from "@/lib/date-iso"

// Regola di casa (memoria «web_date_format_short»): in griglia gg/mm/aa, nei documenti gg/mm/aaaa.
describe("isoToDate / dateToIso", () => {
  it("costruisce la data dai componenti locali: niente scivolamento di fuso", () => {
    const d = isoToDate("2026-09-04")!
    expect([d.getFullYear(), d.getMonth() + 1, d.getDate()]).toEqual([2026, 9, 4])
    // Anche con l'orario dietro: conta solo la parte data.
    expect(dateToIso(isoToDate("2026-09-04T23:59:59")!)).toBe("2026-09-04")
  })

  it("va e torna", () => {
    for (const iso of ["2026-01-01", "2026-02-28", "2026-12-31"]) {
      expect(dateToIso(isoToDate(iso)!)).toBe(iso)
    }
  })

  it("vuoto o malformato → undefined", () => {
    expect(isoToDate(null)).toBeUndefined()
    expect(isoToDate("")).toBeUndefined()
    expect(isoToDate("abc")).toBeUndefined()
  })

  it("toDateOnly taglia l'orario", () => {
    expect(toDateOnly("2026-09-04T10:30:00")).toBe("2026-09-04")
    expect(toDateOnly(null)).toBeNull()
  })
})

describe("formati della UI", () => {
  it("gg/mm/aa in griglia, gg/mm/aaaa nei documenti", () => {
    expect(formatDateShort("2026-09-04")).toBe("04/09/26")
    expect(formatDateShort(new Date(2026, 0, 5))).toBe("05/01/26")
    expect(formatDateFull("2026-09-04")).toBe("04/09/2026")
  })

  it("vuoto → stringa vuota, in cella → trattino", () => {
    expect(formatDateShort(null)).toBe("")
    expect(formatDateShort("non è una data")).toBe("")
    expect(formatDateOrDash(null)).toBe("—")
    expect(formatDateOrDash("2026-09-04")).toBe("04/09/26")
  })
})
