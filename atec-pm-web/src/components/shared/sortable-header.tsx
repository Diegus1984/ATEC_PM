import { ArrowDown, ArrowUp, ChevronsUpDown } from "lucide-react"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

export type SortDir = "asc" | "desc"

export interface SortState {
  by: string
  dir: SortDir
}

/**
 * Intestazione di colonna ordinabile (ordinamento **server-side**). Mostra
 * l'icona di stato (neutro / asc / desc) e, al click, chiama `onSort`. Usata
 * dalle liste paginate (Catalogo, Codex). La pagina tiene lo stato `SortState`
 * e lo passa alla query. Vedi BLOCKS-RULES.md.
 */
export function SortableHeader({
  label,
  columnKey,
  sort,
  onSort,
  align,
}: {
  label: string
  columnKey: string
  sort: SortState
  onSort: (next: SortState) => void
  align?: "right"
}) {
  const active = sort.by === columnKey
  const Icon = !active ? ChevronsUpDown : sort.dir === "asc" ? ArrowUp : ArrowDown
  return (
    <Button
      variant="ghost"
      size="sm"
      data-active={active}
      className={cn(
        "h-8 data-[active=true]:text-foreground",
        align === "right" ? "-mr-2" : "-ml-2"
      )}
      onClick={() =>
        onSort({
          by: columnKey,
          dir: active && sort.dir === "asc" ? "desc" : "asc",
        })
      }
    >
      {label}
      <Icon className="ml-1 size-3.5" />
    </Button>
  )
}
