import * as React from "react"
import { it } from "date-fns/locale"
import { CalendarIcon, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { dateToIso, formatDateShort, isoToDate } from "@/lib/date-iso"
import { WEEKDAYS_SHORT, WEEKDAYS_LONG, isRedDay } from "@/lib/it-holidays"
import { cn } from "@/lib/utils"

interface DateFieldProps {
  value: string | null
  onChange: (value: string | null) => void
  placeholder?: string
  clearable?: boolean
  className?: string
  size?: "sm" | "default"
  /** Disabilita i giorni successivi a questa data (es. date future bloccate). */
  disableAfter?: Date
  /** Disabilita i giorni precedenti a questa data (es. fine ≥ inizio). */
  disableBefore?: Date
  /** Disabilita del tutto il campo (es. inibire la data di fine finché manca l'inizio). */
  disabled?: boolean
  /** Mostra il giorno della settimana dentro il campo (rosso se festivo/weekend). */
  showWeekday?: boolean
  /** Nasconde l'icona calendario. */
  showIcon?: boolean
  /** Giorno della settimana SOPRA la data (due righe) invece che in linea: campo più stretto per griglie dense. */
  stackedWeekday?: boolean
}

/** Etichetta data standard: «Lun, 08/06/2026» (giorno rosso se festivo). */
export function formatDateWithWeekday(
  value: string | null | undefined,
  showWeekday = true
): React.ReactNode {
  const date = isoToDate(value)
  if (!date) return null
  if (!showWeekday) return formatDateShort(date)
  const weekday =
    WEEKDAYS_SHORT[date.getDay()].charAt(0).toUpperCase() +
    WEEKDAYS_SHORT[date.getDay()].slice(1)
  return (
    <>
      <span className={cn(isRedDay(date) && "text-red-600 dark:text-red-400")}>
        {weekday},
      </span>{" "}
      {formatDateShort(date)}
    </>
  )
}

/** Etichetta impilata: giorno della settimana sopra (rosso se festivo), data gg/mm/aa sotto. */
function StackedDateLabel({ value }: { value: string | null | undefined }) {
  const date = isoToDate(value)
  if (!date) return null
  const weekday =
    WEEKDAYS_LONG[date.getDay()].charAt(0).toUpperCase() +
    WEEKDAYS_LONG[date.getDay()].slice(1)
  return (
    <span className="flex min-w-0 flex-col items-start text-left leading-tight">
      <span
        className={cn(
          "text-sm",
          isRedDay(date) && "text-red-600 dark:text-red-400"
        )}
      >
        {weekday}
      </span>
      <span className="truncate text-sm">{formatDateShort(date)}</span>
    </span>
  )
}

/** Stesso aspetto del trigger `DateField`, senza interazione (sola lettura). */
export function ReadonlyDateField({
  value,
  placeholder = "—",
  className,
  size = "default",
  showWeekday = true,
  showIcon = true,
  stackedWeekday = false,
}: {
  value: string | null | undefined
  placeholder?: string
  className?: string
  size?: "sm" | "default"
  showWeekday?: boolean
  showIcon?: boolean
  stackedWeekday?: boolean
}) {
  const date = isoToDate(value)

  return (
    <div
      className={cn(
        "inline-flex w-full items-center justify-start gap-2 rounded-full border border-border bg-background font-normal shadow-xs",
        size === "sm" ? "h-8 px-2.5" : "h-9 px-3",
        date ? "" : "text-muted-foreground",
        className,
        stackedWeekday && "h-10 px-2.5 py-1"
      )}
    >
      {showIcon ? (
        <CalendarIcon className="size-4 shrink-0 text-foreground" />
      ) : null}
      {date && stackedWeekday ? (
        <StackedDateLabel value={value} />
      ) : (
        <span className="truncate text-sm">
          {date ? formatDateWithWeekday(value, showWeekday) : placeholder}
        </span>
      )}
    </div>
  )
}

/** Date picker canonico (`Popover` + `Calendar`) con icona calendario e formato italiano. */
export function DateField({
  value,
  onChange,
  placeholder = "—",
  clearable = true,
  className,
  size = "default",
  disableAfter,
  disableBefore,
  disabled = false,
  showWeekday = true,
  showIcon = true,
  stackedWeekday = false,
}: DateFieldProps) {
  const [open, setOpen] = React.useState(false)
  const date = isoToDate(value)

  const disabledDays = [
    ...(disableBefore ? [{ before: disableBefore }] : []),
    ...(disableAfter ? [{ after: disableAfter }] : []),
  ]

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size={size}
          disabled={disabled}
          className={cn(
            "group w-full justify-start gap-2 rounded-full bg-background font-normal shadow-xs",
            date ? "" : "text-muted-foreground",
            className,
            stackedWeekday && "h-10 px-2.5 py-1"
          )}
        >
          {showIcon ? (
            <CalendarIcon className="size-4 shrink-0 text-foreground" />
          ) : null}
          {date && stackedWeekday ? (
            <StackedDateLabel value={value} />
          ) : (
            <span className="truncate text-sm">
              {date ? formatDateWithWeekday(value, showWeekday) : placeholder}
            </span>
          )}
          {clearable && date && !disabled ? (
            <span
              role="button"
              tabIndex={-1}
              aria-label="Cancella data"
              className="ml-auto inline-flex size-4 shrink-0 items-center justify-center rounded-sm text-muted-foreground opacity-70 hover:opacity-100"
              onClick={(event) => {
                event.preventDefault()
                event.stopPropagation()
                onChange(null)
              }}
            >
              <X className="size-3.5" />
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          locale={it}
          selected={date}
          defaultMonth={date ?? disableBefore}
          disabled={disabledDays.length > 0 ? disabledDays : undefined}
          modifiers={{ festivo: isRedDay }}
          modifiersClassNames={{
            festivo: "text-red-600 dark:text-red-400",
          }}
          onSelect={(next) => {
            if (next) {
              onChange(dateToIso(next))
              setOpen(false)
            }
          }}
          autoFocus
        />
      </PopoverContent>
    </Popover>
  )
}
