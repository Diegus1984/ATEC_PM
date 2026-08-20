import { ChevronDown, Columns3, type LucideIcon } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"

export interface ColumnToggle {
  /** Id univoco della colonna. */
  id: string
  /** Etichetta mostrata nel menu. */
  label: string
  checked: boolean
  onToggle: (checked: boolean) => void
}

/**
 * Menu «Colonne» unico per tutto ATEC PM: pulsante outline + dropdown di
 * checkbox per la visibilità colonne. Master per DataTableCard, liste paginate
 * custom (Codex) e tabelle dashboard — il pannello si adatta SEMPRE alla voce
 * più larga (`w-auto`) con spazio per la spunta, senza andare a capo. Le pagine
 * passano solo la lista `columns` (id, label, checked, onToggle). Vedi BLOCKS-RULES.md.
 *
 * Stesso pezzo anche per filtri a spunte (es. stati delle Segnalazioni): si cambia
 * solo etichetta e icona, la scelta resta quella dell'utente.
 */
export function ColumnsMenu({
  columns,
  triggerLabel = "Colonne",
  menuLabel = "Mostra colonne",
  icon: Icon = Columns3,
  align = "end",
  className,
  onToggleAll,
}: {
  columns: ColumnToggle[]
  triggerLabel?: string
  menuLabel?: string
  /** Icona a sinistra dell'etichetta (default: colonne). */
  icon?: LucideIcon
  align?: "start" | "center" | "end"
  className?: string
  /** Handler batch opzionale per «Seleziona/Deseleziona tutti». */
  onToggleAll?: (checked: boolean) => void
}) {
  if (columns.length === 0) {
    return null
  }

  const allChecked = columns.every((column) => column.checked)

  const handleToggleAll = () => {
    const nextChecked = !allChecked
    if (onToggleAll) {
      onToggleAll(nextChecked)
      return
    }
    columns.forEach((column) => {
      if (column.checked !== nextChecked) {
        column.onToggle(nextChecked)
      }
    })
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className={cn("justify-between gap-2", className)}>
          <span className="flex items-center gap-1.5">
            <Icon className="size-4" />
            {triggerLabel}
          </span>
          <ChevronDown className="size-4 opacity-50" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align={align}
        className="max-h-80 w-auto min-w-48 overflow-y-auto whitespace-nowrap"
      >
        <div className="flex items-center justify-between gap-3 px-2 py-1.5">
          <DropdownMenuLabel className="p-0">{menuLabel}</DropdownMenuLabel>
          <button
            type="button"
            className="shrink-0 text-xs text-muted-foreground hover:text-foreground"
            onClick={handleToggleAll}
          >
            {allChecked ? "Deseleziona tutti" : "Seleziona tutti"}
          </button>
        </div>
        <DropdownMenuSeparator />
        {columns.map((column) => (
          <DropdownMenuCheckboxItem
            key={column.id}
            checked={column.checked}
            onCheckedChange={(value) => column.onToggle(!!value)}
            onSelect={(event) => event.preventDefault()}
          >
            {column.label}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
