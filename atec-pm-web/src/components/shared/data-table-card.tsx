import * as React from "react"
import {
  type ColumnDef,
  type SortingState,
  type VisibilityState,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table"
import { RefreshCw, Search } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { GridScroller } from "@/components/shared/grid-scroller"
import { renderColumnDef } from "@/components/shared/render-column-def"
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
import { cn } from "@/lib/utils"

export interface DataTableCardProps<TData> {
  title?: string
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
  toolbarActions?: React.ReactNode | ((rows: TData[]) => React.ReactNode)
  onRowDoubleClick?: (row: TData) => void
  /** Nasconde CardHeader (es. pagina con titolo esterno). */
  embedded?: boolean
  /** Contenuto tra toolbar e tabella (es. riepilogo o banner interno). */
  aboveTable?: React.ReactNode
  /** Classi Tailwind per riga (es. tinta stato SAL). */
  rowClassName?: (row: TData) => string | undefined
  /** Chiave localStorage per salvare la visibilità delle colonne. Se non fornita, viene generata dal titolo. */
  visibilityStorageKey?: string
  /**
   * Classi dello scroller della griglia, per cambiarne l'altezza rispetto al
   * default `--grid-max-h` (es. `max-h-[40vh]`).
   */
  scrollAreaClassName?: string
  /** Intestazione fissa mentre le righe scorrono. Default `true`. */
  stickyHeader?: boolean
  /** Barra di scorrimento orizzontale SOPRA la griglia invece che sotto. Default `true`. */
  topScrollbar?: boolean
  /** Renderizzatore opzionale per il contenuto di una riga espansa (sotto la riga principale). */
  renderExpandedRow?: (row: TData) => React.ReactNode
  /**
   * Righe di separazione verticali **e** orizzontali, come un foglio di calcolo.
   * Default `false`: si accende sulle griglie da compilare cella per cella (le DDP),
   * dove seguire la riga giusta su venti colonne a occhio nudo è la fatica vera.
   */
  gridLines?: boolean
}

/**
 * Reticolo della griglia (segnalazione #58, 10/08/2026 — «righe di separazione sia per
 * colonne che per righe» nelle DDP).
 * <p>
 * Il colore è <b>currentColor</b>, non un grigio fisso, ed è l'unica scelta che regge qui:
 * le righe delle DDP hanno lo sfondo dello stato (inline, dallo stesso valore in tema chiaro
 * e scuro), quindi una tinta fissa sparirebbe su metà degli stati. Prendendo il colore del
 * testo della riga — scelto apposta per contrastare con quello sfondo — la linea si vede su
 * tutti.
 * <p>
 * L'ultimo `border-b` riaccende la linea orizzontale che le righe colorate spengono
 * (`rowStyle && "border-b-0"`): il selettore discendente vince sulla classe della riga.
 */
const GRID_LINES_CLASS =
  "[&_th]:border-r [&_th]:border-current/20 [&_th:last-child]:border-r-0 " +
  "[&_td]:border-r [&_td]:border-current/20 [&_td:last-child]:border-r-0 " +
  "[&_tbody_tr]:border-b [&_tbody_tr]:border-current/20"

export { GRID_LINES_CLASS }

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
  embedded = false,
  aboveTable,
  rowClassName,
  visibilityStorageKey,
  scrollAreaClassName,
  stickyHeader = true,
  topScrollbar = true,
  renderExpandedRow,
  gridLines = false,
}: DataTableCardProps<TData>) {
  const [sorting, setSorting] = React.useState<SortingState>(defaultSorting)

  const storageKey = React.useMemo(() => {
    if (visibilityStorageKey) return visibilityStorageKey
    if (title) return `table-visibility-${title.toLowerCase().replace(/\s+/g, "-")}`
    return null
  }, [visibilityStorageKey, title])

  const [columnVisibility, setColumnVisibility] =
    React.useState<VisibilityState>(() => {
      if (storageKey) {
        try {
          const saved = localStorage.getItem(storageKey)
          if (saved) {
            return JSON.parse(saved)
          }
        } catch (e) {
          console.error("Failed to load column visibility from localStorage", e)
        }
      }
      return initialColumnVisibility
    })

  const [rowSelection, setRowSelection] = React.useState({})
  const [globalFilter, setGlobalFilter] = React.useState("")

  React.useEffect(() => {
    if (storageKey) {
      try {
        localStorage.setItem(storageKey, JSON.stringify(columnVisibility))
      } catch (e) {
        console.error("Failed to save column visibility to localStorage", e)
      }
    }
  }, [storageKey, columnVisibility])

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
  const visibleRows = table.getRowModel().rows.map((row) => row.original)
  const resolvedToolbarActions =
    typeof toolbarActions === "function"
      ? toolbarActions(visibleRows)
      : toolbarActions

  return (
    <div className="space-y-4">
      <Card>
        {!embedded && (title || description || onRefresh) ? (
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                {title ? <CardTitle>{title}</CardTitle> : null}
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
        ) : null}
        <CardContent className={cn("space-y-4", embedded && "pt-6")}>
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
              {resolvedToolbarActions}
            </div>
          </div>

          {error ? <PageErrorAlert message={error.message} /> : null}

          {aboveTable ? <div className="overflow-x-auto">{aboveTable}</div> : null}

          <GridScroller
            className="rounded-lg border"
            scrollerClassName={scrollAreaClassName}
            stickyHeader={stickyHeader}
            topScrollbar={topScrollbar}
          >
            <Table className={cn(gridLines && GRID_LINES_CLASS)}>
              <TableHeader className="bg-muted/50">
                {table.getHeaderGroups().map((headerGroup) => (
                  <TableRow key={headerGroup.id}>
                    {headerGroup.headers.map((header) => (
                      <TableHead key={header.id}>
                        {header.isPlaceholder
                          ? null
                          : renderColumnDef(
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
                  table.getRowModel().rows.map((row) => {
                    const expandedContent = renderExpandedRow?.(row.original)
                    return (
                      <React.Fragment key={row.id}>
                        <TableRow
                          data-state={row.getIsSelected() && "selected"}
                          className={cn(
                            onRowDoubleClick && "cursor-pointer",
                            rowClassName?.(row.original)
                          )}
                          onDoubleClick={
                            onRowDoubleClick
                              ? () => onRowDoubleClick(row.original)
                              : undefined
                          }
                        >
                          {row.getVisibleCells().map((cell) => (
                            <TableCell key={cell.id}>
                              {renderColumnDef(
                                cell.column.columnDef.cell,
                                cell.getContext()
                              )}
                            </TableCell>
                          ))}
                        </TableRow>
                        {expandedContent ? (
                          <TableRow className="bg-muted/10 hover:bg-muted/10 border-b">
                            <TableCell
                              colSpan={table.getVisibleLeafColumns().length}
                              className="p-3 bg-zinc-50/70"
                            >
                              {expandedContent}
                            </TableCell>
                          </TableRow>
                        ) : null}
                      </React.Fragment>
                    )
                  })
                )}
              </TableBody>
            </Table>
          </GridScroller>

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
