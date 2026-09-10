import { describe, expect, it } from "vitest"

import { statoGiornata } from "@/features/hr/stato-giornata"
import type { HrDay } from "@/lib/api/types"

/** Una giornata feriale passata con i valori del motore; si sovrascrive solo ciò che conta. */
function giornata(over: Partial<HrDay>): HrDay {
  return {
    workDate: "2026-09-07",
    isHoliday: false,
    hasData: true,
    clockIn1: "08:00",
    clockOut1: "12:00",
    clockIn2: "",
    clockOut2: "",
    regularHours: "4h 0m",
    overtime: "0h 0m",
    breakTime: "0h 0m",
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

describe("statoGiornata — ore del contratto (Diego, 10/09/2026)", () => {
  it("una giornata più corta del contratto non è «tutto regolare»: dice quanto manca", () => {
    const st = statoGiornata(giornata({
      clockOut1: "12:30", clockIn2: "13:30", clockOut2: "17:00",
      regularHours: "7h 30m", note: "AUTO_P: Pausa 1h detratta", shortMinutes: 30,
    }))
    expect(st.label).toBe("Mancano 30m sul contratto")
    expect(st.tone).toBe("warn")
  })

  it("l'ora intera si legge come ora, non come sessanta minuti", () => {
    const st = statoGiornata(giornata({ regularHours: "7h 0m", shortMinutes: 60 }))
    expect(st.label).toBe("Mancano 1h sul contratto")
  })

  it("con le ore al completo resta «tutto regolare»", () => {
    const st = statoGiornata(giornata({ regularHours: "8h 0m", shortMinutes: 0 }))
    expect(st.label).toBe("Tutto regolare")
    expect(st.tone).toBe("ok")
    // Senza il campo (giornata vecchia in cache) si comporta come se non mancasse niente.
    expect(statoGiornata(giornata({ regularHours: "8h 0m" })).tone).toBe("ok")
  })
})

describe("statoGiornata — mezza giornata (Diego, 08/09/2026)", () => {
  it("una entrata e una uscita al mattino non sono «tutto regolare»: si dice cosa manca", () => {
    const st = statoGiornata(giornata({ note: "Turno mattutino" }))
    expect(st.label).toBe("Solo mattina, 4h 0m: pomeriggio senza timbrature")
    expect(st.tone).toBe("warn")
    expect(st.assenza).toBe(false)
  })

  it("il turno pomeridiano dice che manca la mattina", () => {
    const st = statoGiornata(
      giornata({ note: "Turno pomeridiano", clockIn1: "13:30", clockOut1: "17:30" })
    )
    expect(st.label).toBe("Solo pomeriggio, 4h 0m: mattina senza timbrature")
    expect(st.tone).toBe("warn")
  })

  it("vale anche per la variante con la seconda uscita ignorata", () => {
    const st = statoGiornata(giornata({ note: "Turno mattutino (seconda uscita ignorata)" }))
    expect(st.label).toMatch(/^Solo mattina/)
    expect(st.tone).toBe("warn")
  })

  it("senza ore calcolabili la frase resta leggibile", () => {
    const st = statoGiornata(giornata({ note: "Turno mattutino", regularHours: "---" }))
    expect(st.label).toBe("Solo mattina: pomeriggio senza timbrature")
  })

  it("la giornata segnalata porta la data del sollecito in coda", () => {
    const st = statoGiornata(
      giornata({ note: "Turno mattutino", lastReminderAt: "2026-09-08T09:00:00" })
    )
    expect(st.label).toBe("Solo mattina, 4h 0m: pomeriggio senza timbrature · segnalata il 08/09/26")
  })
})

describe("statoGiornata — le altre frasi non cambiano", () => {
  it("quattro timbrature a posto = tutto regolare", () => {
    const st = statoGiornata(
      giornata({ note: "OK", clockIn2: "13:30", clockOut2: "17:30", regularHours: "8h 0m" })
    )
    expect(st.label).toBe("Tutto regolare")
    expect(st.tone).toBe("ok")
  })

  it("il turno di notte regolare non viene scambiato per mezza giornata", () => {
    const st = statoGiornata(
      giornata({ note: "Turno notturno", bands: { B2: "6h 0m" }, regularHours: "8h 0m" })
    )
    expect(st.label).toBe("Regolare, con notturno")
    expect(st.tone).toBe("ok")
  })

  it("solo entrata = anomalia rossa del motore", () => {
    const st = statoGiornata(
      giornata({ note: "⚠ INCOMPLETO: Solo entrata", hasAnomaly: true, clockOut1: "??:??" })
    )
    expect(st.label).toBe("Manca l'uscita")
    expect(st.tone).toBe("bad")
  })

  it("l'uscita finale dimenticata è un'anomalia rossa, non una stima (Diego, 09/09/2026)", () => {
    const st = statoGiornata(
      giornata({
        note: "⚠ INCOMPLETO: Uscita mancante",
        hasAnomaly: true,
        clockIn2: "13:30",
        clockOut2: "??:??",
        regularHours: "0h 0m",
      })
    )
    expect(st.label).toBe("Manca l'uscita")
    expect(st.tone).toBe("bad")
  })

  it("la nota vecchia «Stimata 17:00» resta leggibile finché lo storico non è ricalcolato", () => {
    const st = statoGiornata(giornata({ note: "AUTO_P: Uscita mancante - Stimata 17:00" }))
    expect(st.label).toBe("Uscita non timbrata, stimata alle 17:00")
    expect(st.tone).toBe("warn")
  })
})
