import * as React from "react"
import { it } from "date-fns/locale"
import { CalendarIcon, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import {
  Popover,
  PopoverAnchor,
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
  /**
   * Editor a segmenti GG / MM / AAAA digitabile, con il calendario che resta
   * disponibile dall'icona. Da usare nelle griglie dove si compilano molte date
   * di seguito: dal solo popover ogni riga costa due clic in più.
   */
  segmented?: boolean
  /**
   * Pillola piatta a riposo (per le griglie dense): bordo e sfondo compaiono solo
   * al passaggio del mouse o con un segmento a fuoco. Opt-in, solo con `segmented`;
   * gli altri usi restano con la pillola bordata di default.
   */
  flat?: boolean
}

/** Ultimo giorno del mese (gestisce gli anni bisestili). */
function daysInMonth(year: number, month: number): number {
  return new Date(year, month, 0).getDate()
}

function clamp(n: number, min: number, max: number): number {
  return n < min ? min : n > max ? max : n
}

/**
 * Editor data a segmenti GG / MM / AAAA, porting del `wireDateEditor` del prototipo:
 * si digita il giorno e il focus passa da solo al mese, poi all'anno (già compilato
 * con l'anno corrente, così di norma non si tocca). Backspace su un segmento vuoto
 * torna al precedente, Invio conferma, il calendario resta sull'icona.
 *
 * Il valore si scrive solo all'uscita da TUTTO il gruppo: spostarsi fra i segmenti
 * non salva nulla.
 */
function SegmentedDateEditor({
  value,
  onChange,
  disabled,
  disableBefore,
  disableAfter,
  clearable,
  showWeekday,
  flat,
  onOpenCalendar,
}: {
  value: string | null
  onChange: (value: string | null) => void
  disabled: boolean
  disableBefore?: Date
  disableAfter?: Date
  clearable: boolean
  showWeekday: boolean
  flat: boolean
  onOpenCalendar: () => void
}) {
  const date = isoToDate(value)
  const currentYear = new Date().getFullYear()

  const dayRef = React.useRef<HTMLInputElement | null>(null)
  const monthRef = React.useRef<HTMLInputElement | null>(null)
  const yearRef = React.useRef<HTMLInputElement | null>(null)
  const boxRef = React.useRef<HTMLDivElement | null>(null)

  const segmentsOf = React.useCallback(
    (d: Date | undefined) => ({
      day: d ? String(d.getDate()).padStart(2, "0") : "",
      month: d ? String(d.getMonth() + 1).padStart(2, "0") : "",
      // Anno preimpostato quando la data è vuota: è il segmento che non si digita mai.
      year: d ? String(d.getFullYear()) : String(currentYear),
    }),
    [currentYear]
  )

  const [seg, setSeg] = React.useState(() => segmentsOf(date))

  // `commit` gira dentro un setTimeout: legge i segmenti dal ref, non dalla closure,
  // così non può salvare un valore vecchio di un render (es. l'ultima cifra digitata
  // subito prima di uscire dal campo).
  const segRef = React.useRef(seg)
  segRef.current = seg

  // Riallinea i segmenti quando il valore cambia da fuori (calendario, patch, realtime),
  // ma non mentre l'utente sta digitando dentro il gruppo.
  React.useEffect(() => {
    if (boxRef.current?.contains(document.activeElement)) return
    setSeg(segmentsOf(isoToDate(value)))
  }, [value, segmentsOf])

  const digits = (v: string) => v.replace(/[^0-9]/g, "")

  function commit() {
    // setTimeout: al blur di un segmento il focus non è ancora sul successivo.
    setTimeout(() => {
      if (boxRef.current?.contains(document.activeElement)) return

      const current = segRef.current
      const d = digits(current.day)
      const mo = digits(current.month)
      const y = digits(current.year)

      // Giorno e mese entrambi svuotati a mano = data cancellata.
      if (!d && !mo) {
        if (value) onChange(null)
        else setSeg(segmentsOf(undefined))
        return
      }

      const now = new Date()
      let year = y ? Number(y) : currentYear
      if (y.length <= 2) year = 2000 + Number(y) // «26» → 2026
      const month = clamp(mo ? Number(mo) : now.getMonth() + 1, 1, 12)
      // Il giorno si limita alla lunghezza reale del mese: «31/02» diventa 28/02,
      // così non si costruisce mai una data inesistente.
      const day = clamp(d ? Number(d) : now.getDate(), 1, daysInMonth(year, month))

      let next = new Date(year, month - 1, day)
      // Stessi limiti del calendario: se si digita una data fuori range la si porta al bordo
      // (è la regola già usata per «fine ≥ inizio»).
      if (disableBefore && next < disableBefore) next = disableBefore
      if (disableAfter && next > disableAfter) next = disableAfter

      const iso = dateToIso(next)
      setSeg(segmentsOf(next))
      if (iso !== value) onChange(iso)
    }, 0)
  }

  const segClass =
    "w-[2ch] bg-transparent p-0 text-center text-sm tabular-nums outline-none placeholder:text-muted-foreground/50 focus:bg-primary/10 focus:rounded-[3px]"

  return (
    <div
      ref={boxRef}
      className={cn(
        "inline-flex h-8 w-full items-center gap-0.5 rounded-full border px-2",
        flat
          ? // Piatta a riposo: bordo/sfondo da input solo su hover o con un segmento a fuoco.
            "border-transparent bg-transparent shadow-none hover:border-input focus-within:border-input focus-within:bg-background"
          : "border-border bg-background shadow-xs focus-within:border-ring focus-within:ring-[3px] focus-within:ring-ring/50",
        disabled && "pointer-events-none opacity-50"
      )}
    >
      <input
        ref={dayRef}
        className={segClass}
        inputMode="numeric"
        placeholder="gg"
        aria-label="giorno"
        disabled={disabled}
        value={seg.day}
        onFocus={(e) => e.currentTarget.select()}
        onChange={(e) => {
          // NIENTE maxLength: se più cifre arrivano in un colpo solo (digitazione
          // veloce, incolla, autocompletamento) il browser le scarterebbe in silenzio
          // e resterebbe una data sbagliata senza che nessuno se ne accorga.
          // Qui l'eccedenza trabocca nei segmenti successivi.
          const v = digits(e.target.value)
          let day = v.slice(0, 2)
          let rest = v.slice(2)
          // Giorno impossibile (32-99): significa che due cifre sono finite qui prima che
          // il focus riuscisse a passare al mese — succede con la digitazione molto rapida
          // e con l'incolla. Si rilegge come «giorno, poi mese»: «712» → 7 dicembre,
          // non «71» troncato a 31 come sarebbe successo restando muti.
          if (day.length === 2 && Number(day) > 31) {
            rest = day.slice(1) + rest
            day = day.slice(0, 1)
          }
          setSeg((s) => ({
            ...s,
            day,
            month: rest.length > 0 ? rest.slice(0, 2) : s.month,
            year: rest.length > 2 ? rest.slice(2, 6) : s.year,
          }))
          if (rest.length >= 2) {
            yearRef.current?.focus()
            yearRef.current?.select()
          } else if (day.length === 2 || (day.length === 1 && Number(day) > 3)) {
            // Oltre il 3 non può esserci una seconda cifra: si passa avanti subito.
            monthRef.current?.focus()
            monthRef.current?.select()
          }
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
        }}
        onBlur={commit}
      />
      <span className="text-xs text-muted-foreground">/</span>
      <input
        ref={monthRef}
        className={segClass}
        inputMode="numeric"
        placeholder="mm"
        aria-label="mese"
        disabled={disabled}
        value={seg.month}
        onFocus={(e) => e.currentTarget.select()}
        onChange={(e) => {
          const v = digits(e.target.value)
          const month = v.slice(0, 2)
          const rest = v.slice(2)
          setSeg((s) => ({
            ...s,
            month,
            year: rest.length > 0 ? rest.slice(0, 4) : s.year,
          }))
          if (rest.length > 0 || month.length === 2 || (month.length === 1 && Number(month) > 1)) {
            yearRef.current?.focus()
            yearRef.current?.select()
          }
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
          if (e.key === "Backspace" && seg.month === "") dayRef.current?.focus()
        }}
        onBlur={commit}
      />
      <span className="text-xs text-muted-foreground">/</span>
      <input
        ref={yearRef}
        className={cn(segClass, "w-[4ch]")}
        inputMode="numeric"
        maxLength={4}
        placeholder="aaaa"
        aria-label="anno"
        disabled={disabled}
        value={seg.year}
        onFocus={(e) => e.currentTarget.select()}
        onChange={(e) =>
          setSeg((s) => ({ ...s, year: digits(e.target.value).slice(0, 4) }))
        }
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
          if (e.key === "Backspace" && seg.year === "") monthRef.current?.focus()
        }}
        onBlur={commit}
      />

      {showWeekday && date ? (
        <span
          className={cn(
            "ml-1 shrink-0 text-[11px]",
            isRedDay(date) ? "text-red-600 dark:text-red-400" : "text-muted-foreground"
          )}
        >
          {WEEKDAYS_SHORT[date.getDay()].charAt(0).toUpperCase() +
            WEEKDAYS_SHORT[date.getDay()].slice(1)}
        </span>
      ) : null}

      <span className="ml-auto flex shrink-0 items-center">
        {clearable && date && !disabled ? (
          <button
            type="button"
            tabIndex={-1}
            aria-label="Cancella data"
            title="Svuota la data"
            className="inline-flex size-5 items-center justify-center rounded-sm text-muted-foreground opacity-70 hover:opacity-100"
            onClick={() => onChange(null)}
          >
            <X className="size-3.5" />
          </button>
        ) : null}
        <button
          type="button"
          tabIndex={-1}
          aria-label="Apri il calendario"
          title="Apri il calendario"
          disabled={disabled}
          className="inline-flex size-5 items-center justify-center rounded-sm text-muted-foreground hover:text-foreground"
          onClick={onOpenCalendar}
        >
          <CalendarIcon className="size-3.5" />
        </button>
      </span>
    </div>
  )
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
/**
 * Giorno della settimana per esteso sopra la data (due righe), come nelle celle
 * «Data Prev.». Esportata perché la usano anche le colonne di sola lettura che
 * devono avere lo stesso aspetto delle date editabili accanto.
 */
export function StackedDateLabel({ value }: { value: string | null | undefined }) {
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
  segmented = false,
  flat = false,
}: DateFieldProps) {
  const [open, setOpen] = React.useState(false)
  const date = isoToDate(value)

  const disabledDays = [
    ...(disableBefore ? [{ before: disableBefore }] : []),
    ...(disableAfter ? [{ after: disableAfter }] : []),
  ]

  const calendar = (
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
  )

  // Modo a segmenti: il popover si àncora al campo ma lo apre SOLO l'icona calendario,
  // altrimenti ogni clic su un segmento aprirebbe il calendario mentre si digita.
  if (segmented) {
    return (
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverAnchor asChild>
          <div className={cn("w-full", className)}>
            <SegmentedDateEditor
              value={value}
              onChange={onChange}
              disabled={disabled}
              disableBefore={disableBefore}
              disableAfter={disableAfter}
              clearable={clearable}
              showWeekday={showWeekday}
              flat={flat}
              onOpenCalendar={() => setOpen(true)}
            />
          </div>
        </PopoverAnchor>
        {calendar}
      </Popover>
    )
  }

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
      {calendar}
    </Popover>
  )
}
