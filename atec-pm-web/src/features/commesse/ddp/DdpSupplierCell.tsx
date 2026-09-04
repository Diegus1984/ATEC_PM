import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Command as CommandPrimitive } from "cmdk"
import { Check, MoreVertical, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@/components/ui/command"
import { InputGroup, InputGroupAddon } from "@/components/ui/input-group"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { fetchSuppliersLookup } from "@/lib/api/suppliers"
import type { SupplierLookupItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

const MAX_RESULTS = 40

/**
 * Cella «Fornitore»: testo + menu «⋮» (stesso stile della cella Destinazione) che apre
 * un popover di ricerca sull'anagrafica fornitori. L'anagrafica (~2200 voci) si carica
 * solo alla prima apertura e la lista mostra al massimo 40 risultati (pattern
 * SupplierSearchCombobox: MAI una Select con tutta l'anagrafica).
 */
export function DdpSupplierCell({
  supplierId,
  supplierName,
  disabled,
  onSupplierChange,
}: {
  supplierId: number | null
  supplierName: string
  disabled?: boolean
  /** `null` = rimuovi fornitore. */
  onSupplierChange: (supplier: SupplierLookupItem | null) => void
}) {
  const [open, setOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")

  const suppliersQuery = useQuery({
    queryKey: ["suppliers", "lookup"],
    queryFn: fetchSuppliersLookup,
    enabled: open,
  })

  const visible = React.useMemo(() => {
    const active = (suppliersQuery.data ?? []).filter((s) => s.isActive)
    const q = search.trim().toLowerCase()
    if (!q) return active.slice(0, MAX_RESULTS)
    return active
      .filter((s) =>
        [s.companyName, s.contactName]
          .join(" ")
          .toLowerCase()
          .includes(q)
      )
      .slice(0, MAX_RESULTS)
  }, [suppliersQuery.data, search])

  function closePopover() {
    setOpen(false)
    setSearch("")
  }

  function pick(supplier: SupplierLookupItem | null) {
    onSupplierChange(supplier)
    closePopover()
  }

  return (
    <div
      className="flex min-w-[150px] items-center gap-1"
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <span
        className="min-w-0 max-w-[160px] flex-1 truncate whitespace-nowrap"
        title={supplierName || undefined}
      >
        {supplierName || "—"}
      </span>
      <Popover
        open={open}
        onOpenChange={(next) => (next ? setOpen(true) : closePopover())}
      >
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="size-7 shrink-0 text-current hover:bg-black/10 hover:text-current"
            disabled={disabled}
            onClick={(event) => event.stopPropagation()}
          >
            <MoreVertical />
            <span className="sr-only">Scegli fornitore</span>
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="end"
          className="w-72 p-0"
          onClick={(event) => event.stopPropagation()}
        >
          <Command shouldFilter={false}>
            <div className="p-1 pb-0">
              <InputGroup className="h-8! rounded-lg! border-input/30 bg-input/30 shadow-none! *:data-[slot=input-group-addon]:pl-2!">
                <CommandPrimitive.Input
                  value={search}
                  onValueChange={setSearch}
                  placeholder="Cerca fornitore…"
                  className="w-full text-sm outline-hidden"
                />
                <InputGroupAddon>
                  <Search className="size-4 shrink-0 opacity-50" />
                </InputGroupAddon>
              </InputGroup>
            </div>
            <CommandList className="mt-1">
              <CommandEmpty>
                {suppliersQuery.isLoading
                  ? "Caricamento fornitori…"
                  : "Nessun fornitore trovato."}
              </CommandEmpty>
              <CommandGroup>
                {visible.map((supplier) => (
                  <CommandItem
                    key={supplier.id}
                    value={`${supplier.id}-${supplier.companyName}`}
                    onSelect={() => pick(supplier)}
                  >
                    <Check
                      className={cn(
                        "size-4 shrink-0",
                        supplier.id === supplierId ? "opacity-100" : "opacity-0"
                      )}
                    />
                    <div className="min-w-0">
                      <div className="truncate">{supplier.companyName}</div>
                      {supplier.contactName ? (
                        <div className="truncate text-xs text-muted-foreground">
                          {supplier.contactName}
                        </div>
                      ) : null}
                    </div>
                  </CommandItem>
                ))}
              </CommandGroup>
              {supplierId !== null || supplierName ? (
                <>
                  <CommandSeparator />
                  <CommandGroup>
                    <CommandItem
                      value="__clear__"
                      onSelect={() => pick(null)}
                      className="text-muted-foreground"
                    >
                      Rimuovi fornitore
                    </CommandItem>
                  </CommandGroup>
                </>
              ) : null}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
    </div>
  )
}
