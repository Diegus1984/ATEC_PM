import type { MouseEvent } from "react"
import type { Table } from "@tanstack/react-table"
import { ChevronsLeft, ChevronsRight } from "lucide-react"

import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { cn } from "@/lib/utils"

export interface TablePaginationProps<TData> {
  table: Table<TData>
  rowNoun?: string
  pageSizeOptions?: number[]
}

/** Footer paginazione client-side (TanStack Table). */
export function TablePagination<TData>({
  table,
  rowNoun = "righe",
  pageSizeOptions = [5, 10, 20],
}: TablePaginationProps<TData>) {
  const pageIndex = table.getState().pagination.pageIndex
  const pageCount = Math.max(table.getPageCount(), 1)
  const atStart = !table.getCanPreviousPage()
  const atEnd = !table.getCanNextPage()
  const filteredCount = table.getFilteredRowModel().rows.length

  const goTo = (index: number) => (event: MouseEvent<HTMLAnchorElement>) => {
    event.preventDefault()
    table.setPageIndex(index)
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-4">
      <p className="text-sm text-muted-foreground">
        {filteredCount} {rowNoun}
      </p>
      <div className="flex flex-wrap items-center gap-6 lg:gap-8">
        <div className="hidden items-center gap-2 lg:flex">
          <span className="text-sm text-muted-foreground">Righe per pagina</span>
          <Select
            value={`${table.getState().pagination.pageSize}`}
            onValueChange={(value) => table.setPageSize(Number(value))}
          >
            <SelectTrigger size="sm" className="w-20">
              <SelectValue />
            </SelectTrigger>
            <SelectContent side="top">
              {pageSizeOptions.map((pageSize) => (
                <SelectItem key={pageSize} value={`${pageSize}`}>
                  {pageSize}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="text-sm font-medium">
          Pagina {pageIndex + 1} di {pageCount}
        </div>
        <Pagination className="mx-0 w-auto">
          <PaginationContent>
            <PaginationItem className="hidden lg:list-item">
              <PaginationLink
                href="#"
                size="icon"
                aria-label="Prima pagina"
                aria-disabled={atStart}
                className={cn(atStart && "pointer-events-none opacity-50")}
                onClick={goTo(0)}
              >
                <ChevronsLeft />
              </PaginationLink>
            </PaginationItem>
            <PaginationItem>
              <PaginationPrevious
                href="#"
                aria-disabled={atStart}
                className={cn(atStart && "pointer-events-none opacity-50")}
                onClick={(event) => {
                  event.preventDefault()
                  if (!atStart) table.previousPage()
                }}
              />
            </PaginationItem>
            <PaginationItem>
              <PaginationNext
                href="#"
                aria-disabled={atEnd}
                className={cn(atEnd && "pointer-events-none opacity-50")}
                onClick={(event) => {
                  event.preventDefault()
                  if (!atEnd) table.nextPage()
                }}
              />
            </PaginationItem>
            <PaginationItem className="hidden lg:list-item">
              <PaginationLink
                href="#"
                size="icon"
                aria-label="Ultima pagina"
                aria-disabled={atEnd}
                className={cn(atEnd && "pointer-events-none opacity-50")}
                onClick={goTo(pageCount - 1)}
              >
                <ChevronsRight />
              </PaginationLink>
            </PaginationItem>
          </PaginationContent>
        </Pagination>
      </div>
    </div>
  )
}
