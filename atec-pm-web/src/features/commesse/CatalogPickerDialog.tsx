import * as React from "react"
import { useInfiniteQuery, useMutation } from "@tanstack/react-query"
import { Plus } from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { useConfirm } from "@/components/shared/confirm"
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
import { createDdpRow, fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import type { CatalogItemListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { euro } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"

const PAGE_SIZE = 50

interface PickerColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = colonna non filtrabile). */
  filterParam?: string
  cell: (item: CatalogItemListItem) => React.ReactNode
}

function dash(value: string): React.ReactNode {
  return value && value.trim() ? value : "—"
}

const COLUMNS: PickerColumn[] = [
  {
    key: "code",
    label: "Codice",
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
    key: "unit",
    label: "UM",
    cell: (item) => dash(item.unit),
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
    key: "manufacturer",
    label: "Produttore",
    filterParam: "manufacturer",
    cell: (item) => dash(item.manufacturer),
  },
  {
    key: "unitCost",
    label: "Costo",
    align: "right",
    cell: (item) => <span className="tabular-nums">{euro(item.unitCost)}</span>,
  },
]

/**
 * Picker articoli dal Catalogo per la DDP commerciale (gemello di
 * `CatalogPickerWindow` del WPF). Doppio clic (o «+») su una riga = aggiunge
 * l'articolo alla distinta con Qtà=1, stato DO e richiedente = utente corrente,
 * copiando codice/descrizione/UM/costo/fornitore/produttore dal catalogo. Se
 * l'articolo è già presente, chiede se aggiungere +1 alla quantità. Resta aperto
 * per inserimenti multipli, come la finestra WPF.
 */
export function CatalogPickerDialog({
  open,
  projectId,
  onClose,
  onAdded,
}: {
  open: boolean
  projectId: number
  onClose: () => void
  /** Invocato dopo ogni inserimento: il parent ricarica la griglia. */
  onAdded: () => void
}) {
  const confirm = useConfirm()
  const requestedBy = getSession()?.user.fullName ?? ""

  const [filters, setFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(filters, 300)
  const [addedCount, setAddedCount] = React.useState(0)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)

  // Reset alla riapertura: niente residui dell'ultima sessione di inserimento.
  React.useEffect(() => {
    if (open) {
      setFilters({})
      setAddedCount(0)
      setMessage(null)
      setError(null)
    }
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
    queryKey: ["catalog-picker", debouncedFilters],
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

  const addMutation = useMutation({
    mutationFn: async (item: CatalogItemListItem) => {
      setError(null)
      // Dedup come il WPF: rilegge la distinta e confronta per catalogItemId.
      const existing = await fetchDdpRows(projectId, "COMMERCIAL")
      const duplicate = existing.find((r) => r.catalogItemId === item.id)
      if (duplicate) {
        const ok = await confirm({
          title: "Articolo già presente",
          description: `L'articolo ${item.code} è già nella DDP (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
          confirmLabel: "Aggiungi +1",
          destructive: false,
        })
        if (!ok) return null
        await updateDdpRow(projectId, duplicate.id, {
          id: duplicate.id,
          projectId,
          catalogItemId: duplicate.catalogItemId ?? null,
          partNumber: duplicate.partNumber,
          description: duplicate.description,
          unit: duplicate.unit,
          quantity: duplicate.quantity + 1,
          unitCost: duplicate.unitCost,
          supplierId: null,
          manufacturer: duplicate.manufacturer,
          itemStatus: duplicate.itemStatus,
          requestedBy: duplicate.requestedBy,
          daneaRef: duplicate.daneaRef,
          dateNeeded: duplicate.dateNeeded,
          destination: duplicate.destination,
          destinationSpec: duplicate.destinationSpec ?? "",
          notes: duplicate.notes,
          ddpType: "COMMERCIAL",
          expectedUpdatedAt: null,
        })
        return { code: item.code, updated: true }
      }

      await createDdpRow(projectId, {
        id: 0,
        projectId,
        catalogItemId: item.id,
        partNumber: item.code,
        description: item.description,
        unit: item.unit || "PZ",
        quantity: 1,
        unitCost: item.unitCost,
        supplierId: item.supplierId,
        manufacturer: item.manufacturer,
        itemStatus: "DO",
        requestedBy,
        daneaRef: "",
        dateNeeded: null,
        destination: "",
        destinationSpec: "",
        notes: "",
        ddpType: "COMMERCIAL",
        expectedUpdatedAt: null,
      })
      return { code: item.code, updated: false }
    },
    onSuccess: (result) => {
      if (!result) return
      setAddedCount((n) => n + 1)
      setMessage(
        result.updated
          ? `✓ Qtà aggiornata per ${result.code}`
          : `✓ ${result.code} aggiunto`
      )
      onAdded()
    },
    onError: (err: Error) => setError(err.message),
  })

  function handleAdd(item: CatalogItemListItem) {
    if (addMutation.isPending) return
    addMutation.mutate(item)
  }

  // Scroll infinito: avvicinandosi al fondo carica la pagina successiva.
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

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[88vh] flex-col gap-4 sm:max-w-5xl">
        <DialogHeader>
          <DialogTitle>Aggiungi da Catalogo</DialogTitle>
          <DialogDescription>
            Doppio clic su un articolo per aggiungerlo alla distinta (Qtà = 1).
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
                <TableHead className="w-12" />
              </TableRow>
              <TableRow className="hover:bg-transparent">
                {COLUMNS.map((column) => (
                  <TableHead
                    key={column.key}
                    className="h-auto px-2 py-2 align-middle"
                  >
                    {column.filterParam ? (
                      <ColumnFilterInput
                        value={filters[column.filterParam] ?? ""}
                        onChange={(value) =>
                          setColumnFilter(column.filterParam!, value)
                        }
                      />
                    ) : null}
                  </TableHead>
                ))}
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.isError ? (
                <TableRow>
                  <TableCell
                    colSpan={COLUMNS.length + 1}
                    className="h-24 text-center text-destructive"
                  >
                    {(query.error as Error).message ||
                      "Errore nel caricamento del catalogo."}
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
                    className="cursor-pointer"
                    onDoubleClick={() => handleAdd(item)}
                  >
                    {COLUMNS.map((column) => (
                      <TableCell
                        key={column.key}
                        className={
                          column.align === "right" ? "text-right" : undefined
                        }
                      >
                        {column.cell(item)}
                      </TableCell>
                    ))}
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        title="Aggiungi alla distinta"
                        disabled={addMutation.isPending}
                        onClick={() => handleAdd(item)}
                      >
                        <Plus />
                        <span className="sr-only">Aggiungi {item.code}</span>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
          {query.isFetchingNextPage ? (
            <p className="py-2 text-center text-sm text-muted-foreground">
              Caricamento…
            </p>
          ) : null}
        </div>

        <DialogFooter className="sm:justify-between">
          <div className="flex flex-1 items-center gap-3 text-sm">
            <span className="text-muted-foreground">
              {totalCount > 0 ? `${items.length} di ${totalCount} articoli` : ""}
            </span>
            {error ? (
              <span className="text-destructive">{error}</span>
            ) : message ? (
              <span className="font-medium text-primary">{message}</span>
            ) : null}
          </div>
          <div className="flex items-center gap-3">
            {addedCount > 0 ? (
              <span className="text-sm font-semibold tabular-nums">
                ✓ {addedCount} aggiunt{addedCount === 1 ? "o" : "i"}
              </span>
            ) : null}
            <Button variant="outline" onClick={onClose}>
              Chiudi
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
