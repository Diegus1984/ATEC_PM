import * as React from "react"
import { useNavigate } from "react-router-dom"
import { ArrowLeft } from "lucide-react"

import "./risorse-gantt.css"

import { ApiError } from "@/lib/api/client"
import {
  fetchAssignments,
  fetchResourceLookups,
} from "@/lib/api/resource-planner"
import type { LookupItem, ResAssignmentDto } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { useResourcePlannerHub } from "@/lib/signalr/use-resource-planner-hub"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { FerieEditDialog } from "@/features/risorse/FerieEditDialog"
import {
  addDays,
  buildTooltip,
  computeFeriePeak,
  diffDays,
  dowLetter,
  isHoliday,
  isWeekend,
  mondayOf,
  monthName,
  parseDate,
  startOfDay,
  surname,
  toIso,
  workingDayCount,
} from "@/features/risorse/planner-logic"
import { dateToIso } from "@/lib/date-iso"

const LANE_HEIGHT = 26
const BAR_HEIGHT = 18

interface FBar {
  a: ResAssignmentDto
  left: number
  width: number
  lane: number
}
interface FRow {
  resource: LookupItem
  bars: FBar[]
  lanes: number
  workingDays: number
}

function ferieLabel(a: ResAssignmentDto): string {
  return a.descrizione && a.descrizione.trim() ? a.descrizione : "Ferie"
}

function csvCell(v: string): string {
  if (v.includes(";") || v.includes('"') || v.includes("\n")) {
    return `"${v.replace(/"/g, '""')}"`
  }
  return v
}

