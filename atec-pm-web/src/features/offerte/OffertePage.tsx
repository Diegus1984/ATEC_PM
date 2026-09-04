import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Copy,
  FilePlus2,
  FolderOpen,
  Pencil,
  Phone,
  Plus,
  RefreshCw,
  Search,
  Trash2,
  Archive,
} from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { useCopyText } from "@/components/shared/copy-text"
import { GridScroller } from "@/components/shared/grid-scroller"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { ServerPagination } from "@/components/shared/server-pagination"
import { SortableHeader, type SortState } from "@/components/shared/sortable-header"
import { StatusDot } from "@/components/shared/status-dot"
import { Badge } from "@/components/ui/badge"
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
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { canWriteFeature } from "@/lib/auth/permissions"
import {
  deleteSalesOffer,
  fetchNextOfferNumber,
  fetchSalesOffers,
  fetchSalesOfferSellers,
  fetchSalesOfferTypes,
} from "@/lib/api/sales-offers"
import type { SalesOffer } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { dash, euro } from "@/lib/format"
import { useSalesOffersHub } from "@/lib/signalr/use-sales-offers-hub"
import { notifyError, notifySuccess } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

import { BulkCloseDialog } from "./BulkCloseDialog"
import { OfferDialog } from "./OfferDialog"
import { OfferFollowupDialog } from "./OfferFollowupDialog"
import { chanceLabel } from "./sales-offer-number"
import {
  SALES_OFFER_STATUS_FILTERS,
  salesOfferStatusDot,
  salesOfferStatusLabel,
} from "./sales-offer-status"

const PAGE_SIZE = 50

/**
 * 🪤 Chiave VERSIONATA. Chi ha già usato la griglia si porta dietro il proprio salvataggio:
 * senza alzare la versione, una colonna nuova nascerebbe nascosta per lui e visibile per
 * tutti gli altri — e a segnalarlo sarebbe lui, non il codice.
 */
const COLUMN_STORAGE_KEY = "atec_pm_offerte_columns_v1"

interface OfferColumn {
  key: string
  label: string
  defaultHidden?: boolean
  align?: "right"
  /** La colonna non ha una chiave di ordinamento sul server: niente freccette. */
  noSort?: boolean
  cell: (offer: SalesOffer) => React.ReactNode
}

const COLUMNS: OfferColumn[] = [
  {
    key: "number",
    label: "Numero",
    cell: (o) => (
      <span className="font-medium tabular-nums">
        {o.composed}
        {o.isLegacySeries ? (
          <Badge variant="outline" className="ml-2 text-[10px]" title="Serie 2025 proseguita nel 2026: non alza il progressivo">
            serie vecchia
          </Badge>
        ) : null}
      </span>
    ),
  },
  {
    key: "date",
    label: "Data",
    cell: (o) => <span className="tabular-nums">{formatDateShort(o.offerDate)}</span>,
  },
  {
    key: "customer",
    label: "Cliente",
    cell: (o) => (
      <span
        className="block max-w-[280px] truncate"
        // Il nome dell'offerta e quello della rubrica possono differire: si mostra il
        // primo (è quello scritto sul documento) e il secondo va nel suggerimento.
        title={
          o.customerCompanyName && o.customerCompanyName !== o.customerName
            ? `In rubrica: ${o.customerCompanyName}`
            : o.customerName
        }
      >
        {dash(o.customerName)}
        {/* Il pallino segnala un nome NON collegato: sulle 19 bozze, che un nome non ce
            l'hanno ancora, sarebbe solo rumore. */}
        {o.customerName && !o.customerId ? (
          <span className="ml-1 text-muted-foreground" title="Nome non collegato alla rubrica">
            •
          </span>
        ) : null}
      </span>
    ),
  },
  {
    key: "description",
    label: "Descrizione",
    noSort: true,
    cell: (o) => (
      <span className="block max-w-[320px] truncate" title={o.description}>
        {dash(o.description)}
      </span>
    ),
  },
  {
    key: "amount",
    label: "Importo",
    align: "right",
    cell: (o) => (
      <span className="tabular-nums">
        {euro(o.amount)}
        {/* Forbice di prezzo: «fino a» esiste solo se maggiore della base. */}
        {o.amountMax != null && o.amountMax > (o.amount ?? 0) ? (
          <span className="block text-xs text-muted-foreground">
            fino a {euro(o.amountMax)}
          </span>
        ) : null}
      </span>
    ),
  },
  {
    key: "status",
    label: "Esito",
    cell: (o) => (
      <StatusDot color={salesOfferStatusDot(o.status)}>
        {salesOfferStatusLabel(o.status)}
      </StatusDot>
    ),
  },
  {
    key: "chance",
    label: "Chance",
    align: "right",
    cell: (o) => (
      <span className="tabular-nums text-muted-foreground">
        {o.chance == null ? "—" : chanceLabel(o.chance)}
      </span>
    ),
  },
  {
    key: "seller",
    label: "Venditore",
    cell: (o) => (
      <span title={o.sellerName}>{dash(o.sellerCode)}</span>
    ),
  },
  {
    key: "type",
    label: "Tipo",
    cell: (o) => <span title={o.typeName}>{dash(o.typeCode)}</span>,
  },
  {
    key: "orderAmount",
    label: "Ordine",
    align: "right",
    defaultHidden: true,
    cell: (o) =>
      o.isTimeMaterial ? (
        <Badge variant="secondary">a consuntivo</Badge>
      ) : (
        <span className="tabular-nums">{euro(o.orderAmount)}</span>
      ),
  },
  {
    key: "project",
    label: "Commessa",
    noSort: true,
    defaultHidden: true,
    cell: (o) => dash(o.projectCode),
  },
  {
    key: "quote",
    label: "Preventivo",
    noSort: true,
    defaultHidden: true,
    cell: (o) => dash(o.quoteNumber),
  },
  {
    key: "contact",
    label: "Referente",
    noSort: true,
    defaultHidden: true,
    cell: (o) => dash(o.contactName),
  },
  {
    key: "nextContact",
    label: "Prossimo contatto",
    noSort: true,
    cell: (o) => (
      <span className="tabular-nums">{formatDateShort(o.nextContact)}</span>
    ),
  },
  {
    key: "tag",
    label: "Tag",
    noSort: true,
    defaultHidden: true,
    cell: (o) => dash(o.tag),
  },
]

