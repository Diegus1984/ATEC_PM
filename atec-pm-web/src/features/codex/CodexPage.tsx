import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  FilterX,
  Link2,
  ListChecks,
  Pencil,
  Plus,
  RefreshCw,
  Search,
  Tag,
  Trash2,
} from "lucide-react"
import { Link } from "react-router-dom"

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
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  deleteCodex,
  fetchCodex,
  fetchCodexSyncStatus,
  startCodexSync,
} from "@/lib/api/codex"
import type { CodexListItem } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { canWriteFeature } from "@/lib/auth/permissions"
import { decodeHtmlEntities, euro } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"

import { CodexDaneaMappingDialog } from "./CodexDaneaMappingDialog"
import { CodexEditDialog } from "./CodexEditDialog"
import { CodexGeneratePanel } from "./CodexGeneratePanel"
import { CodexNewCodeDialog } from "./CodexNewCodeDialog"
import { canRecodeCodex } from "./codex-roles"
import { dash } from "@/lib/format"

const PAGE_SIZE = 50
const COLUMN_STORAGE_KEY = "atec_pm_codex_columns"

interface CodexColumn {
  key: string
  label: string
  defaultHidden?: boolean
  align?: "right"
  cell: (item: CodexListItem) => React.ReactNode
}

function formatCodexDate(value: string): string {
  if (!value) return "—"
  const date = new Date(value)
  if (Number.isNaN(date.getTime()) || date.getFullYear() <= 1) return "—"
  return formatDateShort(date)
}

