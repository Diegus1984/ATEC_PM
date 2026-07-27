// ── Drag del Gantt: sposta / ridimensiona / crea-per-trascinamento ─────────
// Port di plannerInterop.startDrag + OnDragMove/End. L'hook possiede anche le
// scorciatoie globali (Esc, Canc, Ctrl+F, Shift+rotella): condividono lo stesso
// effetto montato una volta sola e leggono i valori vivi tramite ref.

import * as React from "react"

import { ApiError } from "@/lib/api/client"
import { createAssignments, updateAssignment } from "@/lib/api/resource-planner"
import type { ResAssignmentDto, ResTipo } from "@/lib/api/types"

import { addDays, parseDate, toIso } from "./planner-logic"

type DragMode = "move" | "resizeStart" | "resizeEnd" | "create"

interface DragInfo {
  mode: DragMode
  startX: number
  origStart: Date
  origEnd: Date
  assignmentId: number // -1 per create
  employeeId: number
  createStartCol: number
  lastDelta: number
  moved: boolean
}

export interface DragPreview {
  assignmentId: number // -1 per create
  employeeId: number
  start: Date
  end: Date
}

export interface PlannerDragOptions {
  canEdit: boolean
  dayW: number
  bandStart: Date
  bandDays: number
  scrollRef: React.RefObject<HTMLDivElement | null>
  searchInputRef: React.RefObject<HTMLInputElement | null>
  connRef: React.RefObject<string | null>
  assignments: ResAssignmentDto[]
  /** Tipo usato dalla create-by-drag (primo tipo attivo in legenda). */
  createTipo: ResTipo
  selectedId: number | null
  setSelectedId: (id: number | null) => void
  dialogOpen: boolean
  /** Chiede conferma se la nuova collocazione genera un conflitto. */
  confirmConflict: (
    employeeId: number,
    start: Date,
    end: Date,
    tipo: string,
    excludeId: number
  ) => Promise<boolean>
  flashStatus: (msg: string) => void
  reload: () => Promise<void>
  refreshPendingNotify: () => Promise<void>
  /** Scorciatoia Canc sulla barra selezionata. */
  onDeleteRequest: (a: ResAssignmentDto) => void
}

