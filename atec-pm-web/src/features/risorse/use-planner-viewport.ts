// ── Viewport del Gantt: scroll orizzontale, etichetta periodo, stampa A4 ────

import * as React from "react"

import { NAME_COL_WIDTH_EXPANDED } from "./planner-geometry"
import { addDays, diffDays, monthName } from "./planner-logic"

export function usePlannerViewport({
  bandStart,
  bandDays,
  dayW,
  today,
  loading,
  windowDays,
}: {
  bandStart: Date
  bandDays: number
  dayW: number
  today: Date
  loading: boolean
  /** Giorni della finestra di zoom: è anche l'ampiezza della pagina stampata. */
  windowDays: number
}) {
  const scrollRef = React.useRef<HTMLDivElement | null>(null)
  const [periodLabel, setPeriodLabel] = React.useState("")
  const didInitialScroll = React.useRef(false)

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

  // L'etichetta si aggiorna al più una volta per frame: lo scroll spara a raffica.
  const rafRef = React.useRef<number | null>(null)
  const handleScroll = React.useCallback(() => {
    if (rafRef.current != null) return
    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      updatePeriodLabel()
    })
  }, [updatePeriodLabel])

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

  const scrollByWeek = React.useCallback(
    (weeks: number) => {
      const el = scrollRef.current
      if (el) el.scrollLeft += weeks * 7 * dayW
    },
    [dayW]
  )
  const scrollToToday = React.useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const idx = diffDays(today, bandStart)
    el.scrollLeft = Math.max(0, (idx - 2) * dayW)
  }, [today, bandStart, dayW])

  // ── Stampa: finestra "fotografata" per adattare il Gantt a una pagina A4 ──
  // Durante la stampa il rendering usa questi valori al posto di bandStart/bandDays/dayW
  // (solo per il layout grafico: la logica di business/drag resta sui valori reali).
  const [printOverride, setPrintOverride] = React.useState<{
    start: Date
    days: number
    dayW: number
  } | null>(null)
  const [printing, setPrinting] = React.useState(false)

  const printGantt = React.useCallback(async () => {
    if (printing) return
    const el = scrollRef.current
    if (!el) return
    setPrinting(true)
    try {
      const firstIdx = Math.max(0, Math.floor(el.scrollLeft / dayW))
      const printStart = addDays(bandStart, firstIdx)
      const printDays = windowDays
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
  }, [printing, dayW, bandStart, windowDays])

  // Banda "attiva": quella di stampa se presente, altrimenti quella reale.
  const activeBandStart = printOverride?.start ?? bandStart
  const activeBandDays = printOverride?.days ?? bandDays
  const activeDayW = printOverride?.dayW ?? dayW

  return {
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
    activeTimelineWidth: activeBandDays * activeDayW,
  }
}
