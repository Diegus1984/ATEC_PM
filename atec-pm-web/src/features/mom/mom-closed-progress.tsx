import { cn } from "@/lib/utils"

/** % azioni chiuse sul totale (stessa formula del riepilogo dettaglio verbale). */
export function momClosedPct(itemsCount: number, openCount: number): number {
  if (itemsCount <= 0) return 0
  const closed = Math.max(0, itemsCount - openCount)
  return Math.round((closed / itemsCount) * 100)
}

/**
 * Barra verde + «N% chiuse», come nel riepilogo del dettaglio verbale
 * (e nella richiesta sulle card lista Verbali MoM).
 */
export function MoMClosedProgress({
  itemsCount,
  openCount,
  className,
  trackClassName,
}: {
  itemsCount: number
  openCount: number
  className?: string
  /** Traccia sotto la barra (default `bg-muted` sulle card). */
  trackClassName?: string
}) {
  const pct = momClosedPct(itemsCount, openCount)
  return (
    <div className={cn("flex min-w-28 items-center gap-2", className)}>
      <div
        className={cn(
          "h-1.5 min-w-12 flex-1 overflow-hidden rounded-full bg-muted",
          trackClassName
        )}
      >
        <div
          className="h-full rounded-full bg-green-500"
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="shrink-0 text-xs text-muted-foreground tabular-nums">
        {pct}% chiuse
      </span>
    </div>
  )
}
