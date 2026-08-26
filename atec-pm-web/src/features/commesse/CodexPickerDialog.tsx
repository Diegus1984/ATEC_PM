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
import { fetchCatalogByCodex } from "@/lib/api/catalog"
import { fetchCodex } from "@/lib/api/codex"
import { createDdpRow, fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import type { CodexListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { dash } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"

import { DDP_STATUS_VERIFY } from "./ddp-constants"

const PAGE_SIZE = 50
const ALL_FAMILIES = "__all__"

// Le famiglie della codifica commerciale: le sole a cui il generatore scrive
// `codice_nuovo` (CodexGeneratorService, flag newFamily), quindi le sole che possono
// comparire in questo picker. Le altre (101, 301, 5xx…) non sono acquisti commerciali.
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

/**
 * Picker per la DDP commerciale: SOLO codici ATEC (Codex), non più gli articoli
 * commerciali del catalogo. Un articolo senza codifica qui non compare — l'operatore
 * lo codifica prima (dal Catalogo o dalla Ricodifica Codex) — così ogni riga di
 * distinta nasce ancorata al SUO codice ATEC (1 ATEC = che pezzo è; il fornitore è
 * una scelta d'acquisto). Tendina per famiglia (201/211/221) o tutte.
 *
 * All'aggiunta: se il codice ha UN SOLO articolo Danea associato ne copia i dati
 * (fornitore, costo, codice); con zero o più d'uno la riga nasce «Fornitore da
 * definire» e la scelta si fa dagli Acquisti (o col picker «Per codice ATEC»).
 * Doppio clic o «+» = aggiunge con Qtà 1; se il codice è già in DDP in stato
 * «Verificare magazzino» propone +1. Resta aperto per inserimenti multipli.
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

  const [family, setFamily] = React.useState(ALL_FAMILIES)
  const [codeFilter, setCodeFilter] = React.useState("")
  const [descrFilter, setDescrFilter] = React.useState("")
  const debCode = useDebounced(codeFilter.trim(), 300)
  const debDescr = useDebounced(descrFilter.trim(), 300)
  const [addedCount, setAddedCount] = React.useState(0)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)

  // Reset alla riapertura: niente residui dell'ultima sessione di inserimento.
  React.useEffect(() => {
    if (open) {
      setFamily(ALL_FAMILIES)
      setCodeFilter("")
      setDescrFilter("")
      setAddedCount(0)
      setMessage(null)
      setError(null)
    }
  }, [open])

  // Il filtro digitato sul codice vince sulla tendina famiglia: sono entrambi LIKE su
  // `codice_nuovo` e il server ne accetta uno solo (chi digita un codice sta già
  // cercando più stretto della famiglia).
  const codiceNuovoFilter =
    debCode || (family !== ALL_FAMILIES ? `${family}*` : "")

  const query = useInfiniteQuery({
    queryKey: ["codex-picker", family, debCode, debDescr],
    queryFn: ({ pageParam }) =>
      fetchCodex({
        page: pageParam,
        pageSize: PAGE_SIZE,
        // Codificati e basta: `codice_nuovo` valorizzato (vecchi ricodificati e
        // nuovi nati dal generatore). I non codificati NON si vedono: prima la codifica.
        newCodeState: "done",
        sortBy: "codiceNuovo",
        sortDir: "asc",
        filters: {
          codiceNuovo: codiceNuovoFilter,
          descr: debDescr,
        },
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
    mutationFn: async (item: CodexListItem) => {
      setError(null)
      const atecRaw = (item.codiceNuovo ?? "").replace(/\./g, "").trim()
      if (!atecRaw) return null

      // Dedup solo su righe nello stato di ingresso (VER): se il codice ATEC c'è già,
      // propone +1; altrimenti nuova riga (anche se già presente in altro stato).
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
        return { code: formatCodice(atecRaw), updated: true, tbd: false }
      }

      // Articoli Danea associati al codice: uno solo = dati fornitore copiati subito;
      // zero o più d'uno = riga «da definire», sceglie chi compra.
      const alts = await fetchCatalogByCodex(item.id)
      const cat = alts.length === 1 ? alts[0] : null
      await createDdpRow(projectId, {
        id: 0,
        projectId,
        catalogItemId: cat?.id ?? null,
        partNumber: cat?.code ?? "",
        description: cat?.description || item.descr,
        unit: cat?.unit || "PZ",
        quantity: 1,
        unitCost: cat?.unitCost ?? 0,
        supplierId: cat?.supplierId ?? null,
        manufacturer: cat?.manufacturer ?? "",
        itemStatus: DDP_STATUS_VERIFY,
        requestedBy,
        daneaRef: "",
        dateNeeded: null,
        destination: "",
        destinationSpec: "",
        notes: cat ? "" : "Fornitore da definire",
        ddpType: "COMMERCIAL",
        atecCode: atecRaw,
        expectedUpdatedAt: null,
      })
      return { code: formatCodice(atecRaw), updated: false, tbd: !cat }
    },
    onSuccess: (result) => {
      if (!result) return
      setAddedCount((n) => n + 1)
      setMessage(
        result.updated
          ? `✓ Qtà aggiornata per ${result.code}`
          : result.tbd
            ? `✓ ${result.code} aggiunto (fornitore da definire)`
            : `✓ ${result.code} aggiunto`
      )
      onAdded()
    },
    onError: (err: Error) => setError(err.message),
  })

  function handleAdd(item: CodexListItem) {
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
      <DialogContent className="flex max-h-[88vh] flex-col gap-4 sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>Aggiungi da Codex</DialogTitle>
          <DialogDescription>
            Solo codici ATEC: un articolo non ancora codificato non compare — si
            codifica prima, dal Catalogo o dalla Ricodifica Codex. Doppio clic per
            aggiungerlo alla distinta (Qtà = 1).
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center gap-2">
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
        </div>

        <GridScroller fill className="rounded-lg border" onScroll={handleScroll}>
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="w-44">Cod. ATEC</TableHead>
                <TableHead>Descrizione</TableHead>
                <TableHead className="w-12" />
              </TableRow>
              <TableRow className="hover:bg-transparent">
                <TableHead className="h-auto px-2 py-2 align-middle">
                  <ColumnFilterInput value={codeFilter} onChange={setCodeFilter} />
                </TableHead>
                <TableHead className="h-auto px-2 py-2 align-middle">
                  <ColumnFilterInput value={descrFilter} onChange={setDescrFilter} />
                </TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.isError ? (
                <TableRow>
                  <TableCell colSpan={3} className="h-24 text-center text-destructive">
                    {(query.error as Error).message ||
                      "Errore nel caricamento del Codex."}
                  </TableCell>
                </TableRow>
              ) : query.isLoading ? (
                <TableRow>
                  <TableCell
                    colSpan={3}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Caricamento Codex…
                  </TableCell>
                </TableRow>
              ) : items.length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={3}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Nessun codice ATEC corrisponde ai filtri. Se l'articolo non è
                    ancora codificato, si codifica dal Catalogo (colonna Codice
                    ATEC) o dalla Ricodifica Codex.
                  </TableCell>
                </TableRow>
              ) : (
                items.map((item) => (
                  <TableRow
                    key={item.id}
                    className="cursor-pointer"
                    onDoubleClick={() => handleAdd(item)}
                  >
                    <TableCell className="font-medium tabular-nums text-primary">
                      {formatCodice(item.codiceNuovo)}
                    </TableCell>
                    <TableCell>
                      <span
                        className="block max-w-[420px] truncate"
                        title={item.descr}
                      >
                        {dash(item.descr)}
                      </span>
                    </TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        title="Aggiungi alla distinta"
                        disabled={addMutation.isPending}
                        onClick={() => handleAdd(item)}
                      >
                        <Plus />
                        <span className="sr-only">
                          Aggiungi {item.codiceNuovo}
                        </span>
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
        </GridScroller>

        <DialogFooter className="sm:justify-between">
          <div className="flex flex-1 items-center gap-3 text-sm">
            <span className="text-muted-foreground">
              {totalCount > 0 ? `${items.length} di ${totalCount} codici` : ""}
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