function downloadCsv(filename: string, text: string) {
  const blob = new Blob(["﻿" + text], { type: "text/csv;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

function fmtDate(iso: string): string {
  const d = parseDate(iso)
  const dd = String(d.getDate()).padStart(2, "0")
  const mm = String(d.getMonth() + 1).padStart(2, "0")
  return `${dd}/${mm}/${d.getFullYear()}`
}

export function FeriePage() {
  const navigate = useNavigate()
  // `canWriteFeature` e non `canAccessFeature`: con la funzione concessa in sola lettura
  // le ferie si leggono ma non si assegnano né si cancellano.
  const canEdit = canWriteFeature("resources.edit")

  const [resources, setResources] = React.useState<LookupItem[]>([])
  const [ferie, setFerie] = React.useState<ResAssignmentDto[]>([])
  const [selected, setSelected] = React.useState<Set<number>>(new Set())
  const [dayW, setDayW] = React.useState(32)
  const [filter, setFilter] = React.useState<"all" | "ferie" | "sel">("all")
  const [loading, setLoading] = React.useState(true)
  const [status, setStatus] = React.useState<string | null>(null)
  const [periodLabel, setPeriodLabel] = React.useState("")

  // Dialogo
  const [dialogOpen, setDialogOpen] = React.useState(false)
  const [dialogExisting, setDialogExisting] =
    React.useState<ResAssignmentDto | null>(null)
  const [dialogEmp, setDialogEmp] = React.useState<{ id: number; name: string }>(
    { id: 0, name: "" }
  )

  const connRef = React.useRef<string | null>(null)
  const scrollRef = React.useRef<HTMLDivElement | null>(null)
  const didInitialScroll = React.useRef(false)

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
  const timelineWidth = bandDays * dayW

  function flashStatus(msg: string) {
    setStatus(msg)
    window.setTimeout(() => setStatus(null), 2500)
  }

  const reloadFerie = React.useCallback(async () => {
    const all = await fetchAssignments()
    setFerie(all.filter((a) => a.tipo === "FERIE"))
  }, [])

  React.useEffect(() => {
    let alive = true
    void (async () => {
      setLoading(true)
      try {
        const [res, all] = await Promise.all([
          fetchResourceLookups(),
          fetchAssignments(),
        ])
        if (!alive) return
        setResources(res)
        setFerie(all.filter((a) => a.tipo === "FERIE"))
      } catch (e) {
        if (alive)
          flashStatus(e instanceof ApiError ? e.message : "Errore di caricamento")
      } finally {
        if (alive) setLoading(false)
      }
    })()
    return () => {
      alive = false
    }
  }, [])

  const onRealtime = React.useCallback(() => {
    void reloadFerie()
  }, [reloadFerie])
  useResourcePlannerHub(onRealtime, connRef)

  // ── KPI ───────────────────────────────────────────────────────
  const kpis = React.useMemo(() => {
    const distinct = new Set(ferie.map((a) => a.employeeId)).size
    const totalDays = ferie.reduce(
      (s, a) =>
        s + workingDayCount(parseDate(a.dataInizio), parseDate(a.dataFine)),
      0
    )
    const peak = computeFeriePeak(ferie)
    return { distinct, totalDays, peak }
  }, [ferie])

  // ── Righe ─────────────────────────────────────────────────────
  const rows: FRow[] = React.useMemo(() => {
    const map = new Map<number, LookupItem>()
    for (const r of resources) map.set(r.id, r)
    for (const a of ferie) {
      if (!map.has(a.employeeId))
        map.set(a.employeeId, { id: a.employeeId, name: a.employeeName })
    }
    let people = [...map.values()]
    const withFerie = new Set(ferie.map((a) => a.employeeId))
    if (filter === "ferie") people = people.filter((r) => withFerie.has(r.id))
    else if (filter === "sel") people = people.filter((r) => selected.has(r.id))
    people.sort(
      (a, b) =>
        surname(a.name).localeCompare(surname(b.name)) ||
        a.name.localeCompare(b.name)
    )

    const winEnd = addDays(bandStart, bandDays - 1)
    const result: FRow[] = []
    for (const res of people) {
      const items = ferie
        .filter((a) => {
          if (a.employeeId !== res.id) return false
          const s = parseDate(a.dataInizio)
          const e = parseDate(a.dataFine)
          return e >= bandStart && s <= winEnd
        })
        .sort(
          (x, y) =>
            parseDate(x.dataInizio).getTime() - parseDate(y.dataInizio).getTime()
        )

      const laneEnds: Date[] = []
      const laneOf = new Map<number, number>()
      for (const a of items) {
        const s = parseDate(a.dataInizio)
        const e = parseDate(a.dataFine)
        let lane = laneEnds.findIndex((end) => end < s)
        if (lane === -1) {
          lane = laneEnds.length
          laneEnds.push(e)
        } else {
          laneEnds[lane] = e
        }
        laneOf.set(a.id, lane)
      }

      const bars: FBar[] = items.map((a) => {
        const s = parseDate(a.dataInizio)
        const e = parseDate(a.dataFine)
        const startIdx = Math.max(0, diffDays(s, bandStart))
        const span = diffDays(e, s) + 1
        return {
          a,
          left: startIdx * dayW,
          width: Math.max(dayW, span * dayW),
          lane: laneOf.get(a.id) ?? 0,
        }
      })

      const workingDays = ferie
        .filter((a) => a.employeeId === res.id)
        .reduce(
          (s, a) =>
            s + workingDayCount(parseDate(a.dataInizio), parseDate(a.dataFine)),
          0
        )

      result.push({
        resource: res,
        bars,
        lanes: Math.max(1, laneEnds.length),
        workingDays,
      })
    }
    return result
  }, [resources, ferie, filter, selected, dayW, bandStart, bandDays])

  // ── Header mesi/giorni ────────────────────────────────────────
  const months = React.useMemo(() => {
    const out: { label: string; width: number }[] = []
    let i = 0
    while (i < bandDays) {
      const d = addDays(bandStart, i)
      const m = d.getMonth()
      let span = 0
      while (i + span < bandDays && addDays(bandStart, i + span).getMonth() === m)
        span++
      out.push({ label: `${monthName(m + 1)} ${d.getFullYear()}`, width: span * dayW })
      i += span
    }
    return out
  }, [bandStart, bandDays, dayW])

  const trackBackground = React.useMemo(() => {
    const w = dayW
    const weekend = `repeating-linear-gradient(90deg, transparent 0, transparent ${
      5 * w
    }px, var(--g-weekend) ${5 * w}px, var(--g-weekend) ${7 * w}px)`
    const lines = `repeating-linear-gradient(90deg, var(--g-line-soft) 0, var(--g-line-soft) 1px, transparent 1px, transparent ${w}px)`
    return `${lines}, ${weekend}`
  }, [dayW])

  const overlays = React.useMemo(() => {
    const holidays: { left: number; width: number }[] = []
    for (let i = 0; i < bandDays; i++) {
      const d = addDays(bandStart, i)
      if (isHoliday(d)) holidays.push({ left: i * dayW, width: dayW })
    }
    const todayIdx = diffDays(today, bandStart)
    return {
      holidays,
      todayLeft: todayIdx >= 0 && todayIdx < bandDays ? todayIdx * dayW : -1,
    }
  }, [bandStart, bandDays, dayW, today])

  // ── Scroll: periodo + scroll iniziale ─────────────────────────
  const updatePeriodLabel = React.useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const firstIdx = Math.floor(el.scrollLeft / dayW)
    const lastIdx = Math.floor((el.scrollLeft + el.clientWidth) / dayW)
    const a = addDays(bandStart, Math.max(0, firstIdx))
    const b = addDays(bandStart, Math.min(bandDays - 1, lastIdx))
    if (a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth())
      setPeriodLabel(`${monthName(a.getMonth() + 1)} ${a.getFullYear()}`)
    else if (a.getFullYear() === b.getFullYear())
      setPeriodLabel(
        `${monthName(a.getMonth() + 1)} – ${monthName(b.getMonth() + 1)} ${a.getFullYear()}`
      )
    else
      setPeriodLabel(
        `${monthName(a.getMonth() + 1)} ${a.getFullYear()} – ${monthName(b.getMonth() + 1)} ${b.getFullYear()}`
      )
  }, [bandStart, bandDays, dayW])

  const rafRef = React.useRef<number | null>(null)
  function handleScroll() {
    if (rafRef.current != null) return
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      updatePeriodLabel()
    })
  }
  function goToday() {
    const el = scrollRef.current
    if (!el) return
    const idx = diffDays(mondayOf(today), bandStart)
    el.scrollLeft = Math.max(0, (idx - 7) * dayW)
  }
  React.useEffect(() => {
    if (loading || didInitialScroll.current) return
    const el = scrollRef.current
    if (!el) return
    const idx = diffDays(mondayOf(today), bandStart)
    el.scrollLeft = Math.max(0, (idx - 7) * dayW)
    didInitialScroll.current = true
    updatePeriodLabel()
  }, [loading, today, bandStart, dayW, updatePeriodLabel])
  React.useEffect(() => {
    updatePeriodLabel()
  }, [dayW, updatePeriodLabel])

  // ── Selezione ─────────────────────────────────────────────────
  function toggleSelected(id: number) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }
  function setAllSelected(on: boolean) {
    if (!on) {
      setSelected(new Set())
      return
    }
    setSelected(new Set(rows.map((r) => r.resource.id)))
  }

  // ── Dialog ────────────────────────────────────────────────────
  function addFerie(emp: LookupItem) {
    setDialogExisting(null)
    setDialogEmp({ id: emp.id, name: emp.name })
    setDialogOpen(true)
  }
  function editFerie(a: ResAssignmentDto) {
    if (!canEdit) return
    setDialogExisting(a)
    setDialogEmp({ id: a.employeeId, name: a.employeeName })
    setDialogOpen(true)
  }
  function onSaved(message: string) {
    flashStatus(message)
    void reloadFerie()
  }

  // ── Export CSV ────────────────────────────────────────────────
  async function exportCsv() {
    let list = ferie
    if (selected.size > 0) {
      list = ferie.filter((a) => selected.has(a.employeeId))
    }
    const sorted = [...list].sort(
      (a, b) =>
        surname(a.employeeName).localeCompare(surname(b.employeeName)) ||
        a.employeeName.localeCompare(b.employeeName) ||
        parseDate(a.dataInizio).getTime() - parseDate(b.dataInizio).getTime()
    )
    const lines = ["Risorsa;Inizio;Fine;Giorni;Descrizione"]
    for (const a of sorted) {
      const giorni = workingDayCount(parseDate(a.dataInizio), parseDate(a.dataFine))
      lines.push(
        [
          csvCell(a.employeeName),
          fmtDate(a.dataInizio),
          fmtDate(a.dataFine),
          String(giorni),
          csvCell(a.descrizione ?? ""),
        ].join(";")
      )
    }
    downloadCsv(
      `piano_ferie_${dateToIso(new Date()).replace(/-/g, "")}.csv`,
      lines.join("\r\n")
    )
  }

  return (
    <div className="planner-root flex h-[calc(100vh-7rem)] flex-col">
      {/* Header */}
      <div className="mb-2 flex items-center gap-2">
        <Button
          size="sm"
          variant="outline"
          onClick={() => navigate("/risorse")}
        >
          <ArrowLeft className="mr-1 h-4 w-4" /> Pianificazione
        </Button>
        <h2 className="text-lg font-semibold">Piano ferie</h2>
      </div>

      {/* KPI */}
      <div className="ferie-kpis">
        <div className="kpi-card">
          <div className="kpi-label">Colleghi con ferie</div>
          <div className="kpi-value">
            {kpis.distinct}
            <span className="kpi-sub"> / {resources.length}</span>
          </div>
        </div>
        <div className="kpi-card">
          <div className="kpi-label">Giorni di ferie totali</div>
          <div className="kpi-value">{kpis.totalDays}</div>
        </div>
        <div className="kpi-card">
          <div className="kpi-label">Picco contemporaneo</div>
          <div className="kpi-value">
            {kpis.peak.peak}
            {kpis.peak.date && (
              <span className="kpi-sub">
                {" "}
                il{" "}
                {String(kpis.peak.date.getDate()).padStart(2, "0")}/
                {String(kpis.peak.date.getMonth() + 1).padStart(2, "0")}/
                {kpis.peak.date.getFullYear()}
              </span>
            )}
          </div>
        </div>
      </div>

      {/* Toolbar */}
      <div className="mt-2 flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-card px-3 py-2">
        <div className="flex items-center gap-2">
          <Button size="sm" variant="outline" onClick={goToday}>
            Oggi
          </Button>
          <span className="px-1 text-sm font-semibold">{periodLabel}</span>
          <Select value={String(dayW)} onValueChange={(v) => setDayW(Number(v))}>
            <SelectTrigger className="h-8 w-[130px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="20">Compatto</SelectItem>
              <SelectItem value="32">Normale</SelectItem>
              <SelectItem value="46">Largo</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Select
            value={filter}
            onValueChange={(v) => setFilter(v as "all" | "ferie" | "sel")}
          >
            <SelectTrigger className="h-8 w-[150px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tutti i nomi</SelectItem>
              <SelectItem value="ferie">Solo con ferie</SelectItem>
              <SelectItem value="sel">Solo selezionati</SelectItem>
            </SelectContent>
          </Select>
          <Button size="sm" variant="outline" onClick={() => setAllSelected(true)}>
            Sel. tutti
          </Button>
          <Button size="sm" variant="outline" onClick={() => setAllSelected(false)}>
            Sel. nessuno
          </Button>
          <Button size="sm" variant="outline" onClick={() => void exportCsv()}>
            Esporta CSV
          </Button>
        </div>
      </div>

      {/* Gantt solo ferie */}
      <div className="gantt-scroll mt-2" ref={scrollRef} onScroll={handleScroll}>
        <div
          className="gantt2"
          style={{ "--g2-name-w": "240px" } as React.CSSProperties}
        >
          <div className="g2-head">
            <div className="g2-corner">
              <span className="g2-corner-label">Risorsa</span>
            </div>
            <div className="g2-thead">
              <div className="g2-months" style={{ width: timelineWidth }}>
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

          {rows.length === 0 ? (
            <div className="g2-empty">
              {loading ? "Caricamento…" : "Nessuna risorsa da mostrare."}
            </div>
          ) : (
            rows.map((row) => {
              const rowH = row.lanes * LANE_HEIGHT
              return (
                <div className="g2-row" key={row.resource.id}>
                  <div className="g2-name" style={{ height: rowH }}>
                    <Checkbox
                      className="ferie-sel"
                      checked={selected.has(row.resource.id)}
                      onCheckedChange={() => toggleSelected(row.resource.id)}
                    />
                    <span className="g2-name-text">{row.resource.name}</span>
                    <span className="ferie-days">{row.workingDays} gg</span>
                    {canEdit && (
                      <button
                        className="ferie-add"
                        title="Aggiungi ferie"
                        onClick={() => addFerie(row.resource)}
                      >
                        +
                      </button>
                    )}
                  </div>
                  <div
                    className="g2-track"
                    style={{
                      width: timelineWidth,
                      height: rowH,
                      background: trackBackground,
                    }}
                  >
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
                        style={{ left: overlays.todayLeft, width: dayW }}
                      />
                    )}
                    {row.bars.map((b) => {
                      const s = parseDate(b.a.dataInizio)
                      const e = parseDate(b.a.dataFine)
                      return (
                        <div
                          key={b.a.id}
                          className={`g2-bar tipo-ferie ${
                            b.a.hasConflict ? "conflict" : ""
                          }`}
                          style={{
                            left: b.left,
                            width: b.width,
                            top: b.lane * LANE_HEIGHT + (LANE_HEIGHT - BAR_HEIGHT) / 2,
                            height: BAR_HEIGHT,
                            cursor: canEdit ? "pointer" : "default",
                          }}
                          title={buildTooltip(b.a, s, e)}
                          onClick={() => editFerie(b.a)}
                        >
                          <span className="g2-bar-label">{ferieLabel(b.a)}</span>
                        </div>
                      )
                    })}
                  </div>
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* Status bar */}
      <div className="planner-status mt-2">
        <span>
          {rows.length} risorse · {ferie.length} periodi di ferie ·{" "}
          {selected.size} selezionati
        </span>
        <span className="status-right">
          {status && <span className="font-medium text-foreground">{status}</span>}
        </span>
      </div>

      <FerieEditDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        existing={dialogExisting}
        employeeId={dialogEmp.id}
        employeeName={dialogEmp.name}
        presetStart={
          today >= bandStart && today <= addDays(bandStart, bandDays - 1)
            ? toIso(today)
            : toIso(bandStart)
        }
        connRef={connRef}
        onSaved={onSaved}
      />
    </div>
  )
}
