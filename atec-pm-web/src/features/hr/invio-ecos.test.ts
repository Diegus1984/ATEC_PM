import { describe, expect, it } from "vitest"

import { riassuntoInvioEcos, versoTimbratura } from "@/features/hr/invio-ecos"
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

describe("riassuntoInvioEcos", () => {
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
