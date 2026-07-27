// ── Gantt: intestazione calendario, righe risorsa e barre allocazione ──────

import * as React from "react"

import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import type { ResAssignmentDto, ResTipo } from "@/lib/api/types"

import {
  BAR_HEIGHT,
  LANE_HEIGHT,
  type RowData,
} from "./planner-geometry"
import {
  addDays,
  assignmentLabel,
  buildTooltip,
  diffDays,
  dowLetter,
  initials,
  isHoliday,
  isWeekend,
  parseDate,
  startOfDay,
  surname,
  tipoCssClass,
} from "./planner-logic"
import type { DragPreview } from "./use-planner-drag"
import type { NameColMode } from "./use-planner-settings"

/** Geometria attiva: durante la stampa contiene la finestra «fotografata». */
export interface GanttGeometry {
  bandStart: Date
  bandDays: number
  dayW: number
  timelineWidth: number
  today: Date
  months: { label: string; width: number }[]
  overlays: { holidays: { left: number; width: number }[]; todayLeft: number }
  trackBackground: string
  nameW: number
  nameColCollapsed: boolean
  nameColMode: NameColMode
}

export interface GanttInteraction {
  canEdit: boolean
  preview: DragPreview | null
  createTipo: ResTipo
  /** Banda REALE (non quella di stampa): l'anteprima del drag vive lì. */
  liveBandStart: Date
  liveDayW: number
  selectedId: number | null
  setSelectedId: (id: number | null) => void
  suppressClickRef: React.RefObject<boolean>
  beginMove: (e: React.PointerEvent, a: ResAssignmentDto) => void
  beginResize: (e: React.PointerEvent, a: ResAssignmentDto, end: boolean) => void
  beginCreate: (e: React.PointerEvent, employeeId: number) => void
  onEdit: (a: ResAssignmentDto) => void
  onDuplicate: (a: ResAssignmentDto) => void
  onDelete: (a: ResAssignmentDto) => void
}

function PlannerBar({
  a,
  lane,
  geometry,
  interaction,
}: {
  a: ResAssignmentDto
  lane: number
  geometry: GanttGeometry
  interaction: GanttInteraction
}) {
  const { preview, canEdit, selectedId } = interaction
  const dragging = preview != null && preview.assignmentId === a.id
  const s = dragging ? preview.start : parseDate(a.dataInizio)
  const e = dragging ? preview.end : parseDate(a.dataFine)
  const startIdx = diffDays(s, geometry.bandStart)
  const span = diffDays(e, s) + 1
  const left = startIdx * geometry.dayW
  const width = Math.max(geometry.dayW, span * geometry.dayW)
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
      onPointerDown={(ev) => interaction.beginMove(ev, a)}
      onClick={(ev) => {
        ev.stopPropagation()
        if (interaction.suppressClickRef.current) {
          interaction.suppressClickRef.current = false
          return
        }
        interaction.setSelectedId(a.id)
      }}
      onDoubleClick={(ev) => {
        ev.stopPropagation()
        if (canEdit) interaction.onEdit(a)
      }}
    >
      {canEdit && (
        <span
          className="grip grip-l"
          onPointerDown={(ev) => interaction.beginResize(ev, a, false)}
        />
      )}
      <span className="g2-bar-label">
        {a.hasConflict ? "⚠ " : ""}
        {assignmentLabel(a)}
      </span>
      {canEdit && (
        <span
          className="grip grip-r"
          onPointerDown={(ev) => interaction.beginResize(ev, a, true)}
        />
      )}
    </div>
  )

  if (!canEdit) return bar

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{bar}</ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onSelect={() => interaction.onEdit(a)}>
          Modifica
        </ContextMenuItem>
        <ContextMenuItem onSelect={() => interaction.onDuplicate(a)}>
          Duplica (+7 gg)
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem
          className="text-destructive"
          onSelect={() => interaction.onDelete(a)}
        >
          Elimina
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  )
}

