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

/** Menu ⋮ per cambiare rapidamente lo stato di una riga DDP (opzioni da Conf. DDP). */
export function DdpStatusMenu({
  currentStatusKey,
  statuses,
  disabled,
  onSelect,
}: {
  currentStatusKey: string
  statuses: DdpStatusItem[]
  disabled?: boolean
  onSelect: (statusKey: string) => void
}) {
  const options = React.useMemo(
    () =>
      [...statuses]
        .filter((status) => status.isActive)
        .sort(
          (a, b) =>
            a.sortOrder - b.sortOrder ||
            a.label.localeCompare(b.label, "it")
        ),
    [statuses]
  )

  if (options.length === 0) {
    return null
  }

  return (
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
