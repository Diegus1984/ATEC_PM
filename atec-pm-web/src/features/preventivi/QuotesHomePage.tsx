import * as React from "react"
import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import {
  Building2,
  ChevronDown,
  ChevronRight,
  Copy,
  Download,
  FileText,
  FilterX,
  GitBranch,
  Mail,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  SquarePen,
  Trash2,
} from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { notifyError, notifyInfo } from "@/lib/toast"
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  changeQuoteStatus,
  deleteQuote,
  duplicateQuote,
  createQuoteRevision,
  fetchQuoteChains,
  fetchQuotePdf,
  fetchQuotes,
} from "@/lib/api/quotes"
import type { QuoteDto } from "@/lib/api/types"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import { ConvertQuoteDialog } from "./ConvertQuoteDialog"
import { NewQuoteDialog } from "./NewQuoteDialog"
import {
  QUOTE_STATUS_FILTERS,
  QUOTE_STATUSES,
  quoteStatusMeta,
} from "./quote-status"
import { quoteTypeBadge, quoteTypeLabel } from "./quote-type"

const VIEW_PREF_KEY = "QuotesHomePage.ViewMode"
const COLUMN_STORAGE_KEY = "atec_pm_quotes_columns"

type QuoteColumnFilterParam = "quoteNumber" | "customerName" | "title"

interface QuoteGridColumn {
  key: string
  label: string
  headerClass?: string
  align?: "right"
  defaultHidden?: boolean
  hideable?: boolean
  filterParam?: QuoteColumnFilterParam
}

const QUOTE_GRID_COLUMNS: QuoteGridColumn[] = [
  { key: "expand", label: "", headerClass: "w-8", hideable: false },
  {
    key: "quoteNumber",
    label: "Numero",
    headerClass: "w-44",
    filterParam: "quoteNumber",
  },
  { key: "quoteType", label: "Tipo", headerClass: "w-16" },
  { key: "createdAt", label: "Data", headerClass: "w-24" },
  {
    key: "customerName",
    label: "Cliente",
    headerClass: "min-w-40",
    filterParam: "customerName",
  },
  {
    key: "title",
    label: "Titolo",
    headerClass: "min-w-48",
    filterParam: "title",
  },
  { key: "total", label: "Totale", headerClass: "w-28", align: "right" },
  { key: "profit", label: "Utile", headerClass: "w-28", align: "right" },
  { key: "status", label: "Stato", headerClass: "w-48" },
  {
    key: "createdByName",
    label: "Agente",
    headerClass: "w-28",
    defaultHidden: true,
  },
  {
    key: "actions",
    label: "Azioni",
    headerClass: "w-[260px]",
    align: "right",
    hideable: false,
  },
]

function defaultColumnVisibility(): Record<string, boolean> {
  return Object.fromEntries(
    QUOTE_GRID_COLUMNS.map((col) => [col.key, !col.defaultHidden])
  )
}

function loadColumnVisibility(): Record<string, boolean> {
  try {
    const raw = localStorage.getItem(COLUMN_STORAGE_KEY)
    if (!raw) return defaultColumnVisibility()
    const parsed = JSON.parse(raw) as Record<string, boolean>
    const defaults = defaultColumnVisibility()
    return { ...defaults, ...parsed }
  } catch {
    return defaultColumnVisibility()
  }
}

function fmt2(value: number): string {
  return value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? "" : d.toLocaleDateString("it-IT")
}

// ── Catene di revisione (port di QuotesHomePage) ───────────

function getChain(merged: QuoteDto[], masterId: number): QuoteDto[] {
  return merged
    .filter((q) => q.id === masterId || q.parentQuoteId === masterId)
    .sort((a, b) => b.revision - a.revision || b.createdAt.localeCompare(a.createdAt))
}

function pickDisplayQuote(chain: QuoteDto[]): QuoteDto {
  const active = chain
    .filter((q) => q.status !== "superseded" && q.status !== "converted")
    .sort((a, b) => b.revision - a.revision || b.createdAt.localeCompare(a.createdAt))[0]
  return active ?? chain[0]
}

interface GroupRow {
  display: QuoteDto
  masterId: number
  revCount: number
  subRows: QuoteDto[]
}

