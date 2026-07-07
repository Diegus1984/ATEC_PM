import * as React from "react"
import {
  type ColumnDef,
  type SortingState,
  type VisibilityState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table"
import { RefreshCw, Search } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
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

export interface DataTableCardProps<TData> {
  title: string
  description?: string
  columns: ColumnDef<TData>[]
  data: TData[] | undefined
  /** Etichette per il menu «Colonne», per id colonna. */
  columnLabels?: Record<string, string>
  isLoading?: boolean
  isFetching?: boolean
  error?: Error | null
  onRefresh?: () => void
  searchPlaceholder?: string
  /** Sostantivo per il footer/contatore (es. "clienti"). */
  rowNoun?: string
  emptyMessage?: string
  defaultSorting?: SortingState
  initialColumnVisibility?: VisibilityState
  getRowId?: (row: TData) => string
  /** Pulsanti extra nella toolbar a destra (es. «Aggiungi»). */
  toolbarActions?: React.ReactNode
  onRowDoubleClick?: (row: TData) => void
}

/**
 * DataTable standard ATEC PM (pattern dashboard-01): header `bg-muted/50`,
 * ordinamento per colonna (definito nei columnDef), ricerca globale, menu
 * «Colonne» (visibilità), selezione righe, stati loading/vuoto/errore.
 * Le pagine forniscono solo `columns` + `data` + azioni toolbar/dialog.
 * Vedi BLOCKS-RULES.md.
 */
export function DataTableCard<TData>({
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
}: DataTableCardProps<TData>) {
  const [sorting, setSorting] = React.useState<SortingState>(defaultSorting)
  const [columnVisibility, setColumnVisibility] =
    React.useState<VisibilityState>(initialColumnVisibility)
  const [rowSelection, setRowSelection] = React.useState({})
  const [globalFilter, setGlobalFilter] = React.useState("")

  const table = useReactTable({
    data: data ?? [],
    columns,
    state: { sorting, columnVisibility, rowSelection, globalFilter },
    onSortingChange: setSorting,
    onColumnVisibilityChange: setColumnVisibility,
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
            <div className="relative max-w-sm flex-1">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                value={globalFilter}
                placeholder={searchPlaceholder}
                className="pl-8"
                onChange={(event) => setGlobalFilter(event.target.value)}
              />
            </div>
            <div className="ml-auto flex gap-2">
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

          <div className="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                {table.getHeaderGroups().map((headerGroup) => (
                  <TableRow key={headerGroup.id}>
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
                      {emptyMessage}
                    </TableCell>
                  </TableRow>
                ) : (
                  table.getRowModel().rows.map((row) => (
                    <TableRow
                      key={row.id}
                      data-state={row.getIsSelected() && "selected"}
                      className={onRowDoubleClick ? "cursor-pointer" : undefined}
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
                  ))
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
