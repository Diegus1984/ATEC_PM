import { useMutation, useQuery } from "@tanstack/react-query"

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
import { GridScroller } from "@/components/shared/grid-scroller"
import { fetchCatalogByAtec } from "@/lib/api/catalog"
import { updateDdpRow } from "@/lib/api/project-ddp"
import type { CatalogItemListItem, DdpRowItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"

import { ddpCommercialRowToSaveRequest } from "./ddp-commercial-row"

/**
 * Alternative fornitore dal mapping ATEC per una riga DDP già esistente:
 * scegliendo un articolo Danea aggiorna snapshot (codice/fornitore/costo)
 * senza perdere il codice ATEC.
 */
export function DdpAtecAlternativesDialog({
  open,
  projectId,
  row,
  onClose,
  onApplied,
}: {
  open: boolean
  projectId: number
  row: DdpRowItem | null
  onClose: () => void
  onApplied: () => void
}) {
  const atec = row?.atecCode?.trim() ?? ""

  const altsQuery = useQuery({
    queryKey: ["ddp-atec-alts", atec],
    queryFn: () => fetchCatalogByAtec(atec),
    enabled: open && atec.length > 0,
  })

  const applyMutation = useMutation({
    mutationFn: async (cat: CatalogItemListItem) => {
      if (!row) throw new Error("Riga mancante")
      const base = ddpCommercialRowToSaveRequest(projectId, row)
      await updateDdpRow(projectId, row.id, {
        ...base,
        catalogItemId: cat.id,
        partNumber: cat.code,
        description: cat.description || row.description,
        unit: cat.unit || row.unit || "PZ",
        unitCost: cat.unitCost,
        supplierId: cat.supplierId,
        manufacturer: cat.manufacturer,
        atecCode: atec,
        updateSupplier: true,
        updateCatalogSnapshot: true,
      })
    },
    onSuccess: () => {
      notifyInfo("Fornitore applicato dalla mapping ATEC")
      onApplied()
      onClose()
    },
    onError: (err: Error) => notifyError(err),
  })

  const alts = altsQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Alternative dal mapping</DialogTitle>
          <DialogDescription>
            Codice ATEC {atec || "—"} — scegli un articolo Danea (fornitore /
            prezzo) da applicare a questa riga.
          </DialogDescription>
        </DialogHeader>

        <GridScroller className="rounded-lg border" scrollerClassName="max-h-[50vh]">
          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow>
                <TableHead>Fornitore</TableHead>
                <TableHead>Codice</TableHead>
                <TableHead>Descrizione</TableHead>
                <TableHead className="text-right">Costo</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {!atec ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-muted-foreground">
                    La riga non ha un codice ATEC.
                  </TableCell>
                </TableRow>
              ) : altsQuery.isLoading ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-muted-foreground">
                    Caricamento…
                  </TableCell>
                </TableRow>
              ) : alts.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-muted-foreground">
                    Nessuna alternativa nel mapping per questo codice.
                  </TableCell>
                </TableRow>
              ) : (
                alts.map((alt) => (
                  <TableRow key={alt.id}>
                    <TableCell>{alt.supplierName || "—"}</TableCell>
                    <TableCell className="font-medium">{alt.code}</TableCell>
                    <TableCell className="max-w-[200px] truncate">
                      {alt.description}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(alt.unitCost)}
                    </TableCell>
                    <TableCell className="text-right">
                      <Button
                        size="sm"
                        disabled={
                          applyMutation.isPending ||
                          alt.id === row?.catalogItemId
                        }
                        onClick={() => applyMutation.mutate(alt)}
                      >
                        {alt.id === row?.catalogItemId ? "Attuale" : "Applica"}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </GridScroller>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
