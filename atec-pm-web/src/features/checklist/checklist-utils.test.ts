import { describe, expect, it } from "vitest"

import {
  addDaysIso,
  buildSave,
  computeChecklistStats,
  containerPriorityDots,
  daysFromToday,
  filterChecklistItems,
  groupNameExists,
  priorityMeta,
  sortChecklistItems,
} from "@/features/checklist/checklist-utils"
import type { ChecklistGroup, ChecklistItem } from "@/lib/api/types"
import { dateToIso } from "@/lib/date-iso"

function fraGiorni(n: number): string {
  const d = new Date()
  d.setHours(0, 0, 0, 0)
  d.setDate(d.getDate() + n)
  return dateToIso(d)
}

let seq = 0
function item(over: Partial<ChecklistItem>): ChecklistItem {
  return {
    id: ++seq,
    projectId: 1,
    groupId: null,
    description: "attività",
    priority: 2,
    dueDate: null,
    isCritical: false,
    status: "OPEN",
    rowVersion: 1,
    ...over,
  } as ChecklistItem
}

describe("scadenze relative a oggi", () => {
  it("daysFromToday conta i giorni interi, senza data → null", () => {
    expect(daysFromToday(fraGiorni(0))).toBe(0)
    expect(daysFromToday(fraGiorni(3))).toBe(3)
    expect(daysFromToday(fraGiorni(-2))).toBe(-2)
    expect(daysFromToday(null)).toBeNull()
  })

  it("addDaysIso sposta la scadenza (o parte da oggi se manca)", () => {
    expect(addDaysIso("2026-09-04", 3)).toBe("2026-09-07")
    expect(addDaysIso("2026-12-31", 1)).toBe("2027-01-01")
    expect(addDaysIso(null, 0)).toBe(fraGiorni(0))
  })
})

describe("sortChecklistItems", () => {
  it("chiuse in fondo, critiche in cima, poi priorità e data", () => {
    const chiusa = item({ status: "CLOSED", priority: 0 })
    const critica = item({ isCritical: true, priority: 3 })
    const p0tardi = item({ priority: 0, dueDate: "2026-09-10" })
    const p0presto = item({ priority: 0, dueDate: "2026-09-01" })
    const p2 = item({ priority: 2, dueDate: "2026-08-01" })

    const perPriorita = sortChecklistItems([chiusa, p2, p0tardi, critica, p0presto], "priority")
    expect(perPriorita.map((i) => i.id)).toEqual([critica.id, p0presto.id, p0tardi.id, p2.id, chiusa.id])

    // Per data: la priorità non conta più, ma critiche in cima e chiuse in fondo restano.
    const perData = sortChecklistItems([chiusa, p2, p0tardi, critica, p0presto], "date")
    expect(perData.map((i) => i.id)).toEqual([critica.id, p2.id, p0presto.id, p0tardi.id, chiusa.id])
  })

  it("senza data si va in fondo fra le aperte", () => {
    const senza = item({ priority: 1 })
    const con = item({ priority: 1, dueDate: "2030-01-01" })
    expect(sortChecklistItems([senza, con], "date").map((i) => i.id)).toEqual([con.id, senza.id])
  })
})

describe("statistiche e filtri", () => {
  const items = [
    item({ priority: 0, isCritical: true, dueDate: fraGiorni(-1) }), // scaduta
    item({ priority: 1, dueDate: fraGiorni(0) }), // oggi
    item({ priority: 2, dueDate: fraGiorni(2) }), // entro 3 giorni
    item({ priority: 3, dueDate: fraGiorni(10) }), // in tempo
    item({ priority: 2, dueDate: null }), // senza data
    item({ priority: 0, dueDate: fraGiorni(-5), status: "CLOSED" }), // chiusa: non conta
  ]

  it("computeChecklistStats: le chiuse non concorrono a scadenze, priorità, urgenza", () => {
    const s = computeChecklistStats(items)
    expect(s.total).toBe(6)
    expect([s.overdue, s.today, s.soon, s.ok, s.none]).toEqual([1, 1, 1, 1, 1])
    expect(s.critical).toBe(1)
    expect(s.closed).toBe(1)
    expect(s.byPriority).toEqual({ 0: 1, 1: 1, 2: 2, 3: 1 })
  })

  it("filterChecklistItems: per priorità e per scadenza, le chiuse solo in «tutte»", () => {
    expect(filterChecklistItems(items, "all", "all")).toHaveLength(6)
    expect(filterChecklistItems(items, 2, "all")).toHaveLength(2)
    expect(filterChecklistItems(items, "all", "overdue").map((i) => i.priority)).toEqual([0])
    expect(filterChecklistItems(items, "all", "today")).toHaveLength(1)
    expect(filterChecklistItems(items, "all", "soon")).toHaveLength(1)
    expect(filterChecklistItems(items, "all", "ok")).toHaveLength(1)
    expect(filterChecklistItems(items, "all", "none")).toHaveLength(1)
    expect(filterChecklistItems(items, 0, "overdue")).toHaveLength(1) // la chiusa scaduta NON c'è
  })
})

describe("aiuti", () => {
  it("containerPriorityDots: critiche in cima, P0 assorbita dalla critica, chiuse ignorate", () => {
    const dots = containerPriorityDots([
      item({ priority: 0, isCritical: true }),
      item({ priority: 3 }),
      item({ priority: 1, status: "CLOSED" }),
    ])
    expect(dots.map((d) => d.label)).toEqual(["Attività critiche", "P3 · Bassa"])
  })

  it("groupNameExists ignora maiuscole e spazi ed esclude il gruppo stesso", () => {
    const groups = [{ id: 1, name: "Direzione" }, { id: 2, name: " Varie " }] as ChecklistGroup[]
    expect(groupNameExists(groups, "direzione")).toBe(true)
    expect(groupNameExists(groups, "varie")).toBe(true)
    expect(groupNameExists(groups, "Direzione", 1)).toBe(false)
    expect(groupNameExists(groups, "Nuovo")).toBe(false)
  })

  it("buildSave: la patch vince, la data si può azzerare di proposito, il container resta", () => {
    const base = item({ projectId: 7, groupId: null, dueDate: "2026-09-04", rowVersion: 3 })
    expect(buildSave(base, { description: "nuova" })).toMatchObject({
      projectId: 7,
      groupId: null,
      description: "nuova",
      dueDate: "2026-09-04",
      rowVersion: 3,
    })
    expect(buildSave(base, { dueDate: undefined }).dueDate).toBeNull() // «dueDate» presente = azzera
    expect(buildSave(base, {}).dueDate).toBe("2026-09-04") // assente = resta
  })

  it("priorityMeta: fuori scala → Media", () => {
    expect(priorityMeta(0).code).toBe("P0")
    expect(priorityMeta(9).code).toBe("P2")
  })
})
