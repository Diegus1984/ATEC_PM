import { describe, expect, it } from "vitest"

import {
  NON_COLLEGATO,
  daSistemare,
  descriviBlocco,
  etichettaGiorno,
  riassuntoControllo,
  righeControllo,
  spiegaBlocco,
} from "@/features/hr/controllo-giornaliero"
import type { HrDailyCheck, HrDailyCheckEmployee, HrDay } from "@/lib/api/types"

/** Una giornata feriale passata come la manda il motore; si sovrascrive solo ciò che conta. */
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

/** Nessuna timbratura e nessun dato: il giorno vuoto. */
function vuota(workDate: string, isHoliday = false): HrDay {
  return giornata(workDate, {
    isHoliday,
    hasData: false,
    clockIn1: "",
    clockOut1: "",
    clockIn2: "",
    clockOut2: "",
    regularHours: "",
    overtime: "",
    breakTime: "",
    note: "",
  })
}

function dipendente(
  id: number,
  nome: string,
  days: HrDay[],
  over: Partial<HrDailyCheckEmployee> = {}
): HrDailyCheckEmployee {
  return { employeeId: id, employeeName: nome, departmentName: null, ecosLinked: true, days, ...over }
}

// Il blocco di lunedì 7 settembre 2026: venerdì 4 (lavorativo), sabato 5 e domenica 6.
const VEN = "2026-09-04"
const SAB = "2026-09-05"
const DOM = "2026-09-06"

function bloccoDiLunedi(employees: HrDailyCheckEmployee[]): HrDailyCheck {
  return {
    date: `${DOM}T00:00:00`,
    from: `${VEN}T00:00:00`,
    to: `${DOM}T00:00:00`,
    previousDate: "2026-09-03T00:00:00",
    nextDate: "2026-09-07T00:00:00",
    days: [
      { date: `${VEN}T00:00:00`, isWorkingDay: true },
      { date: `${SAB}T00:00:00`, isWorkingDay: false },
      { date: `${DOM}T00:00:00`, isWorkingDay: false },
    ],
    employees,
  }
}

describe("righeControllo — chi compare e in che ordine", () => {
  it("nel giorno lavorativo ci sono tutti, anche chi non ha timbrato (in rosso)", () => {
    const rossi = dipendente(1, "Mario Rossi", [giornata(VEN), vuota(SAB), vuota(DOM, true)])
    const bianchi = dipendente(2, "Anna Bianchi", [vuota(VEN), vuota(SAB), vuota(DOM, true)])

    const righe = righeControllo(bloccoDiLunedi([rossi, bianchi]))

    const venerdi = righe.filter((r) => r.dataIso === VEN)
    expect(venerdi.map((r) => r.dipendente.employeeName)).toEqual(["Mario Rossi", "Anna Bianchi"])
    expect(venerdi[0].stato.label).toBe("Tutto regolare")
    expect(venerdi[1].stato.label).toBe("Nessuna timbratura")
    expect(venerdi[1].stato.tone).toBe("bad")
    expect(venerdi.every((r) => r.lavorativo)).toBe(true)
  })

  it("nei giorni di riposo compare solo chi ha lavorato lo stesso", () => {
    const rossi = dipendente(1, "Mario Rossi", [
      giornata(VEN),
      giornata(SAB, { clockIn2: "", clockOut2: "", regularHours: "4h 0m", note: "Turno mattutino" }),
      vuota(DOM, true),
    ])
    const bianchi = dipendente(2, "Anna Bianchi", [giornata(VEN), vuota(SAB), vuota(DOM, true)])

    const righe = righeControllo(bloccoDiLunedi([rossi, bianchi]))

    expect(righe.filter((r) => r.dataIso === SAB).map((r) => r.dipendente.employeeId)).toEqual([1])
    expect(righe.filter((r) => r.dataIso === DOM)).toHaveLength(0)
    // Le righe vanno giorno per giorno: prima tutto il venerdì, poi il sabato.
    expect(righe.map((r) => r.dataIso)).toEqual([VEN, VEN, SAB])
    expect(righe[2].lavorativo).toBe(false)
  })

  it("le ferie a cavallo del fine settimana non fanno righe nei riposi", () => {
    const rossi = dipendente(1, "Mario Rossi", [
      giornata(VEN, { note: "VACATION", clockIn1: "", clockOut1: "", clockIn2: "", clockOut2: "", regularHours: "0h 0m" }),
      giornata(SAB, { note: "VACATION", clockIn1: "", clockOut1: "", clockIn2: "", clockOut2: "", regularHours: "0h 0m" }),
      vuota(DOM, true),
    ])

    const righe = righeControllo(bloccoDiLunedi([rossi]))

    expect(righe).toHaveLength(1)
    expect(righe[0].dataIso).toBe(VEN)
    expect(righe[0].stato.label).toBe("Ferie")
  })

  it("chi non è collegato a Ecos non è «senza timbrature»: è da collegare, e nei riposi non compare", () => {
    const neri = dipendente(3, "Luca Neri", [vuota(VEN), vuota(SAB), vuota(DOM, true)], { ecosLinked: false })

    const righe = righeControllo(bloccoDiLunedi([neri]))

    expect(righe).toHaveLength(1)
    expect(righe[0].stato).toBe(NON_COLLEGATO)
    expect(righe[0].stato.tone).toBe("dim")
  })

  it("una rettifica a mano fa dati anche senza Ecos: la giornata si legge come le altre", () => {
    const neri = dipendente(3, "Luca Neri", [giornata(VEN), vuota(SAB), vuota(DOM, true)], { ecosLinked: false })

    const righe = righeControllo(bloccoDiLunedi([neri]))

    expect(righe[0].stato.label).toBe("Tutto regolare")
  })
})

