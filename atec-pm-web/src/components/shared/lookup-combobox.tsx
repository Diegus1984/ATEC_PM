import * as React from "react"
import { ChevronDownIcon } from "lucide-react"

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { cn } from "@/lib/utils"

export type LookupComboboxOption<T extends string | number = number> = {
  id: T
  name: string
  /** Intestazione opzionale: voci con lo stesso testo finiscono nello stesso blocco. */
  group?: string
  /** Seconda riga sotto il nome (es. cliente della commessa). */
  hint?: string
}

/** Senza ricerca mostriamo un sottoinsieme: digitare sblocca il resto. */
const MAX_UNFILTERED = 100

const NONE_VALUE = "__lookup_none__"
const ACTION_VALUE = "__lookup_action__"

/**
 * Combo id/nome con barra di ricerca integrata nella tendina (pattern shadcn
 * Combobox: Popover + Command). Da usare al posto di `Select` su tutte le
 * tendine lunghe (clienti, commesse, dipendenti, …): si cerca digitando invece
 * di scorrere centinaia di voci, e Radix non monta tutti gli item a menu chiuso.
 */
export function LookupCombobox<T extends string | number>({
  options,
  value,
  onValueChange,
  placeholder = "Seleziona…",
  searchPlaceholder = "Cerca…",
  emptyText = "Nessun risultato",
  noneLabel,
  action,
  triggerLabel,
  disabled,
  loading,
  className,
}: {
  options: LookupComboboxOption<T>[]
  value: T | null
  onValueChange: (id: T | null) => void
  placeholder?: string
  searchPlaceholder?: string
  emptyText?: string
  /** Se valorizzato, prima voce che azzera la selezione (es. «— nessuna —»). */
  noneLabel?: string
  /** Voce finale che esegue un'azione invece di selezionare (es. «Nuovo…»). */
  action?: { label: string; icon?: React.ReactNode; onSelect: () => void }
  /** Testo del pulsante quando non deve mostrare la voce selezionata. */
  triggerLabel?: string
  disabled?: boolean
  loading?: boolean
  className?: string
}) {
  const [open, setOpen] = React.useState(false)
  const [query, setQuery] = React.useState("")

  const selected = options.find((option) => option.id === value) ?? null

  const visible = React.useMemo(() => {
    const term = query.trim().toLowerCase()
    if (!term) return options.slice(0, MAX_UNFILTERED)
    return options.filter((option) =>
      `${option.name} ${option.hint ?? ""}`.toLowerCase().includes(term)
    )
  }, [options, query])
  const truncated = !query.trim() && options.length > MAX_UNFILTERED

  // Blocchi nell'ordine in cui compaiono le voci (senza `group` → blocco unico).
  const blocks = React.useMemo(() => {
    const out: { key: string; heading?: string; items: LookupComboboxOption<T>[] }[] = []
    for (const option of visible) {
      const heading = option.group
      const last = out[out.length - 1]
      if (last && last.heading === heading) last.items.push(option)
      else out.push({ key: heading ?? "__default__", heading, items: [option] })
    }
    return out
  }, [visible])

  const label = triggerLabel ?? selected?.name ?? placeholder
  const showAsPlaceholder = triggerLabel == null && !selected

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setQuery("")
      }}
    >
      <PopoverTrigger asChild>
        <button
          type="button"
          role="combobox"
          aria-expanded={open}
          disabled={disabled || loading}
          className={cn(
            "flex h-9 w-full items-center justify-between gap-1.5 rounded-md border border-input bg-transparent py-2 pr-2 pl-2.5 text-left text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30 dark:hover:bg-input/50",
            showAsPlaceholder && "text-muted-foreground",
            className
          )}
        >
          <span className="truncate">{loading ? "Caricamento…" : label}</span>
          <ChevronDownIcon className="pointer-events-none size-4 shrink-0 text-muted-foreground" />
        </button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className="w-(--radix-popover-trigger-width) min-w-56 gap-0 p-0"
      >
        <Command shouldFilter={false} className="h-auto max-h-none">
          <CommandInput
            value={query}
            onValueChange={setQuery}
            placeholder={searchPlaceholder}
          />
          <CommandList className="max-h-64">
            <CommandEmpty>{emptyText}</CommandEmpty>
            {noneLabel ? (
              <CommandGroup>
                <CommandItem
                  value={NONE_VALUE}
                  data-checked={value == null ? "true" : undefined}
                  onSelect={() => {
                    onValueChange(null)
                    setOpen(false)
                  }}
                >
                  <span className="text-muted-foreground">{noneLabel}</span>
                </CommandItem>
              </CommandGroup>
            ) : null}
            {blocks.map((block) => (
              <CommandGroup key={block.key} heading={block.heading}>
                {block.items.map((option) => (
                  <CommandItem
                    key={String(option.id)}
                    value={String(option.id)}
                    data-checked={option.id === value ? "true" : undefined}
                    className={option.hint ? "flex-col items-start gap-0.5" : undefined}
                    onSelect={() => {
                      onValueChange(option.id)
                      setOpen(false)
                    }}
                  >
                    <span className="truncate">{option.name}</span>
                    {option.hint ? (
                      <span className="truncate text-xs text-muted-foreground">
                        {option.hint}
                      </span>
                    ) : null}
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}
            {truncated ? (
              <p className="px-3 py-1.5 text-xs text-muted-foreground">
                +{options.length - MAX_UNFILTERED} altri — digita per cercare
              </p>
            ) : null}
            {action ? (
              <CommandGroup>
                <CommandItem
                  value={ACTION_VALUE}
                  onSelect={() => {
                    setOpen(false)
                    action.onSelect()
                  }}
                >
                  <span className="flex items-center gap-2 text-primary">
                    {action.icon}
                    {action.label}
                  </span>
                </CommandItem>
              </CommandGroup>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
