import * as React from "react"

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import type { SupplierLookupItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

const MAX_RESULTS = 40

/**
 * Combobox fornitori con ricerca (pattern CustomerSearchCombobox): filtra
 * client-side ma renderizza al massimo 40 voci. NON usare una `Select` con tutta
 * l'anagrafica (~2200 fornitori): Radix monta tutti gli item anche da chiusa e
 * rende lento qualsiasi dialog che la contiene.
 *
 * Lavora per ragione sociale (`value` = companyName, "" = nessuno) così i nomi
 * denormalizzati (es. RDO nelle lavorazioni) restano visibili anche se il
 * fornitore non è più in anagrafica; `onValueChange` passa anche l'anagrafica
 * selezionata per chi ha bisogno dell'id.
 */
export function SupplierSearchCombobox({
  suppliers,
  value,
  onValueChange,
  disabled,
  placeholder = "Cerca fornitore…",
  loading,
}: {
  suppliers: SupplierLookupItem[]
  value: string
  onValueChange: (companyName: string, supplier: SupplierLookupItem | null) => void
  disabled?: boolean
  placeholder?: string
  loading?: boolean
}) {
  const [query, setQuery] = React.useState(value)
  const [open, setOpen] = React.useState(false)
  const rootRef = React.useRef<HTMLDivElement>(null)

  // Riallinea il testo quando il valore cambia dall'esterno (e non si sta cercando)
  React.useEffect(() => {
    if (!open) setQuery(value)
  }, [value, open])

  const filtered = React.useMemo(() => {
    const q = query.trim().toLowerCase()
    const active = suppliers.filter((s) => s.isActive)
    if (!q) return active.slice(0, MAX_RESULTS)
    return active
      .filter((s) => {
        const haystack = [s.companyName, s.contactName]
          .join(" ")
          .toLowerCase()
        return haystack.includes(q)
      })
      .slice(0, MAX_RESULTS)
  }, [suppliers, query])

  React.useEffect(() => {
    if (!open) return
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener("mousedown", onPointerDown)
    return () => document.removeEventListener("mousedown", onPointerDown)
  }, [open])

  function selectSupplier(supplier: SupplierLookupItem) {
    onValueChange(supplier.companyName, supplier)
    setQuery(supplier.companyName)
    setOpen(false)
  }

  function handleQueryChange(text: string) {
    setQuery(text)
    setOpen(true)
    // Se il testo diverge dal valore selezionato, la selezione decade
    if (value && text !== value) {
      onValueChange("", null)
    }
  }

  return (
    <div ref={rootRef} className="relative w-full">
      <Command shouldFilter={false} className="overflow-visible bg-transparent">
        <CommandInput
          value={query}
          disabled={disabled || loading}
          placeholder={loading ? "Caricamento fornitori…" : placeholder}
          onValueChange={handleQueryChange}
          onFocus={() => setOpen(true)}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault()
              setOpen(false)
            }
          }}
        />

        {open && !disabled && !loading ? (
          <div className="absolute top-[calc(100%+4px)] z-50 w-full overflow-hidden rounded-md border bg-popover shadow-md">
            <CommandList className="max-h-56">
              <CommandEmpty>Nessun fornitore trovato.</CommandEmpty>
              <CommandGroup>
                {filtered.map((supplier) => (
                  <CommandItem
                    key={supplier.id}
                    value={`${supplier.id}-${supplier.companyName}`}
                    className={cn(
                      "flex-col items-start gap-0.5",
                      value === supplier.companyName && "font-medium"
                    )}
                    onSelect={() => selectSupplier(supplier)}
                  >
                    <span>{supplier.companyName}</span>
                    {supplier.contactName ? (
                      <span className="text-xs text-muted-foreground">
                        {supplier.contactName}
                      </span>
                    ) : null}
                  </CommandItem>
                ))}
              </CommandGroup>
            </CommandList>
          </div>
        ) : null}
      </Command>
    </div>
  )
}
