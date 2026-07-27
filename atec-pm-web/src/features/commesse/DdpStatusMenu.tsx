import * as React from "react"
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
import type { DdpStatusItem } from "@/lib/api/types"

import { filterStatusOptions } from "./ddp-constants"

/**
 * Menu ⋮ per cambiare rapidamente lo stato di una riga DDP (opzioni da Conf. DDP).
 * Con `transitions` (matrice avanzamenti v7) la finestra opzioni si restringe alle sole
 * transizioni ammesse dallo stato corrente; riga senza stato o stato non governato →
 * finestra completa; stato terminale (nessuna uscita) → menu nascosto.
 */
export function DdpStatusMenu({
  currentStatusKey,
  statuses,
  transitions,
  disabled,
  onSelect,
}: {
  currentStatusKey: string
  statuses: DdpStatusItem[]
  transitions?: Record<string, string[]>
  disabled?: boolean
  onSelect: (statusKey: string) => void
}) {
  const options = React.useMemo(
    () =>
      filterStatusOptions(statuses, currentStatusKey, transitions).sort(
        (a, b) =>
          a.sortOrder - b.sortOrder ||
          a.label.localeCompare(b.label, "it")
      ),
    [statuses, transitions, currentStatusKey]
  )

  // Nessuna opzione oltre lo stato corrente (es. terminale ANN/SOST): niente menu.
  if (
    options.every(
      (status) =>
        status.statusKey.toUpperCase() === (currentStatusKey ?? "").toUpperCase()
    )
  ) {
    return null
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          className="size-7 shrink-0 text-current hover:bg-black/10 hover:text-current"
          disabled={disabled}
          onClick={(event) => event.stopPropagation()}
        >
          <MoreVertical />
          <span className="sr-only">Cambia stato</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="max-h-72 w-max min-w-max max-w-[min(28rem,calc(100vw-1rem))] overflow-x-visible overflow-y-auto"
      >
        <DropdownMenuLabel className="whitespace-nowrap">
          Cambia stato
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        {options.map((status) => (
          <DropdownMenuItem
            key={status.statusKey}
            className="gap-2 whitespace-nowrap"
            disabled={status.statusKey === currentStatusKey}
            onClick={() => onSelect(status.statusKey)}
          >
            <span
              className="size-2.5 shrink-0 rounded-full border"
              style={{
                backgroundColor: status.colorBg,
                borderColor: status.colorFg,
              }}
            />
            <span>{status.label}</span>
            {status.statusKey === currentStatusKey ? (
              <Check className="ml-auto size-4 shrink-0 opacity-60" />
            ) : null}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