export function PlannerGantt({
  scrollRef,
  onScroll,
  rows,
  loading,
  onlineIds,
  myEmployeeId,
  geometry,
  interaction,
  onToggleNameCol,
  onToggleNameColCompact,
}: {
  scrollRef: React.RefObject<HTMLDivElement | null>
  onScroll: () => void
  rows: RowData[]
  loading: boolean
  onlineIds: ReadonlySet<number>
  myEmployeeId: number
  geometry: GanttGeometry
  interaction: GanttInteraction
  onToggleNameCol: () => void
  onToggleNameColCompact: () => void
}) {
  const { bandStart, bandDays, dayW, timelineWidth, today, nameColCollapsed } =
    geometry

  return (
    <div className="gantt-scroll" ref={scrollRef} onScroll={onScroll}>
      <div
        className={`gantt2 ${nameColCollapsed ? "name-col-collapsed" : ""}`}
        style={
          {
            "--g2-name-w": `${geometry.nameW}px`,
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
              onClick={onToggleNameCol}
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
                  geometry.nameColMode === "surname"
                    ? "Mostra badge con iniziali"
                    : "Mostra solo cognome"
                }
                onClick={onToggleNameColCompact}
              >
                {geometry.nameColMode === "surname" ? "Ab" : "Co"}
              </button>
            ) : (
              <span className="g2-corner-label">Risorsa</span>
            )}
          </div>
          <div className="g2-thead">
            <div className="g2-months" style={{ width: timelineWidth }}>
              {geometry.months.map((m, idx) => (
                <div
                  key={idx}
                  className="g2-month"
                  style={{ width: m.width, minWidth: m.width }}
                >
                  {m.label}
                </div>
              ))}
            </div>
            <div className="g2-days" style={{ width: timelineWidth }}>
              {Array.from({ length: bandDays }, (_, i) => {
                const d = addDays(bandStart, i)
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
                    style={{ width: dayW, minWidth: dayW }}
                  >
                    <span className="g2-dow">{dowLetter(d)}</span>
                    <span className={`g2-dnum ${we || ho ? "red" : ""}`}>
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
            const { preview } = interaction
            return (
              <div className="g2-row" key={row.resource.id}>
                <div className={`g2-name ${nameRowClass}`} style={{ height: rowH }}>
                  {nameColCollapsed ? (
                    geometry.nameColMode === "surname" ? (
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
                    width: timelineWidth,
                    height: rowH,
                    background: geometry.trackBackground,
                  }}
                  onPointerDown={(ev) => interaction.beginCreate(ev, row.resource.id)}
                  onClick={() => interaction.setSelectedId(null)}
                >
                  {/* Overlay festività + oggi */}
                  {geometry.overlays.holidays.map((h, i) => (
                    <div
                      key={`h${i}`}
                      className="g2-overlay holiday"
                      style={{ left: h.left, width: h.width }}
                    />
                  ))}
                  {geometry.overlays.todayLeft >= 0 && (
                    <div
                      className="g2-overlay today"
                      style={{ left: geometry.overlays.todayLeft, width: dayW }}
                    />
                  )}
                  {row.bars.map((b) => (
                    <PlannerBar
                      key={b.a.id}
                      a={b.a}
                      lane={b.lane}
                      geometry={geometry}
                      interaction={interaction}
                    />
                  ))}
                  {/* Anteprima create-by-drag su questa riga */}
                  {preview != null &&
                    preview.assignmentId === -1 &&
                    preview.employeeId === row.resource.id && (
                      <div
                        className={`g2-bar preview ${tipoCssClass(interaction.createTipo)}`}
                        style={{
                          left:
                            diffDays(preview.start, interaction.liveBandStart) *
                            interaction.liveDayW,
                          width: Math.max(
                            interaction.liveDayW,
                            (diffDays(preview.end, preview.start) + 1) *
                              interaction.liveDayW
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
  )
}
