// ── Campi riusabili del foglio SAL ─────────────────────────────────────────

import * as React from "react"
import { Check, ChevronDown } from "lucide-react"

import { ReadonlyDateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { Textarea } from "@/components/ui/textarea"
import type { SalCondition } from "@/lib/api/types"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { focusNextRowField } from "./sal-sheet-shared"

export function Stat({
  label,
  children,
}: {
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="text-sm font-medium">{children}</span>
    </div>
  )
}

export function GrowTextarea({
  value,
  onChange,
  onCommit,
  placeholder,
  className,
  disabled,
  dataCol,
  dataRow,
}: {
  value: string
  onChange: (v: string) => void
  onCommit: () => void
  placeholder?: string
  className?: string
  disabled?: boolean
  /** Colonna logica per il riempimento verticale con Enter (data-sal-col). */
  dataCol?: string
  /** Indice riga per il riempimento verticale con Enter (data-sal-row). */
  dataRow?: number
}) {
  return (
    <Textarea
      rows={1}
      value={value}
      placeholder={placeholder}
      spellCheck={false}
      disabled={disabled}
      data-sal-col={dataCol}
      data-sal-row={dataRow}
      className={cn(
        "field-sizing-content min-h-10 resize-none px-2 py-2 text-sm leading-5 shadow-none",
        className
      )}
      onChange={(e) => onChange(e.target.value)}
      onBlur={onCommit}
      onKeyDown={(e) => {
        if (e.key === "Enter" && !e.shiftKey) {
          e.preventDefault()
          if (dataCol !== undefined && dataRow !== undefined) {
            focusNextRowField(e.currentTarget, dataCol, dataRow)
          } else {
            onCommit()
            e.currentTarget.blur()
          }
        }
      }}
    />
  )
}

/**
 * Select da anagrafica SAL (Conto SAP / Pagamento / Condizioni): valore storico
 * non più in anagrafica preservato come option extra «{valore} (non in anagrafica)»;
 * in coda (solo ADMIN) casella di aggiunta rapida: testo + Invio → crea e seleziona.
 */
export function AnagraficaSelect({
  value,
  options,
  emptyLabel,
  onChange,
  showManage,
  disabled,
  onAdd,
  dataCol,
}: {
  value: string
  options: SalCondition[]
  emptyLabel: string
  onChange: (v: string) => void
  showManage: boolean
  disabled?: boolean
  onAdd?: (label: string) => Promise<string>
  dataCol?: string
}) {
  const [open, setOpen] = React.useState(false)
  const [newValue, setNewValue] = React.useState("")
  const [adding, setAdding] = React.useState(false)

  const opts = React.useMemo(() => {
    const list = options.map((o) => ({ value: o.label, label: o.label }))
    if (value && !options.some((o) => o.label === value)) {
      list.unshift({ value, label: `${value} (non in anagrafica)` })
    }
    return list
  }, [options, value])

  const currentLabel = React.useMemo(() => {
    const found = opts.find((o) => o.value === value)
    return found ? found.label : value
  }, [opts, value])

  function closePopover() {
    setOpen(false)
    setNewValue("")
  }

  function pick(val: string) {
    onChange(val)
    closePopover()
  }

  async function handleCreateNew() {
    const name = newValue.trim()
    if (!name || !onAdd) return
    // Se la voce esiste già in anagrafica (case-insensitive) la selezioniamo e basta:
    // il server risponderebbe «già esistente» e l'utente non vedrebbe nulla.
    const existing = opts.find((o) => o.value.toLowerCase() === name.toLowerCase())
    if (existing) {
      pick(existing.value)
      return
    }
    setAdding(true)
    try {
      const newLabel = await onAdd(name)
      onChange(newLabel)
      setNewValue("")
      closePopover()
    } catch (err) {
      // L'errore deve arrivare all'utente (es. rifiuto del server), non solo in console
      notifyError(
        err instanceof Error ? err.message : "Errore durante la creazione della voce"
      )
    } finally {
      setAdding(false)
    }
  }

  return (
    <Popover open={open} onOpenChange={(next) => (next ? setOpen(true) : closePopover())}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className="h-10 w-full justify-between shadow-none bg-white dark:bg-zinc-950 border-zinc-200 px-2 font-normal text-sm text-foreground [&>span]:line-clamp-1 disabled:opacity-50"
          disabled={disabled}
          data-sal-col={dataCol}
        >
          <span className="truncate">{currentLabel || emptyLabel}</span>
          <ChevronDown className="h-4 w-4 shrink-0 opacity-50 ml-1" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className="w-64 p-1"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="max-h-60 overflow-y-auto pr-1 space-y-0.5">
          {/* Empty item */}
          <button
            type="button"
            onClick={() => pick("")}
            className={cn(
              "w-full flex items-center text-left text-sm py-1.5 px-2 rounded-sm transition-colors hover:bg-accent hover:text-accent-foreground",
              !value && "bg-accent/40 font-semibold"
            )}
          >
            <Check
              className={cn(
                "size-3.5 shrink-0 mr-2",
                !value ? "opacity-100" : "opacity-0"
              )}
            />
            <span>{emptyLabel}</span>
          </button>

          {/* Options list */}
          {opts.map((o) => (
            <button
              key={o.value}
              type="button"
              onClick={() => pick(o.value)}
              className={cn(
                "w-full flex items-center text-left text-sm py-1.5 px-2 rounded-sm transition-colors hover:bg-accent hover:text-accent-foreground",
                o.value === value && "bg-accent/40 font-semibold"
              )}
            >
              <Check
                className={cn(
                  "size-3.5 shrink-0 mr-2",
                  o.value === value ? "opacity-100" : "opacity-0"
                )}
              />
              <span className="truncate">{o.label}</span>
            </button>
          ))}
        </div>

        {showManage && onAdd && (
          <>
            <div className="h-px bg-zinc-200 dark:bg-zinc-800 my-1" />
            <div className="px-1 py-1" onClick={(e) => e.stopPropagation()}>
              <Input
                value={newValue}
                onChange={(e) => setNewValue(e.target.value)}
                placeholder="Aggiungi nuovo..."
                disabled={adding}
                className="h-8 text-sm w-full shadow-none bg-white dark:bg-zinc-950 border-zinc-200"
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    e.stopPropagation()
                    void handleCreateNew()
                  }
                }}
              />
            </div>
          </>
        )}
      </PopoverContent>
    </Popover>
  )
}

