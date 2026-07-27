import * as React from "react"
import { useNavigate } from "react-router-dom"

import "./risorse-gantt.css"

import { ApiError } from "@/lib/api/client"
import { deleteAssignment as apiDeleteAssignment } from "@/lib/api/resource-planner"
import type { ResAssignmentDto, ResTipo } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { useConfirm } from "@/components/shared/confirm"
import { AssignmentDialog } from "@/features/risorse/AssignmentDialog"
import { NotifyNowDialog } from "@/features/risorse/NotifyNowDialog"

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
import { usePlannerSettings } from "./use-planner-settings"
import { usePlannerViewport } from "./use-planner-viewport"

export function ResourcePlannerPage() {
  const confirm = useConfirm()
  const navigate = useNavigate()
  const myEmployeeId = getSession()?.user.employeeId ?? 0
  const canEdit = canAccessFeature("resources.edit")

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
