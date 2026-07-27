import * as React from "react"
import { Check, ChevronsUpDown, X } from "lucide-react"

import { Button } from "@/components/ui/button"
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

interface ColumnFilterComboboxProps {
  value: string
  onChange: (value: string) => void
  options: string[]
  placeholder?: string
  searchPlaceholder?: string
  allLabel?: string
  emptyText?: string
  className?: string
  loading?: boolean
}

/**
 * Filtro a tendina con ricerca integrata per intestazioni di colonna.
 * Supporta liste estese (es. tutti i fornitori o categorie) con ricerca veloce.
 */
export function ColumnFilterCombobox({
  value,
  onChange,
  options,
  placeholder = "Filtra…",
  searchPlaceholder,
  allLabel = "Tutti",
  emptyText = "Nessun risultato",
  className,
  loading = false,
}: ColumnFilterComboboxProps) {
  const [open, setOpen] = React.useState(false)
  const [filter, setFilter] = React.useState("")

  const searchLabel =
    searchPlaceholder ??
    `Cerca ${placeholder.replace(/[.…]+$/u, "").trim().toLowerCase()}…`

  /** Con liste lunghe (es. 300+ fornitori) senza testo mostriamo un sottoinsieme; la ricerca sblocca il resto. */
  const MAX_UNFILTERED = 80
  const visible = React.useMemo(() => {
    const term = filter.trim().toLowerCase()
    if (!term) return options.slice(0, MAX_UNFILTERED)
    return options.filter((option) => option.toLowerCase().includes(term))
  }, [options, filter])
  const truncated = !filter.trim() && options.length > MAX_UNFILTERED

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setFilter("")
      }}
    >
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className={cn(
            "h-8 w-full justify-between bg-background font-normal px-2 text-xs",
            !value && "text-muted-foreground",
            className
          )}
        >
          <span className="truncate">{value || placeholder}</span>
          <div className="flex items-center shrink-0 ml-1 gap-0.5">
            {value ? (
              <span
                role="button"
                tabIndex={0}
                className="rounded-full hover:bg-muted p-0.5 cursor-pointer"
                onClick={(e) => {
                  e.stopPropagation()
                  onChange("")
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.stopPropagation()
                    onChange("")
                  }
                }}
              >
                <X className="size-3 text-muted-foreground hover:text-foreground" />
              </span>
            ) : (
              <ChevronsUpDown className="size-3 opacity-50" />
            )}
          </div>
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 gap-0 p-0" align="start">
        <Command shouldFilter={false} className="h-auto max-h-none overflow-visible rounded-md">
          <CommandInput
            value={filter}
            onValueChange={setFilter}
            placeholder={loading ? "Caricamento…" : searchLabel}
          />
          <CommandList className="max-h-60 overflow-y-auto p-1">
            <CommandEmpty>{loading ? "Caricamento…" : emptyText}</CommandEmpty>
            <CommandGroup>
              <CommandItem
                value="__all__"
                onSelect={() => {
                  onChange("")
                  setOpen(false)
                }}
                className="text-xs cursor-pointer"
              >
                <Check
                  className={cn(
                    "mr-2 size-3.5",
                    !value ? "opacity-100" : "opacity-0"
                  )}
                />
                {allLabel}
              </CommandItem>
              {visible.map((option) => (
                <CommandItem
                  key={option}
                  value={option}
                  keywords={[option]}
                  onSelect={() => {
                    onChange(option === value ? "" : option)
                    setOpen(false)
                  }}
                  className="text-xs cursor-pointer"
                >
                  <Check
                    className={cn(
                      "mr-2 size-3.5 shrink-0",
                      value === option ? "opacity-100" : "opacity-0"
                    )}
                  />
                  <span className="truncate">{option}</span>
                </CommandItem>
              ))}
              {truncated ? (
                <p className="px-2 py-1.5 text-[11px] text-muted-foreground">
                  +{options.length - MAX_UNFILTERED} altri — digita per cercare
                </p>
              ) : null}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
