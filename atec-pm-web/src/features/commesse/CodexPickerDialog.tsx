import * as React from "react"
import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
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
import { GridScroller } from "@/components/shared/grid-scroller"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { fetchCatalogItems } from "@/lib/api/catalog"
import { createDdpRow, fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import type { CatalogItemListItem } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { euro, dash } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

import { DDP_STATUS_VERIFY } from "./ddp-constants"

const PAGE_SIZE = 50
const ALL_FAMILIES = "__all__"

// Le famiglie della codifica commerciale: le sole a cui il generatore scrive il
// codice ATEC (CodexGeneratorService, flag newFamily) — le altre (101, 301, 5xx…)
// non sono acquisti commerciali.
const FAMILIES: { code: string; label: string }[] = [
  { code: "201", label: "201 — Commerciale generico" },
  { code: "211", label: "211 — Commerciale elettrico" },
  { code: "221", label: "221 — Commerciale pneumatico" },
]

/** Punto prima delle ultime 3 cifre (stessa formattazione della pagina Codex). */
function formatCodice(codice: string): string {
  const raw = (codice ?? "").replace(/\./g, "")
  return raw.length > 3 ? `${raw.slice(0, raw.length - 3)}.${raw.slice(-3)}` : raw
}

interface PickerColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = colonna non filtrabile). */
  filterParam?: string
}

// Definizione statica (etichette e filtri); le celle si disegnano nel render,
// dove servono permessi e handler. La visibilità è scelta dal menu «Colonne».
const COLUMNS: PickerColumn[] = [
  { key: "atecCode", label: "Cod. ATEC", filterParam: "atecCode" },
  { key: "code", label: "Codice", filterParam: "code" },
  { key: "description", label: "Descrizione", filterParam: "description" },
  { key: "unit", label: "UM" },
  { key: "supplierName", label: "Fornitore", filterParam: "supplier" },
  { key: "manufacturer", label: "Produttore", filterParam: "manufacturer" },
  { key: "unitCost", label: "Costo", align: "right" },
]

const DEFAULT_VISIBILITY: Record<string, boolean> = Object.fromEntries(
  COLUMNS.map((column) => [column.key, true])
)

/**
 * Picker per la DDP commerciale: la vista è il Catalogo (si cerca anche per codice
 * commerciale, fornitore, produttore quando il codice Codex non lo si ricorda), ma
 * **si aggiunge SOLO chi ha il codice ATEC**: le righe senza mostrano «Codifica» e
 * si sistemano sul posto (stesso dialog del Catalogo Articoli). Così ogni riga di
 * distinta nasce ancorata al SUO codice ATEC. Tendina per famiglia (201/211/221,
 * filtra il codice ATEC) e menu «Colonne» per scegliere cosa vedere.
 *
 * Doppio clic o «+» = aggiunge con Qtà 1, stato Verificare magazzino, copiando
 * codice/descrizione/UM/costo/fornitore/produttore dell'articolo scelto; se il
 * codice ATEC è già in DDP in quello stato propone +1. Resta aperto per
 * inserimenti multipli, come sempre.
 */
