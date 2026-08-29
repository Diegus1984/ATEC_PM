import { Check, MoreVertical } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"

import { WORK_TYPE_META } from "./ddp-work-type"

/**
 * Cella «Tipo»: natura della lavorazione officina, interna (costruita in ATEC) o
 * esterna (fornitore), con menu «⋮» come Stato / Trattamento / Fornitore.
 *
 * Il valore lo popola da solo il sistema (dalla lavorazione collegata o dallo stato
 * DDP), ma le righe già chiuse quando è stata introdotta la colonna restano «non
 * classificate» e vanno sistemate da qui: sono un costo che il Bilancio mostra a
 * parte finché nessuno dice di che natura sia.
 */
export function DdpWorkTypeCell({
  workType,
  disabled,
  onWorkTypeChange,
}: {
  workType: string
  disabled?: boolean
  onWorkTypeChange: (workType: string) => void
}) {
  const current = WORK_TYPE_META.find((t) => t.value === workType)

  return (
    <div
      className="flex min-w-[120px] items-center gap-1"
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <span className="flex min-w-0 flex-1 items-center gap-1.5">
        {current ? (
          <>
            <span className={cn("size-2 shrink-0 rounded-full", current.dot)} />
            <span className="truncate">{current.label}</span>
          </>
        ) : (
          <span className="text-muted-foreground">—</span>
        )}
      </span>

      <DropdownMenu>
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
            <span className="sr-only">Scegli tipo lavorazione</span>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent
          align="end"
          className="w-48"
          onClick={(event) => event.stopPropagation()}
        >
          <DropdownMenuLabel>Tipo lavorazione</DropdownMenuLabel>
          <DropdownMenuSeparator />
          {WORK_TYPE_META.map((option) => (
            <DropdownMenuItem
              key={option.value}
              className="gap-2"
              onClick={() => onWorkTypeChange(option.value)}
            >
              <Check
                className={cn(
                  "size-4 shrink-0",
                  option.value === workType ? "opacity-100" : "opacity-0"
                )}
              />
              <span className={cn("size-2 shrink-0 rounded-full", option.dot)} />
              <span>{option.label}</span>
            </DropdownMenuItem>
          ))}
          <DropdownMenuSeparator />
          <DropdownMenuItem
            className="gap-2 text-muted-foreground"
            onClick={() => onWorkTypeChange("")}
          >
            <Check
              className={cn(
                "size-4 shrink-0",
                !workType ? "opacity-100" : "opacity-0"
              )}
            />
            <span className="size-2 shrink-0 rounded-full border border-muted-foreground/50" />
            <span>— (Non definita)</span>
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}