function buildGroups(merged: QuoteDto[]): GroupRow[] {
  const roots = merged.filter((q) => q.parentQuoteId == null)
  const rootIds = new Set(roots.map((r) => r.id))
  const groups: GroupRow[] = []
  for (const root of roots) {
    const chain = getChain(merged, root.id)
    const display = pickDisplayQuote(chain)
    const subRows = chain.filter((q) => q.id !== display.id)
    groups.push({ display, masterId: root.id, revCount: chain.length - 1, subRows })
  }
  // Revisioni orfane (master non presente fra i root, es. filtro che matcha solo la revisione).
  const orphans = merged.filter(
    (q) => q.parentQuoteId != null && !rootIds.has(q.parentQuoteId)
  )
  for (const orphan of orphans) {
    groups.push({
      display: orphan,
      masterId: orphan.parentQuoteId ?? orphan.id,
      revCount: 0,
      subRows: [],
    })
  }
  return groups
}

function canConvert(q: QuoteDto): boolean {
  return q.quoteType === "IMPIANTO" && q.status === "accepted"
}

// ── Badge tipo Service / Impianto ──────────────────────────

function TypeBadge({ quoteType }: { quoteType: string }) {
  const isImp = quoteType === "IMPIANTO"
  return (
    <span
      className="inline-block rounded px-1.5 py-0.5 text-[10px] font-bold"
      style={{
        backgroundColor: isImp ? "#FFF7ED" : "#F0FDF4",
        color: isImp ? "#EA580C" : "#059669",
      }}
    >
      {quoteTypeBadge(quoteType)}
    </span>
  )
}

// ── Pill stato statica (superseded / converted) ────────────

function StaticStatusBadge({ status }: { status: string }) {
  if (status === "converted") {
    return (
      <span className="inline-block rounded px-2 py-0.5 text-[10px] font-semibold" style={{ backgroundColor: "#D1FAE5", color: "#059669" }}>
        CONVERTITO
      </span>
    )
  }
  return (
    <span className="inline-block rounded px-2 py-0.5 text-[10px] font-semibold" style={{ backgroundColor: "#E5E7EB", color: "#6B7280" }}>
      SUPERATA
    </span>
  )
}

// ── Select stato inline ────────────────────────────────────