describe("riassuntoControllo e daSistemare", () => {
  it("conta i dipendenti, i regolari, i da sistemare (rossi e ambra), gli assenti e i senza timbrature", () => {
    const controllo = bloccoDiLunedi([
      dipendente(1, "Mario Rossi", [giornata(VEN), vuota(SAB), vuota(DOM, true)]),
      dipendente(2, "Anna Bianchi", [vuota(VEN), vuota(SAB), vuota(DOM, true)]),
      dipendente(3, "Luca Neri", [
        giornata(VEN, { note: "⚠ INCOMPLETO: Uscita mancante", hasAnomaly: true, clockOut2: "??:??", regularHours: "0h 0m" }),
        vuota(SAB),
        vuota(DOM, true),
      ]),
      dipendente(4, "Sara Gialli", [
        giornata(VEN, { note: "PERMIT (4h)", regularHours: "0h 0m" }),
        vuota(SAB),
        vuota(DOM, true),
      ]),
      dipendente(5, "Elena Blu", [giornata(VEN, { note: "⚠ INCOMPLETO: Solo entrata", hasAnomaly: true }), vuota(SAB), vuota(DOM, true)]),
    ])
    const righe = righeControllo(controllo)

    const r = riassuntoControllo(controllo, righe)
    expect(r).toEqual({ dipendenti: 5, regolari: 1, daSistemare: 3, assenti: 1, senzaTimbrature: 1 })
    expect(righe.filter(daSistemare).map((x) => x.dipendente.employeeName)).toEqual([
      "Anna Bianchi",
      "Luca Neri",
      "Elena Blu",
    ])
  })
})

describe("le parole del blocco", () => {
  it("un giorno solo si chiama col suo nome", () => {
    expect(descriviBlocco({ from: "2026-09-07T00:00:00", to: "2026-09-07T00:00:00" })).toBe("Lunedì 7 settembre")
    expect(etichettaGiorno("2026-09-04T00:00:00")).toBe("Venerdì 4 settembre")
  })

  it("più giorni si leggono da–a, col mese una volta sola se è lo stesso", () => {
    expect(descriviBlocco({ from: `${VEN}T00:00:00`, to: `${DOM}T00:00:00` })).toBe(
      "Da venerdì 4 a domenica 6 settembre"
    )
    expect(descriviBlocco({ from: "2026-10-30T00:00:00", to: "2026-11-01T00:00:00" })).toBe(
      "Da venerdì 30 ottobre a domenica 1 novembre"
    )
  })

  it("spiega perché ci sono i riposi", () => {
    expect(spiegaBlocco(bloccoDiLunedi([]))).toBe(
      "L'ultimo giorno lavorativo più 2 giorni di riposo: nei riposi compare solo chi ha timbrato."
    )
    expect(spiegaBlocco({ days: [{ date: "2026-09-07T00:00:00", isWorkingDay: true }] })).toBe("")
    expect(spiegaBlocco({ days: [{ date: "2026-09-05T00:00:00", isWorkingDay: false }] })).toBe(
      "Un giorno di riposo: compare solo chi ha timbrato."
    )
  })
})
