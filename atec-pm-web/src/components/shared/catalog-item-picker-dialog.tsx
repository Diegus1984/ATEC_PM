import * as React from "react"
import { useInfiniteQuery } from "@tanstack/react-query"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchCatalogItems } from "@/lib/api/catalog"
import type { CatalogItemListItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

const PAGE_SIZE = 50

interface PickerColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = colonna non filtrabile). */
  filterParam?: string
  cell: (item: CatalogItemListItem) => React.ReactNode
}

function dash(value: string | undefined): React.ReactNode {
  return value && value.trim() ? value : "—"
}

const COLUMNS: PickerColumn[] = [
  {
    key: "code",
    label: "Codice Danea",
    filterParam: "code",
    cell: (item) => <span className="font-medium">{item.code}</span>,
  },
  {
    key: "description",
    label: "Descrizione",
    filterParam: "description",
    cell: (item) => (
      <span className="block max-w-[320px] truncate" title={item.description}>
        {dash(item.description)}
      </span>
    ),
  },
  {
    key: "supplierName",
    label: "Fornitore",
    filterParam: "supplier",
    cell: (item) => (
      <span className="block max-w-[180px] truncate" title={item.supplierName}>
        {dash(item.supplierName)}
      </span>
    ),
  },
  {
    key: "atecCode",
    label: "Cod. ATEC",
    filterParam: "atecCode",
    cell: (item) => (
      <span className="font-mono text-xs">{dash(item.atecCode)}</span>
    ),
  },
  {
    key: "unitCost",
    label: "Costo",
    align: "right",
    cell: (item) => <span className="tabular-nums">{euro(item.unitCost)}</span>,
  },
]

/**
 * Picker di SELEZIONE di un articolo di catalogo (Danea): a differenza di
 * `CatalogPickerDialog` (che aggiunge righe alla distinta) qui la riga scelta
 * viene solo restituita al chiamante via `onSelect`.
 *
 * Ricerca e paginazione sono server-side con scroll infinito: il catalogo è
 * grande e non va mai scaricato tutto (regola di progetto).
 */
export function CatalogItemPickerDialog({
  open,
  onClose,
  onSelect,
  title = "Scegli articolo Danea",
  description,
  /** Filtri di partenza (es. `{ supplier: "SMC Italia" }`): l'utente può cancellarli. */
  initialFilters,
  /** Articolo attualmente collegato: evidenziato in elenco. */
  selectedId,
}: {
  open: boolean
  onClose: () => void
  onSelect: (item: CatalogItemListItem) => void
  title?: string
  description?: string
  initialFilters?: Record<string, string>
  selectedId?: number | null
}) {
  const [filters, setFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(filters, 300)

  // Alla riapertura si riparte dai filtri suggeriti dal chiamante (niente residui).
  React.useEffect(() => {
    if (open) setFilters(initialFilters ?? {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const setColumnFilter = React.useCallback((param: string, value: string) => {
    setFilters((prev) => {
      const next = { ...prev }
      if (value) next[param] = value
      else delete next[param]
      return next
    })
  }, [])

  const query = useInfiniteQuery({
    queryKey: ["catalog-item-picker", debouncedFilters],
    queryFn: ({ pageParam }) =>
      fetchCatalogItems({
        page: pageParam,
        pageSize: PAGE_SIZE,
        filters: debouncedFilters,
      }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
    enabled: open,
  })

  const items = React.useMemo(
    () => query.data?.pages.flatMap((p) => p.items) ?? [],
    [query.data]
  )
  const totalCount = query.data?.pages[0]?.totalCount ?? 0

  // Scroll infinito con onScroll (IntersectionObserver non scatta nel preview headless).
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query
  const handleScroll = React.useCallback(
    (event: React.UIEvent<HTMLDivElement>) => {
      const el = event.currentTarget
      if (
        hasNextPage &&
        !isFetchingNextPage &&
        el.scrollHeight - el.scrollTop - el.clientHeight < 300
      ) {
        void fetchNextPage()
      }
    },
    [hasNextPage, isFetchingNextPage, fetchNextPage]
  )

  const handlePick = (item: CatalogItemListItem) => {
    onSelect(item)
    onClose()
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[88vh] flex-col gap-4 sm:max-w-5xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>
            {description ??
              "Doppio clic (o «Scegli») sull'articolo da collegare. I filtri cercano sul server."}
          </DialogDescription>
        </DialogHeader>

        <div
          className="min-h-0 flex-1 overflow-auto rounded-lg border [&>div]:overflow-visible"
          onScroll={handleScroll}
        >
          <Table>
            <TableHeader className="sticky top-0 z-20 bg-muted [&_th]:bg-muted">
              <TableRow className="hover:bg-transparent">
                {COLUMNS.map((column) => (
                  <TableHead
                    key={column.key}
                    className={column.align === "right" ? "text-right" : undefined}
                  >
                    {column.label}
                  </TableHead>
                ))}
                <TableHead className="w-20" />
              </TableRow>
              <TableRow className="hover:bg-transparent">
                {COLUMNS.map((column) => (
                  <TableHead key={column.key} className="h-auto px-2 py-2 align-middle">
                    {column.filterParam ? (
                      <ColumnFilterInput
                        value={filters[column.filterParam] ?? ""}
                        onChange={(value) => setColumnFilter(column.filterParam!, value)}
                      />
                    ) : null}
                  </TableHead>
                ))}
                <TableHead className="w-20" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.isError ? (
                <TableRow>
                  <TableCell
                    colSpan={COLUMNS.length + 1}
                    className="h-24 text-center text-destructive"
                  >
                    {(query.error as Error).message || "Errore nel caricamento del catalogo."}
                  </TableCell>
                </TableRow>
              ) : query.isLoading ? (
                <TableRow>
                  <TableCell
                    colSpan={COLUMNS.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Caricamento catalogo…
                  </TableCell>
                </TableRow>
              ) : items.length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={COLUMNS.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Nessun articolo corrisponde ai filtri.
                  </TableCell>
                </TableRow>
              ) : (
                items.map((item) => (
                  <TableRow
                    key={item.id}
                    className={cn(
                      "cursor-pointer",
                      selectedId === item.id && "bg-primary/10"
                    )}
                    onDoubleClick={() => handlePick(item)}
                  >
                    {COLUMNS.map((column) => (
                      <TableCell
                        key={column.key}
                        className={column.align === "right" ? "text-right" : undefined}
                      >
                        {column.cell(item)}
                      </TableCell>
                    ))}
                    <TableCell className="text-right">
                      <Button size="sm" variant="outline" onClick={() => handlePick(item)}>
                        Scegli
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
          {query.isFetchingNextPage ? (
            <p className="py-2 text-center text-sm text-muted-foreground">Caricamento…</p>
          ) : null}
        </div>

        <DialogFooter className="sm:justify-between">
          <span className="text-sm text-muted-foreground">
            {totalCount > 0 ? `${items.length} di ${totalCount} articoli` : ""}
          </span>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
