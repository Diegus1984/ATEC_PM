import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, MoreVertical, Plus, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { fetchActiveDdpTreatments } from "@/lib/api/ddp-config"
import type { DdpTreatmentItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { DdpTreatmentFormDialog } from "./DdpTreatmentFormDialog"
import { normDdpTreatment } from "./ddp-treatment-norm"

function treatmentName(item: DdpTreatmentItem): string {
  const raw =
    item.name ??
    (item as unknown as { Name?: string }).Name ??
    ""
  return String(raw).trim()
}

/**
 * Menu «⋮» trattamento (DropdownMenu). Carica l'anagrafica all'apertura
 * con la stessa API usata da Config. DDP.
 */
export function DdpTreatmentCombo({
  treatment,
  treatments: treatmentsProp,
  disabled,
  onSelect,
}: {
  treatment: string
  treatments: DdpTreatmentItem[]
  disabled?: boolean
  onSelect: (treatment: string) => void
}) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")
  const [formOpen, setFormOpen] = React.useState(false)
  const [formInitial, setFormInitial] = React.useState("")

  const treatmentsQuery = useQuery({
    // Stessa chiave della Config: condivide la cache quando l'utente ha già aperto Conf. DDP.
    queryKey: ["ddp-treatments", "selectable"],
    queryFn: fetchActiveDdpTreatments,
    enabled: open,
    staleTime: 30_000,
  })

  const treatments = React.useMemo(() => {
    if (treatmentsQuery.data && treatmentsQuery.data.length > 0) {
      return treatmentsQuery.data
    }
    if (treatmentsProp.length > 0) return treatmentsProp
    return treatmentsQuery.data ?? treatmentsProp
  }, [treatmentsQuery.data, treatmentsProp])

  const options = React.useMemo(() => {
    const names = treatments
      .map(treatmentName)
      .filter((name) => name.length > 0)
    const current = (treatment ?? "").trim()
    if (
      current &&
      !names.some((name) => name.toUpperCase() === current.toUpperCase())
    ) {
      names.unshift(current)
    }
    return names.sort((a, b) => a.localeCompare(b, "it"))
  }, [treatments, treatment])

  const term = normDdpTreatment(search)
  const visible = React.useMemo(() => {
    if (!term) return options
    return options.filter((name) => normDdpTreatment(name).includes(term))
  }, [options, term])

  function pick(name: string) {
    onSelect(name)
    setOpen(false)
    setSearch("")
  }

  function openNewForm() {
    setFormInitial(search.trim())
    setOpen(false)
    setSearch("")
    setFormOpen(true)
  }

  return (
    <>
      <DropdownMenu
        open={open}
        onOpenChange={(next) => {
          setOpen(next)
          if (!next) setSearch("")
        }}
      >
        <DropdownMenuTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="size-7 shrink-0"
            disabled={disabled}
            onClick={(event) => event.stopPropagation()}
          >
            <MoreVertical />
            <span className="sr-only">Scegli trattamento</span>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent
          align="end"
          className="w-64 p-0"
          onClick={(event) => event.stopPropagation()}
          onCloseAutoFocus={(event) => event.preventDefault()}
        >
          <div className="flex items-center gap-1.5 border-b p-2">
            <div className="relative min-w-0 flex-1">
              <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Cerca…"
                className="h-8 pl-7"
                onPointerDown={(event) => event.stopPropagation()}
                onKeyDown={(event) => {
                  event.stopPropagation()
                  if (event.key === "Enter" && visible.length === 0 && term) {
                    event.preventDefault()
                    openNewForm()
                  }
                }}
              />
            </div>
            <Button
              type="button"
              size="icon-sm"
              title="Nuovo trattamento"
              onClick={(event) => {
                event.preventDefault()
                event.stopPropagation()
                openNewForm()
              }}
            >
              <Plus className="size-4" />
              <span className="sr-only">Nuovo trattamento</span>
            </Button>
          </div>

          <DropdownMenuLabel className="px-2 py-1.5">
            Trattamento
          </DropdownMenuLabel>
          <DropdownMenuSeparator />

          <div className="max-h-64 overflow-y-auto p-1">
            {treatmentsQuery.isFetching && options.length === 0 ? (
              <div className="px-2 py-3 text-center text-sm text-muted-foreground">
                Caricamento…
              </div>
            ) : treatmentsQuery.isError && options.length === 0 ? (
              <div className="px-2 py-3 text-center text-sm text-destructive">
                Errore caricamento trattamenti.
              </div>
            ) : visible.length === 0 ? (
              <div className="px-2 py-3 text-center text-sm text-muted-foreground">
                Nessun trattamento.
                {term ? " Premi Invio o + per crearne uno." : ""}
              </div>
            ) : (
              visible.map((name) => (
                <DropdownMenuItem
                  key={name}
                  className="gap-2"
                  onClick={() => pick(name)}
                >
                  <Check
                    className={cn(
                      "size-4 shrink-0",
                      name === treatment ? "opacity-100" : "opacity-0"
                    )}
                  />
                  <span className="truncate">{name}</span>
                </DropdownMenuItem>
              ))
            )}
          </div>

          {(treatment ?? "").trim() ? (
            <>
              <DropdownMenuSeparator />
              <div className="p-1">
                <DropdownMenuItem
                  className="text-muted-foreground"
                  onClick={() => pick("")}
                >
                  Rimuovi trattamento
                </DropdownMenuItem>
              </div>
            </>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>

      <DdpTreatmentFormDialog
        open={formOpen}
        item={null}
        initialName={formInitial}
        existingNames={treatments.map(treatmentName).filter(Boolean)}
        onClose={() => setFormOpen(false)}
        onSaved={async (name) => {
          setFormOpen(false)
          await queryClient.invalidateQueries({ queryKey: ["ddp-treatments"] })
          await queryClient.invalidateQueries({
            queryKey: ["ddp-treatments", "selectable"],
          })
          onSelect(name)
        }}
      />
    </>
  )
}
