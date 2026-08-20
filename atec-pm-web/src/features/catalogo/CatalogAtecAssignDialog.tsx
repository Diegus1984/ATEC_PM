import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Link2, Plus, Search } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  assignCatalogMapping,
  assignCatalogMappingFromBom,
} from "@/lib/api/catalog"
import {
  confirmCodexReservation,
  fetchCodex,
  fetchCodexPrefixes,
  releaseCodexReservation,
  reserveCodexCode,
} from "@/lib/api/codex"
import type { CatalogItemListItem } from "@/lib/api/types"
import { decodeHtmlEntities } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"

/** Famiglie della nuova codifica commerciale: i generici nascono qui. */
const NEW_FAMILIES = new Set(["201", "211", "221"])

/** Riga distinta (Inbox Acquisti): l'articolo Danea lo risolve il server. */
export interface AtecAssignBomTarget {
  bomItemId: number
  partNumber: string
  description: string
}

/**
 * Assegnazione del codice ATEC a un articolo Danea. Due ingressi:
 * - dal Catalogo (`item`): associazione diretta dell'articolo;
 * - dalla Inbox Acquisti (`bomTarget`): il server risolve l'articolo dalla riga
 *   distinta (link catalogo o match esatto sul codice Danea).
 * Cerca tra le righe Codex con codice ATEC (ricodificate o nate nelle famiglie
 * nuove) oppure CREA un codice generico al volo (famiglia → prenotazione →
 * descrizione → crea e associa), senza uscire dal dialog. Riassegnazione solo
 * con conferma esplicita; scrittura prima su Danea, poi sullo specchio locale.
 */
