import * as React from "react"
import { Cell, Pie, PieChart } from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  type ChecklistDueFilter,
  type ChecklistPriorityFilter,
  type ChecklistStats,
  DUE_STATUS_META,
  PRIORITY_META,
} from "@/features/checklist/checklist-utils"
import { cn } from "@/lib/utils"

const PRIORITY_COLORS: Record<string, string> = {
  all: "hsl(var(--primary))",
  p0: "#ef4444",
  p1: "#f59e0b",
  p2: "#3b82f6",
  p3: "#14b8a6",
}

const DUE_COLORS: Record<ChecklistDueFilter, string> = {
  all: "hsl(var(--primary))",
  overdue: "#ef4444",
  today: "#f97316",
  soon: "#f59e0b",
  ok: "#64748b",
  none: "#94a3b8",
}

type ChartPoint = {
  key: string
  label: string
  description?: string
  count: number
  fill: string
  pct?: string
}

function toggleFilter<T extends string | number>(
  current: T,
  next: T,
  allValue: T
): T {
  return current === next ? allValue : next
}

function withPercentages(data: ChartPoint[]): ChartPoint[] {
  const total = data.reduce((sum, row) => sum + row.count, 0)
  return data.map((row) => ({
    ...row,
    pct: total > 0 ? `${Math.round((row.count / total) * 100)}%` : "0%",
  }))
}

/** Griglia legenda: pallino · voce (max-content) · att. · % */
const LEGEND_GRID_CLASS =
  "grid grid-cols-[auto_max-content_2.25rem_2.5rem] gap-x-4"

function LegendVoceLabel({ row }: { row: ChartPoint }) {
  if (row.description) {
    return (
      <>
        <span className="font-semibold">{row.label}</span>
        <span className="text-muted-foreground"> · {row.description}</span>
      </>
    )
  }
  return <span>{row.label}</span>
}

function ChecklistPieSliceLabel(props: {
  cx?: number
  cy?: number
  midAngle?: number
  innerRadius?: number
  outerRadius?: number
  percent?: number
  payload?: ChartPoint
}) {
  const { cx, cy, midAngle, innerRadius, outerRadius, percent, payload } = props
  if (
    cx == null ||
    cy == null ||
    outerRadius == null ||
    !payload?.label ||
    (percent ?? 0) < 0.06
  ) {
    return null
  }

  const RADIAN = Math.PI / 180
  const inner = Number(innerRadius ?? 0)
  const outer = Number(outerRadius)
  const angle = Number(midAngle ?? 0)
  const radius = inner + (outer - inner) * 0.55
  const x = cx + radius * Math.cos(-angle * RADIAN)
  const y = cy + radius * Math.sin(-angle * RADIAN)
  const labelLen = payload.label.length
  const fontSize = labelLen >= 4 ? 10 : labelLen >= 3 ? 11 : 12

  return (
    <text
      x={x}
      y={y}
      fill="#ffffff"
      textAnchor="middle"
      dominantBaseline="central"
      fontSize={fontSize}
      fontWeight={700}
      style={{ pointerEvents: "none" }}
    >
      {payload.label}
    </text>
  )
}

