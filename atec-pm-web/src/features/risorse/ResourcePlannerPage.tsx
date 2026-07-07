import * as React from "react"
import { useNavigate } from "react-router-dom"

import "./risorse-gantt.css"

import { ApiError } from "@/lib/api/client"
import { fetchNotifyPending } from "@/lib/api/digest"
import {
  deleteAssignment as apiDeleteAssignment,
  createAssignments,
  fetchAssignments,
  fetchProjectLookups,
  fetchResourceLookups,
  updateAssignment,
} from "@/lib/api/resource-planner"
import type { LookupItem, ResAssignmentDto, ResTipo } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { useResourcePlannerHub } from "@/lib/signalr/use-resource-planner-hub"
import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Switch } from "@/components/ui/switch"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { AssignmentDialog } from "@/features/risorse/AssignmentDialog"
import { NotifyNowDialog } from "@/features/risorse/NotifyNowDialog"
import {
  addDays,
  assignmentLabel,
  assignmentMatchesSearch,
  buildTooltip,
  diffDays,
  dowLetter,
  initials,
  isHoliday,
  isWeekend,
  mondayOf,
  monthName,
  parseDate,
  startOfDay,
  surname,
  tipoCssClass,
  toIso,
  wildcardMatch,
  wouldConflict,
} from "@/features/risorse/planner-logic"
import {
  usePlannerSettings,
  type PlannerUiSettings,
} from "@/features/risorse/use-planner-settings"

const LANE_HEIGHT = 26
const BAR_HEIGHT = 18
const ALL_TIPI: ResTipo[] = ["OP", "FLEX", "FERIE"]
const TIPO_LABELS: Record<ResTipo, string> = {
  OP: "Operativo",
  FLEX: "Flessibile",
  FERIE: "Ferie",
}

// Zoom: giorni "di riferimento" → larghezza colonna giorno.
const DAY_WIDTH: Record<number, number> = { 14: 46, 30: 32, 60: 20 }
function dayWidthFor(windowDays: number): number {
  return DAY_WIDTH[windowDays] ?? 32
}
const NAME_COL_WIDTH_EXPANDED = 200
function nameWidthFor(mode: PlannerUiSettings["nameColMode"]): number {
  if (mode === "badge") return 52
  if (mode === "surname") return 96
  return NAME_COL_WIDTH_EXPANDED
}

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

interface DragPreview {
  assignmentId: number // -1 per create
  employeeId: number
  start: Date
  end: Date
}

interface PlacedBar {
  a: ResAssignmentDto
  lane: number
}
interface RowData {
  resource: LookupItem
  bars: PlacedBar[]
  lanes: number
}

/** Lane-packing: distribuisce le allocazioni sovrapposte su corsie verticali. */
function packLanes(items: ResAssignmentDto[]): { placed: PlacedBar[]; lanes: number } {
  const sorted = [...items].sort(
    (a, b) => parseDate(a.dataInizio).getTime() - parseDate(b.dataInizio).getTime()
  )
  const laneEnds: Date[] = []
  const placed: PlacedBar[] = sorted.map((a) => {
    const s = parseDate(a.dataInizio)
    const e = parseDate(a.dataFine)
    let lane = laneEnds.findIndex((end) => end < s)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(e)
    } else {
      laneEnds[lane] = e
    }
    return { a, lane }
  })
  return { placed, lanes: Math.max(1, laneEnds.length) }
}

