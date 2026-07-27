import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Link2, Search, X } from "lucide-react"

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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  assignCatalogMapping,
  fetchCatalogByCodex,
  fetchCatalogItems,
  unassignCatalogMapping,
} from "@/lib/api/catalog"
import type { CatalogItemListItem, CodexListItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"

/**
 * Pannello «Articoli Danea» di una riga Codex ricodificata: elenca gli articoli
 * del catalogo (specchio Danea) associati al codice nuovo (Extra1) e permette di
 * agganciarne altri cercando nel catalogo. Regole: 1 articolo = 1 codice ATEC;
 * riassegnazione da un altro codice solo con conferma esplicita; sgancio con
 * conferma. Ogni scrittura va prima su Danea (Extra1) e poi sullo specchio locale.
 */
export function CodexDaneaMappingDialog({
  item,
  onClose,
}: {
  item: CodexListItem | null
  onClose: () => void
}) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)

  React.useEffect(() => {
    setSearchInput("")
  }, [item?.id])

  const mappedQuery = useQuery({
    queryKey: ["catalog-mapping", item?.id],
    queryFn: () => fetchCatalogByCodex(item!.id),
    enabled: item != null,
  })

  const searchQuery = useQuery({
    queryKey: ["catalog-mapping-search", searchTerm],
    queryFn: () =>
      fetchCatalogItems({ search: searchTerm, pageSize: 25, sortBy: "code" }),
    enabled: item != null && searchTerm.length >= 2,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["catalog-mapping"] })
    void queryClient.invalidateQueries({ queryKey: ["catalog-mapping-search"] })
    void queryClient.invalidateQueries({ queryKey: ["catalog"] })
  }

  const assignMutation = useMutation({
    mutationFn: ({ row, force }: { row: CatalogItemListItem; force: boolean }) =>
      assignCatalogMapping(row.id, item!.id, force),
    onSuccess: async (result, { row }) => {
      if (result.requiresForce) {
        // 1 articolo = 1 codice ATEC: spostamento consapevole da un altro codice.
        const ok = await confirm({
          title: "Articolo già associato",
          description: `${row.code} è già associato al codice ${result.currentAtecCode}.\nSpostarlo su ${item?.codiceNuovo}?`,
          confirmLabel: "Sposta",
        })
        if (ok) assignMutation.mutate({ row, force: true })
        return
      }
      notifyInfo(`${row.code} associato a ${item?.codiceNuovo}`)
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const unassignMutation = useMutation({
    mutationFn: (row: CatalogItemListItem) => unassignCatalogMapping(row.id),
    onSuccess: (_, row) => {
      notifyInfo(`${row.code} sganciato`)
      invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const handleUnassign = async (row: CatalogItemListItem) => {
    const ok = await confirm({
      title: "Sgancia articolo",
      description: `Rimuovere l'associazione di ${row.code} dal codice ${item?.codiceNuovo}?\nIn Danea l'Extra 1 dell'articolo verrà svuotato.`,
      confirmLabel: "Sgancia",
    })
    if (ok) unassignMutation.mutate(row)
  }

  if (!item) return null

  const mapped = mappedQuery.data ?? []
  const mappedIds = new Set(mapped.map((row) => row.id))
  const results = (searchQuery.data?.items ?? []).filter(
    (row) => !mappedIds.has(row.id)
  )
  const busy = assignMutation.isPending || unassignMutation.isPending

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>
            Articoli Danea — {item.codiceNuovo}
            <span className="ml-2 text-sm font-normal text-muted-foreground">
              (ex {item.codice})
            </span>
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div>
            <p className="mb-1.5 text-xs font-medium text-muted-foreground">
              Associati ({mapped.length}) — fornitori alternativi di questo codice
            </p>
            <div className="max-h-48 overflow-y-auto rounded-md border">
              <Table>
                <TableHeader className="sticky top-0 z-10 bg-muted">
                  <TableRow className="hover:bg-transparent">
                    <TableHead>Cod. Danea</TableHead>
                    <TableHead>Descrizione</TableHead>
                    <TableHead>Fornitore</TableHead>
                    <TableHead className="text-right">Costo</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {mapped.length === 0 ? (
                    <TableRow>
                      <TableCell
                        colSpan={5}
                        className="h-16 text-center text-sm text-muted-foreground"
                      >
                        {mappedQuery.isLoading
                          ? "Caricamento…"
                          : "Nessun articolo associato — cerca qui sotto per agganciare."}
                      </TableCell>
                    </TableRow>
                  ) : (
                    mapped.map((row) => (
                      <TableRow key={row.id}>
                        <TableCell className="font-medium">{row.code}</TableCell>
                        <TableCell>
                          <span
                            className="block max-w-[260px] truncate"
                            title={row.description}
                          >
                            {row.description || "—"}
                          </span>
                        </TableCell>
                        <TableCell>{row.supplierName || "—"}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {euro(row.unitCost)}
                        </TableCell>
                        <TableCell>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon-sm"
                            title="Sgancia"
                            disabled={busy}
                            onClick={() => void handleUnassign(row)}
                          >
                            <X />
                            <span className="sr-only">Sgancia {row.code}</span>
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </div>
          </div>

          <div className="space-y-1.5">
            <p className="text-xs font-medium text-muted-foreground">
              Aggancia dal catalogo Danea
            </p>
            <div className="relative">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                value={searchInput}
                placeholder="Cerca per codice, descrizione, fornitore… (min 2 caratteri)"
                className="pl-8"
                onChange={(event) => setSearchInput(event.target.value)}
              />
            </div>
            {searchTerm.length >= 2 ? (
              <div className="max-h-56 overflow-y-auto rounded-md border">
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
                          Nessun articolo trovato.
                        </TableCell>
                      </TableRow>
                    ) : (
                      results.map((row) => (
                        <TableRow key={row.id}>
                          <TableCell className="font-medium">
                            {row.code}
                          </TableCell>
                          <TableCell>
                            <span
                              className="block max-w-[240px] truncate"
                              title={row.description}
                            >
                              {row.description || "—"}
                            </span>
                          </TableCell>
                          <TableCell>{row.supplierName || "—"}</TableCell>
                          <TableCell>
                            {row.atecCode ? (
                              <span
                                className="text-xs text-amber-600"
                                title={`Già associato a ${row.atecCode}`}
                              >
                                → {row.atecCode}
                              </span>
                            ) : null}
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              disabled={busy}
                              onClick={() =>
                                assignMutation.mutate({ row, force: false })
                              }
                            >
                              <Link2 className="size-3.5" />
                              Aggancia
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </div>
            ) : null}
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
