import * as React from "react"

import { cn } from "@/lib/utils"

/** Tinte pastello delle card KPI, come le card colorate del prototipo V41. */
export type KpiTone = "neutral" | "green" | "amber" | "red" | "blue"

const TONE: Record<KpiTone, string> = {
  neutral: "border-border bg-card",
  green:
    "border-emerald-200 bg-emerald-50 dark:border-emerald-900 dark:bg-emerald-950/40",
  amber:
    "border-amber-200 bg-amber-50 dark:border-amber-900 dark:bg-amber-950/40",
  red: "border-red-200 bg-red-50 dark:border-red-900 dark:bg-red-950/40",
  blue: "border-sky-200 bg-sky-50 dark:border-sky-900 dark:bg-sky-950/40",
}

/**
 * Card KPI della Sintesi DDP: etichetta in alto, valore grande, riga di contesto sotto.
 * Se `onOpen` è passata la card è cliccabile e porta al dettaglio corrispondente.
 */
export function DdpKpiCard({
  label,
  value,
  hint,
  tone = "neutral",
  valueClassName,
  small,
  width = 190,
  onOpen,
  children,
}: {
  label: string
  value?: React.ReactNode
  hint?: React.ReactNode
  tone?: KpiTone
  valueClassName?: string
  small?: boolean
  width?: number
  onOpen?: () => void
  /** Contenuto libero al posto del valore (card «Attesa consegna» a 3 righe). */
  children?: React.ReactNode
}) {
  const content = (
    <>
      <div
        title={label}
        className="truncate text-[10px] font-semibold uppercase tracking-wide text-muted-foreground"
      >
        {label}
      </div>
      {children ?? (
        <div
          className={cn(
            "mt-1 font-bold tabular-nums",
            small ? "text-sm" : "text-2xl",
            valueClassName
          )}
        >
          {value}
        </div>
      )}
      {hint ? (
        <div className="mt-0.5 text-[10px] leading-tight text-muted-foreground">
          {hint}
        </div>
      ) : null}
    </>
  )

  const base = cn(
    "rounded-xl border px-3.5 py-2.5 shadow-xs",
    TONE[tone]
  )

  if (!onOpen) {
    return (
      <div className={base} style={{ width }}>
        {content}
      </div>
    )
  }
  return (
    <button
      type="button"
      onClick={onOpen}
      title="Apri il dettaglio"
      className={cn(
        base,
        "text-left transition-colors hover:border-primary/40 hover:bg-accent"
      )}
      style={{ width }}
    >
      {content}
    </button>
  )
}
