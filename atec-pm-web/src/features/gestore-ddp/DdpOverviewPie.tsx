import * as React from "react"
import { Cell, Pie, PieChart } from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import { cn } from "@/lib/utils"

import {
  DEFAULT_DELIVERED,
  type BarRow,
} from "./ddp-sintesi-logic"
import { DdpPieSliceLabel } from "./ddp-pie-slice-label"

/**
 * Fallback degli stati «DDP STOP» (aggregazione A9), usato solo quando il chiamante
 * non passa l'insieme vero: gli stati veri arrivano da «Aggregazioni DDP».
 */
export const DDP_STOP_STATES = ["ANN", "SOSP", "RAM", "SOST"] as const

/** Verde acceso in seed DB (materiale concluso/disponibile). */
// Dalla v75 (segnalazione #54) le chiusure positive sono tre: DISP sul commerciale,
// CON e COS in officina (comprato fuori / costruito in casa).
export const DDP_BRIGHT_GREEN_STATES = ["DISP", "CON", "COS"] as const

export interface DdpHealthBuckets {
  total: number
  /** Consegnato / gestito: stati dell'aggregazione A2. */
  delivered: number
  deliveredPct: string
  /** Stop / escluso dai conteggi: stati dell'aggregazione A9. */
  stop: number
  stopPct: string
  /** Tutto il resto: ancora in lavorazione (DO, IO, RO, VER, …). */
  inProgress: number
  inProgressPct: string
}

function pct(count: number, total: number): string {
  if (total <= 0) return "—"
  return `${((count / total) * 100).toLocaleString("it-IT", {
    maximumFractionDigits: 1,
  })}%`
}

/** Elenco leggibile di stati per i sottotitoli delle pillole. */
function joinStates(states: Set<string>): string {
  return [...states].sort().join(", ") || "—"
}

/**
 * Ripartisce le fette in consegnato / in lavorazione / stop.
 * Gli insiemi arrivano **sempre dal chiamante** (aggregazioni A2 e A9 configurabili):
 * qui non si cablano elenchi di stati, che cambiano con la matrice stati.
 */
export function computeDdpHealthBuckets(
  bars: BarRow[],
  total: number,
  sets: { delivered: Set<string>; stop: Set<string> }
): DdpHealthBuckets {
  let delivered = 0
  let stop = 0
  for (const bar of bars) {
    if (sets.delivered.has(bar.key)) delivered += bar.count
    else if (sets.stop.has(bar.key)) stop += bar.count
  }
  const inProgress = Math.max(0, total - delivered - stop)

  return {
    total,
    delivered,
    deliveredPct: pct(delivered, total),
    stop,
    stopPct: pct(stop, total),
    inProgress,
    inProgressPct: pct(inProgress, total),
  }
}

function chartKey(bar: BarRow, index: number): string {
  return bar.key || `slice-${index}`
}

