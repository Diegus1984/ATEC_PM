import { cn } from "@/lib/utils"

/** Percentuale con al massimo 1 decimale, virgola all'italiana (nessun decimale se intera). */
export function fmtSalPct(n: number): string {
  const r = Math.round(n * 10) / 10
  return Number.isInteger(r) ? String(r) : r.toFixed(1).replace(".", ",")
}

/** Avanzamento incasso v10: Σ% pagate / Σ% totali × 100. */
export function computeSalIncassoProgress(percTotal: number, percPaid: number): number {
  return percTotal > 0 ? (percPaid / percTotal) * 100 : 0
}

interface SalIncassoProgressProps {
  percTotal: number
  percPaid: number
  /** Compatto per l'header card commessa (collassata). */
  compact?: boolean
  className?: string
}

/**
 * Barra «Avanzamento Incasso SAL» (regola v10: Σ% righe Pagata / Σ% righe).
 * Riusata nell'header card globale `/sal` e nel dettaglio `ProjectSal`.
 */
export function SalIncassoProgress({
  percTotal,
  percPaid,
  compact = false,
  className,
}: SalIncassoProgressProps) {
  const advanced = computeSalIncassoProgress(percTotal, percPaid)
  const title = `Avanzamento incasso SAL: ${fmtSalPct(advanced)}% (${fmtSalPct(percPaid)}% di ${fmtSalPct(percTotal)}% incassati)`

  if (compact) {
    return (
      <div
        className={cn(
          "inline-flex items-center gap-1.5 shrink-0 rounded-md border border-emerald-200/80 bg-emerald-50/60 px-2 py-0.5",
          className
        )}
        title={title}
      >
        <span className="text-[10px] font-bold uppercase tracking-wide text-emerald-800/80 whitespace-nowrap">
          Incasso
        </span>
        <span className="font-mono text-xs font-bold tabular-nums text-emerald-700">
          {fmtSalPct(advanced)}%
        </span>
        <span className="h-1.5 w-12 overflow-hidden rounded-full bg-emerald-200/80">
          <span
            className="block h-full bg-emerald-500 transition-all duration-300"
            style={{ width: `${Math.min(100, advanced)}%` }}
          />
        </span>
      </div>
    )
  }

  return (
    <div className={cn("flex flex-col gap-1", className)} title={title}>
      <span className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
        Avanzamento Incasso SAL
      </span>
      <span className="flex items-center gap-2">
        <span className="font-semibold text-sm tabular-nums">{fmtSalPct(advanced)}%</span>
        <span className="h-2 w-24 overflow-hidden rounded bg-zinc-200">
          <span
            className="block h-full bg-emerald-500 transition-all duration-300"
            style={{ width: `${Math.min(100, advanced)}%` }}
          />
        </span>
        <span className="text-xs text-muted-foreground tabular-nums">
          ({fmtSalPct(percPaid)}% di {fmtSalPct(percTotal)}%)
        </span>
      </span>
    </div>
  )
}
