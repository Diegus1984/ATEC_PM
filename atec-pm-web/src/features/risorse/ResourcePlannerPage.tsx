import * as React from "react"
import { useNavigate, useSearchParams } from "react-router-dom"

import "./risorse-gantt.css"

import { ApiError } from "@/lib/api/client"
import { deleteAssignment as apiDeleteAssignment } from "@/lib/api/resource-planner"
import type { ResAssignmentDto, ResTipo } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { useConfirm } from "@/components/shared/confirm"
import { AssignmentDialog } from "@/features/risorse/AssignmentDialog"
import { NotifyNowDialog } from "@/features/risorse/NotifyNowDialog"
import { SyncVpsWarning } from "@/features/risorse/SyncVpsWarning"

import { PlannerGantt } from "./planner-gantt"
import {
  dayOverlays,
  dayWidthFor,
  monthSpans,
  nameWidthFor,
  trackBackgroundCss,
} from "./planner-geometry"
import {
  addDays,
  assignmentLabel,
  diffDays,
  mondayOf,
  parseDate,
  startOfDay,
  toIso,
  wouldConflict,
} from "./planner-logic"
import { PlannerSidePanel } from "./planner-side-panel"
import { PlannerFilters, PlannerToolbar } from "./planner-toolbar"
import { usePlannerData } from "./use-planner-data"
import { usePlannerDrag } from "./use-planner-drag"
import { usePlannerRows } from "./use-planner-rows"
import { usePlannerSettings, type PlannerUiSettings } from "./use-planner-settings"
import { usePlannerViewport } from "./use-planner-viewport"