/** Torta panoramica stato distinta + sintesi salute (in cima alla pagina sintesi). */
export function DdpOverviewPie({
  bars,
  className,
  variant = "commercial",
  title,
  description,
  delivered,
  stop,
  onOpenStatus,
  onOpenDelivered,
  onOpenStop,
  onOpenInProgress,
}: {
  bars: BarRow[]
  className?: string
  variant?: "commercial" | "officina"
  title?: string
  description?: React.ReactNode
  /** Stati «consegnato/gestito» (A2). Omesso ⇒ fallback cablato. */
  delivered?: Set<string>
  /** Stati «stop/escluso» (A9). Omesso ⇒ fallback cablato. */
  stop?: Set<string>
  /** Click su una fetta → drill-down allo stato. */
  onOpenStatus?: (status: string) => void
  onOpenDelivered?: () => void
  onOpenStop?: () => void
  onOpenInProgress?: () => void
}) {
  const total = React.useMemo(
    () => bars.reduce((sum, bar) => sum + bar.count, 0),
    [bars]
  )

  const sets = React.useMemo(
    () => ({
      delivered: delivered ?? new Set<string>(DEFAULT_DELIVERED),
      stop: stop ?? new Set<string>(DDP_STOP_STATES),
    }),
    [delivered, stop]
  )

  const health = React.useMemo(
    () => computeDdpHealthBuckets(bars, total, sets),
    [bars, total, sets]
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
      <div
        className={cn(
          "rounded-xl border bg-card px-4 py-6 text-sm text-muted-foreground shadow-xs",
          className
        )}
      >
        Nessuna riga in distinta.
      </div>
    )
  }

  const targetOk = health.inProgress === 0
  const distintaLabel =
    variant === "officina" ? "distinta meccanica" : "distinta commerciale"

  return (
    <div
      className={cn(
        "rounded-xl border bg-card p-4 shadow-xs",
        className
      )}
    >
      <div className="mb-3">
        <h3 className="text-sm font-semibold">
          {title ?? `Stato della ${distintaLabel}`}
        </h3>
        <p className="mt-0.5 text-xs text-muted-foreground">
          {description ?? (
            <>
              Ogni fetta è un codice stato (colori da Conf. DDP). Obiettivo a
              commessa chiusa: tutto{" "}
              <span className="font-medium text-foreground">
                consegnato/gestito
              </span>{" "}
              o in{" "}
              <span className="font-medium text-foreground">stop</span>{" "}
              (annullato, sospeso, …); durante il cantiere rosso/giallo/arancio
              sono normali.
            </>
          )}
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(220px,300px)_1fr] lg:items-center">
        <ChartContainer
          config={chartConfig}
          className="mx-auto aspect-square max-h-[280px] w-full"
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
              outerRadius="90%"
              paddingAngle={chartData.length > 1 ? 1 : 0}
              strokeWidth={1}
              stroke="var(--background)"
              label={DdpPieSliceLabel}
              labelLine={false}
              className={onOpenStatus ? "cursor-pointer outline-none" : undefined}
              onClick={
                onOpenStatus
                  ? (_data, index) => {
                      const row = chartData[index]
                      if (row?.key) onOpenStatus(row.key)
                    }
                  : undefined
              }
            >
              {chartData.map((entry) => (
                <Cell key={entry.id} fill={entry.fill} />
              ))}
            </Pie>
          </PieChart>
        </ChartContainer>

        <div className="space-y-3">
          <div className="grid gap-2 sm:grid-cols-3">
            <HealthPill
              label="Consegnato / gestito"
              count={health.delivered}
              pct={health.deliveredPct}
              hint={joinStates(sets.delivered)}
              tone="success"
              onOpen={onOpenDelivered}
            />
            <HealthPill
              label="In lavorazione"
              count={health.inProgress}
              pct={health.inProgressPct}
              hint="DO, IO, RO, VER, …"
              tone={health.inProgress > 0 ? "warning" : "muted"}
              onOpen={onOpenInProgress}
            />
            <HealthPill
              label="Stop / escluso"
              count={health.stop}
              pct={health.stopPct}
              hint={joinStates(sets.stop)}
              tone="neutral"
              onOpen={onOpenStop}
            />
          </div>

          <p
            className={cn(
              "rounded-lg border px-3 py-2 text-xs",
              targetOk
                ? "border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-900/50 dark:bg-emerald-950/40 dark:text-emerald-100"
                : "border-amber-200 bg-amber-50 text-amber-950 dark:border-amber-900/50 dark:bg-amber-950/40 dark:text-amber-100"
            )}
          >
            {targetOk ? (
              <>
                Nessuna riga in stati intermedi: la distinta è tutta in stati
                conclusi o in stop.
              </>
            ) : (
              <>
                Restano{" "}
                <strong className="tabular-nums">{health.inProgress}</strong>{" "}
                righe in stati intermedi — da portare a verde (concluso) o a
                nero (annullato/stop).
              </>
            )}
          </p>

        </div>
      </div>
    </div>
  )
}

function HealthPill({
  label,
  count,
  pct,
  hint,
  tone,
  onOpen,
}: {
  label: string
  count: number
  pct: string
  hint: string
  tone: "success" | "warning" | "muted" | "neutral"
  onOpen?: () => void
}) {
  const toneClass =
    tone === "success"
      ? "border-emerald-200/80 bg-emerald-50/80 dark:border-emerald-900/40 dark:bg-emerald-950/30"
      : tone === "warning"
        ? "border-amber-200/80 bg-amber-50/80 dark:border-amber-900/40 dark:bg-amber-950/30"
        : "border-border bg-muted/30"

  const body = (
    <>
      <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="text-xl font-bold tabular-nums">{count}</span>
        <span className="text-sm font-medium text-muted-foreground tabular-nums">
          {pct}
        </span>
      </div>
      <div className="mt-0.5 text-[10px] text-muted-foreground">{hint}</div>
    </>
  )

  if (!onOpen) {
    return <div className={cn("rounded-lg border px-3 py-2", toneClass)}>{body}</div>
  }

  return (
    <button
      type="button"
      onClick={onOpen}
      title="Apri le righe in Avanzamento"
      className={cn(
        "rounded-lg border px-3 py-2 text-left transition-colors hover:border-primary/40",
        toneClass
      )}
    >
      {body}
    </button>
  )
}
