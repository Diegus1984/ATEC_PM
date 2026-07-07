import { cn } from "@/lib/utils"

import { DdpMiniPie } from "./DdpMiniPie"
import type { BarRow } from "./ddp-sintesi-logic"

function legendColumns(count: number): number {
  if (count <= 4) return 1
  if (count <= 8) return 2
  return 3
}

function DdpPieLegend({
  bars,
  className,
}: {
  bars: BarRow[]
  className?: string
}) {
  if (bars.length === 0) return null

  const cols = legendColumns(bars.length)

  return (
    <ul
      className={cn(
        "grid min-w-0 flex-1 gap-x-3 gap-y-0.5",
        cols === 1 && "grid-cols-1",
        cols === 2 && "grid-cols-2",
        cols === 3 && "grid-cols-3",
        className
      )}
    >
      {bars.map((bar) => (
        <li
          key={bar.key || bar.label}
          className="flex min-w-0 items-center gap-1.5 truncate text-xs leading-tight"
          title={`${bar.label}: ${bar.count} (${bar.pct})`}
        >
          <span
            className="size-2 shrink-0 rounded-sm border border-black/10"
            style={{ backgroundColor: bar.bg }}
            aria-hidden
          />
          <span className="min-w-0 truncate">
            <span className="font-semibold">{bar.key || bar.label}</span>
            <span className="text-muted-foreground tabular-nums">
              {" : "}
              {bar.count} ({bar.pct})
            </span>
          </span>
        </li>
      ))}
    </ul>
  )
}

/** Torta stato distinta + legenda compatta a destra (codice stato e %). */
export function DdpPieWithLegend({
  bars,
  className,
  pieSize = 72,
}: {
  bars: BarRow[]
  className?: string
  pieSize?: number
}) {
  const total = bars.reduce((sum, bar) => sum + bar.count, 0)

  if (bars.length === 0 || total === 0) {
    return <span className="text-sm text-muted-foreground">—</span>
  }

  return (
    <div className={cn("flex items-center gap-2.5", className)}>
      <DdpMiniPie
        bars={bars}
        size={pieSize}
        showSliceLabels={false}
        className="shrink-0"
      />
      <DdpPieLegend bars={bars} />
    </div>
  )
}
