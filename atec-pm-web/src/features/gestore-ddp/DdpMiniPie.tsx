import * as React from "react"
import { Cell, Pie, PieChart } from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import { cn } from "@/lib/utils"

import { DdpPieSliceLabel } from "./ddp-pie-slice-label"
import type { BarRow } from "./ddp-sintesi-logic"

function chartKey(bar: BarRow, index: number): string {
  return bar.key || `slice-${index}`
}

/** Torta compatta per cella tabella (Gestore DDP). */
export function DdpMiniPie({
  bars,
  className,
  size = 96,
  showSliceLabels = true,
}: {
  bars: BarRow[]
  className?: string
  /** Lato del quadrato grafico in px. */
  size?: number
  /** Etichette codice stato sulle fette (disattiva se c’è legenda esterna). */
  showSliceLabels?: boolean
}) {
  const total = React.useMemo(
    () => bars.reduce((sum, bar) => sum + bar.count, 0),
    [bars]
  )

  const chartConfig = React.useMemo(() => {
    const config: ChartConfig = {}
    bars.forEach((bar, index) => {
      const id = chartKey(bar, index)
      config[id] = { label: bar.label, color: bar.bg }
    })
    return config
  }, [bars])

  const chartData = React.useMemo(
    () =>
      bars.map((bar, index) => ({
        id: chartKey(bar, index),
        label: bar.label,
        key: bar.key,
        count: bar.count,
        fill: bar.bg,
        fg: bar.fg,
        pct: bar.pct,
      })),
    [bars]
  )

  if (bars.length === 0 || total === 0) {
    return (
      <span
        className={cn("text-xs text-muted-foreground", className)}
        style={{ width: size, height: size }}
      >
        —
      </span>
    )
  }

  return (
    <ChartContainer
      config={chartConfig}
      className={cn("mx-auto aspect-square", className)}
      style={{ width: size, height: size }}
    >
      <PieChart>
        <ChartTooltip
          content={
            <ChartTooltipContent
              hideLabel
              formatter={(value, _name, item) => {
                const row = item.payload as {
                  label: string
                  key: string
                  pct: string
                }
                const code = row.key ? `${row.key} · ` : ""
                return (
                  <span className="font-medium tabular-nums">
                    {code}
                    {row.label}: {value} ({row.pct})
                  </span>
                )
              }}
            />
          }
        />
        <Pie
          data={chartData}
          dataKey="count"
          nameKey="label"
          innerRadius={0}
          outerRadius="92%"
          paddingAngle={chartData.length > 1 ? 1 : 0}
          strokeWidth={1}
          stroke="var(--background)"
          label={showSliceLabels ? DdpPieSliceLabel : false}
          labelLine={false}
          isAnimationActive={false}
        >
          {chartData.map((entry) => (
            <Cell key={entry.id} fill={entry.fill} />
          ))}
        </Pie>
      </PieChart>
    </ChartContainer>
  )
}