export function usePlannerDrag(opts: PlannerDragOptions) {
  const dragRef = React.useRef<DragInfo | null>(null)
  const previewRef = React.useRef<DragPreview | null>(null)
  const [preview, setPreview] = React.useState<DragPreview | null>(null)
  const suppressClickRef = React.useRef(false)
  const setPv = React.useCallback((p: DragPreview | null) => {
    previewRef.current = p
    setPreview(p)
  }, [])

  // Ref ai valori dinamici letti dagli handler globali (montati una volta).
  const dayWRef = React.useRef(opts.dayW)
  dayWRef.current = opts.dayW
  const bandStartRef = React.useRef(opts.bandStart)
  bandStartRef.current = opts.bandStart
  const bandDaysRef = React.useRef(opts.bandDays)
  bandDaysRef.current = opts.bandDays
  const selectedIdRef = React.useRef(opts.selectedId)
  selectedIdRef.current = opts.selectedId
  const assignmentsForKeyRef = React.useRef(opts.assignments)
  assignmentsForKeyRef.current = opts.assignments
  const dialogOpenRef = React.useRef(opts.dialogOpen)
  dialogOpenRef.current = opts.dialogOpen
  const removeAssignmentRef = React.useRef(opts.onDeleteRequest)
  removeAssignmentRef.current = opts.onDeleteRequest
  const scrollRef = opts.scrollRef
  const searchInputRef = opts.searchInputRef
  const finishDragRef = React.useRef<(() => void) | null>(null)

  const { canEdit, dayW, bandStart, bandDays } = opts

  // ── Avvio drag (move / resize / create) ──────────────────────
  function beginMove(e: React.PointerEvent, a: ResAssignmentDto) {
    if (!canEdit || e.button !== 0) return
    e.stopPropagation()
    opts.setSelectedId(a.id)
    dragRef.current = {
      mode: "move",
      startX: e.clientX,
      origStart: parseDate(a.dataInizio),
      origEnd: parseDate(a.dataFine),
      assignmentId: a.id,
      employeeId: a.employeeId,
      createStartCol: 0,
      lastDelta: 0,
      moved: false,
    }
    setPv({
      assignmentId: a.id,
      employeeId: a.employeeId,
      start: parseDate(a.dataInizio),
      end: parseDate(a.dataFine),
    })
  }

  function beginResize(e: React.PointerEvent, a: ResAssignmentDto, end: boolean) {
    if (!canEdit || e.button !== 0) return
    e.stopPropagation()
    opts.setSelectedId(a.id)
    dragRef.current = {
      mode: end ? "resizeEnd" : "resizeStart",
      startX: e.clientX,
      origStart: parseDate(a.dataInizio),
      origEnd: parseDate(a.dataFine),
      assignmentId: a.id,
      employeeId: a.employeeId,
      createStartCol: 0,
      lastDelta: 0,
      moved: false,
    }
    setPv({
      assignmentId: a.id,
      employeeId: a.employeeId,
      start: parseDate(a.dataInizio),
      end: parseDate(a.dataFine),
    })
  }

  function beginCreate(e: React.PointerEvent, employeeId: number) {
    if (!canEdit || e.button !== 0 || dragRef.current) return
    // Solo se il pointer è sul track vuoto (le barre fanno stopPropagation).
    const offsetX = e.nativeEvent.offsetX
    const startCol = Math.min(
      Math.max(Math.floor(offsetX / dayW), 0),
      bandDays - 1
    )
    e.preventDefault()
    dragRef.current = {
      mode: "create",
      startX: e.clientX,
      origStart: bandStart,
      origEnd: bandStart,
      assignmentId: -1,
      employeeId,
      createStartCol: startCol,
      lastDelta: 0,
      moved: false,
    }
    setPv({
      assignmentId: -1,
      employeeId,
      start: addDays(bandStart, startCol),
      end: addDays(bandStart, startCol),
    })
  }

  // Finalizza al rilascio (riassegnato a ogni render: chiude sui dati freschi).
  finishDragRef.current = () => {
    const d = dragRef.current
    const pv = previewRef.current
    dragRef.current = null
    setPv(null)
    if (!d || !pv) return
    if (d.moved) {
      // evita che il click successivo deselezioni/riapra
      suppressClickRef.current = true
    }

    void (async () => {
      if (d.mode === "create") {
        if (!d.moved) return // semplice click su area vuota: niente
        if (
          !(await opts.confirmConflict(
            d.employeeId,
            pv.start,
            pv.end,
            opts.createTipo,
            0
          ))
        )
          return
        try {
          await createAssignments(
            {
              employeeIds: [d.employeeId],
              tipo: opts.createTipo,
              dataInizio: toIso(pv.start),
              dataFine: toIso(pv.end),
            },
            opts.connRef.current
          )
          opts.flashStatus("Allocazione creata")
          await opts.reload()
          void opts.refreshPendingNotify()
        } catch (err) {
          opts.flashStatus(
            err instanceof ApiError ? err.message : "Errore creazione"
          )
        }
        return
      }

      // move / resize
      const a = opts.assignments.find((x) => x.id === d.assignmentId)
      if (!a) return
      const sameStart = toIso(pv.start) === a.dataInizio.slice(0, 10)
      const sameEnd = toIso(pv.end) === a.dataFine.slice(0, 10)
      if (sameStart && sameEnd) return // invariato
      if (
        !(await opts.confirmConflict(a.employeeId, pv.start, pv.end, a.tipo, a.id))
      )
        return
      try {
        await updateAssignment(
          a.id,
          {
            employeeId: a.employeeId,
            tipo: a.tipo,
            dataInizio: toIso(pv.start),
            dataFine: toIso(pv.end),
            projectId: a.projectId,
            serviceId: a.serviceId,
            otherActivityId: a.otherActivityId,
            descrizione: a.descrizione,
            expectedUpdatedAt: a.updatedAt,
          },
          opts.connRef.current
        )
        opts.flashStatus("Salvato")
        await opts.reload()
        void opts.refreshPendingNotify()
      } catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          // Concorrenza: un altro utente ha modificato questa allocazione nel frattempo.
          // Ricarichiamo subito così la barra torna al suo stato reale invece di restare
          // ferma nella posizione di anteprima (mai salvata).
          opts.flashStatus(
            "Modificata da un altro utente nel frattempo — dati aggiornati"
          )
          await opts.reload()
        } else {
          opts.flashStatus(
            err instanceof ApiError ? err.message : "Errore salvataggio"
          )
        }
      }
    })()
  }

  // Listener globali pointer/tastiera per il drag (montati una volta).
  React.useEffect(() => {
    function onMove(ev: PointerEvent) {
      const d = dragRef.current
      if (!d) return
      // Auto-pan ai bordi del viewport (come planner.js).
      const el = scrollRef.current
      if (el) {
        const rect = el.getBoundingClientRect()
        const margin = 48
        const speed = 16
        if (ev.clientX < rect.left + margin) {
          el.scrollLeft = Math.max(0, el.scrollLeft - speed)
        } else if (ev.clientX > rect.right - margin) {
          el.scrollLeft = Math.min(
            el.scrollWidth - el.clientWidth,
            el.scrollLeft + speed
          )
        }
      }
      const w = dayWRef.current
      let delta = Math.round((ev.clientX - d.startX) / w)
      if (ev.shiftKey) delta = Math.round(delta / 7) * 7
      if (delta === d.lastDelta) return
      d.lastDelta = delta
      if (delta !== 0) d.moved = true
      const bStart = bandStartRef.current
      const bDays = bandDaysRef.current
      if (d.mode === "move") {
        setPv({
          assignmentId: d.assignmentId,
          employeeId: d.employeeId,
          start: addDays(d.origStart, delta),
          end: addDays(d.origEnd, delta),
        })
      } else if (d.mode === "resizeStart") {
        let ns = addDays(d.origStart, delta)
        if (ns > d.origEnd) ns = d.origEnd
        setPv({
          assignmentId: d.assignmentId,
          employeeId: d.employeeId,
          start: ns,
          end: d.origEnd,
        })
      } else if (d.mode === "resizeEnd") {
        let ne = addDays(d.origEnd, delta)
        if (ne < d.origStart) ne = d.origStart
        setPv({
          assignmentId: d.assignmentId,
          employeeId: d.employeeId,
          start: d.origStart,
          end: ne,
        })
      } else {
        const col2 = Math.min(Math.max(d.createStartCol + delta, 0), bDays - 1)
        const a = Math.min(d.createStartCol, col2)
        const b = Math.max(d.createStartCol, col2)
        setPv({
          assignmentId: -1,
          employeeId: d.employeeId,
          start: addDays(bStart, a),
          end: addDays(bStart, b),
        })
      }
    }
    function onUp() {
      if (!dragRef.current) return
      finishDragRef.current?.()
    }
    function onKey(ev: KeyboardEvent) {
      // Ctrl/Cmd+F: sempre attivo, mette il focus sulla ricerca (anche mentre si digita altrove).
      if ((ev.key === "f" || ev.key === "F") && (ev.ctrlKey || ev.metaKey)) {
        ev.preventDefault()
        searchInputRef.current?.focus()
        return
      }
      const tag = (ev.target as HTMLElement | null)?.tagName?.toUpperCase() ?? ""
      const typing = tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT"
      if (typing) return
      if (ev.key === "Escape" && dragRef.current) {
        dragRef.current = null
        setPv(null)
        return
      }
      if (ev.key === "Delete") {
        if (dragRef.current || dialogOpenRef.current) return
        const id = selectedIdRef.current
        if (id == null) return
        const a = assignmentsForKeyRef.current.find((x) => x.id === id)
        if (a) removeAssignmentRef.current?.(a)
      }
    }
    // Shift+rotella: scorrimento orizzontale del Gantt (come il sorgente Blazor).
    function onWheel(ev: WheelEvent) {
      if (!ev.shiftKey) return
      ev.preventDefault()
      const el = scrollRef.current
      if (!el) return
      el.scrollLeft += (ev.deltaY > 0 ? 3 : -3) * dayWRef.current
    }
    window.addEventListener("pointermove", onMove, true)
    window.addEventListener("pointerup", onUp, true)
    window.addEventListener("keydown", onKey, true)
    const scrollEl = scrollRef.current
    scrollEl?.addEventListener("wheel", onWheel, { passive: false })
    return () => {
      window.removeEventListener("pointermove", onMove, true)
      window.removeEventListener("pointerup", onUp, true)
      window.removeEventListener("keydown", onKey, true)
      scrollEl?.removeEventListener("wheel", onWheel)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return { preview, beginMove, beginResize, beginCreate, suppressClickRef }
}
