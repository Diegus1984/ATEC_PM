import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import { cn } from "@/lib/utils"

export interface ServerPaginationProps {
  page: number
  totalPages: number
  totalCount: number
  /** Etichetta contatore plurale, es. "articoli". */
  itemNoun?: string
  /** Testo quando non ci sono elementi. */
  emptyLabel?: string
  disabled?: boolean
  onPageChange: (page: number) => void
}

/** Footer paginazione server-side (Codex, Catalogo, …). */
export function ServerPagination({
  page,
  totalPages,
  totalCount,
  itemNoun = "elementi",
  emptyLabel = "Nessun elemento",
  disabled = false,
  onPageChange,
}: ServerPaginationProps) {
  const atStart = page <= 1
  const atEnd = page >= totalPages

  return (
    <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
      <span>
        {totalCount > 0
          ? `Pagina ${page} di ${totalPages} — ${totalCount} ${itemNoun}`
          : emptyLabel}
      </span>
      <Pagination className="mx-0 w-auto justify-end">
        <PaginationContent>
          <PaginationItem>
            <PaginationPrevious
              text="Precedente"
              href="#"
              aria-disabled={atStart || disabled}
              className={cn((atStart || disabled) && "pointer-events-none opacity-50")}
              onClick={(event) => {
                event.preventDefault()
                if (!atStart && !disabled) onPageChange(page - 1)
              }}
            />
          </PaginationItem>
          <PaginationItem>
            <PaginationNext
              text="Successiva"
              href="#"
              aria-disabled={atEnd || disabled}
              className={cn((atEnd || disabled) && "pointer-events-none opacity-50")}
              onClick={(event) => {
                event.preventDefault()
                if (!atEnd && !disabled) onPageChange(page + 1)
              }}
            />
          </PaginationItem>
        </PaginationContent>
      </Pagination>
    </div>
  )
}
