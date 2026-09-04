import * as React from "react"
import { Command as CommandPrimitive } from "cmdk"
import { useQueryClient } from "@tanstack/react-query"
import { Check, MoreVertical, Plus, Search } from "lucide-react"

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
import type { DdpDestinationItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { buildDestinationOptions } from "./ddp-destination-options"
import { DdpDestinationFormDialog } from "./DdpDestinationFormDialog"
import { normDdpDestination } from "./ddp-destination-norm"

/**
 * Menu destinazione DDP: trigger «⋮» (stesso stile del menu Stato) che apre un popover
 * di ricerca (Command + «Cerca…») con l'elenco delle destinazioni attive di Conf. DDP.
 * Il pulsante «+» a destra della ricerca apre lo stesso form «Nuova destinazione» della
 * pagina Conf. DDP (Nome + Attivo) e, al salvataggio, seleziona la voce sulla riga.
 */
export function DdpDestinationCombo({
  destination,
  destinations,
  disabled,
  onSelect,
}: {
  destination: string
  destinations: DdpDestinationItem[]
  disabled?: boolean
  onSelect: (destination: string) => void
}) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")
  const [formOpen, setFormOpen] = React.useState(false)
  const [formInitial, setFormInitial] = React.useState("")

  const options = React.useMemo(
    () => buildDestinationOptions(destinations, destination),
    [destinations, destination]
  )

  const term = normDdpDestination(search)
  const visible = React.useMemo(() => {
    if (!term) return options
    return options.filter((name) => normDdpDestination(name).includes(term))
  }, [options, term])

  function closePopover() {
    setOpen(false)
    setSearch("")
  }

  function pick(name: string) {
    onSelect(name)
    closePopover()
  }

  function openNewForm() {
    setFormInitial(search.trim())
    setOpen(false) // chiudo il popover: sopra apro il form modale
    setFormOpen(true)
  }

  return (
    <>
      <Popover
        open={open}
        onOpenChange={(next) => (next ? setOpen(true) : closePopover())}
      >
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="size-7 shrink-0"
            disabled={disabled}
            onClick={(event) => event.stopPropagation()}
          >
            <MoreVertical />
            <span className="sr-only">Scegli destinazione</span>
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="end"
          className="w-64 p-0"
          onClick={(event) => event.stopPropagation()}
        >
          <Command shouldFilter={false}>
            <div className="flex items-center gap-1.5 p-1 pb-0">
              <InputGroup className="h-8! flex-1 rounded-lg! border-input/30 bg-input/30 shadow-none! *:data-[slot=input-group-addon]:pl-2!">
                <CommandPrimitive.Input
                  value={search}
                  onValueChange={setSearch}
                  placeholder="Cerca…"
                  className="w-full text-sm outline-hidden"
                  onKeyDown={(event) => {
                    if (event.key === "Enter" && visible.length === 0 && term) {
                      event.preventDefault()
                      openNewForm()
                    }
                  }}
                />
                <InputGroupAddon>
                  <Search className="size-4 shrink-0 opacity-50" />
                </InputGroupAddon>
              </InputGroup>
              <Button
                type="button"
                size="icon-sm"
                title="Nuova destinazione"
                onClick={openNewForm}
              >
                <Plus className="size-4" />
                <span className="sr-only">Nuova destinazione</span>
              </Button>
            </div>
            <CommandList className="mt-1">
              <CommandEmpty>Nessuna destinazione.</CommandEmpty>
              <CommandGroup>
                {visible.map((name) => (
                  <CommandItem key={name} value={name} onSelect={() => pick(name)}>
                    <Check
                      className={cn(
                        "size-4 shrink-0",
                        name === destination ? "opacity-100" : "opacity-0"
                      )}
                    />
                    <span className="truncate">{name}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
              {(destination ?? "").trim() ? (
                <>
                  <CommandSeparator />
                  <CommandGroup>
                    <CommandItem
                      value="__clear__"
                      onSelect={() => pick("")}
                      className="text-muted-foreground"
                    >
                      Rimuovi destinazione
                    </CommandItem>
                  </CommandGroup>
                </>
              ) : null}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      <DdpDestinationFormDialog
        open={formOpen}
        item={null}
        initialName={formInitial}
        existingNames={destinations.map((d) => d.name).filter(Boolean)}
        onClose={() => setFormOpen(false)}
        onSaved={async (name) => {
          setFormOpen(false)
          await queryClient.invalidateQueries({ queryKey: ["ddp-destinations"] })
          onSelect(name)
        }}
      />
    </>
  )
}