export function CodexPickerDialog({
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
  const queryClient = useQueryClient()
  const requestedBy = getSession()?.user.fullName ?? ""
  const canAssignAtec = canWriteFeature("action.assign_atec_code")

  const [family, setFamily] = React.useState(ALL_FAMILIES)
  const [filters, setFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(filters, 300)
  const [addedCount, setAddedCount] = React.useState(0)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)
  // Codifica al volo dell'articolo senza codice ATEC (pulsante «Codifica»).
  const [atecTarget, setAtecTarget] = React.useState<CatalogItemListItem | null>(null)

  const [visibility, setVisibility] = usePersistedColumnVisibility(
    "ddp-catalog-picker-cols-v1",
    DEFAULT_VISIBILITY
  )
  const visibleColumns = COLUMNS.filter((column) => visibility[column.key] !== false)

  // Reset alla riapertura: niente residui dell'ultima sessione di inserimento
  // (la scelta delle colonne invece resta: è una preferenza, non un filtro).
  React.useEffect(() => {
    if (open) {
      setFamily(ALL_FAMILIES)
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

  // La tendina famiglia filtra il codice ATEC; il filtro digitato in colonna vince
  // (un solo LIKE per colonna lato server, e chi digita sta già cercando più stretto).
  const effectiveFilters = React.useMemo(() => {
    const merged = { ...debouncedFilters }
    if (!merged.atecCode && family !== ALL_FAMILIES) {
      merged.atecCode = `${family}*`
    }
    return merged
  }, [debouncedFilters, family])

  const query = useInfiniteQuery({
    queryKey: ["catalog-picker", effectiveFilters],
    queryFn: ({ pageParam }) =>
      fetchCatalogItems({
        page: pageParam,
        pageSize: PAGE_SIZE,
        filters: effectiveFilters,
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
      const atecRaw = (item.atecCode || "").replace(/\./g, "").trim()
      // Il cancello del picker: senza codice ATEC non si entra in distinta.
      if (!atecRaw || !item.codexItemId) return null

      // Dedup per codice ATEC solo su righe nello stato di ingresso (VER): se c'è
      // già, propone +1; altrimenti nuova riga (anche se presente in altro stato).
      const existing = await fetchDdpRows(projectId, "COMMERCIAL")
      const duplicate = existing.find(
        (r) =>
          (r.atecCode ?? "").replace(/\./g, "") === atecRaw &&
          r.itemStatus === DDP_STATUS_VERIFY
      )
      if (duplicate) {
        const ok = await confirm({
          title: "Codice già presente",
          description: `Il codice ${formatCodice(atecRaw)} è già nella DDP in stato Verificare magazzino (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
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
        itemStatus: DDP_STATUS_VERIFY,
        requestedBy,
        daneaRef: "",
        dateNeeded: null,
        destination: "",
        destinationSpec: "",
        notes: "",
        ddpType: "COMMERCIAL",
        atecCode: atecRaw,
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
    const atecRaw = (item.atecCode || "").replace(/\./g, "").trim()
    if (!atecRaw || !item.codexItemId) {
      setMessage(null)
      setError(
        `${item.code} è senza codice Codex: codificalo (pulsante «Codifica») prima di metterlo in distinta.`
      )
      return
    }
    addMutation.mutate(item)
  }

  function renderCell(column: PickerColumn, item: CatalogItemListItem) {
    switch (column.key) {
      case "atecCode":
        return item.atecCode && item.codexItemId ? (
          <span
            className="font-medium tabular-nums text-primary"
            title="Codice Codex associato: è questo che entra in distinta"
          >
            {formatCodice(item.atecCode)}
          </span>
        ) : (
          <Button
            size="sm"
            variant="outline"
            className="h-6 px-2 text-xs"
            disabled={!canAssignAtec}
            title={
              canAssignAtec
                ? "Associa (o crea) il codice Codex di questo articolo"
                : "Serve il permesso di codifica"
            }
            onClick={(event) => {
              event.stopPropagation()
              setAtecTarget(item)
            }}
          >
            Codifica
          </Button>
        )
      case "code":
        return <span className="font-medium">{item.code}</span>
      case "description":
        return (
          <span className="block max-w-[320px] truncate" title={item.description}>
            {dash(item.description)}
          </span>
        )
      case "unit":
        return dash(item.unit)
      case "supplierName":
        return (
          <span className="block max-w-[180px] truncate" title={item.supplierName}>
            {dash(item.supplierName)}
          </span>
        )
      case "manufacturer":
        return dash(item.manufacturer)
      case "unitCost":
        return <span className="tabular-nums">{euro(item.unitCost)}</span>
      default:
        return null
    }
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
            Doppio clic per aggiungere alla distinta (Qtà = 1). Si aggiungono solo
            gli articoli col codice Codex: gli altri si codificano prima, col
            pulsante «Codifica» della riga.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-muted-foreground">Famiglia:</span>
          <Select value={family} onValueChange={setFamily}>
            <SelectTrigger size="sm" className="w-64">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_FAMILIES}>Tutte le famiglie</SelectItem>
              {FAMILIES.map((f) => (
                <SelectItem key={f.code} value={f.code}>
                  {f.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <ColumnsMenu
            className="ml-auto"
            modal={false}
            columns={COLUMNS.map((column) => ({
              id: column.key,
              label: column.label,
              checked: visibility[column.key] !== false,
              onToggle: (checked) =>
                setVisibility((prev) => ({ ...prev, [column.key]: checked })),
            }))}
          />
        </div>

        <GridScroller fill className="rounded-lg border" onScroll={handleScroll}>
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                {visibleColumns.map((column) => (
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
                {visibleColumns.map((column) => (
                  <TableHead key={column.key} className="h-auto px-2 py-2 align-middle">
                    {column.filterParam ? (
                      <ColumnFilterInput
                        value={filters[column.filterParam] ?? ""}
                        onChange={(value) => setColumnFilter(column.filterParam!, value)}
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
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-destructive"
                  >
                    {(query.error as Error).message ||
                      "Errore nel caricamento del catalogo."}
                  </TableCell>
                </TableRow>
              ) : query.isLoading ? (
                <TableRow>
                  <TableCell
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Caricamento catalogo…
                  </TableCell>
                </TableRow>
              ) : items.length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Nessun articolo corrisponde ai filtri.
                  </TableCell>
                </TableRow>
              ) : (
                items.map((item) => {
                  const codificato = Boolean(item.atecCode && item.codexItemId)
                  return (
                    <TableRow
                      key={item.id}
                      className="cursor-pointer"
                      onDoubleClick={() => handleAdd(item)}
                    >
                      {visibleColumns.map((column) => (
                        <TableCell
                          key={column.key}
                          className={
                            column.align === "right" ? "text-right" : undefined
                          }
                        >
                          {renderCell(column, item)}
                        </TableCell>
                      ))}
                      <TableCell className="text-right">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title={
                            codificato
                              ? "Aggiungi alla distinta"
                              : "Senza codice Codex: prima la codifica"
                          }
                          disabled={addMutation.isPending || !codificato}
                          onClick={() => handleAdd(item)}
                        >
                          <Plus />
                          <span className="sr-only">Aggiungi {item.code}</span>
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })
              )}
            </TableBody>
          </Table>
          {query.isFetchingNextPage ? (
            <p className="py-2 text-center text-sm text-muted-foreground">
              Caricamento…
            </p>
          ) : null}
        </GridScroller>

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

      {/* Codifica al volo: stesso dialog del Catalogo Articoli. Dopo il salvataggio
          l'elenco si ricarica e l'articolo diventa aggiungibile. */}
      <CatalogAtecAssignDialog
        item={atecTarget}
        onClose={() => setAtecTarget(null)}
        onSaved={() => {
          setAtecTarget(null)
          void queryClient.invalidateQueries({ queryKey: ["catalog-picker"] })
        }}
      />
    </Dialog>
  )
}
