import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, Eraser, FilterX, Link2, RefreshCw, Search, Tag, Wand2 } from "lucide-react"
import { Link } from "react-router-dom"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { useConfirm } from "@/components/shared/confirm"
import { ServerPagination } from "@/components/shared/server-pagination"
import { SortableHeader, type SortState } from "@/components/shared/sortable-header"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
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
  Tabs,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import {
  bulkCommitCodexNewCodes,
  bulkReleaseCodexReservations,
  bulkRemoveCodexNewCodes,
  bulkReserveCodexNewCodes,
  fetchCodex,
  fetchCodexPrefixes,
  fetchCodexRecodeStats,
} from "@/lib/api/codex"
import type { CodexBulkReserveResult, CodexListItem } from "@/lib/api/types"
import { decodeHtmlEntities } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"

import { CodexDaneaMappingDialog } from "./CodexDaneaMappingDialog"
import { CodexNewCodeDialog } from "./CodexNewCodeDialog"
import { canRecodeCodex } from "./codex-roles"


const PAGE_SIZE = 50

type RecodeView = "missing" | "done" | "all"

/** Colonne della griglia (key = parametro filtro/ordinamento server). */
const COLUMNS: { key: string; label: string }[] = [
  { key: "codice", label: "Codice" },
  { key: "codiceNuovo", label: "Codice nuovo" },
  { key: "descr", label: "Descrizione" },
  { key: "fornitore", label: "Fornitore" },
  { key: "categoria", label: "Categoria" },
]

/**
 * Ricodifica Codex "a tappeto" (ampliamento Codex 21/07/2026): lista della famiglia
 * vecchia (default 201xxx) con avanzamento fatti/da fare. La ricodifica resta MANUALE:
 * questa pagina è solo il posto comodo per farla in serie, riga dopo riga.
 * Griglia con ordinamento e filtri per colonna server-side (stesso pattern di Codex
 * Articoli: jolly abc / abc* / *abc / *abc*).
 */