const COLUMNS: CodexColumn[] = [
  {
    key: "codice",
    label: "Codice",
    cell: (item) => (
      <span className="font-medium tabular-nums">{item.codice}</span>
    ),
  },
  {
    key: "codiceNuovo",
    label: "Codice nuovo",
    cell: (item) =>
      item.codiceNuovo ? (
        <span className="font-medium tabular-nums text-primary">
          {item.codiceNuovo}
        </span>
      ) : (
        <span className="text-muted-foreground">—</span>
      ),
  },
  {
    key: "descr",
    label: "Descrizione",
    cell: (item) => (
      <span
        className="block max-w-[320px] truncate"
        title={decodeHtmlEntities(item.descr)}
      >
        {dash(decodeHtmlEntities(item.descr))}
      </span>
    ),
  },
  { key: "codeForn", label: "Cod. Forn.", cell: (item) => dash(item.codeForn) },
  { key: "fornitore", label: "Fornitore", cell: (item) => dash(item.fornitore) },
  {
    key: "prezzoForn",
    label: "Prezzo",
    align: "right",
    cell: (item) => (
      <span className="tabular-nums">{euro(item.prezzoForn)}</span>
    ),
  },
  { key: "iva", label: "IVA", cell: (item) => dash(item.iva) },
  { key: "produttore", label: "Produttore", cell: (item) => dash(item.produttore) },
  {
    key: "data",
    label: "Data",
    cell: (item) => (
      <span className="tabular-nums">{formatCodexDate(item.data)}</span>
    ),
  },
  { key: "categoria", label: "Categoria", cell: (item) => dash(item.categoria) },
  {
    key: "barcode",
    label: "Barcode",
    defaultHidden: true,
    cell: (item) => dash(item.barcode),
  },
  { key: "tipologia", label: "Tipologia", cell: (item) => dash(item.tipologia) },
  {
    key: "extra1",
    label: "Extra1",
    defaultHidden: true,
    cell: (item) => dash(item.extra1),
  },
  {
    key: "extra2",
    label: "Extra2",
    defaultHidden: true,
    cell: (item) => dash(item.extra2),
  },
  {
    key: "extra3",
    label: "Extra3",
    defaultHidden: true,
    cell: (item) => dash(item.extra3),
  },
  { key: "codeProd", label: "Cod. Prod.", cell: (item) => dash(item.codeProd) },
  {
    key: "spec",
    label: "Spec",
    defaultHidden: true,
    cell: (item) => dash(item.spec),
  },
  {
    key: "oper",
    label: "Oper",
    defaultHidden: true,
    cell: (item) => <span className="tabular-nums">{item.oper}</span>,
  },
  { key: "um", label: "UM", cell: (item) => dash(item.um) },
  { key: "ubicazione", label: "Ubicazione", cell: (item) => dash(item.ubicazione) },
  {
    key: "codexforn",
    label: "Codex Forn.",
    defaultHidden: true,
    cell: (item) => dash(item.codexforn),
  },
  {
    key: "note",
    label: "Note",
    cell: (item) => (
      <span className="block max-w-[220px] truncate" title={item.note}>
        {dash(item.note)}
      </span>
    ),
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

export function CodexPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  // Sincronizza / genera / modifica / elimina articolo: governo dell'anagrafica Codex.
  const canManage = canWriteFeature("action.manage_codex")
  const canRecode = canRecodeCodex()

  const [page, setPage] = React.useState(1)
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)
  const [columnFilters, setColumnFilters] = React.useState<
    Record<string, string>
  >({})
  const debouncedFilters = useDebounced(columnFilters, 300)
  const [sort, setSort] = React.useState<SortState>({ by: "codice", dir: "asc" })

  const [visibility, setVisibility] = React.useState<Record<string, boolean>>(
    loadVisibility
  )
  const [showGenerate, setShowGenerate] = React.useState(false)
  const [editItem, setEditItem] = React.useState<CodexListItem | null>(null)
  const [newCodeItem, setNewCodeItem] = React.useState<CodexListItem | null>(null)
  const [daneaItem, setDaneaItem] = React.useState<CodexListItem | null>(null)

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

  // Reset alla prima pagina quando cambia ricerca, filtri o ordinamento.
  React.useEffect(() => {
    setPage(1)
  }, [searchTerm, sort, debouncedFilters])

  React.useEffect(() => {
    try {
      localStorage.setItem(COLUMN_STORAGE_KEY, JSON.stringify(visibility))
    } catch {
      // storage non disponibile: nessuna persistenza
    }
  }, [visibility])

  const listQuery = useQuery({
    queryKey: ["codex", page, searchTerm, sort, debouncedFilters],
    queryFn: () =>
      fetchCodex({
        page,
        pageSize: PAGE_SIZE,
        search: searchTerm,
        sortBy: sort.by,
        sortDir: sort.dir,
        filters: debouncedFilters,
      }),
  })

  const syncQuery = useQuery({
    queryKey: ["codex-sync-status"],
    queryFn: fetchCodexSyncStatus,
    refetchInterval: (query) =>
      query.state.data?.isSyncing ? 3000 : false,
  })

  // Quando la sincronizzazione termina, ricarica la lista.
  const wasSyncing = React.useRef(false)
  React.useEffect(() => {
    const syncing = syncQuery.data?.isSyncing ?? false
    if (wasSyncing.current && !syncing) {
      void queryClient.invalidateQueries({ queryKey: ["codex"] })
    }
    wasSyncing.current = syncing
  }, [syncQuery.data?.isSyncing, queryClient])

  const syncMutation = useMutation({
    mutationFn: startCodexSync,
    onSuccess: () => {
      void syncQuery.refetch()
    },
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteCodex,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["codex"] }),
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (item: CodexListItem) => {
      const ok = await confirm({
        title: "Elimina articolo",
        description: `Eliminare definitivamente l'articolo ${item.codice}?\n\n"${item.descr}"\n\nL'operazione non è reversibile.`,
        confirmLabel: "Elimina",
      })
      if (ok) {
        deleteMutation.mutate(item.id)
      }
    },
    [confirm, deleteMutation]
  )

  const visibleColumns = COLUMNS.filter((c) => visibility[c.key])
  const items = listQuery.data?.items ?? []
  const totalCount = listQuery.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const showActions = canManage || canRecode
  const colSpan = visibleColumns.length + (showActions ? 1 : 0)

  return (
    <div className="space-y-4">
      {showGenerate && canManage ? (
        <CodexGeneratePanel
          onClose={() => setShowGenerate(false)}
          onGenerated={async () => {
            setShowGenerate(false)
            await queryClient.invalidateQueries({ queryKey: ["codex"] })
          }}
        />
      ) : null}

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Codex Articoli</CardTitle>
              <CardDescription>
                Anagrafica articoli sincronizzata dal Codex
                {listQuery.data ? ` — ${totalCount} totali` : ""}
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <CodexSyncStatusBadge
                status={syncQuery.data}
                loading={syncQuery.isLoading}
              />
              {canManage ? (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => syncMutation.mutate()}
                  disabled={syncQuery.data?.isSyncing || syncMutation.isPending}
                >
                  <RefreshCw
                    className={
                      syncQuery.data?.isSyncing ? "animate-spin" : undefined
                    }
                  />
                  Sincronizza
                </Button>
              ) : null}
              {canManage ? (
                <Button size="sm" onClick={() => setShowGenerate(true)}>
                  <Plus />
                  Genera Codice
                </Button>
              ) : null}
              {canRecode ? (
                <Button size="sm" variant="outline" asChild>
                  <Link to="/codex/ricodifica">
                    <ListChecks />
                    Ricodifica
                  </Link>
                </Button>
              ) : null}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative max-w-sm flex-1">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                value={searchInput}
                placeholder="Cerca per codice, descrizione, fornitore…"
                className="pl-8"
                onChange={(event) => setSearchInput(event.target.value)}
              />
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
                onClick={() => listQuery.refetch()}
                disabled={listQuery.isFetching}
              >
                <RefreshCw
                  className={listQuery.isFetching ? "animate-spin" : undefined}
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

          {listQuery.isError ? (
            <p className="text-sm text-destructive">
              {(listQuery.error as Error).message ||
                "Errore nel caricamento degli articoli."}
            </p>
          ) : null}

          <GridScroller className="rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((column) => (
                    <TableHead
                      key={column.key}
                      className={column.align === "right" ? "text-right" : undefined}
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
                  {showActions ? <TableHead className="w-12" /> : null}
                </TableRow>
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((column) => (
                    <TableHead
                      key={column.key}
                      className="h-auto px-2 py-2 align-middle"
                    >
                      <ColumnFilterInput
                        value={columnFilters[column.key] ?? ""}
                        onChange={(value) => setColumnFilter(column.key, value)}
                      />
                    </TableHead>
                  ))}
                  {showActions ? <TableHead className="w-12" /> : null}
                </TableRow>
              </TableHeader>
              <TableBody>
                {listQuery.isLoading ? (
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
                      {searchTerm || hasColumnFilters
                        ? "Nessun articolo corrisponde alla ricerca."
                        : "Nessun articolo — eseguire una sincronizzazione."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.id}
                      className={canManage ? "cursor-pointer" : undefined}
                      onDoubleClick={
                        canManage ? () => setEditItem(item) : undefined
                      }
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
                      {showActions ? (
                        <TableCell className="text-right">
                          <RowActionsMenu
                            label={item.codice}
                            actions={[
                              ...(canRecode
                                ? [
                                    {
                                      label: item.codiceNuovo
                                        ? "Codice nuovo…"
                                        : "Assegna codice nuovo…",
                                      icon: Tag,
                                      onClick: () => setNewCodeItem(item),
                                    },
                                  ]
                                : []),
                              ...(canRecode && item.codiceNuovo
                                ? [
                                    {
                                      label: "Articoli Danea…",
                                      icon: Link2,
                                      onClick: () => setDaneaItem(item),
                                    },
                                  ]
                                : []),
                              ...(canManage
                                ? [
                                    {
                                      label: "Modifica",
                                      icon: Pencil,
                                      onClick: () => setEditItem(item),
                                    },
                                    {
                                      label: "Elimina",
                                      icon: Trash2,
                                      destructive: true,
                                      separatorBefore: true,
                                      onClick: () => {
                                        void handleDelete(item)
                                      },
                                    },
                                  ]
                                : []),
                            ]}
                          />
                        </TableCell>
                      ) : null}
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </GridScroller>

          <ServerPagination
            page={page}
            totalPages={totalPages}
            totalCount={totalCount}
            itemNoun="articoli"
            emptyLabel="Nessun articolo"
            disabled={listQuery.isFetching}
            onPageChange={setPage}
          />
        </CardContent>
      </Card>

      <CodexEditDialog
        item={editItem}
        onClose={() => setEditItem(null)}
        onSaved={async () => {
          setEditItem(null)
          await queryClient.invalidateQueries({ queryKey: ["codex"] })
        }}
      />

      <CodexNewCodeDialog
        item={newCodeItem}
        onClose={() => setNewCodeItem(null)}
        onSaved={() => {
          setNewCodeItem(null)
          void queryClient.invalidateQueries({ queryKey: ["codex"] })
        }}
      />

      <CodexDaneaMappingDialog
        item={daneaItem}
        onClose={() => setDaneaItem(null)}
      />
    </div>
  )
}

function CodexSyncStatusBadge({
  status,
  loading,
}: {
  status:
    | { isSyncing: boolean; lastSync: string | null; totalRows: number; lastError: string | null }
    | undefined
  loading: boolean
}) {
  if (loading || !status) {
    return null
  }

  if (status.isSyncing) {
    return (
      <span className="text-xs text-amber-600">Sincronizzazione in corso…</span>
    )
  }
  if (status.lastError) {
    return (
      <span className="max-w-60 truncate text-xs text-destructive" title={status.lastError}>
        Errore: {status.lastError}
      </span>
    )
  }
  if (status.lastSync) {
    const date = new Date(status.lastSync)
    const when = Number.isNaN(date.getTime())
      ? status.lastSync
      : date.toLocaleString("it-IT", {
          day: "2-digit",
          month: "2-digit",
          year: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        })
    return (
      <span className="text-xs text-emerald-600">
        Ultimo sync: {when} — {status.totalRows.toLocaleString("it-IT")} articoli
      </span>
    )
  }
  return <span className="text-xs text-muted-foreground">Mai sincronizzato</span>
}