export function ResourcePlannerPage() {
  const confirm = useConfirm()
  const navigate = useNavigate()
  const myEmployeeId = getSession()?.user.employeeId ?? 0
  // `canWriteFeature` e non `canAccessFeature`: con la funzione concessa in sola lettura
  // il piano si guarda ma non si tocca, altrimenti l'interfaccia resterebbe scrivibile e a
  // respingere sarebbe solo l'API, a modifica già fatta a video.
  const canEdit = canWriteFeature("resources.edit")

  const [settings, patch] = usePlannerSettings()
  const {
    assignments,
    setAssignments,
    resources,
    projects,
    loading,
    status,
    flashStatus,
    reload,
    pendingNotify,
    emailConfigurata,
    refreshPendingNotify,
    connRef,
    onlineIds,
  } = usePlannerData(canEdit)

  const [selectedId, setSelectedId] = React.useState<number | null>(null)
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
  const [notifyDialogOpen, setNotifyDialogOpen] = React.useState(false)

  const searchInputRef = React.useRef<HTMLInputElement | null>(null)

  // Tipo usato per la create-by-drag (segue il primo tipo attivo in legenda, default OP).
  const createTipo: ResTipo = settings.tipiVisibili.includes("OP")
    ? "OP"
    : ((settings.tipiVisibili[0] as ResTipo) ?? "OP")

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
  const nameColCollapsed = settings.nameColMode !== "full"

  const {
    scrollRef,
    periodLabel,
    handleScroll,
    scrollByWeek,
    scrollToToday,
    scrollToDate,
    printing,
    printGantt,
    activeBandStart,
    activeBandDays,
    activeDayW,
    activeTimelineWidth,
  } = usePlannerViewport({
    bandStart,
    bandDays,
    dayW,
    today,
    loading,
    windowDays: settings.windowDays,
  })

  const {
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
  } = usePlannerRows({
    assignments,
    resources,
    settings,
    patch,
    resSearch,
    sideSearch,
    myEmployeeId,
  })

  // ── Arrivo da una notifica a campanella (#148): /risorse?alloc=ID ───────
  // Si porta in vista la riga del dipendente e il periodo dell'allocazione, selezionandola;
  // se un filtro la nasconde (risorsa spenta, fuori lista, tipo nascosto, «solo mie», «solo
  // conflitti», ricerca) lo si allenta. Il parametro si toglie subito dall'URL (replace), così
  // un ricarico o il tasto indietro non ripetono il salto.
  const [searchParams, setSearchParams] = useSearchParams()
  const allocParam = searchParams.get("alloc")
  const deepLinkDone = React.useRef<string | null>(null)
  React.useEffect(() => {
    if (loading || !allocParam || deepLinkDone.current === allocParam) return
    deepLinkDone.current = allocParam
    setSearchParams(
      (prev) => {
        prev.delete("alloc")
        return prev
      },
      { replace: true }
    )
    const id = Number(allocParam)
    const a = assignments.find((x) => x.id === id)
    if (!a) {
      flashStatus("L'attività della notifica non è più nel planner")
      return
    }
    const p: Partial<PlannerUiSettings> = {}
    if (settings.ganttOffIds.includes(a.employeeId)) {
      p.ganttOffIds = settings.ganttOffIds.filter((x) => x !== a.employeeId)
    }
    if (settings.resourceFilterActive && !settings.selectedResourceIds.includes(a.employeeId)) {
      p.selectedResourceIds = [...settings.selectedResourceIds, a.employeeId]
    }
    if (!settings.tipiVisibili.includes(a.tipo)) {
      p.tipiVisibili = [...settings.tipiVisibili, a.tipo]
    }
    if (settings.mineOnly && a.employeeId !== myEmployeeId) p.mineOnly = false
    if (settings.conflictsOnly && !a.hasConflict) p.conflictsOnly = false
    if (Object.keys(p).length > 0) patch(p)
    if (resSearch) setResSearch("")
    setSelectedId(a.id)
    scrollToDate(parseDate(a.dataInizio))
    // La riga: dopo il render con i filtri allentati. Solo lo scroll verticale, quello
    // orizzontale è già sul periodo.
    window.setTimeout(() => {
      const scroller = scrollRef.current
      const row = scroller?.querySelector<HTMLElement>(`[data-emp-id="${a.employeeId}"]`)
      if (!scroller || !row) return
      const r = row.getBoundingClientRect()
      const s = scroller.getBoundingClientRect()
      scroller.scrollTop += r.top - s.top - s.height / 2 + r.height / 2
    }, 60)
  }, [
    loading,
    allocParam,
    assignments,
    settings,
    patch,
    resSearch,
    myEmployeeId,
    flashStatus,
    scrollToDate,
    scrollRef,
    setSearchParams,
  ])

  // ── Overlay calendario e sfondo track (banda attiva = reale o "stampa") ──
  const overlays = React.useMemo(
    () => dayOverlays(activeBandStart, activeBandDays, activeDayW, today),
    [activeBandStart, activeBandDays, activeDayW, today]
  )
  const trackBackground = React.useMemo(
    () => trackBackgroundCss(activeDayW),
    [activeDayW]
  )
  const months = React.useMemo(
    () => monthSpans(activeBandStart, activeBandDays, activeDayW),
    [activeBandStart, activeBandDays, activeDayW]
  )

  // ── Azioni sul dialogo allocazione ────────────────────────────
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

  const confirmConflict = React.useCallback(
    async (
      employeeId: number,
      start: Date,
      end: Date,
      tipo: string,
      excludeId: number
    ): Promise<boolean> => {
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
    },
    [assignments, confirm]
  )

  const { preview, beginMove, beginResize, beginCreate, suppressClickRef } =
    usePlannerDrag({
      canEdit,
      dayW,
      bandStart,
      bandDays,
      scrollRef,
      searchInputRef,
      connRef,
      assignments,
      createTipo,
      selectedId,
      setSelectedId,
      dialogOpen,
      confirmConflict,
      flashStatus,
      reload,
      refreshPendingNotify,
      onDeleteRequest: (a) => void removeAssignment(a),
    })

  return (
    <div className="planner-root flex h-[calc(100vh-7rem)] flex-col">
      {/* Intestazione di stampa standard con Logo Automation */}
      <div className="gantt-print-header">
        <img className="gantt-print-logo" src="/atec-logo.png" alt="Automation Technology" />
        <div className="gantt-print-title">Pianificazione Risorse</div>
        <div className="gantt-print-subtitle">{periodLabel}</div>
      </div>

      <SyncVpsWarning />

      <PlannerToolbar
        canEdit={canEdit}
        settings={settings}
        patch={patch}
        periodLabel={periodLabel}
        conflictCount={conflictCount}
        pendingNotify={pendingNotify}
        printing={printing}
        onCreate={openCreate}
        onOpenFerie={() => navigate("/risorse/ferie")}
        onNotify={() => setNotifyDialogOpen(true)}
        onScrollBack={() => scrollByWeek(-1)}
        onToday={scrollToToday}
        onScrollForward={() => scrollByWeek(1)}
        onToggleTipo={toggleTipo}
        onPrint={() => void printGantt()}
      />

      <PlannerFilters
        settings={settings}
        patch={patch}
        myEmployeeId={myEmployeeId}
        resources={resources}
        resourceFilterLabel={resourceFilterLabel}
        poolPopoverOpen={poolPopoverOpen}
        setPoolPopoverOpen={setPoolPopoverOpen}
        onToggleResourcePick={toggleResourcePick}
        onSetAllResourcePicks={setAllResourcePicks}
        resSearch={resSearch}
        setResSearch={setResSearch}
        searchInputRef={searchInputRef}
      />

      {/* Corpo: pannello risorse + Gantt */}
      <div className="planner-body mt-2">
        <PlannerSidePanel
          collapsed={settings.resSideCollapsed}
          onToggleCollapsed={() => patch({ resSideCollapsed: !settings.resSideCollapsed })}
          resources={sideResources}
          ganttOn={ganttOn}
          onToggleGanttOff={toggleGanttOff}
          ganttOnCount={ganttOnCount}
          poolCount={poolCount}
          myEmployeeId={myEmployeeId}
          search={sideSearch}
          setSearch={setSideSearch}
          onShowAll={showAll}
          onHideAll={hideAll}
        />

        <PlannerGantt
          scrollRef={scrollRef}
          onScroll={handleScroll}
          rows={rows}
          loading={loading}
          onlineIds={onlineIds}
          myEmployeeId={myEmployeeId}
          onToggleNameCol={() =>
            patch({ nameColMode: nameColCollapsed ? "full" : "badge" })
          }
          onToggleNameColCompact={() =>
            patch({
              nameColMode: settings.nameColMode === "surname" ? "badge" : "surname",
            })
          }
          geometry={{
            bandStart: activeBandStart,
            bandDays: activeBandDays,
            dayW: activeDayW,
            timelineWidth: activeTimelineWidth,
            today,
            months,
            overlays,
            trackBackground,
            nameW: nameWidthFor(settings.nameColMode),
            nameColCollapsed,
            nameColMode: settings.nameColMode,
          }}
          interaction={{
            canEdit,
            preview,
            createTipo,
            liveBandStart: bandStart,
            liveDayW: dayW,
            selectedId,
            setSelectedId,
            suppressClickRef,
            beginMove,
            beginResize,
            beginCreate,
            onEdit: openEdit,
            onDuplicate: openDuplicate,
            onDelete: (a) => void removeAssignment(a),
          }}
        />
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
        onSaved={(message) => {
          flashStatus(message)
          void reload()
          void refreshPendingNotify()
        }}
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
