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
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  Bookmark as BookmarkIcon,
  CalendarClock,
  Eye,
  FileSpreadsheet,
  Printer,
  Wand2,
  X,
} from "lucide-react"
import type { Milestone } from "@/lib/api/types"
import { printMilestoneGantt, printMilestoneTable } from "@/features/milestones/milestone-print"
import { exportGanttViewExcel } from "@/features/milestones/milestone-excel"
import {
  deleteGanttView,
  fetchGanttViews,
  saveGanttView,
  type MilestoneGanttView,
} from "@/lib/api/milestones"
import { notifyError, notifySuccess } from "@/lib/toast"
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

// Composizione della vista: colonne spente, righe spente e intervallo date sono
// PER COMMESSA. Prima le colonne usavano una chiave globale, quindi spegnere una
// colonna su una commessa la spegneva su tutte; l'intervallo non era persistito affatto
// e si perdeva a ogni refresh.
const colsKey = (projectId: number) => `milestones:gantt:columns:${projectId}`
const hiddenKey = (projectId: number) => `milestones:gantt:hidden:${projectId}`
const rangeKey = (projectId: number) => `milestones:gantt:range:${projectId}`
const timelineKey = (projectId: number) => `milestones:gantt:timeline:${projectId}`

/** Nomi delle due viste salvabili, come nel prototipo. */
const VIEW_NAMES = ["Vista Interna", "Vista Cliente"] as const

/**
 * Composizione completa di un Gantt. È ciò che una «vista salvata» conserva:
 * quali colonne sono accese, quali righe sono spente, l'intervallo di date e se
 * il diagramma è visibile. Lo stesso oggetto viaggia verso il server come payload
 * opaco, così aggiungere una voce qui non richiede una migrazione.
 */
interface GanttComposition {
  cols: Record<string, boolean>
  hiddenRows: number[]
  from: string
  to: string
  timeline: boolean
}

const ALL_COLS_ON: Record<string, boolean> = {
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

function readJson<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    if (raw) return JSON.parse(raw) as T
  } catch {
    // ignore
  }
  return fallback
}

function writeJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // ignore
  }
}

/** Domenica della settimana di `d`: chiude la finestra a settimane intere. */
function sundayOf(d: Date): Date {
  return addDays(mondayOf(d), 6)
}

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
  /**
   * Funzione concessa in sola lettura. Il Gantt resta consultabile per intero (zoom,
   * colonne, righe, filtro date, Componi e stampa/export vivono in localStorage o in
   * un file, non sul server): l'unica scrittura è il salvataggio/eliminazione delle
   * viste, e sono quelle a sparire. Le barre non sono trascinabili in nessun profilo,
   * quindi non c'è drag da disattivare.
   */
  readOnly?: boolean
}

