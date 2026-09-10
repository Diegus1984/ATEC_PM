import { describe, expect, it } from "vitest"

import {
  daGiustificareGiornata,
  filtraGiornate,
  totaliMese,
} from "@/features/hr/cartellino-mese"
import type { HrDay } from "@/lib/api/types"

/** Una giornata feriale piena come la manda il motore; si sovrascrive solo ciò che conta. */
function giornata(workDate: string, over: Partial<HrDay> = {}): HrDay {
  return {
    workDate: `${workDate}T00:00:00`,
    isHoliday: false,
    hasData: true,
    clockIn1: "08:00",
    clockOut1: "12:30",
    clockIn2: "13:30",
    clockOut2: "17:00",
    regularHours: "8h 0m",
    overtime: "0h 0m",
    breakTime: "1h 0m",
    bands: {},
    note: "OK",
    hasAnomaly: false,
    punches: [],
    ecosSends: [],
    raw: {} as HrDay["raw"],
    normalized: {} as HrDay["normalized"],
    canRemind: false,
    lastReminderAt: null,
    ecosBreakToInsert: false,
    ...over,
  }
}

/** Il mese di prova: una giornata piena, una con straordinario, ferie, un buco e una corta. */
const MESE: HrDay[] = [
  giornata("2026-09-01"),
  giornata("2026-09-02", { overtime: "1h 0m" }),
  giornata("2026-09-03", { note: "VACATION", regularHours: "0h 0m" }),
  giornata("2026-09-04", {
    hasData: false,
    clockIn1: "",
    clockOut1: "",
    clockIn2: "",
    clockOut2: "",
    regularHours: "",
    overtime: "",
    note: "",
    canRemind: true,
  }),
  giornata("2026-09-07", { regularHours: "7h 30m", shortMinutes: 30 }),
]

describe("totaliMese", () => {
  it("somma le ore e conta le giornate per tipo", () => {
    const t = totaliMese(MESE)

    expect(t.ordinarie).toBe(480 + 480 + 0 + 450)
    expect(t.straordinario).toBe(60)
    expect(t.giorniLavorati).toBe(3)
    expect(t.giorniStraordinario).toBe(1)
    expect(t.assenzeIntere).toBe(1)
    expect(t.assenzeParziali).toBe(0)
  })

  it("«da sistemare» conta il rosso E l'ambra, come nel Controllo di ieri", () => {
    // Il buco del 4 (rosso) e la giornata corta del 7 (ambra, mancano 30 minuti).
    expect(totaliMese(MESE).daSistemare).toBe(2)
  })

  it("conta quante fra quelle da sistemare hanno già avuto l'email", () => {
    const conEmail = MESE.map((g) =>
      g.workDate.startsWith("2026-09-04") ? { ...g, lastReminderAt: "2026-09-05T09:00:00" } : g
    )
    expect(totaliMese(conEmail).segnalate).toBe(1)
  })
})

describe("filtraGiornate — il riquadro mostra quello che ha contato", () => {
  it("ogni filtro rende tante giornate quante ne dice il suo numero", () => {
    const t = totaliMese(MESE)

    expect(filtraGiornate(MESE, "lavorate")).toHaveLength(t.giorniLavorati)
    expect(filtraGiornate(MESE, "straordinario")).toHaveLength(t.giorniStraordinario)
    expect(filtraGiornate(MESE, "assenze")).toHaveLength(t.assenzeIntere + t.assenzeParziali)
    expect(filtraGiornate(MESE, "sistemare")).toHaveLength(t.daSistemare)
    expect(filtraGiornate(MESE, "tutti")).toEqual(MESE)
  })

  it("«da segnalare» resta la regola del server (canRemind)", () => {
    expect(filtraGiornate(MESE, "segnalare").map((g) => g.workDate.slice(0, 10))).toEqual([
      "2026-09-04",
    ])
  })
})

describe("daGiustificareGiornata", () => {
  it("la causale si mette dove mancano ore, non su ferie o giornate a posto", () => {
    expect(MESE.filter(daGiustificareGiornata).map((g) => g.workDate.slice(0, 10))).toEqual([
      "2026-09-04",
      "2026-09-07",
    ])
  })
})