/**
 * Chip PIATTO per i numeri in sola lettura del foglio SAL: a riposo la riga non deve
 * avere contrasti (niente casette bianche) — bordo e sfondo da input compaiono solo
 * sul campo in modifica.
 */
export function SalViewChip({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <span
      className={cn(
        "inline-flex h-8 min-w-10 items-center justify-center rounded-md bg-transparent px-2 text-sm",
        className
      )}
    >
      {children}
    </span>
  )
}

/** Vista a tutta larghezza, PIATTA (sfondo trasparente): si fonde con la riga. */
export function SalViewBox({
  children,
  className,
  empty,
  wrap,
}: {
  children: React.ReactNode
  className?: string
  empty?: boolean
  wrap?: boolean
}) {
  return (
    <div
      className={cn(
        "flex min-h-10 min-w-0 items-center rounded-md bg-transparent px-2 text-sm",
        wrap ? "h-auto py-2 items-start" : "h-10",
        empty && "text-muted-foreground",
        className
      )}
    >
      <span className={cn("min-w-0", wrap ? "whitespace-normal" : "truncate")}>
        {empty ? "—" : children}
      </span>
    </div>
  )
}

/** Percentuale: chip del valore + «%» fuori, come nel prototipo. */
export function SalViewPercent({ value }: { value: string }) {
  const empty = value.trim() === ""
  return (
    <div className="flex h-10 items-center gap-1.5">
      <SalViewChip>{empty ? "—" : value}</SalViewChip>
      <span className="text-sm">%</span>
    </div>
  )
}

export function SalViewSelect({
  label,
  emptyLabel = "—",
}: {
  label: string
  emptyLabel?: string
}) {
  const empty = label.trim() === ""
  return (
    <div className="flex h-10 w-full min-w-0 items-center justify-between gap-1 rounded-md bg-transparent px-2 text-sm">
      <span className={cn("truncate", empty && "text-muted-foreground")}>
        {empty ? emptyLabel : label}
      </span>
      <ChevronDown className="size-4 shrink-0 opacity-40" />
    </div>
  )
}

export function SalViewDate({
  value,
  className,
}: {
  value: string | null | undefined
  className?: string
}) {
  return (
    <ReadonlyDateField
      value={value ?? null}
      size="sm"
      stackedWeekday
      className={cn(
        // Testo di riga UNIFORME: dentro il foglio niente giorno festivo rosso né
        // grigi attenuati — la data prende il colore corrente come tutto il resto.
        "rounded-md border-0 bg-transparent shadow-none [&_span]:text-current!",
        className
      )}
    />
  )
}

export function salStatoFattLabel(stato: string): string {
  if (stato === "daEmettere") return "Da emettere"
  if (stato === "emessa") return "Emessa"
  return stato?.trim() || ""
}