export function CodexRecodePage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const canRecode = canRecodeCodex()

  const [prefix, setPrefix] = React.useState("201")
  const debouncedPrefix = useDebounced(prefix.trim() || "201", 300)
  const [view, setView] = React.useState<RecodeView>("missing")
  const [page, setPage] = React.useState(1)
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)
  const [columnFilters, setColumnFilters] = React.useState<
    Record<string, string>
  >({})
  const debouncedFilters = useDebounced(columnFilters, 300)
  const [sort, setSort] = React.useState<SortState>({ by: "codice", dir: "asc" })
  const [target, setTarget] = React.useState<CodexListItem | null>(null)
  const [daneaTarget, setDaneaTarget] = React.useState<CodexListItem | null>(null)
  // Selezione per l'inserimento massivo: id → la riga ha già un codice nuovo?
  // (la Map sopravvive a pagine/filtri: si seleziona anche su più pagine).
  const [selected, setSelected] = React.useState<Map<number, boolean>>(new Map())

  const toggleSelected = React.useCallback((item: CodexListItem) => {
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(item.id)) next.delete(item.id)
      else next.set(item.id, !!item.codiceNuovo)
      return next
    })
  }, [])

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

  // TUTTE le famiglie del generatore per l'assegnazione massiva (#127, come il
  // dialog singolo): la lista la governa il server (/api/codex/prefixes).
  const prefixesQuery = useQuery({
    queryKey: ["codex-prefixes"],
    queryFn: fetchCodexPrefixes,
  })
  const bulkFamilies = prefixesQuery.data ?? []

  React.useEffect(() => {
    setPage(1)
  }, [debouncedPrefix, view, searchTerm, sort, debouncedFilters])

  const statsQuery = useQuery({
    queryKey: ["codex-recode-stats", debouncedPrefix],
    queryFn: () => fetchCodexRecodeStats(debouncedPrefix),
  })

  const listQuery = useQuery({
    queryKey: [
      "codex-recode",
      debouncedPrefix,
      view,
      page,
      searchTerm,
      sort,
      debouncedFilters,
    ],
    queryFn: () =>
      fetchCodex({
        page,
        pageSize: PAGE_SIZE,
        search: searchTerm,
        sortBy: sort.by,
        sortDir: sort.dir,
        codicePrefixes: [debouncedPrefix],
        newCodeState: view === "all" ? undefined : view,
        filters: debouncedFilters,
      }),
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["codex-recode"] })
    void queryClient.invalidateQueries({ queryKey: ["codex-recode-stats"] })
    void queryClient.invalidateQueries({ queryKey: ["codex"] })
  }

  const selectedIds = React.useMemo(() => [...selected.keys()], [selected])
  const selectedToAssign = React.useMemo(
    () => [...selected.values()].filter((hasNew) => !hasNew).length,
    [selected]
  )
  const selectedWithCode = selectedIds.length - selectedToAssign

  // Anteprima dell'assegnazione massiva (form vecchio→nuovo con codici PRENOTATI).
  const [bulkPreview, setBulkPreview] = React.useState<
    (CodexBulkReserveResult & { family: string }) | null
  >(null)

  const bulkReserveMutation = useMutation({
    mutationFn: ({ family }: { family: string }) =>
      bulkReserveCodexNewCodes(selectedIds, family),
    onSuccess: (result, { family }) => {
      if (result.items.length === 0) {
        notifyInfo("Nessuna riga da codificare tra le selezionate.")
        return
      }
      setBulkPreview({ ...result, family })
    },
    onError: (err: Error) => notifyError(err),
  })

  const bulkCommitMutation = useMutation({
    mutationFn: () => bulkCommitCodexNewCodes(bulkPreview?.items ?? []),
    onSuccess: (result) => {
      notifyInfo(
        result.skipped > 0
          ? `${result.assigned} codici assegnati · ${result.skipped} righe saltate`
          : `${result.assigned} codici assegnati`
      )
      setBulkPreview(null)
      setSelected(new Map())
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const cancelBulkPreview = React.useCallback(() => {
    const preview = bulkPreview
    setBulkPreview(null)
    if (preview) {
      void bulkReleaseCodexReservations(
        preview.items.map((item) => item.reservationId)
      ).catch(() => {})
    }
  }, [bulkPreview])

  const bulkRemoveMutation = useMutation({
    mutationFn: () => bulkRemoveCodexNewCodes(selectedIds),
    onSuccess: (removed) => {
      notifyInfo(`${removed} codici nuovi rimossi`)
      setSelected(new Map())
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const bulkBusy = bulkReserveMutation.isPending || bulkRemoveMutation.isPending

  const handleBulkRemove = async () => {
    const ok = await confirm({
      title: "Rimuovi codici nuovi",
      description: `Rimuovere il codice nuovo da ${selectedWithCode} righe selezionate?\nLe righe torneranno "non ricodificate".`,
      confirmLabel: "Rimuovi",
    })
    if (ok) bulkRemoveMutation.mutate()
  }

  if (!canRecode) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Ricodifica Codex</CardTitle>
          <CardDescription>
            Non hai il permesso di ricodificare gli articoli Codex.
          </CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const items = listQuery.data?.items ?? []
  const totalCount = listQuery.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const stats = statsQuery.data
  const pct =
    stats && stats.total > 0 ? Math.round((stats.done / stats.total) * 100) : 0

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Ricodifica Codex</CardTitle>
              <CardDescription>
                Assegnazione manuale della nuova codifica alla famiglia{" "}
                <span className="font-mono">{debouncedPrefix}xxx</span> — nessuna
                conversione automatica
              </CardDescription>
            </div>
            <Button size="sm" variant="outline" asChild>
              <Link to="/codex">
                <ArrowLeft />
                Codex Articoli
              </Link>
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-end gap-4">
            <div className="grid gap-1.5">
              <span className="text-xs text-muted-foreground">
                Famiglia vecchia
              </span>
              <Input
                value={prefix}
                className="w-24 font-mono tabular-nums"
                onChange={(event) =>
                  setPrefix(event.target.value.replace(/\D/g, "").slice(0, 3))
                }
              />
            </div>
            <div className="min-w-56 flex-1">
              {stats ? (
                <div className="space-y-1">
                  <p className="text-xs text-muted-foreground">
                    Avanzamento:{" "}
                    <span className="font-medium text-foreground">
                      {stats.done}
                    </span>{" "}
                    ricodificati su {stats.total} ({pct}%)
                  </p>
                  <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
                    <div
                      className="h-full rounded-full bg-primary transition-all"
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                </div>
              ) : (
                <Skeleton className="h-6 w-full" />
              )}
            </div>
            <Tabs
              value={view}
              onValueChange={(value) => setView(value as RecodeView)}
            >
              <TabsList>
                <TabsTrigger value="missing">
                  Da fare{stats ? ` (${stats.total - stats.done})` : ""}
                </TabsTrigger>
                <TabsTrigger value="done">
                  Fatti{stats ? ` (${stats.done})` : ""}
                </TabsTrigger>
                <TabsTrigger value="all">Tutti</TabsTrigger>
              </TabsList>
            </Tabs>
          </div>

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
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  void listQuery.refetch()
                  void statsQuery.refetch()
                }}
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

          {selectedIds.length > 0 ? (
            <div className="flex flex-wrap items-center gap-3 rounded-md border border-primary/40 bg-primary/5 px-3 py-2">
              <p className="text-sm">
                <span className="font-semibold">{selectedIds.length}</span>{" "}
                selezionati —{" "}
                <span className="font-medium">{selectedToAssign}</span> da
                codificare
                {selectedWithCode > 0 ? (
                  <span className="text-muted-foreground">
                    {" "}
                    · {selectedWithCode} già codificati
                  </span>
                ) : null}
              </p>
              <div className="flex flex-wrap items-center gap-2">
                {bulkFamilies.map((family) => (
                  <Button
                    key={family.codice}
                    size="sm"
                    variant="outline"
                    disabled={bulkBusy || selectedToAssign === 0}
                    onClick={() =>
                      bulkReserveMutation.mutate({ family: family.codice })
                    }
                  >
                    <Wand2 className="size-3.5" />
                    Assegna {family.codice} — {family.descrizione}
                  </Button>
                ))}
                <Button
                  size="sm"
                  variant="outline"
                  className="text-destructive"
                  disabled={bulkBusy || selectedWithCode === 0}
                  onClick={() => void handleBulkRemove()}
                >
                  <Eraser className="size-3.5" />
                  Rimuovi codice nuovo
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={bulkBusy}
                  onClick={() => setSelected(new Map())}
                >
                  Deseleziona
                </Button>
              </div>
            </div>
          ) : null}

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
                  <TableHead className="w-10 text-center">
                    <Checkbox
                      checked={
                        items.length > 0 &&
                        items.every((item) => selected.has(item.id))
                      }
                      aria-label="Seleziona pagina"
                      onCheckedChange={(value) => {
                        setSelected((prev) => {
                          const next = new Map(prev)
                          for (const item of items) {
                            if (value) next.set(item.id, !!item.codiceNuovo)
                            else next.delete(item.id)
                          }
                          return next
                        })
                      }}
                    />
                  </TableHead>
                  {COLUMNS.map((column) => (
                    <TableHead key={column.key}>
                      <SortableHeader
                        label={column.label}
                        columnKey={column.key}
                        sort={sort}
                        onSort={setSort}
                      />
                    </TableHead>
                  ))}
                  <TableHead className="w-32" />
                </TableRow>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="w-10" />
                  {COLUMNS.map((column) => (
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
                  <TableHead className="w-32" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {listQuery.isLoading ? (
                  Array.from({ length: 8 }).map((_, rowIndex) => (
                    <TableRow key={`skeleton-${rowIndex}`}>
                      {Array.from({ length: 7 }).map((__, cellIndex) => (
                        <TableCell key={cellIndex}>
                          <Skeleton className="h-5 w-full" />
                        </TableCell>
                      ))}
                    </TableRow>
                  ))
                ) : items.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={7}
                      className="h-24 text-center text-muted-foreground"
                    >
                      {searchTerm || hasColumnFilters
                        ? "Nessun articolo corrisponde alla ricerca."
                        : view === "missing"
                          ? "Nessun articolo da ricodificare — famiglia completata."
                          : "Nessun articolo corrisponde ai filtri."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.id}
                      className="cursor-pointer"
                      onDoubleClick={() => setTarget(item)}
                    >
                      <TableCell
                        className="text-center align-middle"
                        onClick={(event) => event.stopPropagation()}
                        onDoubleClick={(event) => event.stopPropagation()}
                      >
                        <Checkbox
                          checked={selected.has(item.id)}
                          aria-label={`Seleziona ${item.codice}`}
                          onCheckedChange={() => toggleSelected(item)}
                        />
                      </TableCell>
                      <TableCell className="font-medium tabular-nums">
                        {item.codice}
                      </TableCell>
                      <TableCell>
                        {item.codiceNuovo ? (
                          <span className="font-medium tabular-nums text-primary">
                            {item.codiceNuovo}
                          </span>
                        ) : (
                          <span className="text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell>
                        <span
                          className="block max-w-[360px] truncate"
                          title={decodeHtmlEntities(item.descr)}
                        >
                          {decodeHtmlEntities(item.descr) || "—"}
                        </span>
                      </TableCell>
                      <TableCell>{item.fornitore || "—"}</TableCell>
                      <TableCell>{item.categoria || "—"}</TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-1.5">
                          {item.codiceNuovo ? (
                            <Button
                              size="sm"
                              variant="outline"
                              title="Articoli Danea associati"
                              onClick={() => setDaneaTarget(item)}
                            >
                              <Link2 className="size-3.5" />
                              Danea
                            </Button>
                          ) : null}
                          <Button
                            size="sm"
                            variant={item.codiceNuovo ? "outline" : "default"}
                            onClick={() => setTarget(item)}
                          >
                            <Tag className="size-3.5" />
                            {item.codiceNuovo ? "Modifica" : "Assegna"}
                          </Button>
                        </div>
                      </TableCell>
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

      <CodexNewCodeDialog
        item={target}
        onClose={() => setTarget(null)}
        onSaved={() => {
          setTarget(null)
          invalidate()
        }}
      />

      <CodexDaneaMappingDialog
        item={daneaTarget}
        onClose={() => setDaneaTarget(null)}
      />

      {/* Form di conferma dell'assegnazione massiva: vecchio codice → nuovo PRENOTATO
          + descrizione. «Assegna» scrive i campi; Annulla libera le prenotazioni. */}
      <Dialog
        open={bulkPreview !== null}
        onOpenChange={(next) => {
          if (!next && !bulkCommitMutation.isPending) cancelBulkPreview()
        }}
      >
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>
              Assegna codici {bulkPreview?.family} — {bulkPreview?.items.length}{" "}
              righe
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-2">
            <p className="text-xs text-muted-foreground">
              Codici generati e prenotati per te — controlla e conferma con
              Assegna; Annulla li libera.
              {bulkPreview && bulkPreview.skipped > 0
                ? ` ${bulkPreview.skipped} righe selezionate sono già codificate e non compaiono.`
                : ""}
            </p>
            <GridScroller
              className="rounded-md border"
              scrollerClassName="max-h-[50vh]"
            >
              <Table>
                <TableHeader className="bg-muted">
                  <TableRow className="hover:bg-transparent">
                    <TableHead>Vecchio codice</TableHead>
                    <TableHead>Nuovo codice</TableHead>
                    <TableHead>Descrizione</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(bulkPreview?.items ?? []).map((item) => (
                    <TableRow key={item.id}>
                      <TableCell className="font-medium tabular-nums">
                        {item.codice}
                      </TableCell>
                      <TableCell className="font-medium tabular-nums text-primary">
                        {item.newCode}
                      </TableCell>
                      <TableCell>
                        <span
                          className="block max-w-[280px] truncate"
                          title={decodeHtmlEntities(item.descr)}
                        >
                          {decodeHtmlEntities(item.descr) || "—"}
                        </span>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </GridScroller>
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              disabled={bulkCommitMutation.isPending}
              onClick={cancelBulkPreview}
            >
              Annulla
            </Button>
            <Button
              disabled={bulkCommitMutation.isPending}
              onClick={() => bulkCommitMutation.mutate()}
            >
              {bulkCommitMutation.isPending ? "Assegnazione…" : "Assegna"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
