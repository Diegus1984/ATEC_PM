import * as React from "react"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import { ColumnsMenu, type ColumnToggle } from "@/components/shared/columns-menu"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Eye, Printer, X } from "lucide-react"
import type { Milestone } from "@/lib/api/types"
import { printMilestoneGantt, printMilestoneTable } from "@/features/milestones/milestone-print"
import { notifyError } from "@/lib/toast"
import {
  addDays,
  dayCount,
  diffDays,
  dowLetter,
  isHoliday,
  isWeekend,
  mondayOf,
  monthName,
  parseDate,
  startOfDay,
  toIso,
} from "@/features/risorse/planner-logic"
import { isoWeek, msStatus, weekLabel, weekTot } from "@/features/milestones/milestone-utils"
import { isoToDate } from "@/lib/date-iso"
import { cn } from "@/lib/utils"
import "./milestones-gantt.css"

const LANE_HEIGHT = 38
const BAR_HEIGHT = 26
const ZOOM_STORAGE_KEY = "milestones:gantt:zoom"

function formatDateShort(iso: string | null): string {
  if (!iso) return ""
  const parts = iso.slice(0, 10).split("-")
  if (parts.length !== 3) return ""
  return `${parts[2]}/${parts[1]}/${parts[0]}`
}

const SIDEBAR_COLUMNS = [
  { id: "nr", label: "NR", width: 32 },
  { id: "descrizione", label: "Descrizione", width: 272 },
  { id: "wInizio", label: "W. Inizio", width: 48 },
  { id: "dataInizio", label: "Data Inizio", width: 80 },
  { id: "wFine", label: "W. Fine", width: 48 },
  { id: "dataFine", label: "Data Fine", width: 80 },
  { id: "wTot", label: "W. Tot", width: 40 },
  { id: "avanzamento", label: "Avanz.", width: 50 },
  { id: "note", label: "Note", width: 120 },
] as const

interface MilestoneGanttProps {
  projectId: number
  items: Milestone[]
  /** Codice commessa (opzionale): usato nella testata della stampa PDF. */
  projectCode?: string
  /** Descrizione commessa (opzionale): usata nella testata della stampa PDF. */
  projectTitle?: string
}