function SummaryPieChart({
  title,
  hint,
  data,
  config,
  activeKey,
  totalLabel,
  onSelect,
  showAllLegend = false,
}: {
  title: string
  hint: string
  data: ChartPoint[]
  config: ChartConfig
  activeKey: string
  totalLabel: string
  onSelect: (key: string) => void
  showAllLegend?: boolean
}) {
  const slices = React.useMemo(
    () => withPercentages(data.filter((row) => row.key !== "all")),
    [data]
  )
  const total = React.useMemo(
    () => slices.reduce((sum, row) => sum + row.count, 0),
    [slices]
  )

  if (total === 0) {
    return (
      <div className="rounded-xl border bg-card p-4 shadow-xs">
        <p className="text-sm font-medium">{title}</p>
        <p className="mt-6 text-center text-sm text-muted-foreground">Nessun dato</p>
      </div>
    )
  }

  const legendRows = showAllLegend
    ? [
        {
          key: "all",
          label: "Tutte",
          count: total,
          fill: PRIORITY_COLORS.all,
          pct: "100%",
        },
        ...slices,
      ]
    : slices

  return (
    <div className="rounded-xl border bg-card p-4 shadow-xs">
      <div className="mb-3">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-xs text-muted-foreground">{hint}</p>
      </div>

      <div className="grid gap-3 sm:grid-cols-[minmax(200px,220px)_minmax(0,1fr)] sm:items-start">
        <div className="relative mx-auto w-full max-w-[220px]">
          <ChartContainer
            config={config}
            className="mx-auto aspect-square max-h-[220px] w-full"
          >
            <PieChart>
              <ChartTooltip
                content={
                  <ChartTooltipContent
                    hideLabel
                    formatter={(value, _name, item) => {
                      const row = item.payload as ChartPoint
                      const text = row.description
                        ? `${row.label} · ${row.description}`
                        : row.label
                      const stats =
                        row.key === "all"
                          ? `${value} att.`
                          : `${value} att. (${row.pct})`
                      return (
                        <span className="font-medium tabular-nums">
                          {text}: {stats}
                        </span>
                      )
                    }}
                  />
                }
              />
              <Pie
                data={slices.filter((row) => row.count > 0)}
                dataKey="count"
                nameKey="label"
                innerRadius="52%"
                outerRadius="92%"
                paddingAngle={slices.filter((row) => row.count > 0).length > 1 ? 2 : 0}
                strokeWidth={2}
                stroke="var(--background)"
                label={ChecklistPieSliceLabel}
                labelLine={false}
                className="cursor-pointer"
                onClick={(slice) => {
                  const key = (slice as { payload?: ChartPoint }).payload?.key
                  if (key) onSelect(key)
                }}
              >
                {slices
                  .filter((row) => row.count > 0)
                  .map((entry) => (
                    <Cell
                      key={entry.key}
                      fill={entry.fill}
                      opacity={
                        activeKey === "all" || activeKey === entry.key ? 1 : 0.35
                      }
                      stroke={activeKey === entry.key ? entry.fill : undefined}
                      strokeWidth={activeKey === entry.key ? 3 : 0}
                    />
                  ))}
              </Pie>
            </PieChart>
          </ChartContainer>
          <button
            type="button"
            className={cn(
              "absolute left-1/2 top-1/2 flex size-[58%] -translate-x-1/2 -translate-y-1/2 flex-col items-center justify-center rounded-full",
              "text-center transition-colors hover:bg-muted/30",
              activeKey === "all" && "ring-2 ring-primary ring-offset-2 ring-offset-background"
            )}
            onClick={() => onSelect("all")}
            aria-label={`Mostra tutte: ${total} ${totalLabel}`}
          >
            <span className="text-2xl font-bold tabular-nums leading-none">{total}</span>
            <span className="mt-0.5 text-[10px] text-muted-foreground">{totalLabel}</span>
          </button>
        </div>

        <div
          className={cn(
            "w-fit max-w-full justify-self-start overflow-hidden rounded-lg border bg-muted/10 px-2 text-sm",
            LEGEND_GRID_CLASS
          )}
        >
          <div
            className="col-span-4 grid grid-cols-subgrid items-center border-b bg-muted/30 py-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
            aria-hidden
          >
            <span className="size-2.5" />
            <span className="text-left">Voce</span>
            <span className="text-right">Att.</span>
            <span className="text-right">%</span>
          </div>

          {legendRows.map((row) => {
            const title = row.description
              ? `${row.label} · ${row.description}`
              : row.label
            const statsText =
              row.key === "all"
                ? `${row.count} att.`
                : `${row.count} att. (${row.pct})`

            return (
              <button
                key={row.key}
                type="button"
                onClick={() => onSelect(row.key)}
                title={`${title} — ${statsText}`}
                className={cn(
                  "col-span-4 grid grid-cols-subgrid items-center border-b py-1.5 text-left transition-colors last:border-b-0",
                  "hover:bg-muted/50",
                  activeKey === row.key
                    ? "bg-primary/5 ring-1 ring-inset ring-primary/25"
                    : "bg-transparent"
                )}
              >
                <span
                  className="size-2.5 shrink-0 justify-self-start rounded-full"
                  style={{ backgroundColor: row.fill }}
                />
                <span className="whitespace-nowrap text-left leading-snug">
                  <LegendVoceLabel row={row} />
                </span>
                <span className="text-right font-semibold tabular-nums">{row.count}</span>
                <span className="text-right text-xs tabular-nums text-muted-foreground">
                  {row.key !== "all" && row.pct ? row.pct : "—"}
                </span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

export function ChecklistSummaryCharts({
  stats,
  priorityFilter,
  dueFilter,
  onPriorityFilter,
  onDueFilter,
  className,
}: {
  stats: ChecklistStats
  priorityFilter: ChecklistPriorityFilter
  dueFilter: ChecklistDueFilter
  onPriorityFilter: (value: ChecklistPriorityFilter) => void
  onDueFilter: (value: ChecklistDueFilter) => void
  className?: string
}) {
  const priorityData = React.useMemo<ChartPoint[]>(
    () => [
      {
        key: "all",
        label: "Tutte",
        // Solo attività aperte: coerente con le fette per priorità (i CLOSED sono esclusi da byPriority).
        count: stats.total - stats.closed,
        fill: PRIORITY_COLORS.all,
      },
      ...PRIORITY_META.map((p) => ({
        key: String(p.value),
        label: p.code,
        description: p.name,
        count: stats.byPriority[p.value] ?? 0,
        fill: PRIORITY_COLORS[`p${p.value}` as keyof typeof PRIORITY_COLORS],
      })),
    ],
    [stats]
  )

  const dueData = React.useMemo<ChartPoint[]>(() => {
    const counts: Record<(typeof DUE_STATUS_META)[number]["key"], number> = {
      overdue: stats.overdue,
      today: stats.today,
      soon: stats.soon,
      ok: stats.ok,
      none: stats.none,
    }
    return DUE_STATUS_META.map((d) => ({
      key: d.key,
      label: d.code,
      description: d.name,
      count: counts[d.key],
      fill: DUE_COLORS[d.key],
    }))
  }, [stats])

  const priorityConfig = React.useMemo<ChartConfig>(() => {
    const cfg: ChartConfig = { count: { label: "Attività" } }
    for (const row of priorityData) {
      cfg[row.key] = {
        label: row.description ? `${row.label} · ${row.description}` : row.label,
        color: row.fill,
      }
    }
    return cfg
  }, [priorityData])

  const dueConfig = React.useMemo<ChartConfig>(() => {
    const cfg: ChartConfig = { count: { label: "Attività" } }
    for (const row of dueData) {
      cfg[row.key] = {
        label: row.description ? `${row.label} · ${row.description}` : row.label,
        color: row.fill,
      }
    }
    return cfg
  }, [dueData])

  const activePriorityKey =
    priorityFilter === "all" ? "all" : String(priorityFilter)
  const activeDueKey = dueFilter

  const activePriorityMeta =
    priorityFilter !== "all"
      ? PRIORITY_META.find((p) => p.value === priorityFilter)
      : undefined

  const activeDueMeta =
    dueFilter !== "all"
      ? DUE_STATUS_META.find((d) => d.key === dueFilter)
      : undefined

  const filterHint =
    priorityFilter !== "all" || dueFilter !== "all" ? (
      <p className="text-xs text-muted-foreground">
        Filtro attivo
        {activePriorityMeta
          ? ` · ${activePriorityMeta.code} · ${activePriorityMeta.name}`
          : ""}
        {activeDueMeta ? ` · ${activeDueMeta.code} · ${activeDueMeta.name}` : ""}
        {" · "}
        <button
          type="button"
          className="font-medium text-primary hover:underline"
          onClick={() => {
            onPriorityFilter("all")
            onDueFilter("all")
          }}
        >
          Azzera filtri
        </button>
      </p>
    ) : (
      <p className="text-xs text-muted-foreground">
        Clicca una fetta, la legenda o il totale al centro per filtrare.
      </p>
    )

  return (
    <div className={cn("space-y-2", className)}>
      <div className="grid gap-4 lg:grid-cols-2">
        <SummaryPieChart
          title="Attività per priorità"
          hint="P0 Critica · P1 Alta · P2 Media · P3 Bassa"
          data={priorityData}
          config={priorityConfig}
          activeKey={activePriorityKey}
          totalLabel="attività"
          showAllLegend
          onSelect={(key) =>
            onPriorityFilter(
              toggleFilter(priorityFilter, key === "all" ? "all" : Number(key), "all")
            )
          }
        />
        <SummaryPieChart
          title="Attività per scadenza"
          hint="SCAD · OGG · 3GG · OK · ND"
          data={dueData}
          config={dueConfig}
          activeKey={activeDueKey}
          totalLabel="attività"
          onSelect={(key) =>
            onDueFilter(toggleFilter(dueFilter, key as ChecklistDueFilter, "all"))
          }
        />
      </div>
      {filterHint}
    </div>
  )
}
