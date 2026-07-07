import * as React from "react"
import { Check, ChevronsUpDown } from "lucide-react"

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

export interface MultiSelectOption {
  id: number
  name: string
}

interface MultiSelectComboProps {
  options: MultiSelectOption[]
  selectedIds: number[]
  onChange: (ids: number[]) => void
  placeholder?: string
  searchPlaceholder?: string
  emptyMessage?: string
  className?: string
  size?: "sm" | "default"
  /** Etichetta a capo automatico (celle datagrid che crescono in altezza). */
  wrapLabel?: boolean
  /** Un nome per riga; larghezza = nome più lungo, altezza = numero di nomi. */
  stackLabel?: boolean
}

/**
 * Combo a selezione multipla con ricerca (analogo del `MultiSelectLookupCombo` WPF).
 * L'ordine di selezione è preservato: la spunta accoda l'id, lo spunta-via lo rimuove —
 * così resp1/resp2/resp3 mantengono l'ordine scelto.
 */
export function MultiSelectCombo({
  options,
  selectedIds,
  onChange,
  placeholder = "Seleziona…",
  searchPlaceholder = "Cerca…",
  emptyMessage = "Nessun risultato.",
  className,
  size = "default",
  wrapLabel = false,
  stackLabel = false,
}: MultiSelectComboProps) {
  const [open, setOpen] = React.useState(false)
  const [filter, setFilter] = React.useState("")

  const nameById = React.useMemo(() => {
    const map = new Map<number, string>()
    for (const option of options) map.set(option.id, option.name)
    return map
  }, [options])

  const selectedNames = selectedIds
    .map((id) => nameById.get(id) ?? `#${id}`)
    .filter(Boolean)

  const selectedLabel = selectedNames.join(", ")

  const visible = React.useMemo(() => {
    const term = filter.trim().toLowerCase()
    if (!term) return options
    return options.filter((option) => option.name.toLowerCase().includes(term))
  }, [options, filter])

  function toggle(id: number) {
    if (selectedIds.includes(id)) {
      onChange(selectedIds.filter((value) => value !== id))
    } else {
      onChange([...selectedIds, id])
    }
  }

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
          type="button"
          variant="outline"
          size={size}
          role="combobox"
          aria-expanded={open}
          className={cn(
            "justify-between gap-2 font-normal",
            stackLabel ? "h-auto w-auto min-h-8 items-start py-1.5" : "w-full",
            !stackLabel && wrapLabel ? "h-auto min-h-8 items-start py-1.5" : "",
            selectedNames.length > 0 ? "" : "text-muted-foreground",
            className
          )}
        >
          {stackLabel ? (
            selectedNames.length > 0 ? (
              <span className="flex flex-col items-start gap-0.5 text-left leading-5">
                {selectedNames.map((name, index) => (
                  <span key={`${name}-${index}`} className="whitespace-nowrap">
                    {name}
                  </span>
                ))}
              </span>
            ) : (
              <span className="whitespace-nowrap">{placeholder}</span>
            )
          ) : (
            <span
              className={cn(
                "text-left",
                wrapLabel ? "whitespace-normal break-words" : "truncate"
              )}
            >
              {selectedLabel || placeholder}
            </span>
          )}
          <ChevronsUpDown className="size-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className="w-(--radix-popover-trigger-width) min-w-56 p-0"
      >
        <Command shouldFilter={false}>
          <CommandInput
            value={filter}
            onValueChange={setFilter}
            placeholder={searchPlaceholder}
          />
          <CommandList>
            <CommandEmpty>{emptyMessage}</CommandEmpty>
            <CommandGroup>
              {visible.map((option) => {
                const checked = selectedIds.includes(option.id)
                return (
                  <CommandItem
                    key={option.id}
                    value={option.name}
                    onSelect={() => toggle(option.id)}
                  >
                    <Check
                      className={cn(
                        "size-4 shrink-0",
                        checked ? "opacity-100" : "opacity-0"
                      )}
                    />
                    <span className="truncate">{option.name}</span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
