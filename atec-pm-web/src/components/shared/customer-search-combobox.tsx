import * as React from "react"

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
import type { CustomerListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

const MAX_RESULTS = 40

export function CustomerSearchCombobox({
  customers,
  value,
  onValueChange,
  disabled,
  placeholder = "Cerca cliente…",
  loading,
}: {
  customers: CustomerListItem[]
  value: number | null
  onValueChange: (id: number | null) => void
  disabled?: boolean
  placeholder?: string
  loading?: boolean
}) {
  const [query, setQuery] = React.useState("")
  const [open, setOpen] = React.useState(false)
  const rootRef = React.useRef<HTMLDivElement>(null)

  const selected = customers.find((c) => c.id === value)

  React.useEffect(() => {
    if (value == null) {
      if (!open) setQuery("")
      return
    }
    if (selected) setQuery(selected.companyName)
  }, [value, selected, open])

  const filtered = React.useMemo(() => {
    const q = query.trim().toLowerCase()
    const active = customers.filter((c) => c.isActive)
    if (!q) return active.slice(0, MAX_RESULTS)
    return active
      .filter((c) => {
        const haystack = [
          c.companyName,
          c.contactName,
          c.vatNumber,
          c.email,
        ]
          .join(" ")
          .toLowerCase()
        return haystack.includes(q)
      })
      .slice(0, MAX_RESULTS)
  }, [customers, query])

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

  function selectCustomer(customer: CustomerListItem) {
    onValueChange(customer.id)
    setQuery(customer.companyName)
    setOpen(false)
  }

  function handleQueryChange(text: string) {
    setQuery(text)
    setOpen(true)
    if (value != null && selected && selected.companyName !== text) {
      onValueChange(null)
    }
  }

  return (
    <div ref={rootRef} className="relative w-full">
      <Command shouldFilter={false} className="overflow-visible bg-transparent">
        <CommandInput
          value={query}
          disabled={disabled || loading}
          placeholder={loading ? "Caricamento clienti…" : placeholder}
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
              <CommandEmpty>Nessun cliente trovato.</CommandEmpty>
              <CommandGroup>
                {filtered.map((customer) => (
                  <CommandItem
                    key={customer.id}
                    value={`${customer.id}-${customer.companyName}`}
                    className={cn(
                      "flex-col items-start gap-0.5",
                      value === customer.id && "font-medium"
                    )}
                    onSelect={() => selectCustomer(customer)}
                  >
                    <span>{customer.companyName}</span>
                    {customer.contactName ? (
                      <span className="text-xs text-muted-foreground">
                        {customer.contactName}
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
