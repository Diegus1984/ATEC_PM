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
import { fetchCodex } from "@/lib/api/codex"
import { fetchCompositionChildren } from "@/lib/api/codex-compositions"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  importOfficinaComposition,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import type {
  CodexListItem,
  OfficinaImportCompositionResult,
} from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { euro, dash } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"

import { CodexGeneratePanel } from "../codex/CodexGeneratePanel"
import { DDP_STATUS_TO_ORDER } from "./ddp-constants"

const PAGE_SIZE = 50

interface PickerColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = colonna non filtrabile). */
  filterParam?: string
  cell: (item: CodexListItem) => React.ReactNode
}

const COLUMNS: PickerColumn[] = [
  {
    key: "codice",
    label: "Codice",
    filterParam: "codice",
    cell: (item) => <span className="font-medium tabular-nums">{item.codice}</span>,
  },
  {
    key: "descr",
    label: "Descrizione",
    filterParam: "descr",
    cell: (item) => (
      <span className="block max-w-[320px] truncate" title={item.descr}>
        {dash(item.descr)}
      </span>
    ),
  },
  {
    key: "um",
    label: "UM",
    cell: (item) => dash(item.um),
  },
  {
    key: "fornitore",
    label: "Fornitore",
    filterParam: "fornitore",
    cell: (item) => (
      <span className="block max-w-[180px] truncate" title={item.fornitore}>
        {dash(item.fornitore)}
      </span>
    ),
  },
  {
    key: "categoria",
    label: "Categoria",
    filterParam: "categoria",
    cell: (item) => dash(item.categoria),
  },
  {
    key: "prezzoForn",
    label: "Costo",
    align: "right",
    cell: (item) => <span className="tabular-nums">{euro(item.prezzoForn)}</span>,
  },
]

