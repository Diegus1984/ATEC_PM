import * as React from "react"
import { Cell, Pie, PieChart } from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import { cn } from "@/lib/utils"

import { barWidthPercent, type BarRow } from "./ddp-sintesi-logic"
import { DdpPieSliceLabel } from "./ddp-pie-slice-label"

function chartKey(bar: BarRow, index: number): string {
  return bar.key || `status-${index}`
}

export function DdpStatusBreakdown({
  bars,
  subtitle,
  className,
  sectionLabel = "Distribuzione",
  totalCaption = "righe totali",
}: {
  bars: BarRow[]
  subtitle?: string
  className?: string
  /** Etichetta per accessibilità e contesto (es. «Ripartizione per stato»). */
  sectionLabel?: string
  totalCaption?: string
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

  if (bars.length === 0) {
    return <p className="text-sm text-muted-foreground">Nessuna riga.</p>
  }

  return (
    <div className={cn("space-y-4", className)}>
      {subtitle ? (
        <p className="text-xs text-muted-foreground">{subtitle}</p>
      ) : null}

      {/* Barra composizione 100% */}
      <div
        className="flex h-3 w-full overflow-hidden rounded-full bg-muted shadow-inner"
        role="img"
        aria-label={`${sectionLabel} su ${total} righe`}
      >
        {bars.map((bar, index) =>
          bar.count > 0 ? (
            <div
              key={chartKey(bar, index)}
              className="h-full min-w-[3px] transition-[flex-grow] duration-300"
              style={{
                flexGrow: bar.count,
                backgroundColor: bar.bg,
              }}
              title={`${bar.label}: ${bar.count} (${bar.pct})`}
            />
          ) : null
        )}
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(200px,260px)_1fr] lg:items-start">
        {/* Donut */}
        <div className="mx-auto w-full max-w-[260px] lg:mx-0">
          <ChartContainer
            config={chartConfig}
            className="mx-auto aspect-square max-h-[240px] w-full"
          >
            <PieChart>
              <ChartTooltip
                content={
                  <ChartTooltipContent
                    hideLabel
                    formatter={(value, _name, item) => {
                      const row = item.payload as {
                        label: string
                        pct: string
                      }
                      return (
                        <span className="font-medium tabular-nums">
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
                innerRadius="58%"
                outerRadius="88%"
                paddingAngle={bars.length > 1 ? 2 : 0}
                strokeWidth={2}
                stroke="var(--background)"
                label={DdpPieSliceLabel}
                labelLine={false}
              >
                {chartData.map((entry) => (
                  <Cell key={entry.id} fill={entry.fill} />
                ))}
              </Pie>
            </PieChart>
          </ChartContainer>
          <p className="mt-1 text-center text-xs text-muted-foreground">
            <span className="text-lg font-bold tabular-nums text-foreground">
              {total}
            </span>{" "}
            {totalCaption}
          </p>
        </div>

        {/* Legenda dettagliata */}
        <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
          {bars.map((bar, index) => (
            <div
              key={chartKey(bar, index)}
              className="flex items-start gap-2.5 rounded-xl border bg-card/60 px-3 py-2.5 shadow-xs"
            >
              {bar.key ? (
                <span
                  className="inline-flex shrink-0 rounded-full px-2 py-0.5 text-[10px] font-bold leading-tight"
                  style={{ backgroundColor: bar.bg, color: bar.fg }}
                >
                  {bar.key}
                </span>
              ) : (
                <span
                  className="mt-0.5 size-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: bar.bg }}
                />
              )}
              <div className="min-w-0 flex-1">
                <div className="flex items-baseline justify-between gap-2">
                  <span
                    className="truncate text-sm font-medium leading-tight"
                    title={bar.label}
                  >
                    {bar.label}
                  </span>
                  <span className="shrink-0 text-sm font-bold tabular-nums">
                    {bar.count}
                  </span>
                </div>
                <div className="mt-2 flex items-center gap-2">
                  <div className="h-2 flex-1 overflow-hidden rounded-full bg-muted">
                    <div
                      className="h-full rounded-full transition-[width] duration-500 ease-out"
                      style={{
                        width: `${barWidthPercent(bar.fraction, bar.count)}%`,
                        backgroundColor: bar.bg,
                      }}
                    />
                  </div>
                  <span className="w-11 shrink-0 text-right text-xs font-medium text-muted-foreground tabular-nums">
                    {bar.pct}
                  </span>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
