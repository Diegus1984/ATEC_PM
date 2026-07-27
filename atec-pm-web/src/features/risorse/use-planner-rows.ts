// ── Filtri del planner: pool «In lista», interruttori Gantt, righe visibili ──

import * as React from "react"

import type { LookupItem, ResAssignmentDto, ResTipo } from "@/lib/api/types"

import { packLanes, type RowData } from "./planner-geometry"
import {
  assignmentMatchesSearch,
  surname,
  wildcardMatch,
} from "./planner-logic"
import type { PlannerUiSettings } from "./use-planner-settings"

export function usePlannerRows({
  assignments,
  resources,
  settings,
  patch,
  resSearch,
  sideSearch,
  myEmployeeId,
}: {
  assignments: ResAssignmentDto[]
  resources: LookupItem[]
  settings: PlannerUiSettings
  patch: (patch: Partial<PlannerUiSettings>) => void
  /** Ricerca globale (toolbar): filtra risorse+attività nel Gantt. */
  resSearch: string
  /** Ricerca del pannello laterale: filtra solo l'elenco checkbox, non il Gantt. */
  sideSearch: string
  myEmployeeId: number
}) {
  // inPool = filtro "In lista" (chi è eleggibile); ganttOn = interruttore per-riga nel pannello.
  const poolIds = React.useMemo(
    () => new Set(settings.selectedResourceIds),
    [settings.selectedResourceIds]
  )
  const inPool = React.useCallback(
    (id: number) => !settings.resourceFilterActive || poolIds.has(id),
    [settings.resourceFilterActive, poolIds]
  )
  const ganttOffSet = React.useMemo(
    () => new Set(settings.ganttOffIds),
    [settings.ganttOffIds]
  )
  const ganttOn = React.useCallback(
    (id: number) => !ganttOffSet.has(id),
    [ganttOffSet]
  )

  // ── Righe filtrate (port di FilteredResources+DisplayRows del Blazor) ──
  // Una risorsa compare anche SENZA allocazioni (riga vuota, pronta per il drag-create),
  // a meno che un filtro attivo (tipo/ricerca/solo occupate) la escluda esplicitamente.
  const rows: RowData[] = React.useMemo(() => {
    const rawByEmp = new Map<number, ResAssignmentDto[]>()
    for (const a of assignments) {
      const arr = rawByEmp.get(a.employeeId)
      if (arr) arr.push(a)
      else rawByEmp.set(a.employeeId, [a])
    }

    const search = resSearch.trim()
    const tipi = settings.tipiVisibili
    const hasActiveFilter = tipi.length < 3 || search.length > 0

    const result: RowData[] = []
    for (const r of resources) {
      if (!inPool(r.id) || !ganttOn(r.id)) continue
      if (settings.mineOnly && r.id !== myEmployeeId) continue

      const raw = rawByEmp.get(r.id) ?? []
      if (settings.conflictsOnly && !raw.some((a) => a.hasConflict)) continue

      // Ricerca a livello risorsa: nome oppure almeno un'attività (di qualsiasi tipo).
      if (search) {
        const nameHit = wildcardMatch(r.name, search)
        const actHit = raw.some((a) => assignmentMatchesSearch(a, search))
        if (!nameHit && !actHit) continue
      }

      // Voci mostrate: filtrate per tipo; per ricerca solo se il nome non ha già "vinto".
      let items = tipi.length < 3 ? raw.filter((a) => tipi.includes(a.tipo)) : raw
      if (search && !wildcardMatch(r.name, search)) {
        items = items.filter((a) => assignmentMatchesSearch(a, search))
      }

      if (hasActiveFilter && items.length === 0 && raw.length > 0) continue
      if (settings.occupiedOnly && items.length === 0) continue

      const { placed, lanes } = packLanes(items)
      result.push({ resource: r, bars: placed, lanes })
    }
    result.sort(
      (a, b) =>
        surname(a.resource.name).localeCompare(surname(b.resource.name)) ||
        a.resource.name.localeCompare(b.resource.name)
    )
    return result
  }, [assignments, resources, settings, resSearch, myEmployeeId, inPool, ganttOn])

  const conflictCount = React.useMemo(
    () => assignments.filter((a) => a.hasConflict).length,
    [assignments]
  )

  // Elenco del pannello laterale: solo membri del pool, filtrati dalla ricerca locale,
  // ordinati per cognome (come EmployeeSort.BySurname).
  const sideResources = React.useMemo(() => {
    const q = sideSearch.trim()
    return resources
      .filter((r) => inPool(r.id))
      .filter((r) => !q || wildcardMatch(r.name, q))
      .sort(
        (a, b) =>
          surname(a.name).localeCompare(surname(b.name)) ||
          a.name.localeCompare(b.name)
      )
  }, [resources, sideSearch, inPool])

  const poolCount = React.useMemo(
    () => resources.filter((r) => inPool(r.id)).length,
    [resources, inPool]
  )
  const ganttOnCount = React.useMemo(
    () => resources.filter((r) => inPool(r.id) && ganttOn(r.id)).length,
    [resources, inPool, ganttOn]
  )

  // ── Comandi: interruttori Gantt, legenda tipi, pool «In lista» ──────────
  // Tutti scrivono nelle preferenze persistite (usePlannerSettings).
  function toggleGanttOff(id: number) {
    const set = new Set(settings.ganttOffIds)
    if (set.has(id)) set.delete(id)
    else set.add(id)
    patch({ ganttOffIds: Array.from(set) })
  }
  /** «Tutte»: riaccende solo il pool corrente, lasciando spente le risorse fuori lista. */
  function showAll() {
    patch({ ganttOffIds: settings.ganttOffIds.filter((id) => !inPool(id)) })
  }
  function hideAll() {
    const set = new Set(settings.ganttOffIds)
    for (const r of resources) if (inPool(r.id)) set.add(r.id)
    patch({ ganttOffIds: Array.from(set) })
  }
  function toggleTipo(t: ResTipo) {
    const set = new Set(settings.tipiVisibili)
    if (set.has(t)) set.delete(t)
    else set.add(t)
    patch({ tipiVisibili: Array.from(set) })
  }

  const resourceFilterLabel = settings.resourceFilterActive
    ? `${settings.selectedResourceIds.length} in lista`
    : "tutte"

  /** Tutte selezionate = nessun filtro (si spegne, non si salva l'elenco completo). */
  function recomputeResourceSelection(checkedIds: Set<number>) {
    const allChecked = resources.length > 0 && checkedIds.size === resources.length
    if (allChecked) {
      patch({ resourceFilterActive: false, selectedResourceIds: [] })
    } else {
      patch({ resourceFilterActive: true, selectedResourceIds: Array.from(checkedIds) })
    }
  }
  function toggleResourcePick(id: number) {
    const base = settings.resourceFilterActive
      ? new Set(settings.selectedResourceIds)
      : new Set(resources.map((r) => r.id))
    if (base.has(id)) base.delete(id)
    else base.add(id)
    recomputeResourceSelection(base)
  }
  function setAllResourcePicks(on: boolean) {
    recomputeResourceSelection(
      on ? new Set(resources.map((r) => r.id)) : new Set()
    )
  }

  return {
    rows,
    conflictCount,
    sideResources,
    poolCount,
    ganttOnCount,
    ganttOn,
    resourceFilterLabel,
    toggleGanttOff,
    showAll,
    hideAll,
    toggleTipo,
    toggleResourcePick,
    setAllResourcePicks,
  }
}
