import * as React from "react"
import {
  type ColumnFiltersState,
  type SortingState,
  type VisibilityState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table"
import { FilterX, RefreshCw, Search } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import type { DataTableCardProps } from "@/components/shared/data-table-card"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import "@/components/shared/data-table-column-meta"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { cn } from "@/lib/utils"

export interface DataTableCardFilteredProps<TData>
  extends DataTableCardProps<TData> {
  /** Mostra anche la ricerca globale sopra la tabella. Default `true`. */
  enableGlobalSearch?: boolean
  /** Placeholder dei campi di ricerca per colonna. Default "Filtra…". */
  columnFilterPlaceholder?: string
  /** Stile inline per riga (es. colore di sfondo/stato sull'intera riga). */
  rowStyle?: (row: TData) => React.CSSProperties | undefined
  /** Evidenzia e scrolla alla riga con questo id (`data-row-id` / getRowId). */
  highlightRowId?: string | null
  /** Contenuto tra toolbar e tabella (es. filtri stato a pillole). */
  aboveTable?: React.ReactNode
  /** Filtri esterni attivi (mostra «Pulisci filtri» e li azzera nel callback). */
  externalFiltersActive?: boolean
  onClearExternalFilters?: () => void
}

/**
 * Variante di {@link DataTableCard} con **ricerca per colonna**: sotto la riga
 * di intestazione compare una barra di filtri, una casella per ogni colonna
 * filtrabile (tutte quelle con un accessor — `select`/`actions` restano vuote).
 * Aggiungendo colonne, la rispettiva casella di ricerca appare in automatico.
 *
 * Drop-in di `DataTableCard`: stesse props (basta cambiare l'import). In più
 * mantiene la ricerca globale (disattivabile con `enableGlobalSearch={false}`).
 * Vedi BLOCKS-RULES.md.
 */
export function DataTableCardFiltered<TData>({
  title,
  description,
  columns,
  data,
  columnLabels = {},
  isLoading,
  isFetching,
  error,
  onRefresh,
  searchPlaceholder = "Cerca…",
  rowNoun = "righe",
  emptyMessage = "Nessun elemento.",
  defaultSorting = [],
  initialColumnVisibility = {},
  getRowId,
  toolbarActions,
  onRowDoubleClick,
  enableGlobalSearch = true,
  columnFilterPlaceholder = "Filtra…",
  rowStyle,
  highlightRowId,
  aboveTable,
  externalFiltersActive = false,
  onClearExternalFilters,
}: DataTableCardFilteredProps<TData>) {
  const [sorting, setSorting] = React.useState<SortingState>(defaultSorting)
  const [columnVisibility, setColumnVisibility] =
    React.useState<VisibilityState>(initialColumnVisibility)
  const [columnFilters, setColumnFilters] = React.useState<ColumnFiltersState>(
    []
  )
  const [rowSelection, setRowSelection] = React.useState({})
  const [globalFilter, setGlobalFilter] = React.useState("")

  const table = useReactTable({
    data: data ?? [],
    columns,
    state: {
      sorting,
      columnVisibility,
      columnFilters,
      rowSelection,
      globalFilter,
    },
    onSortingChange: setSorting,
    onColumnVisibilityChange: setColumnVisibility,
    onColumnFiltersChange: setColumnFilters,
    onRowSelectionChange: setRowSelection,
    onGlobalFilterChange: setGlobalFilter,
    globalFilterFn: "includesString",
    getRowId,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  })

  const selectedCount = Object.keys(rowSelection).length
  const totalRows = data?.length ?? 0
  const filteredRows = table.getFilteredRowModel().rows.length
  const labelOf = (id: string) => columnLabels[id] ?? id
  const hasColumnFilters = columnFilters.length > 0
  const hasAnyFilters = hasColumnFilters || externalFiltersActive || !!globalFilter

  function clearAllFilters() {
    setColumnFilters([])
    setGlobalFilter("")
    onClearExternalFilters?.()
  }

  React.useEffect(() => {
    if (!highlightRowId || isLoading) {
      return
    }
    const timer = window.setTimeout(() => {
      const row = document.querySelector(
        `[data-row-id="${CSS.escape(highlightRowId)}"]`
      )
      row?.scrollIntoView({ block: "center", behavior: "smooth" })
    }, 150)
    return () => window.clearTimeout(timer)
  }, [highlightRowId, isLoading, data])

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>{title}</CardTitle>
              {description ? (
                <CardDescription>
                  {description}
                  {data ? ` — ${totalRows} totali` : ""}
                </CardDescription>
              ) : null}
            </div>
            {onRefresh ? (
              <Button
                variant="outline"
                size="sm"
                onClick={onRefresh}
                disabled={isFetching}
              >
                <RefreshCw className={isFetching ? "animate-spin" : ""} />
                Aggiorna
              </Button>
            ) : null}
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            {enableGlobalSearch ? (
              <div className="relative max-w-sm flex-1">
                <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
                <Input
                  value={globalFilter}
                  placeholder={searchPlaceholder}
                  className="pl-8"
                  onChange={(event) => setGlobalFilter(event.target.value)}
                />
              </div>
            ) : null}
            <div className="ml-auto flex gap-2">
              {hasAnyFilters ? (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={clearAllFilters}
                >
                  <FilterX />
                  Pulisci filtri
                </Button>
              ) : null}
              <ColumnsMenu
                columns={table
                  .getAllColumns()
                  .filter((column) => column.getCanHide())
                  .map((column) => ({
                    id: column.id,
                    label: labelOf(column.id),
                    checked: column.getIsVisible(),
                    onToggle: (value) => column.toggleVisibility(value),
                  }))}
              />
              {toolbarActions}
            </div>
          </div>

          {error ? <PageErrorAlert message={error.message} /> : null}

          {aboveTable ? <div className="overflow-x-auto">{aboveTable}</div> : null}

          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                {table.getHeaderGroups().map((headerGroup) => (
                  <React.Fragment key={headerGroup.id}>
                    <TableRow className="hover:bg-transparent">
                      {headerGroup.headers.map((header) => (
                        <TableHead key={header.id}>
                          {header.isPlaceholder
                            ? null
                            : flexRender(
                                header.column.columnDef.header,
                                header.getContext()
                              )}
                        </TableHead>
                      ))}
                    </TableRow>
                    <TableRow className="hover:bg-transparent">
                      {headerGroup.headers.map((header) => (
                        <TableHead
                          key={header.id}
                          className="h-auto px-2 py-2 align-middle"
                        >
                          {header.column.getCanFilter() ? (
                            header.column.columnDef.meta?.filterInput ? (
                              header.column.columnDef.meta.filterInput({
                                value: header.column.getFilterValue(),
                                onChange: (value) =>
                                  header.column.setFilterValue(value),
                              })
                            ) : (
                              <Input
                                value={
                                  (header.column.getFilterValue() as string) ??
                                  ""
                                }
                                placeholder={columnFilterPlaceholder}
                                className="h-8 bg-background dark:bg-background"
                                onChange={(event) =>
                                  header.column.setFilterValue(
                                    event.target.value
                                  )
                                }
                              />
                            )
                          ) : null}
                        </TableHead>
                      ))}
                    </TableRow>
                  </React.Fragment>
                ))}
              </TableHeader>
              <TableBody>
                {isLoading ? (
                  <TableRow>
                    <TableCell
                      colSpan={columns.length}
                      className="h-24 text-center text-muted-foreground"
                    >
                      Caricamento…
                    </TableCell>
                  </TableRow>
                ) : table.getRowModel().rows.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={columns.length}
                      className="h-24 text-center text-muted-foreground"
                    >
                      {hasAnyFilters
                        ? "Nessun risultato per i filtri impostati."
                        : emptyMessage}
                    </TableCell>
                  </TableRow>
                ) : (
                  table.getRowModel().rows.map((row) => {
                    const rowId = getRowId?.(row.original) ?? row.id
                    const isHighlighted =
                      highlightRowId != null && rowId === highlightRowId
                    return (
                    <TableRow
                      key={row.id}
                      data-row-id={rowId}
                      data-state={row.getIsSelected() && "selected"}
                      style={rowStyle ? rowStyle(row.original) : undefined}
                      className={cn(
                        onRowDoubleClick && "cursor-pointer",
                        rowStyle && "border-b-0",
                        isHighlighted && "ring-2 ring-inset ring-destructive"
                      )}
                      onDoubleClick={
                        onRowDoubleClick
                          ? () => onRowDoubleClick(row.original)
                          : undefined
                      }
                    >
                      {row.getVisibleCells().map((cell) => (
                        <TableCell key={cell.id}>
                          {flexRender(
                            cell.column.columnDef.cell,
                            cell.getContext()
                          )}
                        </TableCell>
                      ))}
                    </TableRow>
                    )
                  })
                )}
              </TableBody>
            </Table>
          </div>

          <div className="flex items-center justify-between text-sm text-muted-foreground">
            <span>
              {selectedCount > 0
                ? `${selectedCount} di ${filteredRows} righe selezionate`
                : `${filteredRows} ${rowNoun}`}
            </span>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
