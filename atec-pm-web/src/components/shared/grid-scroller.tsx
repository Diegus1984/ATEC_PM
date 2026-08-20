import * as React from "react"

import { cn } from "@/lib/utils"

export interface GridScrollerProps {
  /** La griglia (di norma un `<Table>`). */
  children: React.ReactNode
  /** Classi del contenitore esterno: bordo, raggio, margini. */
  className?: string
  /** Classi dello scroller interno (es. `max-h-[40vh]` per un'altezza diversa). */
  scrollerClassName?: string
  /**
   * `true` quando la griglia sta in un contenitore flex che ne stabilisce già
   * l'altezza (pannelli a tutta pagina, dialoghi): riempie lo spazio del
   * genitore invece di usare `--grid-max-h`.
   */
  fill?: boolean
  /** Intestazione ferma in alto mentre le righe scorrono. Default `true`. */
  stickyHeader?: boolean
  /** Barra di scorrimento orizzontale SOPRA la griglia. Default `true`. */
  topScrollbar?: boolean
  /** Ref allo scroller, per chi deve pilotarlo (es. autoscroll durante un drag). */
  scrollerRef?: React.RefObject<HTMLDivElement | null>
  onDragOver?: React.DragEventHandler<HTMLDivElement>
  /** Scorrimento dello scroller (es. scroll infinito dei picker). */
  onScroll?: React.UIEventHandler<HTMLDivElement>
}

/**
 * Scroller standard delle griglie ATEC PM (regola di prodotto, vedi
 * BLOCKS-RULES.md): le righe scorrono **dentro** la griglia fino a
 * `--grid-max-h`, l'**intestazione resta fissa** in alto e la barra di
 * scorrimento **orizzontale sta sopra** la griglia invece che sotto — così è
 * sempre a portata di mouse senza dover arrivare in fondo alle righe.
 *
 * Sostituisce il vecchio `<div className="overflow-x-auto rounded-lg border">`
 * attorno alle tabelle. Le shell `DataTableCard`/`DataTableCardFiltered` lo
 * usano già: serve solo per le griglie scritte a mano.
 */
export function GridScroller({
  children,
  className,
  scrollerClassName,
  fill = false,
  stickyHeader = true,
  topScrollbar = true,
  scrollerRef: externalScrollerRef,
  onDragOver,
  onScroll,
}: GridScrollerProps) {
  // La barra in alto è un div finto largo quanto il contenuto, tenuto in
  // sincrono con lo scroller vero. Si assegna scrollLeft solo se diverso, così
  // l'evento di rimbalzo non genera un loop.
  const scrollerRef = React.useRef<HTMLDivElement>(null)
  const topBarRef = React.useRef<HTMLDivElement>(null)
  const [contentWidth, setContentWidth] = React.useState(0)
  const [overflows, setOverflows] = React.useState(false)

  React.useEffect(() => {
    if (!topScrollbar) return
    const scroller = scrollerRef.current
    if (!scroller) return

    let frame = 0
    const measure = () => {
      frame = 0
      setContentWidth(scroller.scrollWidth)
      // Niente barra finta se la griglia ci sta tutta: sarebbe una striscia vuota.
      setOverflows(scroller.scrollWidth > scroller.clientWidth + 1)
    }
    const schedule = () => {
      if (frame) return
      frame = requestAnimationFrame(measure)
    }

    // La larghezza del contenuto cambia con i dati (righe/colonne) e con la
    // tabella stessa (celle che si allargano scrivendo): servono entrambi gli
    // osservatori, la sola dimensione dello scroller non basta.
    const resize = new ResizeObserver(schedule)
    resize.observe(scroller)
    const tables = new Set<Element>()
    const observeTables = () => {
      for (const table of scroller.querySelectorAll("table")) {
        if (tables.has(table)) continue
        tables.add(table)
        resize.observe(table)
      }
    }
    observeTables()
    const mutations = new MutationObserver(() => {
      observeTables()
      schedule()
    })
    mutations.observe(scroller, { childList: true, subtree: true })

    measure()
    return () => {
      if (frame) cancelAnimationFrame(frame)
      resize.disconnect()
      mutations.disconnect()
    }
  }, [topScrollbar])

  const syncScroll = (from: HTMLDivElement | null, to: HTMLDivElement | null) => {
    if (!from || !to || to.scrollLeft === from.scrollLeft) return
    to.scrollLeft = from.scrollLeft
  }

  return (
    <div
      className={cn(
        "flex min-w-0 flex-col overflow-hidden",
        fill && "min-h-0 flex-1",
        className
      )}
    >
      {topScrollbar && overflows ? (
        <div
          ref={topBarRef}
          onScroll={() => syncScroll(topBarRef.current, scrollerRef.current)}
          aria-hidden
          className="h-3 shrink-0 overflow-x-auto overflow-y-hidden border-b"
        >
          <div className="h-px" style={{ width: contentWidth || "100%" }} />
        </div>
      ) : null}

      <div
        ref={(node) => {
          scrollerRef.current = node
          if (externalScrollerRef) externalScrollerRef.current = node
        }}
        onDragOver={onDragOver}
        onScroll={(event) => {
          if (topScrollbar) syncScroll(scrollerRef.current, topBarRef.current)
          onScroll?.(event)
        }}
        className={cn(
          "grid-scroller overflow-auto",
          fill ? "min-h-0 flex-1" : "grid-scroll-area",
          stickyHeader && "grid-sticky-head",
          topScrollbar && "grid-scroller-no-hbar",
          scrollerClassName
        )}
      >
        {children}
      </div>
    </div>
  )
}
