import { describe, expect, it } from "vitest"

import { motivoPasswordNonValida } from "@/features/admin/digest/password-smtp"

describe("cambia password SMTP con doppia conferma (09/09/2026)", () => {
  it("passa solo quando le due password coincidono", () => {
    expect(motivoPasswordNonValida("Segreta1", "Segreta1")).toBeNull()
    expect(motivoPasswordNonValida("Segreta1", "Segreta2")).toBe("Le due password non coincidono.")
    expect(motivoPasswordNonValida("Segreta1", "")).toBe("Le due password non coincidono.")
  })

  it("vuota o con spazi ai bordi si ferma prima di salvare", () => {
    expect(motivoPasswordNonValida("", "")).toBe("Scrivi la nuova password.")
    expect(motivoPasswordNonValida("Segreta1 ", "Segreta1 ")).toContain("spazio")
    expect(motivoPasswordNonValida(" Segreta1", " Segreta1")).toContain("spazio")
    // Uno spazio in mezzo è legittimo.
    expect(motivoPasswordNonValida("Segre ta1", "Segre ta1")).toBeNull()
  })
})
