import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  FilterX,
  Link2,
  Link2Off,
  Pencil,
  Plus,
  RefreshCw,
  Search,
  Trash2,
} from "lucide-react"

import { ColumnFilterCombobox } from "@/components/shared/column-filter-combobox"
import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { ServerPagination } from "@/components/shared/server-pagination"
import { notifyError } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { SortableHeader, type SortState } from "@/components/shared/sortable-header"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  deleteCatalogItem,
  fetchCatalogFilterMeta,
  fetchCatalogItems,
  unassignCatalogMapping,
} from "@/lib/api/catalog"
import { fetchDaneaSyncStatus, runDaneaSync } from "@/lib/api/danea-sync"
import type { CatalogItemListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { euro, dash } from "@/lib/format"
import { notifyInfo } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"

import { canRecodeCodex } from "@/features/codex/codex-roles"
import { CatalogAtecAssignDialog } from "./CatalogAtecAssignDialog"
import { CatalogItemDialog } from "./CatalogItemDialog"

const PAGE_SIZE = 50
const COLUMN_STORAGE_KEY = "atec_pm_catalog_columns"

interface CatalogColumn {
  key: string
  label: string
  defaultHidden?: boolean
  align?: "right"
  /** Parametro server per la ricerca per colonna (assente = colonna non filtrabile). */
  filterParam?: string
  cell: (item: CatalogItemListItem) => React.ReactNode
}

const COLUMNS: CatalogColumn[] = [
  {
    key: "code",
    label: "Codice",
    filterParam: "code",
    cell: (item) => <span className="font-medium">{item.code}</span>,
  },
  {
    key: "atecCode",
    label: "Codice ATEC",
    filterParam: "atecCode",
    cell: (item) =>
      item.atecCode ? (
        <span className="font-medium tabular-nums text-primary">
          {item.atecCode}
        </span>
      ) : (
        <span className="text-muted-foreground">—</span>
      ),
  },
  {
    key: "description",
    label: "Descrizione",
    filterParam: "description",
    cell: (item) => (
      <span className="block max-w-[360px] truncate" title={item.description}>
        {dash(item.description)}
      </span>
    ),
  },
  {
    key: "supplierName",
    label: "Fornitore",
    filterParam: "supplier",
    cell: (item) => dash(item.supplierName),
  },
  {
    key: "manufacturer",
    label: "Produttore",
    defaultHidden: true,
    filterParam: "manufacturer",
    cell: (item) => dash(item.manufacturer),
  },
  {
    key: "category",
    label: "Categoria",
    filterParam: "category",
    cell: (item) => dash(item.category),
  },
  {
    key: "subcategory",
    label: "Sottocategoria",
    filterParam: "subcategory",
    cell: (item) => dash(item.subcategory),
  },
  {
    key: "unit",
    label: "UdM",
    defaultHidden: true,
    cell: (item) => dash(item.unit),
  },
  {
    key: "unitCost",
    label: "Acquisto",
    align: "right",
    cell: (item) => <span className="tabular-nums">{euro(item.unitCost)}</span>,
  },
  {
    key: "listPrice",
    label: "Listino",
    align: "right",
    cell: (item) => <span className="tabular-nums">{euro(item.listPrice)}</span>,
  },
]

function defaultVisibility(): Record<string, boolean> {
  return Object.fromEntries(COLUMNS.map((c) => [c.key, !c.defaultHidden]))
}

function loadVisibility(): Record<string, boolean> {
  const base = defaultVisibility()
  try {
    const raw = localStorage.getItem(COLUMN_STORAGE_KEY)
    if (raw) {
      const saved = JSON.parse(raw) as Record<string, boolean>
      for (const key of Object.keys(base)) {
        if (typeof saved[key] === "boolean") base[key] = saved[key]
      }
    }
  } catch {
    // ignora: usa i default
  }
  return base
}

export function CatalogoPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [page, setPage] = React.useState(1)
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)
  const [columnFilters, setColumnFilters] = React.useState<
    Record<string, string>
  >({})
  const debouncedFilters = useDebounced(columnFilters, 300)
  const [sort, setSort] = React.useState<SortState>({ by: "code", dir: "asc" })
  const [visibility, setVisibility] = React.useState<Record<string, boolean>>(
    loadVisibility
  )
  const [dialogItem, setDialogItem] = React.useState<number | "new" | null>(null)
  const [atecItem, setAtecItem] = React.useState<CatalogItemListItem | null>(null)
  /** Filtro mapping: all | missing | done | orphans (Extra1 senza match Codex). */
  const [atecState, setAtecState] = React.useState<
    "" | "missing" | "done" | "orphans"
  >("")
  const canMapAtec = canRecodeCodex(getSession()?.user.userRole)

  const setColumnFilter = React.useCallback((param: string, value: string) => {
    setColumnFilters((prev) => {
      const next = { ...prev }
      if (value) {
        next[param] = value
      } else {
        delete next[param]
      }
      return next
    })
  }, [])

  const hasColumnFilters = Object.keys(columnFilters).length > 0

  React.useEffect(() => {
    setPage(1)
  }, [searchTerm, sort, debouncedFilters, atecState])

  React.useEffect(() => {
    try {
      localStorage.setItem(COLUMN_STORAGE_KEY, JSON.stringify(visibility))
    } catch {
      // storage non disponibile: nessuna persistenza
    }
  }, [visibility])

  const query = useQuery({
    queryKey: ["catalog", page, searchTerm, sort, debouncedFilters, atecState],
    queryFn: () =>
      fetchCatalogItems({
        page,
        pageSize: PAGE_SIZE,
        search: searchTerm,
        sortBy: sort.by,
        sortDir: sort.dir,
        filters: debouncedFilters,
        atecState: atecState || undefined,
      }),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["catalog"] })

  // Valori distinti per i filtri a tendina (pattern pagina Trasferimento Danea).
  const filterMetaQuery = useQuery({
    queryKey: ["catalog-filter-meta"],
    queryFn: fetchCatalogFilterMeta,
  })
  const filterOptions: Record<string, string[]> = React.useMemo(
    () => ({
      supplier: filterMetaQuery.data?.suppliers ?? [],
      manufacturer: filterMetaQuery.data?.manufacturers ?? [],
      category: filterMetaQuery.data?.categories ?? [],
      subcategory: filterMetaQuery.data?.subcategories ?? [],
    }),
    [filterMetaQuery.data]
  )

  // Sync manuale con Danea (archivio Atec_PM): avvio + polling dello stato finché gira.
  const [syncPolling, setSyncPolling] = React.useState(false)
  const syncStatusQuery = useQuery({
    queryKey: ["danea-sync-status"],
    queryFn: fetchDaneaSyncStatus,
    enabled: syncPolling,
    refetchInterval: syncPolling ? 1500 : false,
  })
  React.useEffect(() => {
    const s = syncStatusQuery.data
    if (!syncPolling || !s || s.isSyncing) return
    setSyncPolling(false)
    if (s.lastError) {
      notifyError(`Sync Danea fallito: ${s.lastError}`)
    } else {
      notifyInfo(
        `Sync Danea completato: ${s.articles} articoli, ${s.suppliers} fornitori, ${s.customers} clienti`
      )
    }
    void invalidate()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [syncStatusQuery.data, syncPolling])

  const handleSyncDanea = React.useCallback(async () => {
    try {
      await runDaneaSync()
      // Piccola grazia prima del primo poll: il task parte in background.
      setTimeout(() => setSyncPolling(true), 1200)
    } catch (err) {
      notifyError(err as Error)
    }
  }, [])

  const deleteMutation = useMutation({
    mutationFn: deleteCatalogItem,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (item: CatalogItemListItem) => {
      const ok = await confirm({
        title: "Elimina articolo",
        description: `Disattivare "${item.code} — ${item.description}"?`,
        confirmLabel: "Elimina",
      })
      if (ok) {
        deleteMutation.mutate(item.id)
      }
    },
    [confirm, deleteMutation]
  )

  const unassignMutation = useMutation({
    mutationFn: (item: CatalogItemListItem) => unassignCatalogMapping(item.id),
    onSuccess: (_, item) => {
      notifyInfo(`${item.code} sganciato dal codice ATEC`)
      void invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const handleUnassignAtec = React.useCallback(
    async (item: CatalogItemListItem) => {
      const ok = await confirm({
        title: "Sgancia codice ATEC",
        description: `Rimuovere l'associazione di ${item.code} dal codice ${item.atecCode}?\nIn Danea l'Extra 1 dell'articolo verrà svuotato.`,
        confirmLabel: "Sgancia",
      })
      if (ok) unassignMutation.mutate(item)
    },
    [confirm, unassignMutation]
  )

  const visibleColumns = COLUMNS.filter((c) => visibility[c.key])
  const items = query.data?.items ?? []
  const totalCount = query.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const colSpan = visibleColumns.length + 1

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Catalogo articoli</CardTitle>
              <CardDescription>
                Articoli di catalogo
                {query.data ? ` — ${totalCount} totali` : ""}
              </CardDescription>
            </div>
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={syncPolling}
                title={
                  syncPolling
                    ? (syncStatusQuery.data?.progress ?? "Sincronizzazione in corso…")
                    : "Allinea il catalogo all'archivio Danea Atec_PM (specchio: gli articoli assenti vengono disattivati)"
                }
                onClick={() => void handleSyncDanea()}
              >
                <RefreshCw className={syncPolling ? "animate-spin" : undefined} />
                {syncPolling ? "Sincronizzazione…" : "Sincronizza Danea"}
              </Button>
              <Button size="sm" onClick={() => setDialogItem("new")}>
                <Plus />
                Nuovo articolo
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative max-w-sm flex-1">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                value={searchInput}
                placeholder="Cerca per codice, descrizione, fornitore, produttore, categoria…"
                className="pl-8"
                onChange={(event) => setSearchInput(event.target.value)}
              />
            </div>
            <div className="flex flex-wrap gap-1">
              {(
                [
                  { key: "", label: "Tutti" },
                  { key: "missing", label: "Senza ATEC" },
                  { key: "done", label: "Con ATEC" },
                  { key: "orphans", label: "Orfani Extra1" },
                ] as const
              ).map((opt) => (
                <Button
                  key={opt.key || "all"}
                  variant={atecState === opt.key ? "default" : "outline"}
                  size="sm"
                  onClick={() => setAtecState(opt.key)}
                >
                  {opt.label}
                  {opt.key === "orphans" && atecState === "orphans" && query.data
                    ? ` (${totalCount})`
                    : null}
                </Button>
              ))}
            </div>
            <div className="ml-auto flex gap-2">
              {hasColumnFilters ? (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setColumnFilters({})}
                >
                  <FilterX />
                  Pulisci filtri
                </Button>
              ) : null}
              <ColumnsMenu
                columns={COLUMNS.map((column) => ({
                  id: column.key,
                  label: column.label,
                  checked: visibility[column.key],
                  onToggle: (value) =>
                    setVisibility((prev) => ({
                      ...prev,
                      [column.key]: value,
                    })),
                }))}
              />
              <Button
                variant="outline"
                size="sm"
                onClick={() => query.refetch()}
                disabled={query.isFetching}
              >
                <RefreshCw
                  className={query.isFetching ? "animate-spin" : undefined}
                />
                Aggiorna
              </Button>
            </div>
          </div>

          <p className="text-xs text-muted-foreground">
            Ricerca: <code className="font-mono">abc</code> contiene ·{" "}
            <code className="font-mono">abc*</code> inizia con ·{" "}
            <code className="font-mono">*abc</code> finisce con ·{" "}
            <code className="font-mono">*abc*</code> contiene
          </p>

          {query.isError ? (
            <p className="text-sm text-destructive">
              {(query.error as Error).message ||
                "Errore nel caricamento del catalogo."}
            </p>
          ) : null}

          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((column) => (
                    <TableHead
                      key={column.key}
                      className={
                        column.align === "right" ? "text-right" : undefined
                      }
                    >
                      <SortableHeader
                        label={column.label}
                        columnKey={column.key}
                        sort={sort}
                        onSort={setSort}
                        align={column.align}
                      />
                    </TableHead>
                  ))}
                  <TableHead className="w-12" />
                </TableRow>
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((column) => (
                    <TableHead
                      key={column.key}
                      className="h-auto px-2 py-2 align-middle"
                    >
                      {column.filterParam && filterOptions[column.filterParam] ? (
                        <ColumnFilterCombobox
                          value={columnFilters[column.filterParam] ?? ""}
                          onChange={(value) =>
                            setColumnFilter(column.filterParam!, value)
                          }
                          options={filterOptions[column.filterParam]}
                          placeholder={`${column.label}…`}
                          searchPlaceholder={`Cerca ${column.label.toLowerCase()}…`}
                          loading={
                            filterMetaQuery.isLoading &&
                            filterOptions[column.filterParam].length === 0
                          }
                          emptyText="Nessun valore"
                        />
                      ) : column.filterParam ? (
                        <ColumnFilterInput
                          value={columnFilters[column.filterParam] ?? ""}
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
                {query.isLoading ? (
                  Array.from({ length: 8 }).map((_, rowIndex) => (
                    <TableRow key={`skeleton-${rowIndex}`}>
                      {Array.from({ length: colSpan }).map((__, cellIndex) => (
                        <TableCell key={cellIndex}>
                          <Skeleton className="h-5 w-full" />
                        </TableCell>
                      ))}
                    </TableRow>
                  ))
                ) : items.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={colSpan}
                      className="h-24 text-center text-muted-foreground"
                    >
                      {atecState === "orphans"
                        ? "Nessun orfano Extra1: tutti i codici ATEC del catalogo esistono nel Codex."
                        : searchTerm || hasColumnFilters || atecState
                          ? "Nessun articolo corrisponde alla ricerca."
                          : "Nessun articolo trovato."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.id}
                      className="cursor-pointer"
                      onDoubleClick={() => setDialogItem(item.id)}
                    >
                      {visibleColumns.map((column) => (
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
                        <RowActionsMenu
                          label={item.code}
                          actions={[
                            {
                              label: "Modifica",
                              icon: Pencil,
                              onClick: () => setDialogItem(item.id),
                            },
                            ...(canMapAtec
                              ? [
                                  {
                                    label: item.atecCode
                                      ? "Codice ATEC…"
                                      : "Assegna codice ATEC…",
                                    icon: Link2,
                                    onClick: () => setAtecItem(item),
                                  },
                                ]
                              : []),
                            ...(canMapAtec && item.atecCode
                              ? [
                                  {
                                    label: "Sgancia codice ATEC",
                                    icon: Link2Off,
                                    destructive: true,
                                    onClick: () => {
                                      void handleUnassignAtec(item)
                                    },
                                  },
                                ]
                              : []),
                            {
                              label: "Elimina",
                              icon: Trash2,
                              destructive: true,
                              separatorBefore: true,
                              onClick: () => {
                                void handleDelete(item)
                              },
                            },
                          ]}
                        />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>

          <ServerPagination
            page={page}
            totalPages={totalPages}
            totalCount={totalCount}
            itemNoun="articoli"
            emptyLabel="Nessun articolo"
            disabled={query.isFetching}
            onPageChange={setPage}
          />
        </CardContent>
      </Card>

      <CatalogItemDialog
        open={dialogItem !== null}
        itemId={dialogItem}
        onClose={() => setDialogItem(null)}
        onSaved={async () => {
          setDialogItem(null)
          await invalidate()
        }}
      />

      <CatalogAtecAssignDialog
        item={atecItem}
        onClose={() => setAtecItem(null)}
        onSaved={() => {
          setAtecItem(null)
          void invalidate()
        }}
      />
    </>
  )
}