export function CatalogAtecAssignDialog({
  item,
  bomTarget = null,
  onClose,
  onSaved,
}: {
  item: CatalogItemListItem | null
  bomTarget?: AtecAssignBomTarget | null
  onClose: () => void
  /** Chiamato solo dopo associazione riuscita (Extra1 + specchio locale). */
  onSaved: () => void
}) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const open = item != null || bomTarget != null
  const targetCode = item?.code ?? bomTarget?.partNumber ?? ""
  const targetDescription = item?.description ?? bomTarget?.description ?? ""

  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)
  /** Errore dell'ultima operazione, mostrato NEL dialog (il toast in angolo si perde). */
  const [error, setError] = React.useState<string | null>(null)

  // Creazione generico al volo: famiglia scelta → codice prenotato → descrizione.
  const [reserved, setReserved] = React.useState<{
    family: string
    codice: string
    reservationId: number
  } | null>(null)
  const [genDescription, setGenDescription] = React.useState("")
  const reservedRef = React.useRef(reserved)
  reservedRef.current = reserved

  React.useEffect(() => {
    if (!open) return
    setSearchInput("")
    setReserved(null)
    setGenDescription("")
    setError(null)
  }, [open, item?.id, bomTarget?.bomItemId])

  // Prenotazione orfana alla chiusura del dialog: rilascio best-effort
  // (comunque scade da sola con il TTL di 10 minuti).
  const releaseIfPending = React.useCallback(() => {
    const r = reservedRef.current
    if (r) {
      void releaseCodexReservation(r.reservationId).catch(() => {})
      setReserved(null)
    }
  }, [])

  const handleClose = React.useCallback(() => {
    releaseIfPending()
    onClose()
  }, [releaseIfPending, onClose])

  // Solo righe Codex con codice ATEC (codice_nuovo: ricodifiche + nati nuovi).
  const searchQuery = useQuery({
    queryKey: ["catalog-atec-search", searchTerm],
    queryFn: () =>
      fetchCodex({
        search: searchTerm,
        newCodeState: "done",
        pageSize: 25,
        sortBy: "codiceNuovo",
      }),
    enabled: open && searchTerm.length >= 2,
  })

  const prefixesQuery = useQuery({
    queryKey: ["codex-prefixes"],
    queryFn: fetchCodexPrefixes,
    enabled: open,
  })
  const families = (prefixesQuery.data ?? []).filter((p) =>
    NEW_FAMILIES.has(p.codice)
  )

  const assignMutation = useMutation({
    mutationFn: ({
      codexItemId,
      force,
    }: {
      codexItemId: number
      label: string
      force: boolean
    }) => {
      setError(null)
      return bomTarget
        ? assignCatalogMappingFromBom(bomTarget.bomItemId, codexItemId, force)
        : assignCatalogMapping(item!.id, codexItemId, force)
    },
    onSuccess: async (result, vars) => {
      if (result.requiresForce) {
        const ok = await confirm({
          title: "Articolo già associato",
          description: `${targetCode} è già associato al codice ${result.currentAtecCode}.\nSpostarlo su ${vars.label}?`,
          confirmLabel: "Sposta",
        })
        if (ok) assignMutation.mutate({ ...vars, force: true })
        return
      }
      if (!result.assigned) return
      notifyInfo(`${targetCode} associato a ${vars.label}`)
      void queryClient.invalidateQueries({ queryKey: ["catalog"] })
      void queryClient.invalidateQueries({ queryKey: ["catalog-mapping"] })
      onSaved()
    },
    onError: (err: Error) => {
      setError(err.message)
      notifyError(err)
    },
  })

  const reserveMutation = useMutation({
    mutationFn: (family: string) => {
      setError(null)
      return reserveCodexCode(family).then((r) => ({ family, r }))
    },
    onSuccess: ({ family, r }) => {
      setReserved({
        family,
        codice: r.codice,
        reservationId: r.reservationId,
      })
      setGenDescription(targetDescription)
    },
    onError: (err: Error) => {
      setError(err.message)
      notifyError(err)
    },
  })

  // Crea l'articolo Codex generico (conferma prenotazione) e associa subito.
  const createAndAssignMutation = useMutation({
    mutationFn: async () => {
      setError(null)
      if (!reserved) throw new Error("Nessun codice prenotato")
      const descr = genDescription.trim()
      if (!descr) throw new Error("Inserire la descrizione del codice generico")
      const created = await confirmCodexReservation(
        reserved.reservationId,
        descr
      )
      return created
    },
    onSuccess: (created) => {
      setReserved(null)
      void queryClient.invalidateQueries({ queryKey: ["codex"] })
      assignMutation.mutate({
        codexItemId: created.id,
        label: created.codice,
        force: false,
      })
    },
    onError: (err: Error) => {
      setError(err.message)
      notifyError(err)
    },
  })

  const cancelReservation = () => {
    releaseIfPending()
    setGenDescription("")
  }

  if (!open) return null

  const results = searchQuery.data?.items ?? []
  const busy =
    assignMutation.isPending ||
    reserveMutation.isPending ||
    createAndAssignMutation.isPending

  return (
    <Dialog open onOpenChange={(next) => !next && handleClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Codice ATEC — {targetCode || "riga distinta"}</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-muted-foreground">
            <span className="block max-w-full truncate" title={targetDescription}>
              {targetDescription || "(senza descrizione)"}
            </span>
            {item?.atecCode ? (
              <span className="mt-1 block">
                Attualmente associato a{" "}
                <span className="font-mono font-medium">{item.atecCode}</span>
              </span>
            ) : null}
          </p>

          <div className="relative">
            <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
            <Input
              value={searchInput}
              autoFocus
              placeholder="Cerca il codice ATEC (min 2 caratteri)…"
              className="pl-8"
              onChange={(event) => setSearchInput(event.target.value)}
            />
          </div>

          {searchTerm.length >= 2 ? (
            <GridScroller className="rounded-md border" scrollerClassName="max-h-56">
              <Table>
                <TableBody>
                  {searchQuery.isFetching && results.length === 0 ? (
                    <TableRow>
                      <TableCell className="h-14 text-center text-sm text-muted-foreground">
                        Ricerca…
                      </TableCell>
                    </TableRow>
                  ) : results.length === 0 ? (
                    <TableRow>
                      <TableCell className="h-14 text-center text-sm text-muted-foreground">
                        Nessun codice ATEC corrisponde: crealo qui sotto come
                        generico, oppure ricodifica la riga dal Codex.
                      </TableCell>
                    </TableRow>
                  ) : (
                    results.map((codex) => (
                      <TableRow key={codex.id}>
                        <TableCell className="font-medium tabular-nums text-primary">
                          {codex.codiceNuovo}
                        </TableCell>
                        <TableCell className="text-xs text-muted-foreground tabular-nums">
                          {codex.codice !== codex.codiceNuovo
                            ? `ex ${codex.codice}`
                            : "nato nuovo"}
                        </TableCell>
                        <TableCell>
                          <span
                            className="block max-w-[260px] truncate"
                            title={decodeHtmlEntities(codex.descr)}
                          >
                            {decodeHtmlEntities(codex.descr) || "—"}
                          </span>
                        </TableCell>
                        <TableCell className="text-right">
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={busy}
                            onClick={() =>
                              assignMutation.mutate({
                                codexItemId: codex.id,
                                label: codex.codiceNuovo,
                                force: false,
                              })
                            }
                          >
                            <Link2 className="size-3.5" />
                            Associa
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </GridScroller>
          ) : null}

          {/* Codice generico al volo: famiglia → prenotazione → descrizione → crea e associa. */}
          <div className="rounded-md border p-3">
            {reserved === null ? (
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm text-muted-foreground">
                  Il codice non esiste ancora? Nuovo generico:
                </span>
                {families.map((f) => (
                  <Button
                    key={f.codice}
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={busy}
                    onClick={() => reserveMutation.mutate(f.codice)}
                  >
                    <Plus className="size-3.5" />
                    {f.codice} — {f.descrizione}
                  </Button>
                ))}
              </div>
            ) : (
              <div className="space-y-2">
                <p className="text-sm">
                  Codice prenotato:{" "}
                  <span className="font-mono font-semibold tabular-nums">
                    {reserved.codice}
                  </span>{" "}
                  <span className="text-xs text-muted-foreground">
                    (famiglia {reserved.family}, scade in 10 min)
                  </span>
                </p>
                <div className="space-y-1">
                  <Label htmlFor="gen-descr">Descrizione del codice generico</Label>
                  <Input
                    id="gen-descr"
                    value={genDescription}
                    placeholder="es. Alimentatore 24V"
                    onChange={(event) => setGenDescription(event.target.value)}
                  />
                </div>
                <div className="flex justify-end gap-2">
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={busy}
                    onClick={cancelReservation}
                  >
                    Annulla prenotazione
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    disabled={busy || genDescription.trim().length === 0}
                    onClick={() => createAndAssignMutation.mutate()}
                  >
                    Crea e associa
                  </Button>
                </div>
              </div>
            )}
          </div>
          {error ? (
            <p className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {error}
            </p>
          ) : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
