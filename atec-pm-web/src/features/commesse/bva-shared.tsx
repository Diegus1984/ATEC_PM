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
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { hours } from "@/features/commesse/preventivo-dialogs"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

import { useBvaWindow } from "./bva-expand"

/** Rosso se il delta sfora (consuntivo > preventivo), verde se sotto (#66). */
export function deltaClass(delta: number): string {
  if (delta > 0.05) return "text-red-600"
  if (delta < -0.05) return "text-emerald-600"
  return "text-muted-foreground"
}

export function deltaText(delta: number): string {
  const sign = delta > 0 ? "+" : ""
  return `Δ ${sign}${delta.toFixed(1)} h`
}

/** Stili condivisi ore / € / etichette (#65, #66): rosso pastello, blu pastello, nero. */
export const bvaLabelClass = "font-bold text-black dark:text-foreground"
export const bvaHoursClass = "font-bold tabular-nums text-rose-500"
export const bvaMoneyClass = "font-bold tabular-nums text-sky-600"
/** Ore delle fasi (#68): preventivate, assegnate/lavorate e % in grassetto nero. */
export const bvaPhaseHoursClass =
  "font-bold tabular-nums text-black dark:text-foreground"
/**
 * Titolo «Risorse a consuntivo» (#104): grassetto rosso, così si stacca dal
 * fratello grigio «Risorse pianificate» che gli sta sopra nella stessa colonna.
 */
export const bvaActualTitleClass =
  "text-[10px] font-bold uppercase tracking-wide text-destructive"
export function bvaDeltaClass(delta: number): string {
  return cn("font-bold tabular-nums", deltaClass(delta))
}

/**
 * Riquadro KPI del Bilancio.
 *
 * `explain` (segnalazioni #35 / #45) compare al passaggio del mouse: solo da dove
 * esce il numero (titolo + formula + cifre), senza commenti. Stile chiaro come negli
 * esempi della #45; il Tooltip scuro di default dell'app qui non andrebbe bene.
 */
export function Kpi({
  label,
  value,
  hint,
  accent,
  explain,
  onEdit,
}: {
  label: string
  value: React.ReactNode
  hint?: React.ReactNode
  accent?: string
  /** Spiegazione del calcolo, mostrata al passaggio del mouse. */
  explain?: React.ReactNode
  onEdit?: () => void
}) {
  const card = (
    <Card size="sm" className={explain ? "cursor-help" : undefined}>
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

  if (!explain) return card

  return (
    <Tooltip>
      {/* `asChild` su un div e non sulla Card: il trigger deve accettare i ref e gli
          handler senza che la Card perda le sue classi. */}
      <TooltipTrigger asChild>
        <div>{card}</div>
      </TooltipTrigger>
      <TooltipContent
        side="bottom"
        className="max-w-sm border border-border bg-card px-3 py-2.5 text-xs leading-snug text-card-foreground shadow-md"
      >
        {explain}
      </TooltipContent>
    </Tooltip>
  )
}

/** Blocco Preventivato / Assegnato / Consuntivato (ore + €) con delta colorato (#66). */
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
      <span>
        <span className={bvaLabelClass}>Preventivato</span>{" "}
        <span className={bvaHoursClass}>{hours(budgetHours)}</span>
        {" · "}
        <span className={bvaMoneyClass}>{euro(budgetCost)}</span>
      </span>
      {travel != null && travel > 0 ? (
        <span className="text-muted-foreground">
          + Trasferta <span className={bvaMoneyClass}>{euro(travel)}</span>
        </span>
      ) : null}
      <span>
        <span className={bvaLabelClass}>Assegnato</span>{" "}
        <span className={bvaHoursClass}>{hours(assignedHours)}</span>
        {" · "}
        <span className={bvaMoneyClass}>{euro(assignedCost)}</span>
      </span>
      <span>
        <span className={bvaLabelClass}>Consuntivato</span>{" "}
        <span className={bvaHoursClass}>{hours(actualHours)}</span>
        {" · "}
        <span className={bvaMoneyClass}>{euro(actualCost)}</span>
      </span>
      {Math.abs(deltaHours) > 0.05 ? (
        <span className={bvaDeltaClass(deltaHours)}>{deltaText(deltaHours)}</span>
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
  const { open, toggle } = useBvaWindow(defaultOpen)
  return (
    <div className="overflow-hidden rounded-lg border">
      <div
        className="flex w-full items-center justify-between px-4 py-2.5 text-white"
        style={{ backgroundColor: "#2563EB" }}
      >
        <button
          type="button"
          className="flex items-center gap-2 text-left"
          onClick={toggle}
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