function StatusSelect({
  status,
  disabled,
  onChange,
}: {
  status: string
  disabled: boolean
  onChange: (next: string) => void
}) {
  const meta = quoteStatusMeta(status)
  return (
    <Select value={status} disabled={disabled} onValueChange={onChange}>
      <SelectTrigger
        size="sm"
        className="h-7 w-[180px] border-transparent text-xs font-semibold"
        style={meta ? { backgroundColor: meta.bg, color: meta.fg } : undefined}
      >
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {QUOTE_STATUSES.map((s) => (
          <SelectItem key={s.key} value={s.key}>
            <span className="flex items-center gap-2">
              <span
                className="inline-block size-2.5 rounded-full"
                style={{ backgroundColor: s.dot }}
              />
              {s.label}
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

// ── PAGINA ─────────────────────────────────────────────────

export function QuotesHomePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [search, setSearch] = React.useState("")
  const [statusFilter, setStatusFilter] = React.useState("")
  const [typeFilter, setTypeFilter] = React.useState("")
  const [fNumber, setFNumber] = React.useState("")
  const [fCustomer, setFCustomer] = React.useState("")
  const [fTitle, setFTitle] = React.useState("")
  const [columnVisibility, setColumnVisibility] = React.useState(loadColumnVisibility)

  const debSearch = useDebounced(search.trim(), 300)
  const debNumber = useDebounced(fNumber.trim(), 300)
  const debCustomer = useDebounced(fCustomer.trim(), 300)
  const debTitle = useDebounced(fTitle.trim(), 300)

  const [view, setView] = React.useState<"grid" | "grouped">(() =>
    localStorage.getItem(VIEW_PREF_KEY) === "grouped" ? "grouped" : "grid"
  )
  const [expanded, setExpanded] = React.useState<Set<number>>(new Set())
  const [newOpen, setNewOpen] = React.useState(false)
  const [convert, setConvert] = React.useState<{ quoteId: number; quoteNumber: string } | null>(null)

  const filters = React.useMemo(
    () => ({
      search: debSearch,
      status: statusFilter,
      quoteType: typeFilter,
      quoteNumber: debNumber,
      customerName: debCustomer,
      title: debTitle,
    }),
    [debSearch, statusFilter, typeFilter, debNumber, debCustomer, debTitle]
  )

  const quotesQuery = useInfiniteQuery({
    queryKey: ["quotes", filters],
    queryFn: ({ pageParam }) =>
      fetchQuotes({ ...filters, page: pageParam, pageSize: 50 }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
  })

  const pageItems = React.useMemo(
    () => quotesQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [quotesQuery.data]
  )
  const totalCount = quotesQuery.data?.pages[0]?.totalCount ?? 0

  const masterIds = React.useMemo(
    () =>
      Array.from(new Set(pageItems.map((q) => q.parentQuoteId ?? q.id))).sort(
        (a, b) => a - b
      ),
    [pageItems]
  )

  const chainsQuery = useQuery({
    queryKey: ["quote-chains", masterIds.join(",")],
    queryFn: () => fetchQuoteChains(masterIds),
    enabled: masterIds.length > 0,
  })

  const merged = React.useMemo(() => {
    const map = new Map<number, QuoteDto>()
    for (const q of pageItems) map.set(q.id, q)
    for (const q of chainsQuery.data ?? []) if (!map.has(q.id)) map.set(q.id, q)
    return Array.from(map.values())
  }, [pageItems, chainsQuery.data])

  const groups = React.useMemo(() => buildGroups(merged), [merged])

  function reload() {
    void queryClient.invalidateQueries({ queryKey: ["quotes"] })
    void queryClient.invalidateQueries({ queryKey: ["quote-chains"] })
  }

  // ── Mutazioni ──
  const statusMutation = useMutation({
    mutationFn: (vars: { id: number; newStatus: string }) =>
      changeQuoteStatus(vars.id, { newStatus: vars.newStatus, notes: "" }),
    onSuccess: reload,
    onError: (err: Error) => {
      notifyError(err)
      reload()
    },
  })
  const revisionMutation = useMutation({
    mutationFn: (vars: { id: number; masterId: number }) =>
      createQuoteRevision(vars.id),
    onSuccess: (_id, vars) => {
      setExpanded((prev) => new Set(prev).add(vars.masterId))
      reload()
    },
    onError: (err: Error) => notifyError(err),
  })
  const duplicateMutation = useMutation({
    mutationFn: (id: number) => duplicateQuote(id),
    onSuccess: reload,
    onError: (err: Error) => notifyError(err),
  })
  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteQuote(id),
    onSuccess: reload,
    onError: (err: Error) => notifyError(err),
  })

  // ── PDF ──
  async function openPdf(id: number) {
    try {
      const blob = await fetchQuotePdf(id)
      window.open(URL.createObjectURL(blob), "_blank")
    } catch (err) {
      notifyError(err)
    }
  }
  async function downloadPdf(id: number, quoteNumber: string) {
    try {
      const blob = await fetchQuotePdf(id)
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement("a")
      anchor.href = url
      anchor.download = `${(quoteNumber || "Preventivo").replace(/\//g, "-")}.pdf`
      anchor.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      notifyError(err)
    }
  }

  // ── Azioni ──
  function openDetail(id: number, readOnly = false) {
    navigate(`/preventivi/${id}${readOnly ? "?readonly=1" : ""}`)
  }

  async function handleRevision(q: QuoteDto, masterId: number) {
    const ok = await confirm({
      title: "Crea revisione",
      description: `Creare una nuova revisione partendo da ${q.quoteNumber}? Viene copiato il contenuto di questa versione; la versione scelta diventa SUPERATA, le altre restano nello storico.`,
      confirmLabel: "Crea revisione",
    })
    if (ok) revisionMutation.mutate({ id: q.id, masterId })
  }

  async function handleDeleteMaster(q: QuoteDto) {
    if (q.status !== "draft") {
      notifyError("Solo i preventivi in bozza possono essere eliminati.")
      return
    }
    const ok = await confirm({
      title: "Elimina preventivo",
      description: `Eliminare il preventivo ${q.quoteNumber}?`,
      confirmLabel: "Elimina",
      destructive: true,
    })
    if (ok) deleteMutation.mutate(q.id)
  }

  async function handleDeleteRevision(rev: QuoteDto, masterId: number) {
    const ok = await confirm({
      title: "Elimina revisione",
      description: `Eliminare la revisione ${rev.quoteNumber}? Se è l'ultima revisione, la precedente verrà riattivata.`,
      confirmLabel: "Elimina",
      destructive: true,
    })
    if (!ok) return
    const chain = getChain(merged, masterId)
    const isLast = chain.length > 0 && chain[0].id === rev.id
    try {
      await deleteQuote(rev.id)
      if (isLast && chain.length > 1 && chain[1].status === "superseded") {
        await changeQuoteStatus(chain[1].id, { newStatus: "draft", notes: "" })
      }
      reload()
    } catch (err) {
      notifyError(err)
    }
  }

  async function handleReactivate(rev: QuoteDto) {
    const ok = await confirm({
      title: "Riattiva revisione",
      description: `Riattivare la revisione ${rev.quoteNumber}? Diventerà di nuovo BOZZA.`,
      confirmLabel: "Riattiva",
    })
    if (ok) statusMutation.mutate({ id: rev.id, newStatus: "draft" })
  }

  function setViewMode(next: "grid" | "grouped") {
    setView(next)
    localStorage.setItem(VIEW_PREF_KEY, next)
  }

  function toggleExpand(masterId: number) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(masterId)) next.delete(masterId)
      else next.add(masterId)
      return next
    })
  }

  // Ordinamento/raggruppamento per la vista.
  const orderedGroups = React.useMemo(() => {
    if (view !== "grouped") return groups
    return [...groups].sort(
      (a, b) =>
        a.display.customerName.localeCompare(b.display.customerName) ||
        b.display.createdAt.localeCompare(a.display.createdAt)
    )
  }, [groups, view])

  const shownCount = groups.length
  const totalValue = groups.reduce((acc, g) => acc + g.display.total, 0)
  const totalProfit = groups.reduce((acc, g) => acc + g.display.profit, 0)

  const visibleColumns = React.useMemo(
    () => QUOTE_GRID_COLUMNS.filter((col) => columnVisibility[col.key] !== false),
    [columnVisibility]
  )
  const hasColumnFilters = !!(debNumber || debCustomer || debTitle)
  const hasAnyFilters = !!(
    debSearch ||
    statusFilter ||
    typeFilter ||
    hasColumnFilters
  )

  function getColumnFilterValue(param: QuoteColumnFilterParam): string {
    if (param === "quoteNumber") return fNumber
    if (param === "customerName") return fCustomer
    return fTitle
  }

  function setColumnFilterValue(param: QuoteColumnFilterParam, value: string) {
    if (param === "quoteNumber") setFNumber(value)
    else if (param === "customerName") setFCustomer(value)
    else setFTitle(value)
  }

  function clearAllFilters() {
    setSearch("")
    setStatusFilter("")
    setTypeFilter("")
    setFNumber("")
    setFCustomer("")
    setFTitle("")
  }

  function setColumnVisible(key: string, visible: boolean) {
    setColumnVisibility((prev) => {
      const next = { ...prev, [key]: visible }
      try {
        localStorage.setItem(COLUMN_STORAGE_KEY, JSON.stringify(next))
      } catch {
        /* storage opzionale */
      }
      return next
    })
  }

  // Costruzione righe di rendering (con header gruppo per «Per Cliente»).
  const renderRows: React.ReactNode[] = []
  let lastCustomer: string | null = null
  for (const group of orderedGroups) {
    if (view === "grouped" && group.display.customerName !== lastCustomer) {
      lastCustomer = group.display.customerName
      const count = orderedGroups.filter(
        (g) => g.display.customerName === lastCustomer
      ).length
      renderRows.push(
        <TableRow key={`hdr-${lastCustomer}`} className="bg-[#EFF6FF] hover:bg-[#EFF6FF]">
          <TableCell colSpan={visibleColumns.length} className="py-1.5">
            <span className="font-bold text-[#1E3A5F]">{lastCustomer}</span>
            <span className="ml-2 rounded bg-[#DBEAFE] px-1.5 py-0.5 text-[11px] font-semibold text-[#2563EB]">
              {count} prev
            </span>
          </TableCell>
        </TableRow>
      )
    }
    renderRows.push(
      <QuoteRow
        key={`q-${group.masterId}-${group.display.id}`}
        group={group}
        visibleColumns={visibleColumns}
        isExpanded={expanded.has(group.masterId)}
        onToggle={() => toggleExpand(group.masterId)}
        onOpenDetail={openDetail}
        onStatus={(newStatus) => statusMutation.mutate({ id: group.display.id, newStatus })}
        onPreview={() => openPdf(group.display.id)}
        onDownload={() => downloadPdf(group.display.id, group.display.quoteNumber)}
        onRevision={() => handleRevision(group.display, group.masterId)}
        onDuplicate={() => duplicateMutation.mutate(group.display.id)}
        onConvert={() =>
          setConvert({ quoteId: group.display.id, quoteNumber: group.display.quoteNumber })
        }
        onSend={() => notifyInfo("Funzione invio email in arrivo!")}
        onDelete={() => handleDeleteMaster(group.display)}
      />
    )
    if (expanded.has(group.masterId)) {
      for (const sub of group.subRows) {
        renderRows.push(
          <QuoteSubRow
            key={`sub-${sub.id}`}
            quote={sub}
            masterId={group.masterId}
            visibleColumns={visibleColumns}
            onOpenDetail={openDetail}
            onPreview={() => openPdf(sub.id)}
            onDownload={() => downloadPdf(sub.id, sub.quoteNumber)}
            onRevision={() => handleRevision(sub, group.masterId)}
            onReactivate={() => handleReactivate(sub)}
            onDelete={() => handleDeleteRevision(sub, group.masterId)}
          />
        )
      }
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <CardTitle>Preventivi</CardTitle>
              <CardDescription>
                {totalCount > 0
                  ? `${shownCount} in vista${pageItems.length < totalCount ? ` · ${pageItems.length} di ${totalCount} caricati` : ""}`
                  : "Gestione preventivi commerciale"}
              </CardDescription>
            </div>
            <Button size="sm" onClick={() => setNewOpen(true)}>
              <Plus className="size-4" />
              Crea preventivo
            </Button>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative max-w-sm flex-1">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                value={search}
                placeholder="Cerca numero, titolo, cliente, agente…"
                className="pl-8"
                onChange={(event) => setSearch(event.target.value)}
              />
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-2">
              {hasAnyFilters ? (
                <Button variant="outline" size="sm" onClick={clearAllFilters}>
                  <FilterX />
                  Pulisci filtri
                </Button>
              ) : null}
              <ColumnsMenu
                columns={QUOTE_GRID_COLUMNS.filter((col) => col.hideable !== false).map(
                  (col) => ({
                    id: col.key,
                    label: col.label || col.key,
                    checked: columnVisibility[col.key] !== false,
                    onToggle: (value) => setColumnVisible(col.key, value),
                  })
                )}
              />
              <Button
                size="sm"
                variant="outline"
                onClick={reload}
                disabled={quotesQuery.isFetching}
              >
                <RefreshCw className={quotesQuery.isFetching ? "animate-spin" : ""} />
                Aggiorna
              </Button>
              <div className="flex overflow-hidden rounded-md border">
                <button
                  type="button"
                  className={cn(
                    "px-3 py-1.5 text-xs",
                    view === "grid" ? "bg-primary text-primary-foreground" : "bg-background"
                  )}
                  onClick={() => setViewMode("grid")}
                >
                  Griglia
                </button>
                <button
                  type="button"
                  className={cn(
                    "px-3 py-1.5 text-xs",
                    view === "grouped" ? "bg-primary text-primary-foreground" : "bg-background"
                  )}
                  onClick={() => setViewMode("grouped")}
                >
                  Per cliente
                </button>
              </div>
              <Select
                value={typeFilter || "__all__"}
                onValueChange={(v) => setTypeFilter(v === "__all__" ? "" : v)}
              >
                <SelectTrigger size="sm" className="h-8 w-32">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">Tipo: tutti</SelectItem>
                  <SelectItem value="SERVICE">{quoteTypeLabel("SERVICE")}</SelectItem>
                  <SelectItem value="IMPIANTO">{quoteTypeLabel("IMPIANTO")}</SelectItem>
                </SelectContent>
              </Select>
              <Select
                value={statusFilter || "__all__"}
                onValueChange={(v) => setStatusFilter(v === "__all__" ? "" : v)}
              >
                <SelectTrigger size="sm" className="h-8 w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {QUOTE_STATUS_FILTERS.map((f) => (
                    <SelectItem key={f.value || "__all__"} value={f.value || "__all__"}>
                      {f.value === "" ? "Stato: tutti" : f.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <p className="text-xs text-muted-foreground">
            Ricerca colonne: <code className="font-mono">abc</code> contiene ·{" "}
            <code className="font-mono">abc*</code> inizia con ·{" "}
            <code className="font-mono">*abc</code> finisce con
          </p>

          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((col) => (
                    <TableHead
                      key={col.key}
                      className={cn(
                        col.headerClass,
                        col.align === "right" && "text-right"
                      )}
                    >
                      {col.label}
                    </TableHead>
                  ))}
                </TableRow>
                <TableRow className="hover:bg-transparent">
                  {visibleColumns.map((col) => (
                    <TableHead
                      key={`filter-${col.key}`}
                      className="h-auto px-2 py-2 align-middle"
                    >
                      {col.filterParam ? (
                        <ColumnFilterInput
                          value={getColumnFilterValue(col.filterParam)}
                          onChange={(value) =>
                            setColumnFilterValue(col.filterParam!, value)
                          }
                        />
                      ) : null}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {quotesQuery.isLoading ? (
                  <TableRow>
                    <TableCell
                      colSpan={visibleColumns.length}
                      className="h-24 text-center text-sm text-muted-foreground"
                    >
                      Caricamento…
                    </TableCell>
                  </TableRow>
                ) : renderRows.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={visibleColumns.length}
                      className="h-24 text-center text-sm text-muted-foreground"
                    >
                      {hasAnyFilters
                        ? "Nessun risultato per i filtri impostati."
                        : "Nessun preventivo."}
                    </TableCell>
                  </TableRow>
                ) : (
                  renderRows
                )}
              </TableBody>
            </Table>
          </div>

          {quotesQuery.hasNextPage ? (
            <div className="text-center">
              <Button
                variant="outline"
                size="sm"
                onClick={() => void quotesQuery.fetchNextPage()}
                disabled={quotesQuery.isFetchingNextPage}
              >
                {quotesQuery.isFetchingNextPage ? "Caricamento…" : "Carica altri preventivi"}
              </Button>
            </div>
          ) : null}

          <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-2 text-xs text-muted-foreground">
            <span>
              {shownCount} preventivi · Valore: {fmt2(totalValue)}€ · Utile:{" "}
              {fmt2(totalProfit)}€
            </span>
            <span>{visibleColumns.length} colonne visibili</span>
          </div>
        </CardContent>
      </Card>

      <NewQuoteDialog
        open={newOpen}
        onClose={() => setNewOpen(false)}
        onCreated={(id) => {
          setNewOpen(false)
          openDetail(id)
        }}
      />
      <ConvertQuoteDialog
        open={convert != null}
        quoteId={convert?.quoteId ?? null}
        quoteNumber={convert?.quoteNumber ?? ""}
        onClose={() => setConvert(null)}
        onConverted={(projectId) => {
          setConvert(null)
          reload()
          navigate(`/commesse/${projectId}`)
        }}
      />
    </div>
  )
}

// ── Riga master ────────────────────────────────────────────

interface RowActionsProps {
  onOpenDetail: (id: number, readOnly?: boolean) => void
}

function ActionButton({
  title,
  color,
  onClick,
  children,
}: {
  title: string
  color: string
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      title={title}
      className="text-white hover:opacity-90"
      style={{ backgroundColor: color }}
      onClick={onClick}
    >
      {children}
    </Button>
  )
}

function QuoteRow({
  group,
  visibleColumns,
  isExpanded,
  onToggle,
  onOpenDetail,
  onStatus,
  onPreview,
  onDownload,
  onRevision,
  onDuplicate,
  onConvert,
  onSend,
  onDelete,
}: {
  group: GroupRow
  visibleColumns: QuoteGridColumn[]
  isExpanded: boolean
  onToggle: () => void
  onStatus: (next: string) => void
  onPreview: () => void
  onDownload: () => void
  onRevision: () => void
  onDuplicate: () => void
  onConvert: () => void
  onSend: () => void
  onDelete: () => void
} & RowActionsProps) {
  const q = group.display
  const isConverted = q.status === "converted"
  const isSuperseded = q.status === "superseded"
  const isInactive = isConverted || isSuperseded

  function renderCell(col: QuoteGridColumn) {
    switch (col.key) {
      case "expand":
        return (
          <TableCell key={col.key}>
            {group.revCount > 0 ? (
              <button
                type="button"
                className="text-muted-foreground"
                title="Mostra revisioni"
                onClick={(e) => {
                  e.stopPropagation()
                  onToggle()
                }}
              >
                {isExpanded ? (
                  <ChevronDown className="size-4" />
                ) : (
                  <ChevronRight className="size-4" />
                )}
              </button>
            ) : null}
          </TableCell>
        )
      case "quoteNumber":
        return (
          <TableCell key={col.key}>
            <div className="flex items-center gap-1">
              <span className="font-semibold">{q.quoteNumber}</span>
              {group.revCount > 0 ? (
                <span className="rounded bg-[#DBEAFE] px-1 py-0.5 text-[9px] font-bold text-[#2563EB]">
                  {group.revCount} rev
                </span>
              ) : null}
            </div>
          </TableCell>
        )
      case "quoteType":
        return (
          <TableCell key={col.key}>
            <TypeBadge quoteType={q.quoteType} />
          </TableCell>
        )
      case "createdAt":
        return (
          <TableCell key={col.key} className="text-xs">
            {formatDate(q.createdAt)}
          </TableCell>
        )
      case "customerName":
        return (
          <TableCell key={col.key} className="text-[#2563EB]">
            {q.customerName}
          </TableCell>
        )
      case "title":
        return <TableCell key={col.key}>{q.title}</TableCell>
      case "total":
        return (
          <TableCell key={col.key} className="text-right font-semibold tabular-nums">
            {fmt2(q.total)}€
          </TableCell>
        )
      case "profit":
        return (
          <TableCell key={col.key} className="text-right font-bold tabular-nums text-[#16A34A]">
            {fmt2(q.profit)}€
          </TableCell>
        )
      case "status":
        return (
          <TableCell key={col.key}>
            {isInactive ? (
              <StaticStatusBadge status={q.status} />
            ) : (
              <StatusSelect status={q.status} disabled={false} onChange={onStatus} />
            )}
          </TableCell>
        )
      case "createdByName":
        return (
          <TableCell key={col.key} className="text-xs">
            {q.createdByName}
          </TableCell>
        )
      case "actions":
        return (
          <TableCell key={col.key}>
            <div className="flex items-center justify-end gap-1">
              {canConvert(q) ? (
                <ActionButton title="Converti in commessa" color="#059669" onClick={onConvert}>
                  <Building2 className="size-3.5" />
                </ActionButton>
              ) : null}
              <ActionButton title="Visualizza PDF" color="#10B981" onClick={onPreview}>
                <FileText className="size-3.5" />
              </ActionButton>
              <ActionButton title="Scarica PDF" color="#3B82F6" onClick={onDownload}>
                <Download className="size-3.5" />
              </ActionButton>
              {!isInactive ? (
                <ActionButton title="Invia al cliente" color="#8B5CF6" onClick={onSend}>
                  <Mail className="size-3.5" />
                </ActionButton>
              ) : null}
              {!isConverted ? (
                <ActionButton title="Crea revisione" color="#0891B2" onClick={onRevision}>
                  <GitBranch className="size-3.5" />
                </ActionButton>
              ) : null}
              {!isInactive ? (
                <ActionButton title="Duplica" color="#F59E0B" onClick={onDuplicate}>
                  <Copy className="size-3.5" />
                </ActionButton>
              ) : null}
              <ActionButton
                title={isConverted ? "Visualizza (sola lettura)" : "Modifica"}
                color="#06B6D4"
                onClick={() => onOpenDetail(q.id, isConverted)}
              >
                <SquarePen className="size-3.5" />
              </ActionButton>
              <ActionButton title="Elimina" color="#EF4444" onClick={onDelete}>
                <Trash2 className="size-3.5" />
              </ActionButton>
            </div>
          </TableCell>
        )
      default:
        return null
    }
  }

  return (
    <TableRow
      className="cursor-pointer"
      onDoubleClick={() => onOpenDetail(q.id, isConverted)}
    >
      {visibleColumns.map(renderCell)}
    </TableRow>
  )
}

// ── Riga revisione (sotto-riga) ────────────────────────────

function QuoteSubRow({
  quote,
  visibleColumns,
  onOpenDetail,
  onPreview,
  onDownload,
  onRevision,
  onReactivate,
  onDelete,
}: {
  quote: QuoteDto
  masterId: number
  visibleColumns: QuoteGridColumn[]
  onPreview: () => void
  onDownload: () => void
  onRevision: () => void
  onReactivate: () => void
  onDelete: () => void
} & RowActionsProps) {
  const isSuperseded = quote.status === "superseded"

  function renderCell(col: QuoteGridColumn) {
    switch (col.key) {
      case "expand":
        return <TableCell key={col.key} />
      case "quoteNumber":
        return (
          <TableCell key={col.key}>
            <div className="flex items-center gap-1 pl-4">
              <span className="text-xs text-muted-foreground">↳</span>
              <span className="rounded bg-[#DBEAFE] px-1 py-0.5 text-[9px] font-bold text-[#2563EB]">
                Rev {quote.revision}
              </span>
              <span className="font-semibold">{quote.quoteNumber}</span>
            </div>
          </TableCell>
        )
      case "quoteType":
        return (
          <TableCell key={col.key}>
            <TypeBadge quoteType={quote.quoteType} />
          </TableCell>
        )
      case "createdAt":
        return (
          <TableCell key={col.key} className="text-xs">
            {formatDate(quote.createdAt)}
          </TableCell>
        )
      case "customerName":
        return (
          <TableCell key={col.key} className="text-[#2563EB]">
            {quote.customerName}
          </TableCell>
        )
      case "title":
        return <TableCell key={col.key}>{quote.title}</TableCell>
      case "total":
        return (
          <TableCell key={col.key} className="text-right tabular-nums">
            {fmt2(quote.total)}€
          </TableCell>
        )
      case "profit":
        return (
          <TableCell key={col.key} className="text-right tabular-nums text-[#16A34A]">
            {fmt2(quote.profit)}€
          </TableCell>
        )
      case "status":
        return (
          <TableCell key={col.key}>
            <StaticStatusBadge status={quote.status} />
          </TableCell>
        )
      case "createdByName":
        return (
          <TableCell key={col.key} className="text-xs">
            {quote.createdByName}
          </TableCell>
        )
      case "actions":
        return (
          <TableCell key={col.key}>
            <div className="flex items-center justify-end gap-1">
              <ActionButton title="Visualizza PDF" color="#10B981" onClick={onPreview}>
                <FileText className="size-3.5" />
              </ActionButton>
              <ActionButton title="Scarica PDF" color="#3B82F6" onClick={onDownload}>
                <Download className="size-3.5" />
              </ActionButton>
              <ActionButton title="Crea revisione da questa versione" color="#0891B2" onClick={onRevision}>
                <GitBranch className="size-3.5" />
              </ActionButton>
              {isSuperseded ? (
                <ActionButton title="Riattiva come bozza" color="#16A34A" onClick={onReactivate}>
                  <RotateCcw className="size-3.5" />
                </ActionButton>
              ) : null}
              <ActionButton
                title="Visualizza (sola lettura)"
                color="#06B6D4"
                onClick={() => onOpenDetail(quote.id, true)}
              >
                <SquarePen className="size-3.5" />
              </ActionButton>
              <ActionButton title="Elimina revisione" color="#EF4444" onClick={onDelete}>
                <Trash2 className="size-3.5" />
              </ActionButton>
            </div>
          </TableCell>
        )
      default:
        return null
    }
  }

  return (
    <TableRow
      className={cn("cursor-pointer bg-muted/40", isSuperseded && "opacity-60")}
      onDoubleClick={() => onOpenDetail(quote.id, true)}
    >
      {visibleColumns.map(renderCell)}
    </TableRow>
  )
}