export function MilestoneGantt({
  projectId,
  items,
  projectCode,
  projectTitle,
  readOnly = false,
}: MilestoneGanttProps) {
  const scrollRef = React.useRef<HTMLDivElement | null>(null)
  const didInitialScroll = React.useRef(false)

  // Seconda barra orizzontale, agganciata SOTTO la testata (#72): quella in fondo resta,
  // ma per arrivarci bisogna scorrere tutte le righe. È una barra finta, larga quanto la
  // parte visibile e con dentro un distanziatore largo quanto il contenuto, tenuta in
  // sincrono con lo scroller vero nei due sensi.
  const headRef = React.useRef<HTMLDivElement | null>(null)
  const hbarRef = React.useRef<HTMLDivElement | null>(null)
  const [hbar, setHbar] = React.useState({ top: 0, viewport: 0, content: 0 })

  React.useEffect(() => {
    const scroller = scrollRef.current
    const head = headRef.current
    if (!scroller || !head) return

    let frame = 0
    const measure = () => {
      frame = 0
      setHbar((prev) => {
        const next = {
          top: head.offsetHeight,
          viewport: scroller.clientWidth,
          content: scroller.scrollWidth,
        }
        return prev.top === next.top &&
          prev.viewport === next.viewport &&
          prev.content === next.content
          ? prev
          : next
      })
    }
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(measure)
    }

    // Sullo scroller per la larghezza visibile, sulla testata perché è larga quanto il
    // contenuto: cambiando periodo o zoom la timeline si allarga senza toccare lo scroller.
    const resize = new ResizeObserver(schedule)
    resize.observe(scroller)
    resize.observe(head)
    const mutations = new MutationObserver(schedule)
    mutations.observe(scroller, { childList: true, subtree: true })

    measure()
    return () => {
      if (frame) cancelAnimationFrame(frame)
      resize.disconnect()
      mutations.disconnect()
    }
  }, [])

  const syncScroll = (from: HTMLDivElement | null, to: HTMLDivElement | null) => {
    if (!from || !to || to.scrollLeft === from.scrollLeft) return
    to.scrollLeft = from.scrollLeft
  }

  // Colonne tabella laterale del Gantt, persistite PER COMMESSA
  const [visibleCols, setVisibleCols] = React.useState<Record<string, boolean>>(() =>
    readJson(colsKey(projectId), ALL_COLS_ON)
  )

  const toggleCol = React.useCallback(
    (colId: string, checked: boolean) => {
      setVisibleCols((prev) => {
        const next = { ...prev, [colId]: checked }
        writeJson(colsKey(projectId), next)
        return next
      })
    },
    [projectId]
  )

  const toggleAllCols = React.useCallback(
    (checked: boolean) => {
      setVisibleCols(() => {
        const next = Object.fromEntries(
          SIDEBAR_COLUMNS.map((col) => [col.id, checked])
        ) as Record<string, boolean>
        writeJson(colsKey(projectId), next)
        return next
      })
      // «Tutte / Nessuna» comprende anche il diagramma, che nella combo è una voce come le altre.
      setShowTimeline(checked)
      writeJson(timelineKey(projectId), checked)
    },
    [projectId]
  )

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
  const [hiddenRowIds, setHiddenRowIds] = React.useState<number[]>(() =>
    readJson<number[]>(hiddenKey(projectId), [])
  )

  // Filtri Date (Dal / Al), persistiti per commessa
  const [filterFrom, setFilterFrom] = React.useState(
    () => readJson(rangeKey(projectId), { from: "", to: "" }).from
  )
  const [filterTo, setFilterTo] = React.useState(
    () => readJson(rangeKey(projectId), { from: "", to: "" }).to
  )

  // Diagramma visibile: spegnendolo resta la sola tabella e le colonne si riproporzionano
  // per occupare tutta la larghezza. È ciò che rende utile la «Vista Cliente».
  const [showTimeline, setShowTimeline] = React.useState(() =>
    readJson(timelineKey(projectId), true)
  )

  // Modalità Componi: mette una ⊗ sulle intestazioni e sulle righe, così si spegne
  // quello che non deve andare al cliente cliccandolo sul diagramma invece di cercarlo
  // nei due menu a tendina.
  const [composeMode, setComposeMode] = React.useState(false)

  // Cambio commessa: ricarica l'intera composizione salvata per quella commessa.
  React.useEffect(() => {
    setVisibleCols(readJson(colsKey(projectId), ALL_COLS_ON))
    setHiddenRowIds(readJson<number[]>(hiddenKey(projectId), []))
    const range = readJson(rangeKey(projectId), { from: "", to: "" })
    setFilterFrom(range.from)
    setFilterTo(range.to)
    setShowTimeline(readJson(timelineKey(projectId), true))
    setComposeMode(false)
  }, [projectId])

  const toggleTimeline = React.useCallback(
    (visible: boolean) => {
      setShowTimeline(visible)
      writeJson(timelineKey(projectId), visible)
    },
    [projectId]
  )

  // Voci della combo «Colonne»: le colonne del pannello più il diagramma, che si
  // spegne come una colonna qualsiasi (senza, resta la sola tabella).
  const colToggles = React.useMemo(() => {
    return [
      ...SIDEBAR_COLUMNS.map((col) => ({
        id: col.id,
        label: col.label,
        checked: !!visibleCols[col.id],
        onToggle: (checked: boolean) => toggleCol(col.id, checked),
      })),
      {
        id: "__timeline",
        label: "Diagramma",
        checked: showTimeline,
        onToggle: (checked: boolean) => toggleTimeline(checked),
      },
    ]
  }, [visibleCols, toggleCol, showTimeline, toggleTimeline])

  const applyRange = React.useCallback(
    (from: string, to: string) => {
      setFilterFrom(from)
      setFilterTo(to)
      writeJson(rangeKey(projectId), { from, to })
    },
    [projectId]
  )

  // Mostra/nasconde una singola riga dal Gantt (spegnimenti persistiti per progetto).
  const setRowVisible = React.useCallback(
    (id: number, visible: boolean) => {
      setHiddenRowIds((prev) => {
        const next = visible
          ? prev.filter((x) => x !== id)
          : prev.includes(id)
            ? prev
            : [...prev, id]
        writeJson(hiddenKey(projectId), next)
        return next
      })
    },
    [projectId]
  )

  const showAllRows = React.useCallback(() => {
    setHiddenRowIds([])
    try {
      localStorage.removeItem(hiddenKey(projectId))
    } catch {
      // ignore
    }
  }, [projectId])

  // Filtra le sole milestone attive (non spente nel DB)
  const activeMilestones = React.useMemo(() => {
    return items.filter((m) => !m.spento)
  }, [items])

  const hideAllRows = React.useCallback(() => {
    const allIds = activeMilestones.map((m) => m.id)
    setHiddenRowIds(allIds)
    writeJson(hiddenKey(projectId), allIds)
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

  // Numero di riga = posizione nell'elenco COMPLETO, cioè la colonna «#» della griglia.
  // Vale per la colonna NR del pannello, per la combo «Righe» e per la stampa: prima
  // erano tre numerazioni diverse e la stessa milestone si chiamava «03» qui e «05» là.
  const rowNumbers = React.useMemo(
    () => new Map(items.map((m, i) => [m.id, i + 1])),
    [items]
  )

  // Voci per la combo «Righe» (la stessa combo delle grid): tutte le righe attive, spuntate = visibili.
  const rowToggles = React.useMemo<ColumnToggle[]>(() => {
    return activeMilestones.map((m) => ({
      id: String(m.id),
      label: `${String(rowNumbers.get(m.id) ?? 0).padStart(2, "0")} · ${
        m.descrizione || "(Nessuna descrizione)"
      }`,
      checked: !hiddenRowIds.includes(m.id),
      onToggle: (checked) => setRowVisible(m.id, checked),
    }))
  }, [rowNumbers, activeMilestones, hiddenRowIds, setRowVisible])

  // Filtra ulteriormente le milestone nascoste in modalità Componi
  const visibleMilestones = React.useMemo(() => {
    return activeMilestones.filter((m) => !hiddenRowIds.includes(m.id))
  }, [activeMilestones, hiddenRowIds])

  // Periodo reale della commessa (min/max sulle righe attive): precompila il filtro date.
  const projectPeriod = React.useMemo(() => {
    const dates: string[] = []
    activeMilestones.forEach((m) => {
      if (m.dataInizio) dates.push(m.dataInizio.slice(0, 10))
      if (m.dataFine) dates.push(m.dataFine.slice(0, 10))
    })
    if (!dates.length) return null
    dates.sort()
    return { from: dates[0], to: dates[dates.length - 1] }
  }, [activeMilestones])

  const hiddenColsCount = React.useMemo(
    () => SIDEBAR_COLUMNS.filter((col) => !visibleCols[col.id]).length,
    [visibleCols]
  )
  const hasRangeFilter = !!(filterFrom || filterTo)

  // Quante cose sono spente in tutto: colonne + righe + il filtro date (che conta 1)
  // + il diagramma spento. Prima il pulsante contava solo le righe, quindi una vista
  // «composta» con colonne spente o un intervallo attivo sembrava una vista normale.
  const composedCount =
    hiddenColsCount +
    hiddenRowIds.length +
    (hasRangeFilter ? 1 : 0) +
    (showTimeline ? 0 : 1)

  /** Reset unico: rimette colonne, righe, diagramma e azzera l'intervallo. */
  const resetComposition = React.useCallback(() => {
    setVisibleCols(ALL_COLS_ON)
    writeJson(colsKey(projectId), ALL_COLS_ON)
    showAllRows()
    applyRange("", "")
    toggleTimeline(true)
    setComposeMode(false)
  }, [projectId, showAllRows, applyRange, toggleTimeline])

  // ── Viste salvate ──────────────────────────────────────────────────────────
  const composition = React.useMemo<GanttComposition>(
    () => ({
      cols: visibleCols,
      hiddenRows: hiddenRowIds,
      from: filterFrom,
      to: filterTo,
      timeline: showTimeline,
    }),
    [visibleCols, hiddenRowIds, filterFrom, filterTo, showTimeline]
  )

  const [views, setViews] = React.useState<Record<string, MilestoneGanttView>>({})

  const reloadViews = React.useCallback(async () => {
    try {
      const list = await fetchGanttViews(projectId)
      setViews(Object.fromEntries(list.map((v) => [v.name, v])))
    } catch {
      // Le viste sono un di più: se il caricamento fallisce il Gantt resta usabile.
      setViews({})
    }
  }, [projectId])

  React.useEffect(() => {
    void reloadViews()
  }, [reloadViews])

  const saveView = React.useCallback(
    async (name: string) => {
      // Unica scrittura del Gantt (con `removeView`): guardia in testa alla callback,
      // come nelle altre pagine in sola lettura — la voce di menu è già nascosta.
      if (readOnly) return
      try {
        await saveGanttView(projectId, name, JSON.stringify(composition))
        await reloadViews()
        notifySuccess(`«${name}» salvata`)
      } catch (err) {
        notifyError(err as Error)
      }
    },
    [projectId, composition, reloadViews, readOnly]
  )

  /**
   * Composizione di una vista salvata, con i valori mancanti riportati ai default.
   * Restituisce null (e avvisa) se il payload non è leggibile.
   */
  const readView = React.useCallback(
    (name: string): GanttComposition | null => {
      const view = views[name]
      if (!view) return null
      let parsed: Partial<GanttComposition>
      try {
        parsed = JSON.parse(view.payload) as Partial<GanttComposition>
      } catch {
        notifyError(null, `«${name}» non è leggibile: risalvala.`)
        return null
      }
      return {
        cols: { ...ALL_COLS_ON, ...(parsed.cols ?? {}) },
        hiddenRows: Array.isArray(parsed.hiddenRows) ? parsed.hiddenRows : [],
        from: parsed.from ?? "",
        to: parsed.to ?? "",
        timeline: parsed.timeline !== false,
      }
    },
    [views]
  )

  const applyView = React.useCallback(
    (name: string) => {
      const parsed = readView(name)
      if (!parsed) return
      const cols = parsed.cols
      const rows = parsed.hiddenRows
      const timeline = parsed.timeline

      setVisibleCols(cols)
      writeJson(colsKey(projectId), cols)
      setHiddenRowIds(rows)
      writeJson(hiddenKey(projectId), rows)
      applyRange(parsed.from, parsed.to)
      toggleTimeline(timeline)
      setComposeMode(false)
      notifySuccess(`«${name}» applicata`)
    },
    [readView, projectId, applyRange, toggleTimeline]
  )

  const removeView = React.useCallback(
    async (name: string) => {
      if (readOnly) return
      try {
        await deleteGanttView(projectId, name)
        await reloadViews()
        notifySuccess(`«${name}» eliminata`)
      } catch (err) {
        notifyError(err as Error)
      }
    },
    [projectId, reloadViews, readOnly]
  )

  // Stampa / PDF del Gantt: rispetta colonne/righe visibili e filtri data attivi,
  // scalando la timeline per far rientrare tutto in una pagina A3 orizzontale.
  const handlePrintGantt = React.useCallback(() => {
    printMilestoneGantt({
      projectCode,
      projectTitle,
      milestones: visibleMilestones,
      allMilestones: items,
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
    items,
    visibleColsList,
    filterFrom,
    filterTo,
  ])

  // Export Excel della composizione attuale: una colonna per giorno, barre come celle
  // colorate, bande mesi/settimane e blocco riquadri sul pannello.
  const handleExportExcel = React.useCallback(() => {
    exportGanttViewExcel({
      projectCode,
      projectTitle,
      viewName: "Composizione attuale",
      milestones: visibleMilestones,
      rowNumbers,
      panelColumns: visibleColsList.map((c) => ({ id: c.id, label: c.label })),
      showTimeline,
      filterFrom: filterFrom || null,
      filterTo: filterTo || null,
      onError: (msg) => notifyError(msg, msg),
    })
  }, [
    projectCode,
    projectTitle,
    visibleMilestones,
    rowNumbers,
    visibleColsList,
    showTimeline,
    filterFrom,
    filterTo,
  ])

  /**
   * Export Excel di una vista SALVATA senza applicarla: si esporta il Gantt che si
   * consegna al cliente restando su quello che si sta guardando. La composizione
   * corrente non viene toccata.
   */
  const exportSavedView = React.useCallback(
    (name: string) => {
      const view = readView(name)
      if (!view) return
      exportGanttViewExcel({
        projectCode,
        projectTitle,
        viewName: name,
        milestones: activeMilestones.filter((m) => !view.hiddenRows.includes(m.id)),
        rowNumbers,
        panelColumns: SIDEBAR_COLUMNS.filter((col) => view.cols[col.id]).map((c) => ({
          id: c.id,
          label: c.label,
        })),
        showTimeline: view.timeline,
        filterFrom: view.from || null,
        filterTo: view.to || null,
        onError: (msg) => notifyError(msg, msg),
      })
    },
    [readView, projectCode, projectTitle, activeMilestones, rowNumbers]
  )

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

    // Il margine di una settimana serve solo quando l'intervallo è dedotto dalle attività
    // (per non far toccare le barre ai bordi). Se «Dal»/«Al» li ha scelti l'utente, la
    // finestra va rispettata: si parte dal lunedì di «Dal» e si finisce alla domenica di
    // «Al» — settimane intere, come la stampa e l'Excel.
    const start = filterFrom
      ? mondayOf(minDate)
      : mondayOf(addDays(minDate, -7))
    const end = filterTo ? sundayOf(maxDate) : addDays(maxDate, 7)
    const days = Math.max(1, diffDays(end, start) + 1)

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

  /** Riporta la timeline su oggi (pulsante «Oggi», come nel planner Risorse e in Ferie). */
  const scrollToToday = React.useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    if (overlays.todayLeft < 0) {
      notifyError(
        null,
        "Oggi è fuori dal periodo visualizzato: azzera il filtro date per rivederlo."
      )
      return
    }
    el.scrollTo({
      left: Math.max(0, overlays.todayLeft - sidebarWidth / 2),
      behavior: "smooth",
    })
  }, [overlays.todayLeft, sidebarWidth])

  // Con il diagramma spento la tabella si vede comunque: l'assenza di date non è più
  // un motivo per bloccare la pagina, perché non c'è nessuna timeline da disegnare.
  if (!hasDates && showTimeline) {
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
                onClick={() => applyRange("", "")}
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

            {/* Un solo contatore per TUTTA la composizione (colonne + righe + intervallo)
                e un solo pulsante per rimetterla a posto. */}
            {composedCount > 0 && (
              <Button
                variant="outline"
                size="sm"
                className="h-8 gap-1 border-dashed text-xs text-destructive hover:bg-destructive/10"
                title={[
                  hiddenColsCount > 0 ? `${hiddenColsCount} colonne spente` : "",
                  hiddenRowIds.length > 0 ? `${hiddenRowIds.length} righe spente` : "",
                  hasRangeFilter ? "intervallo date attivo" : "",
                ]
                  .filter(Boolean)
                  .join(" · ")}
                onClick={resetComposition}
              >
                <Eye className="size-3.5" />
                Mostra tutto ({composedCount})
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
                // Regola coppia date (come nelle altre pagine): se "Al" resta prima del nuovo "Dal", allinealo.
                let to = filterTo
                if (val && to) {
                  const from = isoToDate(val)
                  const toDate = isoToDate(to)
                  if (from && toDate && toDate < from) to = val
                }
                applyRange(val, to)
              }}
              size="sm"
              segmented
              placeholder="Dal"
              className="h-8 w-[190px] shadow-none"
            />
            <span>Al</span>
            <DateField
              value={filterTo || null}
              onChange={(v) => applyRange(filterFrom, v ?? "")}
              size="sm"
              segmented
              placeholder="Al"
              disabled={!filterFrom}
              disableBefore={isoToDate(filterFrom)}
              className="h-8 w-[190px] shadow-none"
            />
            {projectPeriod && (
              <Button
                variant="ghost"
                size="sm"
                className="h-8 text-xs"
                title="Precompila l'intervallo con il periodo reale della commessa"
                onClick={() => applyRange(projectPeriod.from, projectPeriod.to)}
              >
                Periodo commessa
              </Button>
            )}
            {hasRangeFilter && (
              <Button
                variant="ghost"
                size="icon"
                className="h-8 w-8 text-muted-foreground hover:text-foreground"
                title="Azzera l'intervallo date"
                onClick={() => applyRange("", "")}
              >
                <X className="size-4" />
              </Button>
            )}
          </div>
        </div>

        <div className="flex items-center gap-3">
          {/* Componi: spegne colonne e righe cliccandole sul diagramma. */}
          <Button
            variant={composeMode ? "default" : "outline"}
            size="sm"
            className="h-8 gap-1.5 text-xs"
            title="Spegni colonne e righe cliccando la ⊗ sul diagramma"
            onClick={() => setComposeMode((v) => !v)}
          >
            <Wand2 className="size-3.5" />
            Componi
          </Button>

          {/* Viste salvate: la composizione con cui si consegna il Gantt. */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" className="h-8 gap-1.5 text-xs">
                <BookmarkIcon className="size-3.5" />
                Viste
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-64">
              {VIEW_NAMES.map((name) => {
                const saved = views[name]
                return (
                  <React.Fragment key={name}>
                    <DropdownMenuLabel className="flex items-baseline justify-between gap-2 font-normal">
                      <span className="font-medium">{name}</span>
                      <span className="text-[11px] text-muted-foreground">
                        {/* Senza testo esplicito il «—» si legge come «questa vista non
                            esiste»: qui invece manca solo il primo salvataggio. */}
                        {saved
                          ? `salvata ${formatDateShort(saved.updatedAt)}`
                          : "non ancora salvata"}
                      </span>
                    </DropdownMenuLabel>
                    <DropdownMenuItem
                      disabled={!saved}
                      onClick={() => applyView(name)}
                    >
                      Applica
                    </DropdownMenuItem>
                    {/* Salvare una vista scrive sul server (`/gantt-views`): in sola
                        lettura resta solo applicarla o esportarla. */}
                    {!readOnly && (
                      <DropdownMenuItem onClick={() => void saveView(name)}>
                        {saved ? "Sovrascrivi con la composizione attuale" : "Salva la composizione attuale"}
                      </DropdownMenuItem>
                    )}
                    <DropdownMenuItem
                      disabled={!saved}
                      onClick={() => exportSavedView(name)}
                    >
                      <FileSpreadsheet className="size-3.5" />
                      Esporta in Excel (.xls)
                    </DropdownMenuItem>
                    {saved && !readOnly && (
                      <DropdownMenuItem
                        variant="destructive"
                        onClick={() => void removeView(name)}
                      >
                        Elimina
                      </DropdownMenuItem>
                    )}
                    <DropdownMenuSeparator />
                  </React.Fragment>
                )
              })}
              <DropdownMenuItem
                disabled={visibleMilestones.length === 0}
                onClick={handleExportExcel}
              >
                <FileSpreadsheet className="size-3.5" />
                Esporta Excel della composizione
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          <Button
            variant="outline"
            size="sm"
            className="h-8 gap-1.5 text-xs"
            title="Riporta la timeline su oggi"
            disabled={!showTimeline}
            onClick={scrollToToday}
          >
            <CalendarClock className="size-3.5" />
            Oggi
          </Button>
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
              <DropdownMenuItem onClick={handlePrintGantt} disabled={!showTimeline}>
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
      <div
        className="m-gantt-scroll"
        ref={scrollRef}
        onScroll={() => syncScroll(scrollRef.current, hbarRef.current)}
      >
        <div
          className="m-gantt"
          style={
            {
              // Timeline spenta: il pannello prende tutta la larghezza e le colonne si
              // riproporzionano (flex-grow sulla larghezza nominale) invece di lasciare
              // mezza pagina vuota.
              "--m-name-w": showTimeline ? `${sidebarWidth}px` : "100%",
            } as React.CSSProperties
          }
        >
          {/* Testate delle colonne e della timeline */}
          <div className="m-head" ref={headRef}>
            <div className="m-corner">
              {visibleColsList.map((col) => {
                const isLast = col.id === lastVisibleColId
                return (
                  <div
                    key={col.id}
                    className={cn(
                      "flex items-center text-[9px] font-bold text-muted-foreground select-none text-center justify-center h-full",
                      showTimeline ? "shrink-0" : "min-w-0",
                      !isLast && "border-r border-zinc-200",
                      col.id === "descrizione" && "justify-start pl-2",
                      col.id === "note" && "justify-start pl-2",
                      composeMode && "relative"
                    )}
                    style={
                      showTimeline
                        ? { width: col.width, minWidth: col.width }
                        : { flex: `${col.width} 1 0`, minWidth: 32 }
                    }
                  >
                    <span className="truncate">
                      {col.id === "descrizione" ? "ATTIVITÀ / DESCRIZIONE" : col.label.toUpperCase()}
                    </span>
                    {composeMode && (
                      <button
                        type="button"
                        aria-label={`Spegni la colonna ${col.label}`}
                        title={`Spegni la colonna ${col.label}`}
                        className="absolute right-0.5 top-0.5 inline-flex size-4 items-center justify-center rounded-full bg-background/90 text-destructive shadow-sm hover:bg-destructive hover:text-destructive-foreground"
                        onClick={() => toggleCol(col.id, false)}
                      >
                        <X className="size-3" />
                      </button>
                    )}
                  </div>
                )
              })}
            </div>
            {showTimeline && (
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
            )}
          </div>

          {/* Barra orizzontale sotto la riga dei titoli (#72). Sparisce se non c'è
              niente da scorrere: sarebbe una striscia grigia e basta. */}
          {hbar.content > hbar.viewport + 1 ? (
            <div
              ref={hbarRef}
              aria-hidden
              className="m-hbar"
              style={{ top: hbar.top, width: hbar.viewport }}
              onScroll={() => syncScroll(hbarRef.current, scrollRef.current)}
            >
              <div className="m-hbar-space" style={{ width: hbar.content }} />
            </div>
          ) : null}

          {/* Righe delle Milestones */}
          {visibleMilestones.map((m) => {
            const status = msStatus(m)
            const wIn = weekLabel(m.dataInizio)
            const wFine = weekLabel(m.dataFine)

            // Posizionamento della barra, TAGLIATO ai bordi dell'intervallo visualizzato.
            // Prima si usava `startIdx * dayW` grezzo: con il filtro Dal/Al attivo una
            // milestone iniziata prima produceva un `left` negativo (barra fuori posto)
            // invece di una barra tagliata al bordo. È il calcolo già usato in stampa
            // (`milestone-print.ts`), qui portato a video.
            let barLeft = 0
            let barWidth = 0
            let hasBar = false
            let clippedStart = false
            let clippedEnd = false

            if (m.dataInizio && m.dataFine) {
              const start = parseDate(m.dataInizio)
              const end = parseDate(m.dataFine)
              const offset = diffDays(start, bandStart)
              const span = dayCount(start, end)
              // Fuori banda del tutto: nessuna barra.
              if (span > 0 && offset + span > 0 && offset < bandDays) {
                hasBar = true
                clippedStart = offset < 0
                clippedEnd = offset + span > bandDays
                const from = Math.max(0, offset)
                const to = Math.min(bandDays, offset + span)
                barLeft = from * dayW
                barWidth = Math.max(dayW * 0.8, (to - from) * dayW)
              }
            }

            return (
              <div className="m-row" key={m.id}>
                {/* Pannello sinistro: Colonne attività */}
                <div
                  className={cn(
                    "m-name flex items-stretch p-0",
                    m.evidenza && "bg-red-50/50 dark:bg-red-950/20",
                    composeMode && "relative"
                  )}
                  style={{ height: LANE_HEIGHT }}
                >
                  {composeMode && (
                    <button
                      type="button"
                      aria-label="Spegni questa riga"
                      title="Spegni questa riga dal Gantt"
                      className="absolute -left-0.5 top-1/2 z-10 inline-flex size-4 -translate-y-1/2 items-center justify-center rounded-full bg-background text-destructive shadow-sm hover:bg-destructive hover:text-destructive-foreground"
                      onClick={() => setRowVisible(m.id, false)}
                    >
                      <X className="size-3" />
                    </button>
                  )}
                  {visibleColsList.map((col) => {
                    const isLast = col.id === lastVisibleColId
                    const borderCls = !isLast ? "border-r border-zinc-100" : ""
                    // Timeline spenta: le colonne crescono in proporzione invece di
                    // restare fisse lasciando il resto della riga vuoto.
                    const sizeStyle = showTimeline
                      ? { width: col.width, minWidth: col.width }
                      : { flex: `${col.width} 1 0`, minWidth: 32 }
                    const sizeCls = showTimeline ? "shrink-0" : "min-w-0"

                    switch (col.id) {
                      case "nr":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center justify-center text-xs text-muted-foreground font-mono", borderCls)}
                            style={sizeStyle}
                          >
                            {rowNumbers.get(m.id) ?? 0}
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
                            style={sizeStyle}
                            title={m.descrizione}
                          >
                            <span className="truncate">{m.descrizione || "(Nessuna descrizione)"}</span>
                          </div>
                        )
                      case "wInizio":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center justify-center", borderCls)}
                            style={sizeStyle}
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
                            className={cn(sizeCls, "flex items-center justify-center text-xs text-muted-foreground", borderCls)}
                            style={sizeStyle}
                          >
                            {formatDateShort(m.dataInizio) || "—"}
                          </div>
                        )
                      case "wFine":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center justify-center", borderCls)}
                            style={sizeStyle}
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
                            className={cn(sizeCls, "flex items-center justify-center text-xs text-muted-foreground", borderCls)}
                            style={sizeStyle}
                          >
                            {formatDateShort(m.dataFine) || "—"}
                          </div>
                        )
                      case "wTot":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center justify-center text-xs font-mono text-muted-foreground", borderCls)}
                            style={sizeStyle}
                          >
                            {weekTot(m.dataInizio, m.dataFine) || "—"}
                          </div>
                        )
                      case "avanzamento":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center justify-center text-xs font-mono text-muted-foreground", borderCls)}
                            style={sizeStyle}
                          >
                            {m.avanzamento !== null && m.avanzamento !== undefined ? `${m.avanzamento}%` : "—"}
                          </div>
                        )
                      case "note":
                        return (
                          <div
                            key={col.id}
                            className={cn(sizeCls, "flex items-center pl-2 pr-1 text-xs text-muted-foreground truncate", borderCls)}
                            style={sizeStyle}
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
                {showTimeline && (
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
                      className={cn(
                        `m-bar status-${status}`,
                        m.evidenza && "m-bar-evidenza",
                        // Bordo squadrato dal lato tagliato: si vede che la barra continua
                        // fuori dall'intervallo invece di sembrare che finisca lì.
                        clippedStart && "rounded-l-none",
                        clippedEnd && "rounded-r-none"
                      )}
                      style={{
                        left: barLeft,
                        width: barWidth,
                        top: (LANE_HEIGHT - BAR_HEIGHT) / 2,
                        height: BAR_HEIGHT,
                      }}
                      // Niente percentuale nel tooltip: sulla barra non deve comparire
                      // nessun riferimento all'avanzamento (resta la colonna «Avanz.»).
                      title={`${m.descrizione}\nPeriodo: ${
                        m.dataInizio ? toIso(parseDate(m.dataInizio)) : "—"
                      } → ${m.dataFine ? toIso(parseDate(m.dataFine)) : "—"}`}
                    />
                  )}
                </div>
                )}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
