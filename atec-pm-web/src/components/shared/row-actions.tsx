import * as React from "react"
import { MoreVertical, type LucideIcon } from "lucide-react"

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

export interface RowAction {
  label: string
  icon?: LucideIcon
  onClick: () => void
  /** Voce distruttiva (rossa). */
  destructive?: boolean
  /** Inserisce un separatore prima di questa voce. */
  separatorBefore?: boolean
  disabled?: boolean
}

/**
 * Menu azioni di riga unificato: un solo pulsante «⋮» a fine riga che raccoglie
 * tutte le azioni (modifica, elimina, ecc.). Standard per ogni lista/albero —
 * vedi BLOCKS-RULES.md. NON mettere cluster di icone in-linea.
 */
export function RowActionsMenu({
  actions,
  label,
  triggerClassName,
  size = "icon",
}: {
  actions: RowAction[]
  label?: string
  triggerClassName?: string
  size?: "icon" | "icon-sm"
}) {
  if (actions.length === 0) {
    return null
  }
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size={size}
          className={cn(triggerClassName)}
        >
          <MoreVertical />
          <span className="sr-only">Azioni</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {label ? (
          <>
            <DropdownMenuLabel className="max-w-56 truncate">
              {label}
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
          </>
        ) : null}
        {actions.map((action, index) => {
          const Icon = action.icon
          return (
            <React.Fragment key={`${action.label}-${index}`}>
              {action.separatorBefore ? <DropdownMenuSeparator /> : null}
              <DropdownMenuItem
                disabled={action.disabled}
                variant={action.destructive ? "destructive" : "default"}
                onClick={action.onClick}
              >
                {Icon ? <Icon /> : null}
                {action.label}
              </DropdownMenuItem>
            </React.Fragment>
          )
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