const DEFAULT_VISIBILITY = Object.fromEntries(
  COLUMNS.map((c) => [c.key, !c.defaultHidden])
)

/** Anni selezionabili nel filtro: dal 2022 (prima riga dello storico) a oggi. */
function anniDisponibili(): number[] {
  const corrente = new Date().getFullYear()
  const anni: number[] = []
  for (let a = corrente; a >= 2022; a--) anni.push(a)
  return anni
}

export function OffertePage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const copyText = useCopyText()

  const canEdit = canWriteFeature("action.edit_offerta")
  const canDelete = canWriteFeature("action.delete_offerta")
  const canBulkClose = canWriteFeature("action.bulk_close_offerte")

  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput, 300)
  const [year, setYear] = React.useState<string>(String(new Date().getFullYear()))
  const [status, setStatus] = React.useState("")
  const [sellerCode, setSellerCode] = React.useState("")
  const [typeCode, setTypeCode] = React.useState("")
  const [sort, setSort] = React.useState<SortState>({ by: "number", dir: "desc" })
  const [page, setPage] = React.useState(1)
  const [visibility, setVisibility] = usePersistedColumnVisibility(
    COLUMN_STORAGE_KEY,
    DEFAULT_VISIBILITY
  )

  const [editing, setEditing] = React.useState<SalesOffer | null>(null)
  const [creating, setCreating] = React.useState(false)
  const [followupOn, setFollowupOn] = React.useState<SalesOffer | null>(null)
  const [chiusuraInBlocco, setChiusuraInBlocco] = React.useState(false)

  React.useEffect(() => {
    setPage(1)
  }, [searchTerm, year, status, sellerCode, typeCode, sort])

  const listQuery = useQuery({
    queryKey: ["sales-offers", page, searchTerm, year, status, sellerCode, typeCode, sort],
    queryFn: () =>
      fetchSalesOffers({
        page,
        pageSize: PAGE_SIZE,
        search: searchTerm,
        year: year === "all" ? null : Number(year),
        status,
        sellerCode,
        typeCode,
        sortBy: sort.by,
        sortDir: sort.dir,
      }),
  })

  // Anagrafiche: cambiano di rado, ma restano dati operativi — nessuno `staleTime`.
  const typesQuery = useQuery({
    queryKey: ["sales-offer-types"],
    queryFn: () => fetchSalesOfferTypes(),
  })
  const sellersQuery = useQuery({
    queryKey: ["sales-offer-sellers"],
    queryFn: () => fetchSalesOfferSellers(),
  })

  const annoProssimo = year === "all" ? new Date().getFullYear() : Number(year)
  const nextNumberQuery = useQuery({
    queryKey: ["sales-offer-next-number", annoProssimo],
    queryFn: () => fetchNextOfferNumber(annoProssimo),
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["sales-offers"] })
    void queryClient.invalidateQueries({ queryKey: ["sales-offer-next-number"] })
  }, [queryClient])

  // Diretta: il registro lo compilano più venditori insieme, e il numero che stai per usare
  // può essere già stato preso da un altro.
  useSalesOffersHub(true, invalidate)

  const deleteMutation = useMutation({
    mutationFn: deleteSalesOffer,
    onSuccess: () => {
      notifySuccess("Offerta eliminata")
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (offer: SalesOffer) => {
      const ok = await confirm({
        title: "Elimina offerta",
        description: `Eliminare l'offerta ${offer.composed} — ${offer.customerName || "senza cliente"}?\n\nSpariscono anche i contatti col referente e lo storico modifiche. L'operazione non è reversibile.`,
        confirmLabel: "Elimina",
        destructive: true,
      })
      if (ok) deleteMutation.mutate(offer.id)
    },
    [confirm, deleteMutation]
  )

  /**
   * I percorsi NAS si **copiano**, non si aprono: in produzione il gestionale gira in HTTP e
   * dal browser un file su `\\NAS-ATEC\…` non si apre. Era l'unica cosa che il vecchio
   * applicativo faceva meglio, perché aveva un avviatore locale.
   */
  const handleCopyPath = React.useCallback(
    (path: string, cosa: string) => {
      void copyText(path, `Percorso ${cosa} copiato`)
    },
    [copyText]
  )

  const visibleColumns = COLUMNS.filter((c) => visibility[c.key])
  const items = listQuery.data?.items ?? []
  const total = listQuery.data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const showActions = canEdit || canDelete
  const colSpan = visibleColumns.length + (showActions ? 1 : 0)

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Registro Offerte</CardTitle>
              <CardDescription>
                Numerazione univoca, esito e chance di tutte le offerte emesse
                {listQuery.data ? ` — ${total} nel filtro` : ""}
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {nextNumberQuery.data ? (
                <span className="rounded-md border bg-muted/40 px-3 py-1.5 text-sm">
                  Prossimo numero{" "}
                  <span className="font-medium tabular-nums">
                    {String(nextNumberQuery.data.number).padStart(3, "0")}/
                    {nextNumberQuery.data.year}
                  </span>
                </span>
              ) : null}
              {canBulkClose ? (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => setChiusuraInBlocco(true)}
                  title="Porta a «persa» le offerte ancora aperte degli anni già conclusi"
                >
                  <Archive />
                  Chiudi anni passati
                </Button>
              ) : null}
              {canEdit ? (
                <Button size="sm" onClick={() => setCreating(true)}>
                  <Plus />
                  Nuova offerta
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
                placeholder="Cerca per numero, cliente, descrizione…"
                className="pl-8"
                onChange={(event) => setSearchInput(event.target.value)}
              />
            </div>

            <Select value={year} onValueChange={setYear}>
              <SelectTrigger size="sm" className="w-[130px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tutti gli anni</SelectItem>
                {anniDisponibili().map((a) => (
                  <SelectItem key={a} value={String(a)}>
                    {a}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={status || "all"} onValueChange={(v) => setStatus(v === "all" ? "" : v)}>
              <SelectTrigger size="sm" className="w-[150px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SALES_OFFER_STATUS_FILTERS.map((f) => (
                  <SelectItem key={f.value || "all"} value={f.value || "all"}>
                    {f.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select
              value={sellerCode || "all"}
              onValueChange={(v) => setSellerCode(v === "all" ? "" : v)}
            >
              <SelectTrigger size="sm" className="w-[160px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tutti i venditori</SelectItem>
                {(sellersQuery.data ?? []).map((s) => (
                  <SelectItem key={s.code} value={s.code}>
                    {s.name ? `${s.code} — ${s.name}` : s.code}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select
              value={typeCode || "all"}
              onValueChange={(v) => setTypeCode(v === "all" ? "" : v)}
            >
              <SelectTrigger size="sm" className="w-[170px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tutti i tipi</SelectItem>
                {(typesQuery.data ?? []).map((t) => (
                  <SelectItem key={t.code} value={t.code}>
                    {t.code} — {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <div className="ml-auto flex gap-2">
              <ColumnsMenu
                columns={COLUMNS.map((column) => ({
                  id: column.key,
                  label: column.label,
                  checked: visibility[column.key],
                  onToggle: (value) =>
                    setVisibility((prev) => ({ ...prev, [column.key]: value })),
                }))}
              />
              <Button
                variant="outline"
                size="sm"
                onClick={() => listQuery.refetch()}
                disabled={listQuery.isFetching}
              >
                <RefreshCw className={listQuery.isFetching ? "animate-spin" : undefined} />
                Aggiorna
              </Button>
            </div>
          </div>

          {listQuery.isError ? (
            <p className="text-sm text-destructive">
              {(listQuery.error as Error).message || "Errore nel caricamento delle offerte."}
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
                      {column.noSort ? (
                        <span className="px-2 text-sm font-medium">{column.label}</span>
                      ) : (
                        <SortableHeader
                          label={column.label}
                          columnKey={column.key}
                          sort={sort}
                          onSort={setSort}
                          align={column.align}
                        />
                      )}
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
                      Nessuna offerta con questi filtri.
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((offer) => (
                    <TableRow
                      key={offer.id}
                      className={canEdit ? "cursor-pointer" : undefined}
                      onDoubleClick={canEdit ? () => setEditing(offer) : undefined}
                    >
                      {visibleColumns.map((column) => (
                        <TableCell
                          key={column.key}
                          className={column.align === "right" ? "text-right" : undefined}
                        >
                          {column.cell(offer)}
                        </TableCell>
                      ))}
                      {showActions ? (
                        <TableCell className="text-right">
                          <RowActionsMenu
                            label={offer.composed}
                            actions={[
                              ...(canEdit
                                ? [
                                    {
                                      label: "Apri scheda",
                                      icon: Pencil,
                                      onClick: () => setEditing(offer),
                                    },
                                    {
                                      label: "Annota contatto",
                                      icon: Phone,
                                      onClick: () => setFollowupOn(offer),
                                    },
                                    {
                                      label: "Duplica",
                                      icon: FilePlus2,
                                      onClick: () => setEditing({ ...offer, id: 0 }),
                                    },
                                  ]
                                : []),
                              ...(offer.offerPath
                                ? [
                                    {
                                      label: "Copia percorso offerta",
                                      icon: FolderOpen,
                                      separatorBefore: true,
                                      onClick: () =>
                                        handleCopyPath(offer.offerPath, "dell'offerta"),
                                    },
                                  ]
                                : []),
                              ...(offer.calcPath
                                ? [
                                    {
                                      label: "Copia percorso calcolo",
                                      icon: Copy,
                                      onClick: () =>
                                        handleCopyPath(offer.calcPath, "della tabella di calcolo"),
                                    },
                                  ]
                                : []),
                              ...(canDelete
                                ? [
                                    {
                                      label: "Elimina",
                                      icon: Trash2,
                                      destructive: true,
                                      separatorBefore: true,
                                      onClick: () => void handleDelete(offer),
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
            totalCount={total}
            itemNoun="offerte"
            emptyLabel="Nessuna offerta"
            disabled={listQuery.isFetching}
            onPageChange={setPage}
          />
        </CardContent>
      </Card>

      {creating || editing ? (
        <OfferDialog
          open
          offer={editing}
          types={typesQuery.data ?? []}
          sellers={sellersQuery.data ?? []}
          defaultYear={annoProssimo}
          onClose={() => {
            setCreating(false)
            setEditing(null)
          }}
          onSaved={() => {
            setCreating(false)
            setEditing(null)
            invalidate()
          }}
        />
      ) : null}

      {chiusuraInBlocco ? (
        <BulkCloseDialog
          open
          onClose={() => setChiusuraInBlocco(false)}
          onDone={() => {
            setChiusuraInBlocco(false)
            invalidate()
          }}
        />
      ) : null}

      {followupOn ? (
        <OfferFollowupDialog
          open
          offer={followupOn}
          onClose={() => setFollowupOn(null)}
          onSaved={() => {
            setFollowupOn(null)
            invalidate()
          }}
        />
      ) : null}
    </div>
  )
}
