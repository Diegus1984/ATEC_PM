import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowRightLeft, Check, Image as ImageIcon, Search, TriangleAlert } from "lucide-react"

import { ColumnFilterCombobox } from "@/components/shared/column-filter-combobox"
import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { ServerPagination } from "@/components/shared/server-pagination"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
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
  fetchDaneaMigrationStatus,
  fetchDaneaOldArticles,
  transferDaneaArticles,
  type DaneaOldArticle,
  type DaneaTransferReport,
} from "@/lib/api/danea-migration"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

const PAGE_SIZE = 50
const MAX_BATCH = 500

function uniqueSorted(values: Iterable<string>): string[] {
  return [...new Set([...values].map((v) => v.trim()).filter(Boolean))].sort((a, b) =>
    a.localeCompare(b, "it", { sensitivity: "base" })
  )
}

/**
 * Colonne della griglia articoli del vecchio archivio: `filter` è il parametro del
 * filtro di colonna (null = colonna senza filtro). Menu «Colonne»: BLOCKS-RULES.md.
 */
const DANEA_COLUMNS: {
  id: string
  label: string
  filter: string | null
  headClass?: string
}[] = [
  { id: "codArticolo", label: "Codice", filter: "codArticolo" },
  { id: "descrizione", label: "Descrizione", filter: "descrizione" },
  { id: "categoria", label: "Categoria", filter: "categoria" },
  { id: "sottocategoria", label: "Sottocategoria", filter: "sottocategoria" },
  { id: "udm", label: "UM", filter: null },
  { id: "fornitore", label: "Fornitore", filter: "fornitore" },
  { id: "produttore", label: "Produttore", filter: "produttore" },
  { id: "prezzoForn", label: "€ Forn.", filter: null, headClass: "text-right" },
  { id: "extra1", label: "Cod. ATEC", filter: "extra1" },
  { id: "hasImage", label: "Img", filter: null, headClass: "text-center" },
  { id: "stato", label: "Stato", filter: null },
]
const DANEA_COLUMNS_DEFAULT = Object.fromEntries(
  DANEA_COLUMNS.map((column) => [column.id, true])
)

/**
 * F2 migrazione Danea: trasferimento selettivo degli articoli dal vecchio archivio
 * al nuovo «Atec_PM» (articolo + fornitore già agganciato + IVA/categoria/prezzi +
 * immagini). Il vecchio archivio è di sola lettura; il badge «In Atec_PM» segna
 * l'avanzamento. Selezione multi-pagina, lotti max 500.
 */