export function ResourcePlannerPage() {
  const confirm = useConfirm()
  const navigate = useNavigate()
  const myEmployeeId = getSession()?.user.employeeId ?? 0
  const canEdit = canAccessFeature("resources.edit")

  const [settings, patch] = usePlannerSettings()
  const [assignments, setAssignments] = React.useState<ResAssignmentDto[]>([])
  const [resources, setResources] = React.useState<LookupItem[]>([])
  const [projects, setProjects] = React.useState<LookupItem[]>([])
  const [loading, setLoading] = React.useState(true)
  const [status, setStatus] = React.useState<string | null>(null)
  const [selectedId, setSelectedId] = React.useState<number | null>(null)
  // Ricerca globale (toolbar): filtra risorse+attività nel Gantt. Distinta dalla ricerca
  // del pannello laterale (sideSearch), che filtra solo l'elenco checkbox senza toccare il Gantt.
  const [resSearch, setResSearch] = React.useState("")
  const [sideSearch, setSideSearch] = React.useState("")
  const [poolPopoverOpen, setPoolPopoverOpen] = React.useState(false)

  // Dialogo allocazione
  const [dialogOpen, setDialogOpen] = React.useState(false)
  const [dialogExisting, setDialogExisting] =
    React.useState<ResAssignmentDto | null>(null)
  const [dialogPreset, setDialogPreset] = React.useState<{
    employeeId?: number | null
    employeeName?: string | null
    start?: string | null
    end?: string | null
    tipo?: ResTipo | null
  }>({})

  // Digest email: badge "modifiche da notificare" + dialog di invio selettivo.
  const [pendingNotify, setPendingNotify] = React.useState(0)
  const [emailConfigurata, setEmailConfigurata] = React.useState(false)
  const [notifyDialogOpen, setNotifyDialogOpen] = React.useState(false)

  const connRef = React.useRef<string | null>(null)
  const scrollRef = React.useRef<HTMLDivElement | null>(null)
  const [periodLabel, setPeriodLabel] = React.useState("")
  const didInitialScroll = React.useRef(false)

  // Tipo usato per la create-by-drag (segue il primo tipo attivo in legenda, default OP).
  const createTipo: ResTipo = settings.tipiVisibili.includes("OP")
    ? "OP"
    : ((settings.tipiVisibili[0] as ResTipo) ?? "OP")

  // ── Stato drag (port di plannerInterop.startDrag + OnDragMove/End) ──
  const dragRef = React.useRef<DragInfo | null>(null)
  const previewRef = React.useRef<DragPreview | null>(null)
  const [preview, setPreview] = React.useState<DragPreview | null>(null)
  const suppressClickRef = React.useRef(false)
  const setPv = React.useCallback((p: DragPreview | null) => {
    previewRef.current = p
    setPreview(p)
  }, [])

  // ── Geometria banda fissa (anno corrente + prossimo) ──────────
  const { bandStart, bandDays, today } = React.useMemo(() => {
    const now = new Date()
    const start = mondayOf(new Date(now.getFullYear(), 0, 1))
    const end = new Date(now.getFullYear() + 1, 11, 31)
    return {
      bandStart: start,
      bandDays: diffDays(end, start) + 1,
      today: startOfDay(now),
    }
  }, [])
  const dayW = dayWidthFor(settings.windowDays)
  const nameW = nameWidthFor(settings.nameColMode)
  const nameColCollapsed = settings.nameColMode !== "full"

  // ── Stampa: finestra "fotografata" per adattare il Gantt a una pagina A4 ──
  // Durante la stampa il rendering usa questi valori al posto di bandStart/bandDays/dayW
  // (solo per il layout grafico: la logica di business/drag resta sui valori reali).
  const [printOverride, setPrintOverride] = React.useState<{
    start: Date
    days: number
    dayW: number
  } | null>(null)
  const [printing, setPrinting] = React.useState(false)
  const activeBandStart = printOverride?.start ?? bandStart
  const activeBandDays = printOverride?.days ?? bandDays
  const activeDayW = printOverride?.dayW ?? dayW
  const activeTimelineWidth = activeBandDays * activeDayW

  // Ref ai valori dinamici letti dagli handler pointer (montati una volta).
  const dayWRef = React.useRef(dayW)
  dayWRef.current = dayW
  const bandStartRef = React.useRef(bandStart)
  bandStartRef.current = bandStart
  const bandDaysRef = React.useRef(bandDays)
  bandDaysRef.current = bandDays
  const finishDragRef = React.useRef<(() => void) | null>(null)

  // Ref ai valori dinamici letti dalla scorciatoia "Canc" (stesso mount-effect del drag).
  const selectedIdRef = React.useRef(selectedId)
  selectedIdRef.current = selectedId
  const assignmentsForKeyRef = React.useRef(assignments)
  assignmentsForKeyRef.current = assignments
  const dialogOpenRef = React.useRef(dialogOpen)
  dialogOpenRef.current = dialogOpen
  const removeAssignmentRef = React.useRef<((a: ResAssignmentDto) => void) | null>(
    null
  )
  const searchInputRef = React.useRef<HTMLInputElement | null>(null)

  function flashStatus(msg: string) {
    setStatus(msg)
    window.setTimeout(() => setStatus(null), 2500)
  }

  // ── Caricamento dati ──────────────────────────────────────────
  const reload = React.useCallback(async () => {
    try {
      const list = await fetchAssignments()
      setAssignments(list)
    } catch (e) {
      flashStatus(e instanceof ApiError ? e.message : "Errore di caricamento")
    }
  }, [])

  React.useEffect(() => {
    let alive = true
    void (async () => {
      setLoading(true)
      try {
        const [a, res, proj] = await Promise.all([
          fetchAssignments(),
          fetchResourceLookups(),
          fetchProjectLookups(),
        ])
        if (!alive) return
        setAssignments(a)
        setResources(res)
        setProjects(proj)
      } catch (e) {
        if (alive)
          flashStatus(
            e instanceof ApiError ? e.message : "Errore di caricamento"
          )
      } finally {
        if (alive) setLoading(false)
      }
    })()
    return () => {
      alive = false
    }
  }, [])

  // ── Digest email: refresh del conteggio "modifiche da notificare" ─────
  const refreshPendingNotify = React.useCallback(async () => {
    if (!canEdit) return
    try {
      const pending = await fetchNotifyPending()
      setPendingNotify(pending.totalChanges)
      setEmailConfigurata(pending.emailConfigurata)
    } catch {
      /* badge best-effort: non disturbare l'utente per un conteggio informativo */
    }
  }, [canEdit])

  React.useEffect(() => {
    void refreshPendingNotify()
  }, [refreshPendingNotify])

  // ── Realtime: ricarica quando un altro utente modifica ────────
  const onRealtime = React.useCallback(() => {
    void reload().then(() => flashStatus("Aggiornato"))
    void refreshPendingNotify()
  }, [reload, refreshPendingNotify])
  const onlineIds = useResourcePlannerHub(onRealtime, connRef)

  // ── Festività/oggi: overlay precalcolati sulla banda (attiva = reale o "stampa") ──
  const overlays = React.useMemo(() => {
    const holidays: { left: number; width: number }[] = []
    for (let i = 0; i < activeBandDays; i++) {
      const d = addDays(activeBandStart, i)
      if (isHoliday(d)) holidays.push({ left: i * activeDayW, width: activeDayW })
    }
    const todayIdx = diffDays(today, activeBandStart)
    const todayLeft =
      todayIdx >= 0 && todayIdx < activeBandDays ? todayIdx * activeDayW : -1
    return { holidays, todayLeft }
  }, [activeBandStart, activeBandDays, activeDayW, today])

  // Sfondo track: weekend (gradiente, banda parte di lunedì) + linee giorno.
  const trackBackground = React.useMemo(() => {
    const w = activeDayW
    const weekend = `repeating-linear-gradient(90deg, transparent 0, transparent ${
      5 * w
    }px, var(--g-weekend) ${5 * w}px, var(--g-weekend) ${7 * w}px)`
    const lines = `repeating-linear-gradient(90deg, var(--g-line-soft) 0, var(--g-line-soft) 1px, transparent 1px, transparent ${w}px)`
    return `${lines}, ${weekend}`
  }, [activeDayW])

  // ── Pool "In lista" + interruttori pannello ────────────────────
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

  // ── Scroll: etichetta periodo + scroll iniziale su oggi ───────
  const updatePeriodLabel = React.useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const firstIdx = Math.floor(el.scrollLeft / dayW)
    const lastIdx = Math.floor((el.scrollLeft + el.clientWidth) / dayW)
    const a = addDays(bandStart, Math.max(0, firstIdx))
    const b = addDays(bandStart, Math.min(bandDays - 1, lastIdx))
    if (a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth()) {
      setPeriodLabel(`${monthName(a.getMonth() + 1)} ${a.getFullYear()}`)
    } else if (a.getFullYear() === b.getFullYear()) {
      setPeriodLabel(
        `${monthName(a.getMonth() + 1)} – ${monthName(b.getMonth() + 1)} ${a.getFullYear()}`
      )
    } else {
      setPeriodLabel(
        `${monthName(a.getMonth() + 1)} ${a.getFullYear()} – ${monthName(
          b.getMonth() + 1
        )} ${b.getFullYear()}`
      )
    }
  }, [bandStart, bandDays, dayW])

  const rafRef = React.useRef<number | null>(null)
  function handleScroll() {
    if (rafRef.current != null) return
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      updatePeriodLabel()
    })
  }

  React.useEffect(() => {
    if (loading || didInitialScroll.current) return
    const el = scrollRef.current
    if (!el) return
    const todayIdx = diffDays(today, bandStart)
    el.scrollLeft = Math.max(0, (todayIdx - 2) * dayW)
    didInitialScroll.current = true
    updatePeriodLabel()
  }, [loading, today, bandStart, dayW, updatePeriodLabel])

  React.useEffect(() => {
    updatePeriodLabel()
  }, [dayW, updatePeriodLabel])

  // ── Header: mesi (span) + giorni ──────────────────────────────
  const months = React.useMemo(() => {
    const out: { label: string; width: number }[] = []
    let i = 0
    while (i < activeBandDays) {
      const d = addDays(activeBandStart, i)
      const m = d.getMonth()
      let span = 0
      while (i + span < activeBandDays) {
        const dd = addDays(activeBandStart, i + span)
        if (dd.getMonth() !== m) break
        span++
      }
      out.push({
        label: `${monthName(m + 1)} ${d.getFullYear()}`,
        width: span * activeDayW,
      })
      i += span
    }
    return out
  }, [activeBandStart, activeBandDays, activeDayW])

  // ── Azioni ────────────────────────────────────────────────────
  function openCreate() {
    setDialogExisting(null)
    setDialogPreset({})
    setDialogOpen(true)
  }
  function openEdit(a: ResAssignmentDto) {
    setDialogExisting(a)
    setDialogPreset({})
    setDialogOpen(true)
  }
  function openDuplicate(a: ResAssignmentDto) {
    const s = addDays(parseDate(a.dataInizio), 7)
    const e = addDays(parseDate(a.dataFine), 7)
    setDialogExisting(null)
    setDialogPreset({
      employeeId: a.employeeId,
      employeeName: a.employeeName,
      start: toIso(s),
      end: toIso(e),
      tipo: a.tipo,
    })
    setDialogOpen(true)
  }
  async function removeAssignment(a: ResAssignmentDto) {
    const ok = await confirm({
      title: "Eliminare l'allocazione?",
      description: `${a.employeeName} · ${assignmentLabel(a)}`,
    })
    if (!ok) return
    try {
      await apiDeleteAssignment(a.id, connRef.current)
      setAssignments((prev) => prev.filter((x) => x.id !== a.id))
      if (selectedId === a.id) setSelectedId(null)
      flashStatus("Allocazione eliminata")
      void refreshPendingNotify()
    } catch (e) {
      flashStatus(e instanceof ApiError ? e.message : "Errore eliminazione")
    }
  }
  removeAssignmentRef.current = removeAssignment

  function onSaved(message: string) {
    flashStatus(message)
    void reload()
    void refreshPendingNotify()
  }

  // ── Stampa del Gantt (periodo visibile, adattato a una pagina A4 orizzontale) ──
  async function printGantt() {
    if (printing) return
    const el = scrollRef.current
    if (!el) return
    setPrinting(true)
    try {
      const firstIdx = Math.max(0, Math.floor(el.scrollLeft / dayW))
      const printStart = addDays(bandStart, firstIdx)
      const printDays = settings.windowDays
      const printDayW = Math.max(
        12,
        Math.floor((1040 - NAME_COL_WIDTH_EXPANDED) / printDays)
      )
      setPrintOverride({ start: printStart, days: printDays, dayW: printDayW })
      await new Promise((r) => setTimeout(r, 250)) // lascia completare il render
      document.body.classList.add("printing-gantt")
      const style = document.createElement("style")
      style.id = "gantt-print-page"
      style.textContent = "@page { size: A4 landscape; margin: 1cm; }"
      document.head.appendChild(style)
      window.print() // blocca finché il dialogo di stampa è aperto (Chromium)
    } finally {
      document.body.classList.remove("printing-gantt")
      document.getElementById("gantt-print-page")?.remove()
      setPrintOverride(null)
      setPrinting(false)
    }
  }

  // ── Collassa pannello risorse / colonna nomi (badge o solo cognome) ────
  function toggleResSide() {
    patch({ resSideCollapsed: !settings.resSideCollapsed })
  }
  function toggleNameCol() {
    patch({ nameColMode: nameColCollapsed ? "full" : "badge" })
  }
  function toggleNameColCompact() {
    patch({ nameColMode: settings.nameColMode === "surname" ? "badge" : "surname" })
  }

  // ── Drag: avvio (move / resize / create) ──────────────────────
  function beginMove(e: React.PointerEvent, a: ResAssignmentDto) {
    if (!canEdit || e.button !== 0) return
    e.stopPropagation()
    setSelectedId(a.id)
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
    setSelectedId(a.id)
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

  async function confirmConflict(
    employeeId: number,
    start: Date,
    end: Date,
    tipo: string,
    excludeId: number
  ): Promise<boolean> {
    if (!wouldConflict(assignments, employeeId, start, end, tipo, excludeId)) {
      return true
    }
    return confirm({
      title: "Genera un conflitto",
      description:
        "L'allocazione si sovrappone a un'altra incompatibile per questa risorsa. Salvare comunque?",
      destructive: false,
      confirmLabel: "Salva comunque",
    })
  }

  // Finalizza al rilascio (aggiornato a ogni render via finishDragRef).
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
        if (!(await confirmConflict(d.employeeId, pv.start, pv.end, createTipo, 0)))
          return
        try {
          await createAssignments(
            {
              employeeIds: [d.employeeId],
              tipo: createTipo,
              dataInizio: toIso(pv.start),
              dataFine: toIso(pv.end),
            },
            connRef.current
          )
          flashStatus("Allocazione creata")
          await reload()
          void refreshPendingNotify()
        } catch (err) {
          flashStatus(err instanceof ApiError ? err.message : "Errore creazione")
        }
        return
      }

      // move / resize
      const a = assignments.find((x) => x.id === d.assignmentId)
      if (!a) return
      const sameStart = toIso(pv.start) === a.dataInizio.slice(0, 10)
      const sameEnd = toIso(pv.end) === a.dataFine.slice(0, 10)
      if (sameStart && sameEnd) return // invariato
      if (!(await confirmConflict(a.employeeId, pv.start, pv.end, a.tipo, a.id)))
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
          connRef.current
        )
        flashStatus("Salvato")
        await reload()
        void refreshPendingNotify()
      } catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          // Concorrenza: un altro utente ha modificato questa allocazione nel frattempo.
          // Ricarichiamo subito così la barra torna al suo stato reale invece di restare
          // ferma nella posizione di anteprima (mai salvata).
          flashStatus("Modificata da un altro utente nel frattempo — dati aggiornati")
          await reload()
        } else {
          flashStatus(err instanceof ApiError ? err.message : "Errore salvataggio")
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

  // ── Pannello risorse: interruttori Gantt (solo tra i membri del pool) ──
  function toggleGanttOff(id: number) {
    const set = new Set(settings.ganttOffIds)
    if (set.has(id)) set.delete(id)
    else set.add(id)
    patch({ ganttOffIds: Array.from(set) })
  }
  function showAll() {
    const offOutsidePool = settings.ganttOffIds.filter((id) => !inPool(id))
    patch({ ganttOffIds: offOutsidePool })
  }
  function hideAll() {
    const inPoolIds = resources.filter((r) => inPool(r.id)).map((r) => r.id)
    const set = new Set(settings.ganttOffIds)
    for (const id of inPoolIds) set.add(id)
    patch({ ganttOffIds: Array.from(set) })
  }

  function toggleTipo(t: ResTipo) {
    const set = new Set(settings.tipiVisibili)
    if (set.has(t)) set.delete(t)
    else set.add(t)
    patch({ tipiVisibili: Array.from(set) })
  }

  // ── Filtro "In lista" (pool ristretto di risorse) ──────────────
  const resourceFilterLabel = settings.resourceFilterActive
    ? `${settings.selectedResourceIds.length} in lista`
    : "tutte"

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
  function recomputeResourceSelection(checkedIds: Set<number>) {
    const allChecked = resources.length > 0 && checkedIds.size === resources.length
    if (allChecked) {
      patch({ resourceFilterActive: false, selectedResourceIds: [] })
    } else {
      patch({ resourceFilterActive: true, selectedResourceIds: Array.from(checkedIds) })
    }
  }

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

  // ── Render barra ──────────────────────────────────────────────
  function renderBar(a: ResAssignmentDto, lane: number) {
    const dragging = preview != null && preview.assignmentId === a.id
    const s = dragging ? preview.start : parseDate(a.dataInizio)
    const e = dragging ? preview.end : parseDate(a.dataFine)
    const startIdx = diffDays(s, activeBandStart)
    const span = diffDays(e, s) + 1
    const left = startIdx * activeDayW
    const width = Math.max(activeDayW, span * activeDayW)
    const top = lane * LANE_HEIGHT + (LANE_HEIGHT - BAR_HEIGHT) / 2
    const classes = [
      "g2-bar",
      tipoCssClass(a.tipo),
      a.hasConflict ? "conflict" : "",
      selectedId === a.id ? "selected" : "",
      dragging ? "dragging" : "",
    ]
      .filter(Boolean)
      .join(" ")

    const bar = (
      <div
        className={classes}
        style={{ left, width, top, height: BAR_HEIGHT }}
        title={buildTooltip(a, s, e)}
        onPointerDown={(ev) => beginMove(ev, a)}
        onClick={(ev) => {
          ev.stopPropagation()
          if (suppressClickRef.current) {
            suppressClickRef.current = false
            return
          }
          setSelectedId(a.id)
        }}
        onDoubleClick={(ev) => {
          ev.stopPropagation()
          if (canEdit) openEdit(a)
        }}
      >
        {canEdit && (
          <span
            className="grip grip-l"
            onPointerDown={(ev) => beginResize(ev, a, false)}
          />
        )}
        <span className="g2-bar-label">
          {a.hasConflict ? "⚠ " : ""}
          {assignmentLabel(a)}
        </span>
        {canEdit && (
          <span
            className="grip grip-r"
            onPointerDown={(ev) => beginResize(ev, a, true)}
          />
        )}
      </div>
    )

    if (!canEdit) return <React.Fragment key={a.id}>{bar}</React.Fragment>

    return (
      <ContextMenu key={a.id}>
        <ContextMenuTrigger asChild>{bar}</ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem onSelect={() => openEdit(a)}>
            Modifica
          </ContextMenuItem>
          <ContextMenuItem onSelect={() => openDuplicate(a)}>
            Duplica (+7 gg)
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem
            className="text-destructive"
            onSelect={() => void removeAssignment(a)}
          >
            Elimina
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    )
  }

  return (
    <div className="planner-root flex h-[calc(100vh-7rem)] flex-col">
      {/* Toolbar */}
      <div className="no-print flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-card px-3 py-2">
        <div className="flex flex-wrap items-center gap-2">
          {canEdit && (
            <Button size="sm" onClick={openCreate}>
              + Allocazione
            </Button>
          )}
          <Button
            size="sm"
            variant="outline"
            onClick={() => navigate("/risorse/ferie")}
          >
            Piano ferie
          </Button>
          {canEdit && pendingNotify > 0 && (
            <button
              type="button"
              className="tb-notify-info no-print"
              title="Clic per scegliere quali modifiche notificare subito"
              onClick={() => setNotifyDialogOpen(true)}
            >
              ⏳ {pendingNotify} {pendingNotify === 1 ? "modifica" : "modifiche"} da notificare
            </button>
          )}
          <div className="flex items-center gap-1">
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                const el = scrollRef.current
                if (el) el.scrollLeft -= 7 * dayW
              }}
            >
              ◀
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                const el = scrollRef.current
                if (!el) return
                const idx = diffDays(today, bandStart)
                el.scrollLeft = Math.max(0, (idx - 2) * dayW)
              }}
            >
              Oggi
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                const el = scrollRef.current
                if (el) el.scrollLeft += 7 * dayW
              }}
            >
              ▶
            </Button>
          </div>
          <span className="px-1 text-sm font-semibold">{periodLabel}</span>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {conflictCount > 0 && (
            <span className="rounded-md border border-red-200 bg-red-50 px-2 py-1 text-xs font-semibold text-red-600">
              {conflictCount} conflitti
            </span>
          )}
          {/* Legenda / toggle tipo */}
          <div className="legend">
            {ALL_TIPI.map((t) => (
              <span
                key={t}
                className={`legend-chip ${
                  settings.tipiVisibili.includes(t) ? "active" : ""
                }`}
                onClick={() => toggleTipo(t)}
              >
                <span
                  className="dot"
                  style={{
                    background:
                      t === "OP"
                        ? "#3B82F6"
                        : t === "FLEX"
                          ? "#9CA3AF"
                          : "#EF4444",
                  }}
                />
                {TIPO_LABELS[t]}
              </span>
            ))}
          </div>
          {/* Zoom */}
          <Select
            value={String(settings.windowDays)}
            onValueChange={(v) => patch({ windowDays: Number(v) })}
          >
            <SelectTrigger className="h-8 w-[130px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="60">Compatto</SelectItem>
              <SelectItem value="30">Normale</SelectItem>
              <SelectItem value="14">Largo</SelectItem>
            </SelectContent>
          </Select>
          <Button
            size="sm"
            variant="outline"
            title="Stampa il periodo visibile del Gantt"
            disabled={printing}
            onClick={() => void printGantt()}
          >
            🖨 Stampa
          </Button>
          <Popover>
            <PopoverTrigger asChild>
              <Button size="sm" variant="outline" className="btn-help" title="Scorciatoie">
                ?
              </Button>
            </PopoverTrigger>
            <PopoverContent className="help-popup w-96" align="end">
              <strong>Scorciatoie</strong>
              <div className="help-section">CALENDARIO</div>
              <div>Scorri orizzontalmente — avanti/indietro nel tempo</div>
              <div>Shift + rotella — scorrimento orizzontale</div>
              <div className="help-section">GANTT · MOUSE</div>
              <div>Trascina barra — sposta le date (Shift = snap settimana)</div>
              <div>Bordi barra — ridimensiona inizio/fine</div>
              <div>Trascina su riga vuota — nuova allocazione (causale selezionata)</div>
              <div>Click — seleziona · Doppio click — modifica · Tasto destro — menu</div>
              <div className="help-section">TASTIERA</div>
              <div>Ctrl + F — cerca · Canc — elimina selezione · Esc — annulla</div>
            </PopoverContent>
          </Popover>
        </div>
      </div>

      {/* Filtri */}
      <div className="no-print mt-2 flex flex-wrap items-center gap-3 rounded-lg border bg-card px-3 py-2 text-sm">
        {myEmployeeId > 0 && (
          <label className="flex cursor-pointer items-center gap-2 text-sm">
            <Switch
              size="sm"
              checked={settings.mineOnly}
              onCheckedChange={(checked) => patch({ mineOnly: checked })}
            />
            Solo mie attività
          </label>
        )}
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <Switch
            size="sm"
            checked={settings.conflictsOnly}
            onCheckedChange={(checked) => patch({ conflictsOnly: checked })}
          />
          Solo conflitti
        </label>
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <Switch
            size="sm"
            checked={settings.occupiedOnly}
            onCheckedChange={(checked) => patch({ occupiedOnly: checked })}
          />
          Solo occupate
        </label>

        <span className="filter-lbl">In lista:</span>
        <Popover open={poolPopoverOpen} onOpenChange={setPoolPopoverOpen}>
          <PopoverTrigger asChild>
            <Button size="sm" variant="outline">
              {resourceFilterLabel} ▾
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-80" align="start">
            <div className="mb-2 flex gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => setAllResourcePicks(true)}
              >
                Tutte
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => setAllResourcePicks(false)}
              >
                Nessuna
              </Button>
            </div>
            <div className="grid max-h-72 grid-cols-2 gap-1 overflow-y-auto">
              {[...resources]
                .sort(
                  (a, b) =>
                    surname(a.name).localeCompare(surname(b.name)) ||
                    a.name.localeCompare(b.name)
                )
                .map((r) => (
                  <label
                    key={r.id}
                    className="flex cursor-pointer items-center gap-1.5 text-xs"
                  >
                    <Checkbox
                      checked={
                        !settings.resourceFilterActive ||
                        settings.selectedResourceIds.includes(r.id)
                      }
                      onCheckedChange={() => toggleResourcePick(r.id)}
                    />
                    <span className="truncate">{r.name}</span>
                  </label>
                ))}
            </div>
          </PopoverContent>
        </Popover>

        <input
          ref={searchInputRef}
          className="ml-auto h-8 w-[230px] rounded-md border px-2 text-sm"
          placeholder="Cerca risorsa o attività…"
          value={resSearch}
          onChange={(e) => setResSearch(e.target.value)}
        />
      </div>

      {/* Corpo: pannello risorse + Gantt */}
      <div className="planner-body mt-2">
        {/* Pannello laterale */}
        <div className={`res-side no-print ${settings.resSideCollapsed ? "collapsed" : ""}`}>
          <div className="res-side-head">
            <button
              type="button"
              className="res-side-toggle"
              title={settings.resSideCollapsed ? "Mostra il pannello risorse" : "Nascondi il pannello risorse"}
              aria-label={settings.resSideCollapsed ? "Mostra il pannello risorse" : "Nascondi il pannello risorse"}
              onClick={toggleResSide}
            >
              <svg
                className="g2-burger"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={2}
                strokeLinecap="round"
                aria-hidden="true"
              >
                <line x1="4" y1="7" x2="20" y2="7" />
                <line x1="4" y1="12" x2="20" y2="12" />
                <line x1="4" y1="17" x2="20" y2="17" />
              </svg>
            </button>
            <div className="res-side-title">Risorse</div>
            <div className="res-side-counter">
              {ganttOnCount} / {poolCount} in vista
            </div>
            <input
              className="res-side-search"
              placeholder="Cerca nominativo…"
              value={sideSearch}
              onChange={(e) => setSideSearch(e.target.value)}
            />
          </div>
          <div className="res-side-actions">
            <Button size="sm" variant="outline" onClick={showAll}>
              Tutte
            </Button>
            <Button size="sm" variant="outline" onClick={hideAll}>
              Nessuna
            </Button>
          </div>
          <div className="res-side-list">
            {sideResources.map((r) => {
              const on = ganttOn(r.id)
              return (
                <label
                  key={r.id}
                  className={`res-person ${r.id === myEmployeeId ? "sel" : ""}`}
                >
                  <Checkbox
                    checked={on}
                    onCheckedChange={() => toggleGanttOff(r.id)}
                  />
                  <span className="res-person-name">{r.name}</span>
                </label>
              )
            })}
            {poolCount === 0 && (
              <div className="px-2.5 py-2 text-xs text-muted-foreground">
                Nessuna risorsa in lista. Usa «In lista» in alto per sceglierle.
              </div>
            )}
          </div>
        </div>

        {/* Gantt */}
        <div className="gantt-scroll" ref={scrollRef} onScroll={handleScroll}>
          <div
            className={`gantt2 ${nameColCollapsed ? "name-col-collapsed" : ""}`}
            style={
              {
                "--g2-name-w": `${nameW}px`,
              } as React.CSSProperties
            }
          >
            {/* Header */}
            <div className="g2-head">
              <div className="g2-corner">
                <button
                  type="button"
                  className="g2-name-toggle no-print"
                  title={nameColCollapsed ? "Espandi colonna risorse" : "Comprimi colonna risorse"}
                  aria-label={nameColCollapsed ? "Espandi colonna risorse" : "Comprimi colonna risorse"}
                  onClick={toggleNameCol}
                >
                  <svg
                    className="g2-burger"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth={2}
                    strokeLinecap="round"
                    aria-hidden="true"
                  >
                    <line x1="4" y1="7" x2="20" y2="7" />
                    <line x1="4" y1="12" x2="20" y2="12" />
                    <line x1="4" y1="17" x2="20" y2="17" />
                  </svg>
                </button>
                {nameColCollapsed ? (
                  <button
                    type="button"
                    className="g2-name-mode no-print"
                    title={
                      settings.nameColMode === "surname"
                        ? "Mostra badge con iniziali"
                        : "Mostra solo cognome"
                    }
                    onClick={toggleNameColCompact}
                  >
                    {settings.nameColMode === "surname" ? "Ab" : "Co"}
                  </button>
                ) : (
                  <span className="g2-corner-label">Risorsa</span>
                )}
              </div>
              <div className="g2-thead">
                <div className="g2-months" style={{ width: activeTimelineWidth }}>
                  {months.map((m, idx) => (
                    <div
                      key={idx}
                      className="g2-month"
                      style={{ width: m.width, minWidth: m.width }}
                    >
                      {m.label}
                    </div>
                  ))}
                </div>
                <div className="g2-days" style={{ width: activeTimelineWidth }}>
                  {Array.from({ length: activeBandDays }, (_, i) => {
                    const d = addDays(activeBandStart, i)
                    const we = isWeekend(d)
                    const ho = isHoliday(d)
                    const isToday = startOfDay(d).getTime() === today.getTime()
                    const cls = [
                      "g2-day",
                      isToday ? "today" : "",
                      we ? "weekend" : "",
                      ho ? "holiday" : "",
                    ]
                      .filter(Boolean)
                      .join(" ")
                    return (
                      <div
                        key={i}
                        className={cls}
                        style={{ width: activeDayW, minWidth: activeDayW }}
                      >
                        <span className="g2-dow">{dowLetter(d)}</span>
                        <span
                          className={`g2-dnum ${we || ho ? "red" : ""}`}
                        >
                          {d.getDate()}
                        </span>
                      </div>
                    )
                  })}
                </div>
              </div>
            </div>

            {/* Righe */}
            {rows.length === 0 ? (
              <div className="g2-empty">
                {loading
                  ? "Caricamento…"
                  : "Nessuna risorsa da mostrare con i filtri correnti."}
              </div>
            ) : (
              rows.map((row) => {
                const rowH = row.lanes * LANE_HEIGHT
                const isSelf = row.resource.id === myEmployeeId
                const isOnline = onlineIds.has(row.resource.id)
                const nameRowClass = nameColCollapsed
                  ? isOnline
                    ? "on"
                    : "off"
                  : isSelf
                    ? "self"
                    : ""
                return (
                  <div className="g2-row" key={row.resource.id}>
                    <div className={`g2-name ${nameRowClass}`} style={{ height: rowH }}>
                      {nameColCollapsed ? (
                        settings.nameColMode === "surname" ? (
                          <span
                            className="g2-name-surname"
                            title={`${row.resource.name} — ${isOnline ? "Online" : "Disconnesso"}`}
                          >
                            {surname(row.resource.name)}
                          </span>
                        ) : (
                          <span
                            className="g2-name-badge"
                            title={`${row.resource.name} — ${isOnline ? "Online" : "Disconnesso"}`}
                          >
                            {initials(row.resource.name)}
                          </span>
                        )
                      ) : (
                        <>
                          <span
                            className={`presence ${isOnline ? "on" : "off"}`}
                            title={isOnline ? "Online" : "Disconnesso"}
                          />
                          <span className="g2-name-text">{row.resource.name}</span>
                        </>
                      )}
                    </div>
                    <div
                      className="g2-track"
                      style={{
                        width: activeTimelineWidth,
                        height: rowH,
                        background: trackBackground,
                      }}
                      onPointerDown={(ev) => beginCreate(ev, row.resource.id)}
                      onClick={() => setSelectedId(null)}
                    >
                      {/* Overlay festività + oggi */}
                      {overlays.holidays.map((h, i) => (
                        <div
                          key={`h${i}`}
                          className="g2-overlay holiday"
                          style={{ left: h.left, width: h.width }}
                        />
                      ))}
                      {overlays.todayLeft >= 0 && (
                        <div
                          className="g2-overlay today"
                          style={{ left: overlays.todayLeft, width: activeDayW }}
                        />
                      )}
                      {row.bars.map((b) => renderBar(b.a, b.lane))}
                      {/* Anteprima create-by-drag su questa riga */}
                      {preview != null &&
                        preview.assignmentId === -1 &&
                        preview.employeeId === row.resource.id && (
                          <div
                            className={`g2-bar preview ${tipoCssClass(createTipo)}`}
                            style={{
                              left: diffDays(preview.start, bandStart) * dayW,
                              width: Math.max(
                                dayW,
                                (diffDays(preview.end, preview.start) + 1) * dayW
                              ),
                              top: (LANE_HEIGHT - BAR_HEIGHT) / 2,
                              height: BAR_HEIGHT,
                            }}
                          />
                        )}
                    </div>
                  </div>
                )
              })
            )}
          </div>
        </div>
      </div>

      {/* Status bar */}
      <div className="planner-status no-print mt-2">
        <span>
          {rows.length} risorse · {assignments.length} allocazioni ·{" "}
          {conflictCount} conflitti
        </span>
        <span className="status-right">
          {status && <span className="font-medium text-foreground">{status}</span>}
        </span>
      </div>

      <AssignmentDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        existing={dialogExisting}
        presetEmployeeId={dialogPreset.employeeId}
        presetEmployeeName={dialogPreset.employeeName}
        presetStart={dialogPreset.start}
        presetEnd={dialogPreset.end}
        presetTipo={dialogPreset.tipo}
        resources={resources}
        projects={projects}
        connRef={connRef}
        onSaved={onSaved}
      />

      <NotifyNowDialog
        open={notifyDialogOpen}
        onOpenChange={setNotifyDialogOpen}
        emailConfigurata={emailConfigurata}
        onSent={(message) => {
          flashStatus(message)
          void refreshPendingNotify()
        }}
      />
    </div>
  )
}
