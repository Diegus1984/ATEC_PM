// ── Mattoni presentazionali del Preventivo vs Consuntivo ───────────────────
// KPI, riga Prev/Assegn/Cons e macro-sezione blu: usati sia dalla pagina che
// dai blocchi sezione/economici.

import * as React from "react"
import { ChevronRight, Pencil } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Collapsible } from "@/components/ui/collapsible"
import { hours } from "@/features/commesse/preventivo-dialogs"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

/** Rosso se il delta sfora (consuntivo > preventivo), verde se sotto. */
function deltaClass(delta: number): string {
  if (delta > 0.05) return "text-destructive"
  if (delta < -0.05) return "text-emerald-600"
  return "text-muted-foreground"
}

function deltaText(delta: number): string {
  const sign = delta > 0 ? "+" : ""
  return `Δ ${sign}${delta.toFixed(1)} h`
}

export function Kpi({
  label,
  value,
  hint,
  accent,
  onEdit,
}: {
  label: string
  value: string
  hint?: string
  accent?: string
  onEdit?: () => void
}) {
  return (
    <Card size="sm">
      <CardHeader>
        <div className="flex items-center justify-between gap-2">
          <CardDescription>{label}</CardDescription>
          {onEdit ? (
            <Button
              variant="ghost"
              size="icon-sm"
              onClick={onEdit}
              aria-label={`Modifica ${label}`}
            >
              <Pencil />
            </Button>
          ) : null}
        </div>
        <CardTitle className={cn("text-xl tabular-nums", accent)}>
          {value}
        </CardTitle>
        {hint ? (
          <CardDescription className="text-xs">{hint}</CardDescription>
        ) : null}
      </CardHeader>
    </Card>
  )
}

/** Blocco Prev / Assegn / Cons (ore + €) con delta colorato. */
export function ThreeColumn({
  budgetHours,
  budgetCost,
  assignedHours,
  assignedCost,
  actualHours,
  actualCost,
  deltaHours,
  travel,
}: {
  budgetHours: number
  budgetCost: number
  assignedHours: number
  assignedCost: number
  actualHours: number
  actualCost: number
  deltaHours: number
  travel?: number
}) {
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-xs tabular-nums">
      <span className="text-muted-foreground">
        Prev:{" "}
        <span className="font-medium text-foreground">{hours(budgetHours)}</span>{" "}
        · {euro(budgetCost)}
      </span>
      {travel != null && travel > 0 ? (
        <span className="text-muted-foreground">+ Trasferta {euro(travel)}</span>
      ) : null}
      <span className="text-muted-foreground">
        Assegn:{" "}
        <span className="font-medium text-foreground">
          {hours(assignedHours)}
        </span>{" "}
        · {euro(assignedCost)}
      </span>
      <span className="text-muted-foreground">
        Cons:{" "}
        <span className="font-medium text-foreground">{hours(actualHours)}</span>{" "}
        · {euro(actualCost)}
      </span>
      {Math.abs(deltaHours) > 0.05 ? (
        <span className={cn("font-medium", deltaClass(deltaHours))}>
          {deltaText(deltaHours)}
        </span>
      ) : null}
    </div>
  )
}

/** Macro-sezione collassabile con header blu #2563EB (come il WPF BudgetVsActualControl). */
export function BlueSection({
  title,
  action,
  defaultOpen = true,
  children,
}: {
  title: string
  action?: React.ReactNode
  defaultOpen?: boolean
  children: React.ReactNode
}) {
  const [open, setOpen] = React.useState(defaultOpen)
  return (
    <div className="overflow-hidden rounded-lg border">
      <div
        className="flex w-full items-center justify-between px-4 py-2.5 text-white"
        style={{ backgroundColor: "#2563EB" }}
      >
        <button
          type="button"
          className="flex items-center gap-2 text-left"
          onClick={() => setOpen((v) => !v)}
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
              open && "rotate-90"
            )}
          />
          <span className="text-sm font-semibold uppercase tracking-wide">
            {title}
          </span>
        </button>
        {action}
      </div>
      <Collapsible open={open}>
        <div className="space-y-3 p-3">{children}</div>
      </Collapsible>
    </div>
  )
}
