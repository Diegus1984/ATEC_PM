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
  /** Vero mentre si stampa: lo scroll non è più quello dell'utente, l'etichetta è congelata. */
  const printingRef = React.useRef(false)
  /** Scroll da rimettere quando si torna dalla banda di stampa a quella reale. */
  const restoreScrollRef = React.useRef<number | null>(null)

  const labelForRange = React.useCallback((a: Date, b: Date) => {
    if (a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth()) {
      return `${monthName(a.getMonth() + 1)} ${a.getFullYear()}`
    }
    if (a.getFullYear() === b.getFullYear()) {
      return `${monthName(a.getMonth() + 1)} – ${monthName(b.getMonth() + 1)} ${a.getFullYear()}`
    }
    return `${monthName(a.getMonth() + 1)} ${a.getFullYear()} – ${monthName(
      b.getMonth() + 1
    )} ${b.getFullYear()}`
  }, [])

  const updatePeriodLabel = React.useCallback(() => {
    // Durante la stampa il Gantt si restringe alla finestra A4 e il browser azzera lo
    // scroll: ricalcolare qui darebbe il periodo dell'inizio banda (sbagliato) proprio
    // sull'intestazione che si sta stampando. L'etichetta resta quella impostata da printGantt.
    if (printingRef.current) return
    const el = scrollRef.current
    if (!el) return
    const firstIdx = Math.floor(el.scrollLeft / dayW)
    const lastIdx = Math.floor((el.scrollLeft + el.clientWidth) / dayW)
    const a = addDays(bandStart, Math.max(0, firstIdx))
    const b = addDays(bandStart, Math.min(bandDays - 1, lastIdx))
    setPeriodLabel(labelForRange(a, b))
  }, [bandStart, bandDays, dayW, labelForRange])

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
    // Va letto PRIMA di restringere la banda: con la timeline di stampa il contenuto
    // diventa più stretto del viewport e il browser porta lo scrollLeft a 0.
    const savedScrollLeft = el.scrollLeft
    restoreScrollRef.current = savedScrollLeft
    printingRef.current = true
    try {
      const firstIdx = Math.max(0, Math.floor(savedScrollLeft / dayW))
      const printStart = addDays(bandStart, firstIdx)
      const printDays = windowDays
      const printDayW = Math.max(
        12,
        Math.floor((1040 - NAME_COL_WIDTH_EXPANDED) / printDays)
      )
      // L'intestazione dichiara la finestra che si sta stampando davvero.
      setPeriodLabel(labelForRange(printStart, addDays(printStart, printDays - 1)))
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
  }, [printing, dayW, bandStart, windowDays, labelForRange])

  // Tornati alla banda reale (il DOM ha già la timeline piena): si rimette lo scroll dov'era
  // prima della stampa e si ricalcola l'etichetta, congelata per tutta la stampa.
  React.useEffect(() => {
    if (printOverride != null) return
    const saved = restoreScrollRef.current
    if (saved == null) return
    restoreScrollRef.current = null
    const el = scrollRef.current
    if (el) el.scrollLeft = saved
    printingRef.current = false
    updatePeriodLabel()
  }, [printOverride, updatePeriodLabel])

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
