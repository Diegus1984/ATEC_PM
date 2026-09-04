import { describe, expect, it } from "vitest"

import {
  computeFeriePeak,
  dayCount,
  displayDayCount,
  forbidden,
  initials,
  isHoliday,
  isWeekend,
  mondayOf,
  overlap,
  recalcConflicts,
  surname,
  toIso,
  workingDayCount,
  wouldConflict,
} from "@/features/risorse/planner-logic"
import type { ResAssignmentDto } from "@/lib/api/types"

// Port fedele di PlannerLogic.cs (programma ATEC Risorse): le regole sono quelle del planner.
const D = (y: number, m: number, d: number) => new Date(y, m - 1, d)

let seq = 0
function alloc(over: Partial<ResAssignmentDto> & { tipo: string; dataInizio: string; dataFine: string }): ResAssignmentDto {
  return { id: ++seq, employeeId: 1, employeeName: "Mario Rossi", hasConflict: false, ...over } as ResAssignmentDto
}

describe("calendario", () => {
  it("mondayOf torna al lunedì ISO della settimana", () => {
    expect(toIso(mondayOf(D(2026, 9, 4)))).toBe("2026-08-31") // venerdì → lunedì
    expect(toIso(mondayOf(D(2026, 9, 6)))).toBe("2026-08-31") // domenica → lunedì PRIMA
    expect(toIso(mondayOf(D(2026, 8, 31)))).toBe("2026-08-31") // lunedì → sé stesso
  })

  it("weekend e festivi fissi + Lunedì dell'Angelo", () => {
    expect(isWeekend(D(2026, 9, 5))).toBe(true) // sabato
    expect(isWeekend(D(2026, 9, 7))).toBe(false)
    expect(isHoliday(D(2026, 8, 15))).toBe(true)
    expect(isHoliday(D(2026, 12, 26))).toBe(true)
    expect(isHoliday(D(2026, 4, 6))).toBe(true) // Pasqua 2026 = 5 aprile → Lunedì dell'Angelo il 6
    expect(isHoliday(D(2025, 4, 21))).toBe(true) // Pasqua 2025 = 20 aprile
    expect(isHoliday(D(2026, 4, 7))).toBe(false)
  })

  it("giorni di calendario vs lavorativi: le FERIE contano solo i lavorativi", () => {
    const lun = D(2026, 8, 31)
    const dom = D(2026, 9, 6)
    expect(dayCount(lun, dom)).toBe(7)
    expect(workingDayCount(lun, dom)).toBe(5)
    expect(workingDayCount(dom, lun)).toBe(0) // fine prima dell'inizio
    expect(displayDayCount("FERIE", lun, dom)).toBe(5)
    expect(displayDayCount("OP", lun, dom)).toBe(7)
  })
})

describe("conflitti", () => {
  it("FLEX non va mai in conflitto; OP e FERIE sì, anche FERIE con FERIE", () => {
    expect(forbidden("OP", "OP")).toBe(true)
    expect(forbidden("OP", "FERIE")).toBe(true)
    expect(forbidden("FERIE", "FERIE")).toBe(true)
    expect(forbidden("FLEX", "OP")).toBe(false)
    expect(forbidden("FERIE", "FLEX")).toBe(false)
  })

  it("overlap è inclusivo sui confini", () => {
    const a = alloc({ tipo: "OP", dataInizio: "2026-09-01", dataFine: "2026-09-05" })
    expect(overlap(a, alloc({ tipo: "OP", dataInizio: "2026-09-05", dataFine: "2026-09-10" }))).toBe(true)
    expect(overlap(a, alloc({ tipo: "OP", dataInizio: "2026-09-06", dataFine: "2026-09-10" }))).toBe(false)
  })

  it("recalcConflicts marca solo la stessa persona, wouldConflict esclude la riga che si sta spostando", () => {
    const op = alloc({ employeeId: 1, tipo: "OP", dataInizio: "2026-09-01", dataFine: "2026-09-05" })
    const ferie = alloc({ employeeId: 1, tipo: "FERIE", dataInizio: "2026-09-03", dataFine: "2026-09-04" })
    const flex = alloc({ employeeId: 1, tipo: "FLEX", dataInizio: "2026-09-01", dataFine: "2026-09-30" })
    const altro = alloc({ employeeId: 2, tipo: "OP", dataInizio: "2026-09-01", dataFine: "2026-09-05" })
    const tutte = [op, ferie, flex, altro]
    recalcConflicts(tutte)
    expect(tutte.map((a) => a.hasConflict)).toEqual([true, true, false, false])

    expect(wouldConflict(tutte, 1, D(2026, 9, 4), D(2026, 9, 8), "OP", op.id)).toBe(true) // contro le ferie
    expect(wouldConflict(tutte, 1, D(2026, 9, 6), D(2026, 9, 8), "OP", op.id)).toBe(false)
    expect(wouldConflict(tutte, 1, D(2026, 9, 1), D(2026, 9, 8), "FLEX", 0)).toBe(false)
    expect(wouldConflict(tutte, 2, D(2026, 9, 1), D(2026, 9, 1), "OP", altro.id)).toBe(false) // sé stessa esclusa
  })
})

describe("computeFeriePeak", () => {
  it("il picco è il giorno con più persone in ferie contemporaneamente", () => {
    const ferie = [
      alloc({ employeeId: 1, tipo: "FERIE", dataInizio: "2026-08-10", dataFine: "2026-08-14" }),
      alloc({ employeeId: 2, tipo: "FERIE", dataInizio: "2026-08-12", dataFine: "2026-08-20" }),
      alloc({ employeeId: 2, tipo: "FERIE", dataInizio: "2026-08-13", dataFine: "2026-08-13" }), // stessa persona: conta uno
      alloc({ employeeId: 3, tipo: "FERIE", dataInizio: "2026-08-14", dataFine: "2026-08-14" }),
    ]
    const { peak, date } = computeFeriePeak(ferie)
    expect(peak).toBe(3)
    expect(toIso(date!)).toBe("2026-08-14")
    expect(computeFeriePeak([])).toEqual({ peak: 0, date: null })
  })
})

describe("nominativi", () => {
  it("iniziali e cognome", () => {
    expect(initials("Mario Rossi")).toBe("MR")
    expect(initials("Anna Maria De Luca")).toBe("AL")
    expect(initials("Madonna")).toBe("MA")
    expect(initials("  ")).toBe("?")
    expect(surname("Anna Maria De Luca")).toBe("Luca")
  })
})