export function DaneaMigrationPage() {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [page, setPage] = React.useState(1)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebounced(searchInput.trim(), 350)
  const [onlyMissing, setOnlyMissing] = React.useState(true)
  /** Filtri per colonna (chiave = parametro server), debounced come la ricerca. */
  const [columnFilters, setColumnFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(columnFilters, 300)
  /** Selezione multi-pagina: id articolo → codice (per il riepilogo). */
  const [selected, setSelected] = React.useState<Map<number, string>>(new Map())
  const [report, setReport] = React.useState<DaneaTransferReport | null>(null)

  const [visible, setVisible] = usePersistedColumnVisibility(
    "danea-migrazione-columns-v1",
    DANEA_COLUMNS_DEFAULT
  )
  const show = (id: string) => visible[id] ?? true
  const visibleColumns = DANEA_COLUMNS.filter((column) => show(column.id))
  const columnToggles = DANEA_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: show(id),
    onToggle: (value: boolean) =>
      setVisible((prev) => ({ ...prev, [id]: value })),
  }))

  const setColumnFilter = React.useCallback((param: string, value: string) => {
    setColumnFilters((prev) => {
      const next = { ...prev }
      if (value.trim()) next[param] = value
      else delete next[param]
      return next
    })
  }, [])

  React.useEffect(() => {
    setPage(1)
  }, [search, onlyMissing, debouncedFilters])

  const statusQuery = useQuery({
    queryKey: ["danea-migration-status"],
    queryFn: fetchDaneaMigrationStatus,
  })
  const listQuery = useQuery({
    queryKey: ["danea-migration-old", page, search, onlyMissing, debouncedFilters],
    queryFn: () =>
      fetchDaneaOldArticles({
        page,
        pageSize: PAGE_SIZE,
        search,
        onlyMissing,
        filters: debouncedFilters,
      }),
  })

  const items = React.useMemo(() => listQuery.data?.items ?? [], [listQuery.data])
  const totalCount = listQuery.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const status = statusQuery.data

  /** Liste filtro: dallo status (completo), altrimenti valori distinti della pagina. */
  const categoryOptions = React.useMemo(() => {
    const fromStatus = status?.categories ?? []
    if (fromStatus.length > 0) return fromStatus
    return uniqueSorted(items.map((i) => i.categoria))
  }, [status?.categories, items])

  const subcategoryOptions = React.useMemo(() => {
    const fromStatus = status?.subcategories ?? []
    if (fromStatus.length > 0) return fromStatus
    return uniqueSorted(items.map((i) => i.sottocategoria))
  }, [status?.subcategories, items])

  const supplierOptions = React.useMemo(() => {
    const fromStatus = status?.suppliers ?? []
    if (fromStatus.length > 0) return fromStatus
    return uniqueSorted(items.map((i) => i.fornitore))
  }, [status?.suppliers, items])

  const manufacturerOptions = React.useMemo(() => {
    const fromStatus = status?.manufacturers ?? []
    if (fromStatus.length > 0) return fromStatus
    return uniqueSorted(items.map((i) => i.produttore))
  }, [status?.manufacturers, items])

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["danea-migration-status"] })
    void queryClient.invalidateQueries({ queryKey: ["danea-migration-old"] })
  }, [queryClient])

  const selectableOnPage = items.filter((i) => !i.transferred)
  const allPageSelected =
    selectableOnPage.length > 0 &&
    selectableOnPage.every((i) => selected.has(i.idArticolo))

  const toggleRow = (item: DaneaOldArticle) => {
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(item.idArticolo)) next.delete(item.idArticolo)
      else next.set(item.idArticolo, item.codArticolo)
      return next
    })
  }

  const togglePage = () => {
    setSelected((prev) => {
      const next = new Map(prev)
      if (allPageSelected)
        for (const i of selectableOnPage) next.delete(i.idArticolo)
      else for (const i of selectableOnPage) next.set(i.idArticolo, i.codArticolo)
      return next
    })
  }

  const transferMutation = useMutation({
    mutationFn: (ids: number[]) => transferDaneaArticles(ids),
    onSuccess: (rep) => {
      setReport(rep)
      setSelected(new Map())
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const handleTransfer = async () => {
    const ids = [...selected.keys()]
    if (ids.length === 0 || transferMutation.isPending) return
    if (ids.length > MAX_BATCH) {
      notifyError(`Massimo ${MAX_BATCH} articoli per lotto: riduci la selezione.`)
      return
    }
    const ok = await confirm({
      title: "Trasferire in Atec_PM?",
      description:
        `${ids.length} articoli verranno creati nel nuovo archivio Danea «Atec_PM» ` +
        "con fornitore, IVA, categoria, prezzi correnti e immagini. " +
        "Niente giacenze: ripartono da zero.",
      confirmLabel: `Trasferisci ${ids.length} articoli`,
    })
    if (ok) transferMutation.mutate(ids)
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Trasferimento catalogo Danea
          </h1>
          <p className="text-sm text-muted-foreground">
            Dal vecchio archivio ({status?.oldArticles ?? "…"} articoli) al nuovo
            «Atec_PM» ({status?.newArticles ?? "…"} trasferiti). Il vecchio non
            viene mai modificato.
          </p>
        </div>
        {status && (!status.imagesSourceReachable || !status.imagesTargetReachable) ? (
          <div className="flex flex-col items-start gap-1 sm:items-end">
            <Badge variant="destructive" className="gap-1">
              <TriangleAlert className="size-3.5" />
              Cartella immagini non raggiungibile: gli articoli passano senza foto
            </Badge>
            {status.imagesError ? (
              <p className="max-w-md text-xs text-muted-foreground sm:text-right">
                {status.imagesError}
              </p>
            ) : null}
          </div>
        ) : null}
      </div>

      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <CardTitle>Catalogo vecchio archivio</CardTitle>
              <CardDescription>
                {onlyMissing
                  ? `${totalCount} articoli ancora da trasferire`
                  : `${totalCount} articoli totali`}
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
                <Input
                  value={searchInput}
                  placeholder="Cerca codice, descrizione, fornitore…"
                  className="w-72 pl-8"
                  onChange={(e) => setSearchInput(e.target.value)}
                />
              </div>
              <ColumnsMenu columns={columnToggles} />
              <Button
                variant={onlyMissing ? "default" : "outline"}
                size="sm"
                onClick={() => setOnlyMissing((v) => !v)}
              >
                Solo da trasferire
              </Button>
              <Button
                size="sm"
                disabled={selected.size === 0 || transferMutation.isPending}
                onClick={() => void handleTransfer()}
              >
                <ArrowRightLeft />
                {transferMutation.isPending
                  ? "Trasferimento…"
                  : `Trasferisci (${selected.size})`}
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <GridScroller className="rounded-md border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead className="w-9">
                    <Checkbox
                      checked={allPageSelected}
                      disabled={selectableOnPage.length === 0}
                      onCheckedChange={togglePage}
                      aria-label="Seleziona pagina"
                    />
                  </TableHead>
                  {visibleColumns.map((column) => (
                    <TableHead key={column.id} className={column.headClass}>
                      {column.label}
                    </TableHead>
                  ))}
                </TableRow>
                <TableRow className="hover:bg-transparent">
                  <TableHead />
                  {visibleColumns.map(({ id, filter: param }) => (
                    <TableHead key={id} className="h-auto px-2 py-2 align-middle">
                      {param === "categoria" ? (
                        <ColumnFilterCombobox
                          value={columnFilters.categoria ?? ""}
                          onChange={(value) => setColumnFilter("categoria", value)}
                          options={categoryOptions}
                          placeholder="Categoria…"
                          searchPlaceholder="Cerca categoria…"
                          loading={statusQuery.isLoading && categoryOptions.length === 0}
                          emptyText="Nessuna categoria"
                        />
                      ) : param === "sottocategoria" ? (
                        <ColumnFilterCombobox
                          value={columnFilters.sottocategoria ?? ""}
                          onChange={(value) => setColumnFilter("sottocategoria", value)}
                          options={subcategoryOptions}
                          placeholder="Sottocat.…"
                          searchPlaceholder="Cerca sottocategoria…"
                          loading={statusQuery.isLoading && subcategoryOptions.length === 0}
                          emptyText="Nessuna sottocategoria"
                        />
                      ) : param === "fornitore" ? (
                        <ColumnFilterCombobox
                          value={columnFilters.fornitore ?? ""}
                          onChange={(value) => setColumnFilter("fornitore", value)}
                          options={supplierOptions}
                          placeholder="Fornitore…"
                          searchPlaceholder="Cerca fornitore…"
                          loading={statusQuery.isLoading && supplierOptions.length === 0}
                          emptyText="Nessun fornitore"
                        />
                      ) : param === "produttore" ? (
                        <ColumnFilterCombobox
                          value={columnFilters.produttore ?? ""}
                          onChange={(value) => setColumnFilter("produttore", value)}
                          options={manufacturerOptions}
                          placeholder="Produttore…"
                          searchPlaceholder="Cerca produttore…"
                          loading={statusQuery.isLoading && manufacturerOptions.length === 0}
                          emptyText="Nessun produttore"
                        />
                      ) : param ? (
                        <ColumnFilterInput
                          value={columnFilters[param] ?? ""}
                          onChange={(value) => setColumnFilter(param, value)}
                        />
                      ) : null}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {listQuery.isLoading ? (
                  <TableRow>
                    <TableCell colSpan={visibleColumns.length + 1} className="h-16 text-center text-muted-foreground">
                      Caricamento dal vecchio archivio…
                    </TableCell>
                  </TableRow>
                ) : listQuery.error ? (
                  <TableRow>
                    <TableCell colSpan={visibleColumns.length + 1} className="h-16 text-center text-destructive">
                      {(listQuery.error as Error).message}
                    </TableCell>
                  </TableRow>
                ) : items.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={visibleColumns.length + 1} className="h-16 text-center text-muted-foreground">
                      {onlyMissing && !search
                        ? "Niente da trasferire: catalogo completo."
                        : "Nessun articolo corrisponde."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.idArticolo}
                      className={item.transferred ? "opacity-60" : undefined}
                    >
                      <TableCell>
                        <Checkbox
                          checked={selected.has(item.idArticolo)}
                          disabled={item.transferred}
                          onCheckedChange={() => toggleRow(item)}
                          aria-label={`Seleziona ${item.codArticolo}`}
                        />
                      </TableCell>
                      {show("codArticolo") && (
                        <TableCell className="font-medium">{item.codArticolo}</TableCell>
                      )}
                      {show("descrizione") && (
                        <TableCell className="max-w-[280px] truncate" title={item.descrizione}>
                          {item.descrizione || "—"}
                        </TableCell>
                      )}
                      {show("categoria") && (
                        <TableCell className="max-w-[140px] truncate" title={item.categoria}>
                          {item.categoria || "—"}
                        </TableCell>
                      )}
                      {show("sottocategoria") && (
                        <TableCell
                          className="max-w-[140px] truncate"
                          title={item.sottocategoria}
                        >
                          {item.sottocategoria || "—"}
                        </TableCell>
                      )}
                      {show("udm") && <TableCell>{item.udm || "—"}</TableCell>}
                      {show("fornitore") && (
                        <TableCell className="max-w-[160px] truncate">
                          {item.fornitore || "—"}
                        </TableCell>
                      )}
                      {show("produttore") && (
                        <TableCell className="max-w-[140px] truncate" title={item.produttore}>
                          {item.produttore || "—"}
                        </TableCell>
                      )}
                      {show("prezzoForn") && (
                        <TableCell className="text-right tabular-nums">
                          {euro(item.prezzoForn)}
                        </TableCell>
                      )}
                      {show("extra1") && (
                        <TableCell className="tabular-nums">{item.extra1 || "—"}</TableCell>
                      )}
                      {show("hasImage") && (
                        <TableCell className="text-center">
                          {item.hasImage ? (
                            <ImageIcon className="mx-auto size-4 text-muted-foreground" />
                          ) : (
                            "—"
                          )}
                        </TableCell>
                      )}
                      {show("stato") && (
                        <TableCell>
                          {item.transferred ? (
                            <Badge variant="secondary" className="gap-1">
                              <Check className="size-3" />
                              In Atec_PM
                            </Badge>
                          ) : (
                            <span className="text-xs text-muted-foreground">
                              Da trasferire
                            </span>
                          )}
                        </TableCell>
                      )}
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

      <TransferReportDialog report={report} onClose={() => setReport(null)} />
    </div>
  )
}

function TransferReportDialog({
  report,
  onClose,
}: {
  report: DaneaTransferReport | null
  onClose: () => void
}) {
  if (!report) return null
  const problems = report.results.filter(
    (r) => r.outcome === "error" || r.imageWarning
  )
  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Trasferimento completato</DialogTitle>
          <DialogDescription>
            {report.ok} trasferiti · {report.skipped} già presenti ·{" "}
            {report.errors} errori · {report.imagesCopied} file immagine copiati
          </DialogDescription>
        </DialogHeader>
        {report.catalogWarning ? (
          <p className="text-sm text-amber-700">{report.catalogWarning}</p>
        ) : report.catalogAligned ? (
          <p className="text-sm text-muted-foreground">
            Catalogo articoli già aggiornato ({report.catalogAligned}{" "}
            {report.catalogAligned === 1 ? "riga" : "righe"}): li trovi subito in
            elenco, non serve «Sincronizza Danea».
          </p>
        ) : null}
        {problems.length > 0 ? (
          <div className="max-h-64 space-y-1 overflow-y-auto rounded-md border p-2 text-sm">
            {problems.map((r) => (
              <p key={r.idArticolo}>
                <span className="font-medium">{r.codArticolo || `#${r.idArticolo}`}</span>
                {": "}
                <span className={r.outcome === "error" ? "text-destructive" : "text-amber-700"}>
                  {r.outcome === "error" ? r.error : r.imageWarning}
                </span>
              </p>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Nessun problema segnalato.</p>
        )}
        <DialogFooter>
          <Button onClick={onClose}>Chiudi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