/**
 * Picker articoli dal Codex per la DDP officina (gemello di `CodexPickerWindow`
 * del WPF). Doppio clic (o «+») su una riga = aggiunge il particolare con Qtà=1,
 * stato DO e richiedente = utente corrente, copiando codice/descrizione/costo/
 * fornitore dal Codex. «Nuovo codice Codex» genera un codice e lo aggiunge subito.
 * Se esiste già una riga in stato «Da Ordinare», chiede se aggiungere +1 alla
 * quantità; altrimenti inserisce una nuova riga. Se il codice è un padre di
 * composizione Codex, propone l'import dei figli diretti in distinta. Resta
 * aperto per inserimenti multipli, come la finestra WPF.
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
  const requestedBy = getSession()?.user.fullName ?? ""

  const [filters, setFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(filters, 300)
  const [addedCount, setAddedCount] = React.useState(0)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)
  const [showGenerate, setShowGenerate] = React.useState(false)

  React.useEffect(() => {
    if (open) {
      setFilters({})
      setAddedCount(0)
      setMessage(null)
      setError(null)
      setShowGenerate(false)
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
    queryKey: ["codex-picker", debouncedFilters],
    queryFn: ({ pageParam }) =>
      fetchCodex({
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

  // Se l'articolo appena aggiunto è un padre di composizione Codex, propone l'import
  // dei figli diretti in distinta (i codici già presenti sommano le quantità).
  // Le quantità dei componenti vengono moltiplicate per la Qtà del padre in distinta
  // (parentQty qui serve solo per il testo: il valore autorevole lo rilegge il server).
  const maybeImportComposition = React.useCallback(
    async (
      item: CodexListItem,
      parentQty: number
    ): Promise<OfficinaImportCompositionResult | null> => {
      const children = await fetchCompositionChildren(item.id)
      const codexChildren = children.filter((ch) => ch.source === "codex")
      if (codexChildren.length === 0) return null
      const pieces = codexChildren.reduce((sum, ch) => sum + ch.quantity, 0)
      const piecesText =
        parentQty > 1
          ? `${pieces} pezzi × ${parentQty} (Qtà del padre) = ${pieces * parentQty} pezzi totali`
          : `${pieces} pezzi totali`
      const ok = await confirm({
        title: "Importare la composizione?",
        description: `${item.codice} è un padre di composizione: ${codexChildren.length} componenti (${piecesText}).\n\nVuoi importare anche i componenti nella DDP Officina? I codici già presenti sommeranno le quantità.`,
        confirmLabel: "Importa componenti",
        destructive: false,
      })
      if (!ok) return null
      return importOfficinaComposition(projectId, {
        codexParentId: item.id,
        requestedBy,
      })
    },
    [confirm, projectId, requestedBy]
  )

  const addMutation = useMutation({
    mutationFn: async (item: CodexListItem) => {
      setError(null)
      // Dedup solo su righe «Da Ordinare»: se esiste, propone +1; altrimenti
      // nuova riga (anche se lo stesso codice è già in altro stato).
      const existing = await fetchOfficinaItems(projectId)
      const duplicate = existing.find(
        (r) =>
          r.partNumber === item.codice && r.itemStatus === DDP_STATUS_TO_ORDER
      )
      if (duplicate) {
        const ok = await confirm({
          title: "Articolo già presente",
          description: `L'articolo ${item.codice} è già nella DDP Officina in stato Da Ordinare (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
          confirmLabel: "Aggiungi +1",
          destructive: false,
        })
        if (!ok) return null
        // L'update officina riscrive tutti i campi editabili: vanno ricopiati dalla riga esistente.
        await updateOfficinaItem(projectId, duplicate.id, {
          id: duplicate.id,
          projectId,
          partNumber: duplicate.partNumber,
          description: duplicate.description,
          quantity: duplicate.quantity + 1,
          quantityProduced: duplicate.quantityProduced ?? 0,
          unitCost: duplicate.unitCost,
          material: duplicate.material,
          treatment: duplicate.treatment,
          supplierName: duplicate.supplierName,
          itemStatus: duplicate.itemStatus,
          requestedBy: duplicate.requestedBy,
          daneaRef: duplicate.daneaRef,
          dateNeeded: duplicate.dateNeeded,
          orderDate: duplicate.orderDate,
          destination: duplicate.destination,
          destinationSpec: duplicate.destinationSpec ?? "",
          notes: duplicate.notes,
          expectedUpdatedAt: null,
        })
        // Se i componenti sono già collegati («comanda il padre»), il +1 li ha già
        // riallineati lato server: un secondo import sommerebbe un set doppio.
        const hasLinkedChildren = existing.some(
          (r) => r.parentOfficinaItemId === duplicate.id
        )
        const imported = hasLinkedChildren
          ? null
          : await maybeImportComposition(item, duplicate.quantity + 1)
        return {
          code: item.codice,
          updated: true,
          imported,
          childrenFollowed: hasLinkedChildren,
        }
      }

      await addOfficinaItem(projectId, {
        id: 0,
        projectId,
        partNumber: item.codice,
        description: item.descr,
        quantity: 1,
        quantityProduced: 0,
        unitCost: item.prezzoForn,
        material: "",
        treatment: "",
        supplierName: item.fornitore,
        itemStatus: DDP_STATUS_TO_ORDER,
        requestedBy,
        daneaRef: "",
        dateNeeded: null,
        orderDate: null,
        destination: "",
        destinationSpec: "",
        notes: "",
      })
      const imported = await maybeImportComposition(item, 1)
      return { code: item.codice, updated: false, imported, childrenFollowed: false }
    },
    onSuccess: (result) => {
      if (!result) return
      setAddedCount((n) => n + 1)
      const mult =
        result.imported && result.imported.parentQuantity !== 1
          ? ` ×${result.imported.parentQuantity}`
          : ""
      const compo = result.imported
        ? ` + composizione${mult} (${result.imported.added} nuovi, ${result.imported.updated} aggiornati)`
        : result.childrenFollowed
          ? " (componenti allineati al padre)"
          : ""
      setMessage(
        result.updated
          ? `✓ Qtà aggiornata per ${result.code}${compo}`
          : `✓ ${result.code} aggiunto${compo}`
      )
      onAdded()
    },
    onError: (err: Error) => setError(err.message),
  })

  function handleAdd(item: CodexListItem) {
    if (addMutation.isPending) return
    addMutation.mutate(item)
  }

  // Codice appena generato: lo rileggo dal Codex (per codice esatto) e lo aggiungo subito.
  async function handleGenerated(codice: string) {
    setShowGenerate(false)
    try {
      const page = await fetchCodex({ filters: { codice }, pageSize: 50 })
      const created = page.items.find((i) => i.codice === codice)
      if (!created) {
        setError(`Codice ${codice} generato ma non ritrovato nel Codex.`)
        return
      }
      await query.refetch()
      addMutation.mutate(created)
    } catch (err) {
      setError((err as Error).message)
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
          <div className="flex items-start justify-between gap-4">
            <div className="space-y-1.5">
              <DialogTitle>Aggiungi da Codex</DialogTitle>
              <DialogDescription>
                Doppio clic su un articolo per aggiungerlo alla distinta officina
                (Qtà = 1). Per i codici con composizione Codex viene proposto
                l&apos;import dei componenti.
              </DialogDescription>
            </div>
            <Button
              size="sm"
              variant="outline"
              className="shrink-0"
              onClick={() => setShowGenerate((v) => !v)}
            >
              <Plus />
              Nuovo codice Codex
            </Button>
          </div>
        </DialogHeader>

        {showGenerate ? (
          <CodexGeneratePanel
            onClose={() => setShowGenerate(false)}
            onGenerated={handleGenerated}
          />
        ) : null}

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
                      "Errore nel caricamento del Codex."}
                  </TableCell>
                </TableRow>
              ) : query.isLoading ? (
                <TableRow>
                  <TableCell
                    colSpan={COLUMNS.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Caricamento Codex…
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
                        title="Aggiungi alla distinta officina"
                        disabled={addMutation.isPending}
                        onClick={() => handleAdd(item)}
                      >
                        <Plus />
                        <span className="sr-only">Aggiungi {item.codice}</span>
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
