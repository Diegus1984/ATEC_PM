import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Link2, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
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
import { fetchCatalogByAtec, fetchCatalogByCodex } from "@/lib/api/catalog"
import { fetchCodex } from "@/lib/api/codex"
import { createDdpRow } from "@/lib/api/project-ddp"
import type { CatalogItemListItem, CodexListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { euro } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"
import { DDP_STATUS_VERIFY } from "./ddp-constants"

/**
 * Picker «per codice ATEC»: cerca nel Codex (codice nuovo), mostra le alternative
 * Danea del mapping e permette di (a) scegliere subito un fornitore oppure
 * (b) inserire solo il codice ATEC con fornitore da definire (stato DO).
 */
export function AtecPickerDialog({
  open,
  projectId,
  onClose,
  onAdded,
}: {
  open: boolean
  projectId: number
  onClose: () => void
  onAdded: () => void
}) {
  const requestedBy = getSession()?.user.fullName ?? ""
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebounced(searchInput.trim(), 300)
  const [selected, setSelected] = React.useState<CodexListItem | null>(null)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (open) {
      setSearchInput("")
      setSelected(null)
      setMessage(null)
      setError(null)
    }
  }, [open])

  const codexQuery = useQuery({
    queryKey: ["atec-picker-codex", search],
    queryFn: () =>
      fetchCodex({
        page: 1,
        pageSize: 40,
        search: search || undefined,
        newCodeState: "done",
      }),
    enabled: open,
  })

  const altsQuery = useQuery({
    queryKey: ["atec-picker-alts", selected?.id, selected?.codiceNuovo],
    queryFn: async () => {
      if (!selected) return [] as CatalogItemListItem[]
      if (selected.id > 0) return fetchCatalogByCodex(selected.id)
      return fetchCatalogByAtec(selected.codiceNuovo)
    },
    enabled: open && !!selected?.codiceNuovo,
  })

  const addMutation = useMutation({
    mutationFn: async (opts: {
      atecCode: string
      description: string
      catalog?: CatalogItemListItem | null
      tbd?: boolean
    }) => {
      setError(null)
      const cat = opts.catalog
      await createDdpRow(projectId, {
        id: 0,
        projectId,
        catalogItemId: cat?.id ?? null,
        partNumber: cat?.code ?? "",
        description: cat?.description || opts.description,
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
        notes: opts.tbd ? "Fornitore da definire" : "",
        ddpType: "COMMERCIAL",
        atecCode: opts.atecCode,
        expectedUpdatedAt: null,
      })
      return opts.atecCode
    },
    onSuccess: (code) => {
      setMessage(`✓ ${code} aggiunto`)
      onAdded()
    },
    onError: (err: Error) => setError(err.message),
  })

  const items = codexQuery.data?.items ?? []
  const alts = altsQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="flex max-h-[90vh] max-w-4xl flex-col gap-3 overflow-hidden">
        <DialogHeader>
          <DialogTitle>Aggiungi per codice ATEC</DialogTitle>
          <DialogDescription>
            Cerca un codice nuovo Codex, poi scegli un fornitore Danea oppure
            lascia la riga «da definire».
          </DialogDescription>
        </DialogHeader>

        <div className="relative">
          <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
          <Input
            value={searchInput}
            placeholder="Cerca codice nuovo, descrizione…"
            className="pl-8"
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </div>

        <div className="grid min-h-0 flex-1 gap-3 md:grid-cols-2">
          <div className="overflow-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>Cod. ATEC</TableHead>
                  <TableHead>Descrizione</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={2} className="text-center text-muted-foreground">
                      {codexQuery.isLoading
                        ? "Caricamento…"
                        : "Nessun codice nuovo trovato."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.id}
                      className={
                        selected?.id === item.id
                          ? "cursor-pointer bg-accent"
                          : "cursor-pointer"
                      }
                      onClick={() => setSelected(item)}
                    >
                      <TableCell className="font-medium tabular-nums text-primary">
                        {item.codiceNuovo || item.codice}
                      </TableCell>
                      <TableCell className="max-w-[200px] truncate">
                        {item.descr || "—"}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>

          <div className="flex min-h-0 flex-col gap-2 overflow-auto rounded-lg border p-2">
            {!selected ? (
              <p className="p-4 text-sm text-muted-foreground">
                Seleziona un codice ATEC a sinistra.
              </p>
            ) : (
              <>
                <div className="flex items-center justify-between gap-2 px-1">
                  <div>
                    <p className="font-medium tabular-nums text-primary">
                      {selected.codiceNuovo}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {selected.descr || "—"}
                    </p>
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={addMutation.isPending || !selected.codiceNuovo}
                    onClick={() =>
                      addMutation.mutate({
                        atecCode: selected.codiceNuovo,
                        description: selected.descr,
                        tbd: true,
                      })
                    }
                  >
                    <Link2 />
                    Solo ATEC (da definire)
                  </Button>
                </div>
                <Table>
                  <TableHeader className="bg-muted/50">
                    <TableRow>
                      <TableHead>Fornitore</TableHead>
                      <TableHead>Codice</TableHead>
                      <TableHead className="text-right">Costo</TableHead>
                      <TableHead />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {altsQuery.isLoading ? (
                      <TableRow>
                        <TableCell colSpan={4} className="text-muted-foreground">
                          Caricamento alternative…
                        </TableCell>
                      </TableRow>
                    ) : alts.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={4} className="text-muted-foreground">
                          Nessun articolo Danea associato. Usa «Solo ATEC» oppure
                          completa il mapping dal Catalogo/Codex.
                        </TableCell>
                      </TableRow>
                    ) : (
                      alts.map((alt) => (
                        <TableRow key={alt.id}>
                          <TableCell className="max-w-[140px] truncate">
                            {alt.supplierName || "—"}
                          </TableCell>
                          <TableCell className="font-medium">{alt.code}</TableCell>
                          <TableCell className="text-right tabular-nums">
                            {euro(alt.unitCost)}
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              size="sm"
                              disabled={addMutation.isPending}
                              onClick={() =>
                                addMutation.mutate({
                                  atecCode: selected.codiceNuovo,
                                  description: selected.descr,
                                  catalog: alt,
                                })
                              }
                            >
                              Scegli
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </>
            )}
          </div>
        </div>

        {message ? <p className="text-sm text-green-700">{message}</p> : null}
        {error ? <p className="text-sm text-destructive">{error}</p> : null}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
