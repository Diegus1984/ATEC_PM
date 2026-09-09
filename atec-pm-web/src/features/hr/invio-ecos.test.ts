import { describe, expect, it } from "vitest"

import { oraSuEcos, riassuntoInvioEcos, versoTimbratura } from "@/features/hr/invio-ecos"
import type { HrPunch } from "@/lib/api/types"

function timbratura(over: Partial<HrPunch>): HrPunch {
  return {
    id: 1,
    punchedAt: "2026-02-05T07:58:00",
    direction: "IN",
    source: "ECOS",
    ecosStampId: "s1",
    roundedAt: "2026-02-05T08:00:00",
    canSendToEcos: true,
    toSendToEcos: true,
    ecosInsert: false,
    ecosUncertain: false,
    ...over,
  }
}

describe("oraSuEcos — l'ora che Ecos ha adesso, sotto quella timbrata", () => {
  const giornata = {
    punches: [
      timbratura({ id: 1, punchedAt: "2026-09-08T07:48:09", direction: "IN", ecosPunchedAt: "2026-09-08T08:00:00" }),
      timbratura({ id: 2, punchedAt: "2026-09-08T12:30:56", direction: "OUT", ecosPunchedAt: null }),
      timbratura({ id: 3, punchedAt: "2026-09-08T13:30:00", direction: "IN", ecosPunchedAt: "2026-09-08T13:30:00" }),
    ],
  }

  it("dopo la scrittura dice l'ora che Ecos ha, se diversa da quella timbrata", () => {
    expect(oraSuEcos(giornata, "07:48", "IN")).toBe("08:00")
  })

  it("tace quando non è mai partita, quando è uguale, o quando la cella è vuota", () => {
    expect(oraSuEcos(giornata, "12:30", "OUT")).toBeNull()
    expect(oraSuEcos(giornata, "13:30", "IN")).toBeNull()
    expect(oraSuEcos(giornata, "--:--", "OUT")).toBeNull()
    expect(oraSuEcos(giornata, "", "IN")).toBeNull()
  })

  it("non confonde un'entrata con un'uscita allo stesso minuto", () => {
    expect(oraSuEcos(giornata, "07:48", "OUT")).toBeNull()
  })
})

describe("riassuntoInvioEcos", () => {
  it("la pausa dedotta è una cosa da scrivere e toglie l'allineamento (09/09/2026)", () => {
    const allineate = [timbratura({ id: 1, toSendToEcos: false }), timbratura({ id: 2, toSendToEcos: false })]
    const senza = riassuntoInvioEcos({ punches: allineate })
    expect(senza.pausaDaInserire).toBe(false)
    expect(senza.daScrivere).toBe(false)
    expect(senza.allineato).toBe(true)

    const con = riassuntoInvioEcos({ punches: allineate, ecosBreakToInsert: true })
    expect(con.pausaDaInserire).toBe(true)
    expect(con.daScrivere).toBe(true)
    expect(con.allineato).toBe(false)
    expect(con.daInviare).toHaveLength(0)
  })

  it("una rettifica da inserire conta fra le cose da inviare, come inserimento", () => {
    const r = riassuntoInvioEcos({
      punches: [
        timbratura({ id: 1 }),
        timbratura({ id: 2, source: "ADJUSTMENT", ecosStampId: null, ecosInsert: true }),
      ],
    })
    expect(r.diEcos).toBe(2)
    expect(r.daInviare.map((t) => t.id)).toEqual([1, 2])
    expect(r.daInserire.map((t) => t.id)).toEqual([2])
    expect(r.allineato).toBe(false)
  })

  it("una rettifica con esito incerto non riparte e toglie l'allineamento", () => {
    const r = riassuntoInvioEcos({
      punches: [
        timbratura({ id: 1, toSendToEcos: false }),
        timbratura({
          id: 2,
          source: "ADJUSTMENT",
          ecosStampId: null,
          ecosInsert: true,
          ecosUncertain: true,
          canSendToEcos: false,
          toSendToEcos: false,
        }),
      ],
    })
    expect(r.daInviare).toHaveLength(0)
    expect(r.incerte).toBe(1)
    expect(r.nonInviabili).toBe(0)
    expect(r.allineato).toBe(false)
  })

  it("è allineato quando nessuna timbratura di Ecos è da inviare", () => {
    const r = riassuntoInvioEcos({
      punches: [
        timbratura({ id: 1, toSendToEcos: false, ecosSentAt: "2026-02-06T09:00:00" }),
        timbratura({ id: 2, toSendToEcos: false, ecosSentAt: "2026-02-06T09:30:00" }),
      ],
    })
    expect(r.daInviare).toHaveLength(0)
    expect(r.allineato).toBe(true)
    expect(r.ultimoInvio).toBe("2026-02-06T09:30:00")
  })

  it("una timbratura non inviabile toglie l'allineamento e si conta a parte", () => {
    const r = riassuntoInvioEcos({
      punches: [timbratura({ id: 1, toSendToEcos: false, canSendToEcos: false })],
    })
    expect(r.nonInviabili).toBe(1)
    expect(r.allineato).toBe(false)
    expect(r.ultimoInvio).toBeNull()
  })

  it("senza timbrature di Ecos non c'è niente da dire", () => {
    const r = riassuntoInvioEcos({ punches: [] })
    expect(r.diEcos).toBe(0)
    expect(r.allineato).toBe(false)
  })
})

describe("versoTimbratura", () => {
  it("traduce il verso di Ecos", () => {
    expect(versoTimbratura("IN")).toBe("Entrata")
    expect(versoTimbratura("OUT")).toBe("Uscita")
  })
})