export function MilestoneGantt({
  projectId,
  items,
  projectCode,
  projectTitle,
}: MilestoneGanttProps) {
  const scrollRef = React.useRef<HTMLDivElement | null>(null)
  const didInitialScroll = React.useRef(false)

  // Colonne tabella laterale del Gantt persistite
  const [visibleCols, setVisibleCols] = React.useState<Record<string, boolean>>(() => {
    try {
      const saved = localStorage.getItem("milestones:gantt:columns")
      if (saved) {
        return JSON.parse(saved)
      }
    } catch {
      // ignore
    }
    return {
      nr: true,
      descrizione: true,
      wInizio: true,
      dataInizio: true,
      wFine: true,
      dataFine: true,
      wTot: true,
      avanzamento: true,
      note: true,
    }
  })

  const toggleCol = React.useCallback((colId: string, checked: boolean) => {
    setVisibleCols((prev) => {
      const next = { ...prev, [colId]: checked }
      try {
        localStorage.setItem("milestones:gantt:columns", JSON.stringify(next))
      } catch {
        // ignore
      }
      return next
    })
  }, [])

  const toggleAllCols = React.useCallback((checked: boolean) => {
    setVisibleCols(() => {
      const next = Object.fromEntries(
        SIDEBAR_COLUMNS.map((col) => [col.id, checked])
      ) as Record<string, boolean>
      try {
        localStorage.setItem("milestones:gantt:columns", JSON.stringify(next))
      } catch {
        // ignore
      }
      return next
    })
  }, [])

  const sidebarWidth = React.useMemo(() => {
    return SIDEBAR_COLUMNS.reduce((acc, col) => {
      return acc + (visibleCols[col.id] ? col.width : 0)
    }, 0)
  }, [visibleCols])

  const visibleColsList = React.useMemo(() => {
    return SIDEBAR_COLUMNS.filter((col) => visibleCols[col.id])
  }, [visibleCols])

  const lastVisibleColId = React.useMemo(() => {
    return visibleColsList[visibleColsList.length - 1]?.id
  }, [visibleColsList])

  const colToggles = React.useMemo(() => {
    return SIDEBAR_COLUMNS.map((col) => ({
      id: col.id,
      label: col.label,
      checked: !!visibleCols[col.id],
      onToggle: (checked: boolean) => toggleCol(col.id, checked),
    }))
  }, [visibleCols, toggleCol])

  // Zoom / larghezza giorno persistita
  const [dayW, setDayW] = React.useState<number>(() => {
    try {
      const stored = localStorage.getItem(ZOOM_STORAGE_KEY)
      if (stored) {
        const val = Number(stored)
        if ([20, 32, 46].includes(val)) return val
      }
    } catch {
      // ignore
    }
    return 32 // valore di default ("Normale")
  })

  const changeZoom = React.useCallback((value: string) => {
    const val = Number(value)
    setDayW(val)
    try {
      localStorage.setItem(ZOOM_STORAGE_KEY, value)
    } catch {
      // ignore
    }
  }, [])

  // Oggi a mezzanotte
  const today = React.useMemo(() => startOfDay(new Date()), [])

  // Spegnimenti manuali (Modalità Componi)
  const [hiddenRowIds, setHiddenRowIds] = React.useState<number[]>(() => {
    try {
      const stored = localStorage.getItem(`milestones:gantt:hidden:${projectId}`)
      if (stored) return JSON.parse(stored)
    } catch {
      // ignore
    }
    return []
  })

  // Sincronizza hiddenRowIds se cambia il progetto (necessario per visualizzazione globale)
  React.useEffect(() => {
    try {
      const stored = localStorage.getItem(`milestones:gantt:hidden:${projectId}`)
      if (stored) {
        setHiddenRowIds(JSON.parse(stored))
        return
      }
    } catch {
      // ignore
    }
    setHiddenRowIds([])
  }, [projectId])

  // Mostra/nasconde una singola riga dal Gantt (spegnimenti persistiti per progetto).
  const setRowVisible = React.useCallback(
    (id: number, visible: boolean) => {
      setHiddenRowIds((prev) => {
        const next = visible
          ? prev.filter((x) => x !== id)
          : prev.includes(id)
            ? prev
            : [...prev, id]
        try {
          localStorage.setItem(
            `milestones:gantt:hidden:${projectId}`,
            JSON.stringify(next)
          )
        } catch {
          // ignore
        }
        return next
      })
    },
    [projectId]
  )

  const showAllRows = React.useCallback(() => {
    setHiddenRowIds([])
    try {
      localStorage.removeItem(`milestones:gantt:hidden:${projectId}`)
    } catch {
      // ignore
    }
  }, [projectId])

  // Filtri Date (Dal / Al)
  const [filterFrom, setFilterFrom] = React.useState("")
  const [filterTo, setFilterTo] = React.useState("")

  // Filtra le sole milestone attive (non spente nel DB)
  const activeMilestones = React.useMemo(() => {
    return items.filter((m) => !m.spento)
  }, [items])

  const hideAllRows = React.useCallback(() => {
    const allIds = activeMilestones.map((m) => m.id)
    setHiddenRowIds(allIds)
    try {
      localStorage.setItem(
        `milestones:gantt:hidden:${projectId}`,
        JSON.stringify(allIds)
      )
    } catch {
      // ignore
    }
  }, [activeMilestones, projectId])

  const toggleAllRows = React.useCallback(
    (checked: boolean) => {
      if (checked) {
        showAllRows()
      } else {
        hideAllRows()
      }
    },
    [showAllRows, hideAllRows]
  )

  // Voci per la combo «Righe» (la stessa combo delle grid): tutte le righe attive, spuntate = visibili.
  const rowToggles = React.useMemo<ColumnToggle[]>(
    () =>
      activeMilestones.map((m, idx) => ({
        id: String(m.id),
        label: `${String(idx + 1).padStart(2, "0")} · ${
          m.descrizione || "(Nessuna descrizione)"
        }`,
        checked: !hiddenRowIds.includes(m.id),
        onToggle: (checked) => setRowVisible(m.id, checked),
      })),
    [activeMilestones, hiddenRowIds, setRowVisible]
  )

  // Filtra ulteriormente le milestone nascoste in modalità Componi
  const visibleMilestones = React.useMemo(() => {
    return activeMilestones.filter((m) => !hiddenRowIds.includes(m.id))
  }, [activeMilestones, hiddenRowIds])

  // Stampa / PDF del Gantt: rispetta colonne/righe visibili e filtri data attivi,
  // scalando la timeline per far rientrare tutto in una pagina A3 orizzontale.
  const handlePrintGantt = React.useCallback(() => {
    printMilestoneGantt({
      projectCode,
      projectTitle,
      milestones: visibleMilestones,
      visibleColumnIds: visibleColsList.map((c) => c.id),
      showTimeline: true,
      filterFrom: filterFrom || null,
      filterTo: filterTo || null,
      onError: (msg) => notifyError(msg, msg),
    })
  }, [
    projectCode,
    projectTitle,
    visibleMilestones,
    visibleColsList,
    filterFrom,
    filterTo,
  ])

  // Stampa / PDF della Tabella Milestone: rispetta le righe visibili,
  // formattando il tutto in un documento A4 orizzontale.
  const handlePrintTable = React.useCallback(() => {
    printMilestoneTable({
      projectCode,
      projectTitle,
      milestones: visibleMilestones,
      allMilestones: items,
      onError: (msg) => notifyError(msg, msg),
    })
  }, [
    projectCode,
    projectTitle,
    visibleMilestones,
    items,
  ])

  // Calcolo delle date minime e massime per l'intervallo temporale (band)
  const { bandStart, bandDays, hasDates } = React.useMemo(() => {
    let minDate: Date | null = null
    let maxDate: Date | null = null

    if (filterFrom) {
      minDate = parseDate(filterFrom)
    }
    if (filterTo) {
      maxDate = parseDate(filterTo)
    }

    if (!minDate || !maxDate) {
      const allDates: Date[] = []
      visibleMilestones.forEach((m) => {
        if (m.dataInizio) {
          allDates.push(parseDate(m.dataInizio))
        }
        if (m.dataFine) {
          allDates.push(parseDate(m.dataFine))
        }
      })

      if (allDates.length > 0) {
        if (!minDate) {
          minDate = new Date(Math.min(...allDates.map((d) => d.getTime())))
        }
        if (!maxDate) {
          maxDate = new Date(Math.max(...allDates.map((d) => d.getTime())))
        }
      }
    }

    if (!minDate && !maxDate) {
      return {
        bandStart: today,
        bandDays: 0,
        hasDates: false,
      }
    }

    if (!minDate) minDate = today
    if (!maxDate) maxDate = today

    // Includi sempre oggi nel range (se non escluso dai filtri)
    if (!filterFrom && today < minDate) {
      minDate = today
    }
    if (!filterTo && today > maxDate) {
      maxDate = today
    }

    // Margine di circa 1 settimana prima e dopo
    const startWithMargin = addDays(minDate, -7)
    const endWithMargin = addDays(maxDate, 7)

    const start = mondayOf(startWithMargin)
    const days = diffDays(endWithMargin, start) + 1

    return {
      bandStart: start,
      bandDays: days,
      hasDates: true,
    }
  }, [visibleMilestones, filterFrom, filterTo, today])

  const timelineWidth = bandDays * dayW

  // Suddivisione dei mesi per l'header
  const months = React.useMemo(() => {
    if (!hasDates) return []
    const out: { label: string; width: number }[] = []
    let i = 0
    while (i < bandDays) {
      const d = addDays(bandStart, i)
      const m = d.getMonth()
      let span = 0
      while (
        i + span < bandDays &&
        addDays(bandStart, i + span).getMonth() === m
      ) {
        span++
      }
      out.push({
        label: `${monthName(m + 1)} ${d.getFullYear()}`,
        width: span * dayW,
      })
      i += span
    }
    return out
  }, [bandStart, bandDays, dayW, hasDates])

  // Suddivisione delle settimane per l'header (con cap a 7 giorni per crossover anno)
  const weeks = React.useMemo(() => {
    if (!hasDates) return []
    const out: { label: string; width: number }[] = []
    let i = 0
    while (i < bandDays) {
      const d = addDays(bandStart, i)
      const w = isoWeek(d)
      let span = 0
      while (
        i + span < bandDays &&
        isoWeek(addDays(bandStart, i + span)) === w &&
        span < 7
      ) {
        span++
      }
      out.push({
        label: `W${String(w).padStart(2, "0")}`,
        width: span * dayW,
      })
      i += span
    }
    return out
  }, [bandStart, bandDays, dayW, hasDates])

  // Griglia di sfondo (linee verticali e weekend)
  const trackBackground = React.useMemo(() => {
    const w = dayW
    const weekend = `repeating-linear-gradient(90deg, transparent 0, transparent ${
      5 * w
    }px, var(--g-weekend) ${5 * w}px, var(--g-weekend) ${7 * w}px)`
    const lines = `repeating-linear-gradient(90deg, var(--g-line-soft) 0, var(--g-line-soft) 1px, transparent 1px, transparent ${w}px)`
    return `${lines}, ${weekend}`
  }, [dayW])

  // Overlay festività e linea oggi
  const overlays = React.useMemo(() => {
    if (!hasDates) return { holidays: [], todayLeft: -1 }
    const holidays: { left: number; width: number }[] = []
    for (let i = 0; i < bandDays; i++) {
      const d = addDays(bandStart, i)
      if (isHoliday(d)) {
        holidays.push({ left: i * dayW, width: dayW })
      }
    }
    const todayIdx = diffDays(today, bandStart)
    const todayLeft = todayIdx >= 0 && todayIdx < bandDays ? todayIdx * dayW : -1

    return { holidays, todayLeft }
  }, [bandStart, bandDays, dayW, today, hasDates])

  // Scroll iniziale per centrare su oggi (o allineare al lunedì corrente con margine di 1 settimana)
  React.useEffect(() => {
    if (!hasDates || didInitialScroll.current) return
    const el = scrollRef.current
    if (!el) return

    const todayIdx = diffDays(today, bandStart)
    // Centra posizionando oggi con un offset di circa una settimana
    el.scrollLeft = Math.max(0, (todayIdx - 7) * dayW)
    didInitialScroll.current = true
  }, [hasDates, today, bandStart, dayW])

  // Reset dello scroll iniziale se cambiano le date totali o il progetto (per ricalcolare una volta sola)
  React.useEffect(() => {
    didInitialScroll.current = false
  }, [projectId])

  if (!hasDates) {
    // Distingue il "vuoto" reale dal "vuoto" prodotto da spegnimenti/filtri: in questi due casi
    // la toolbar (con Ripristina/Azzera) non verrebbe renderizzata, quindi le azioni per uscire dal
    // blocco vanno offerte qui — altrimenti chi nasconde tutte le righe resterebbe incastrato.
    const hasHidden = hiddenRowIds.length > 0
    const hasFilter = !!(filterFrom || filterTo)
    return (
      <div className="m-empty flex flex-col items-center justify-center gap-3 py-16 text-muted-foreground">
        <div className="text-center">
          <p className="text-sm font-medium">
            {hasHidden
              ? "Tutte le righe sono nascoste"
              : hasFilter
                ? "Nessuna milestone nell'intervallo selezionato"
                : "Nessuna data pianificata"}
          </p>
          <p className="mt-1 text-xs">
            {hasHidden
              ? `Hai nascosto ${hiddenRowIds.length} riga/e dal Gantt.`
              : hasFilter
                ? "Modifica o azzera il filtro date per rivedere le milestone."
                : "Aggiungi le date di inizio e fine nelle milestone per visualizzarle nel Gantt."}
          </p>
        </div>
        {(hasHidden || hasFilter) && (
          <div className="flex items-center gap-2">
            {hasHidden && (
              <Button
                variant="outline"
                size="sm"
                className="h-8 gap-1.5 text-xs"
                onClick={showAllRows}
              >
                <Eye className="size-3.5" />
                Ripristina righe ({hiddenRowIds.length})
              </Button>
            )}
            {hasFilter && (
              <Button
                variant="ghost"
                size="sm"
                className="h-8 gap-1.5 text-xs"
                onClick={() => {
                  setFilterFrom("")
                  setFilterTo("")
                }}
              >
                <X className="size-3.5" />
                Azzera filtro date
              </Button>
            )}
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="m-planner-root">
      {/* Barra strumenti zoom, componi e filtri data del Gantt */}
      <div className="flex flex-wrap items-center justify-between border-b pb-2 mb-2 bg-background/95 backdrop-blur gap-2">
        <div className="flex flex-wrap items-center gap-6">
          {/* Zoom */}
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Zoom
            </span>
            <Select value={String(dayW)} onValueChange={changeZoom}>
              <SelectTrigger size="sm" className="h-8 w-[130px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="20">Compatto</SelectItem>
                <SelectItem value="32">Normale</SelectItem>
                <SelectItem value="46">Largo</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {/* Colonne visibili — combo per mostrare/nascondere le colonne del pannello sinistro */}
          <div className="flex items-center gap-1.5">
            <ColumnsMenu
              columns={colToggles}
              triggerLabel="Colonne"
              menuLabel="Mostra colonne tabella"
              align="start"
              className="w-[120px]"
              onToggleAll={toggleAllCols}
            />
          </div>

          {/* Righe visibili — combo standard delle grid («Colonne») */}
          <div className="flex items-center gap-1.5">
            <ColumnsMenu
              columns={rowToggles}
              triggerLabel="Righe"
              menuLabel="Mostra righe nel Gantt"
              align="start"
              className="w-[120px]"
              onToggleAll={toggleAllRows}
            />

            {hiddenRowIds.length > 0 && (
              <Button
                variant="outline"
                size="sm"
                className="h-8 text-xs border-dashed text-destructive hover:bg-destructive/10 gap-1"
                onClick={showAllRows}
              >
                Ripristina ({hiddenRowIds.length})
              </Button>
            )}
          </div>

          {/* Filtro Date */}
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <span>Dal</span>
            <DateField
              value={filterFrom || null}
              onChange={(v) => {
                const val = v ?? ""
                setFilterFrom(val)
                // Regola coppia date (come nelle altre pagine): se "Al" resta prima del nuovo "Dal", allinealo.
                if (val && filterTo) {
                  const from = isoToDate(val)
                  const to = isoToDate(filterTo)
                  if (from && to && to < from) setFilterTo(val)
                }
              }}
              size="sm"
              placeholder="Dal"
              className="h-8 w-[180px] shadow-none"
            />
            <span>Al</span>
            <DateField
              value={filterTo || null}
              onChange={(v) => setFilterTo(v ?? "")}
              size="sm"
              placeholder="Al"
              disabled={!filterFrom}
              disableBefore={isoToDate(filterFrom)}
              className="h-8 w-[180px] shadow-none"
            />
            {(filterFrom || filterTo) && (
              <Button
                variant="ghost"
                size="icon"
                className="h-8 w-8 text-muted-foreground hover:text-foreground"
                onClick={() => {
                  setFilterFrom("")
                  setFilterTo("")
                }}
              >
                <X className="size-4" />
              </Button>
            )}
          </div>
        </div>

        <div className="flex items-center gap-3">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="outline"
                size="sm"
                className="h-8 gap-1.5 text-xs"
                disabled={visibleMilestones.length === 0}
              >
                <Printer className="size-3.5" />
                Stampa
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={handlePrintTable}>
                Stampa Tabella (A4)
              </DropdownMenuItem>
              <DropdownMenuItem onClick={handlePrintGantt}>
                Stampa Gantt (A3)
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          <span className="text-xs text-muted-foreground">
            {visibleMilestones.length} milestone visualizzate
          </span>
        </div>
      </div>

      {/* Area di scrolling del Gantt */}
      <div className="m-gantt-scroll" ref={scrollRef}>
        <div
          className="m-gantt"
          style={{ "--m-name-w": `${sidebarWidth}px` } as React.CSSProperties}
        >
          {/* Testate delle colonne e della timeline */}
          <div className="m-head">
            <div className="m-corner">
              {visibleColsList.map((col) => {
                const isLast = col.id === lastVisibleColId
                return (
                  <div
                    key={col.id}
                    className={cn(
                      "shrink-0 flex items-center text-[9px] font-bold text-muted-foreground select-none text-center justify-center h-full",
                      !isLast && "border-r border-zinc-200",
                      col.id === "descrizione" && "justify-start pl-2",
                      col.id === "note" && "justify-start pl-2"
                    )}
                    style={{ width: col.width, minWidth: col.width }}
                  >
                    {col.id === "descrizione" ? "ATTIVITÀ / DESCRIZIONE" : col.label.toUpperCase()}
                  </div>
                )
              })}
            </div>
            <div className="m-thead">
              {/* Riga dei mesi */}
              <div className="m-months" style={{ width: timelineWidth }}>
                {months.map((m, idx) => (
                  <div
                    key={idx}
                    className="m-month"
                    style={{ width: m.width, minWidth: m.width }}
                  >
                    {m.label}
                  </div>
                ))}
              </div>
              {/* Riga delle settimane */}
              <div className="m-weeks" style={{ width: timelineWidth }}>
                {weeks.map((w, idx) => (
                  <div
                    key={idx}
                    className="m-week"
                    style={{ width: w.width, minWidth: w.width }}
                  >
                    {w.label}
                  </div>
                ))}
              </div>
              {/* Riga dei giorni */}
              <div className="m-days" style={{ width: timelineWidth }}>
                {Array.from({ length: bandDays }, (_, i) => {
                  const d = addDays(bandStart, i)
                  const we = isWeekend(d)
                  const ho = isHoliday(d)
                  const isToday = d.getTime() === today.getTime()
                  const cls = [
                    "m-day",
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
                      <span className="m-dow">{dowLetter(d)}</span>
                      <span className={`m-dnum ${we || ho ? "red" : ""}`}>
                        {d.getDate()}
                      </span>
                    </div>
                  )
                })}
              </div>
            </div>
          </div>

          {/* Righe delle Milestones */}
          {visibleMilestones.map((m, index) => {
            const status = msStatus(m)
            const wIn = weekLabel(m.dataInizio)
            const wFine = weekLabel(m.dataFine)

            // Calcolo del posizionamento della barra (se sono presenti entrambe le date)
            const hasBar = m.dataInizio && m.dataFine
            let barLeft = 0
            let barWidth = 0

            if (hasBar && m.dataInizio && m.dataFine) {
              const start = parseDate(m.dataInizio)
              const end = parseDate(m.dataFine)
              const startIdx = diffDays(start, bandStart)
              const span = dayCount(start, end)
              barLeft = startIdx * dayW
              barWidth = Math.max(dayW, span * dayW)
            }

            return (
              <div className="m-row" key={m.id}>
                {/* Pannello sinistro: Colonne attività */}
                <div
                  className={`m-name flex items-stretch p-0 ${
                    m.evidenza ? "bg-red-50/50 dark:bg-red-950/20" : ""
                  }`}
                  style={{ height: LANE_HEIGHT }}
                >
                  {visibleColsList.map((col) => {
                    const isLast = col.id === lastVisibleColId
                    const borderCls = !isLast ? "border-r border-zinc-100" : ""

                    switch (col.id) {
                      case "nr":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center text-xs text-muted-foreground font-mono", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {index + 1}
                          </div>
                        )
                      case "descrizione":
                        return (
                          <div
                            key={col.id}
                            className={cn(
                              "shrink-0 flex items-center pl-2 pr-1 text-xs truncate",
                              borderCls,
                              m.evidenza ? "font-semibold text-red-700 dark:text-red-400" : ""
                            )}
                            style={{ width: col.width, minWidth: col.width }}
                            title={m.descrizione}
                          >
                            <span className="truncate">{m.descrizione || "(Nessuna descrizione)"}</span>
                          </div>
                        )
                      case "wInizio":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {wIn ? (
                              <span className="px-1.5 py-0.5 rounded-sm bg-sky-100 dark:bg-sky-950 text-sky-800 dark:text-sky-300 text-[10px] font-bold font-mono">
                                {wIn}
                              </span>
                            ) : (
                              <span className="text-zinc-300">—</span>
                            )}
                          </div>
                        )
                      case "dataInizio":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center text-xs text-muted-foreground", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {formatDateShort(m.dataInizio) || "—"}
                          </div>
                        )
                      case "wFine":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {wFine ? (
                              <span className="px-1.5 py-0.5 rounded-sm bg-sky-100 dark:bg-sky-950 text-sky-800 dark:text-sky-300 text-[10px] font-bold font-mono">
                                {wFine}
                              </span>
                            ) : (
                              <span className="text-zinc-300">—</span>
                            )}
                          </div>
                        )
                      case "dataFine":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center text-xs text-muted-foreground", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {formatDateShort(m.dataFine) || "—"}
                          </div>
                        )
                      case "wTot":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center text-xs font-mono text-muted-foreground", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {weekTot(m.dataInizio, m.dataFine) || "—"}
                          </div>
                        )
                      case "avanzamento":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center justify-center text-xs font-mono text-muted-foreground", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                          >
                            {m.avanzamento !== null && m.avanzamento !== undefined ? `${m.avanzamento}%` : "—"}
                          </div>
                        )
                      case "note":
                        return (
                          <div
                            key={col.id}
                            className={cn("shrink-0 flex items-center pl-2 pr-1 text-xs text-muted-foreground truncate", borderCls)}
                            style={{ width: col.width, minWidth: col.width }}
                            title={m.note}
                          >
                            <span className="truncate">{m.note || ""}</span>
                          </div>
                        )
                      default:
                        return null
                    }
                  })}
                </div>

                {/* Pannello destro: Tracciato timeline */}
                <div
                  className="m-track"
                  style={{
                    width: timelineWidth,
                    height: LANE_HEIGHT,
                    background: trackBackground,
                  }}
                >
                  {/* Evidenziazione festivi */}
                  {overlays.holidays.map((h, i) => (
                    <div
                      key={`h${i}`}
                      className="m-overlay holiday"
                      style={{ left: h.left, width: h.width }}
                    />
                  ))}

                  {/* Linea oggi verticale */}
                  {overlays.todayLeft >= 0 && (
                    <div
                      className="m-today-line"
                      style={{ left: overlays.todayLeft }}
                    />
                  )}

                  {/* Renderizzazione della barra del Gantt per questa milestone */}
                  {hasBar && (
                    <div
                      className={`m-bar status-${status} ${
                        m.evidenza ? "m-bar-evidenza" : ""
                      }`}
                      style={{
                        left: barLeft,
                        width: barWidth,
                        top: (LANE_HEIGHT - BAR_HEIGHT) / 2,
                        height: BAR_HEIGHT,
                      }}
                      title={`${m.descrizione}\nPeriodo: ${
                        m.dataInizio ? toIso(parseDate(m.dataInizio)) : "—"
                      } → ${
                        m.dataFine ? toIso(parseDate(m.dataFine)) : "—"
                      }\nAvanzamento: ${m.avanzamento ?? 0}%`}
                    >
                      {/* Riempimento avanzamento */}
                      {m.avanzamento != null && m.avanzamento > 0 && (
                        <div
                           className="m-bar-fill"
                           style={{ width: `${m.avanzamento}%` }}
                        />
                      )}
                      <span className="m-bar-label">
                        {m.descrizione}{" "}
                        {m.avanzamento != null ? `(${m.avanzamento}%)` : ""}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
