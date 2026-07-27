import { useQuery } from "@tanstack/react-query"
import { FileCheck2 } from "lucide-react"

import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { fetchDaneaOrder } from "@/lib/api/danea-orders"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"

const ORDER_STATUS_LABELS: Record<string, string> = {
  Conf: "Confermato",
  InAtt: "In attesa",
  Evaso: "Evaso",
  Annull: "Annullato",
}

/**
 * Popup «ordine come su Danea»: rendering in sola lettura dell'ordine fornitore
 * scritto in Atec_PM (testata + righe + riepilogo IVA), aperto dal Rif. Danea
 * della DDP commerciale o dal riferimento ordine delle RDO.
 */
export function DaneaOrderDialog({
  idDoc,
  onClose,
}: {
  idDoc: number | null
  onClose: () => void
}) {
  const query = useQuery({
    queryKey: ["danea-order", idDoc],
    queryFn: () => fetchDaneaOrder(idDoc!),
    enabled: idDoc != null,
  })
  const order = query.data

  return (
    <Dialog open={idDoc != null} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-3 overflow-hidden sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <FileCheck2 className="size-5 text-teal-700" />
            {order
              ? `Ordine fornitore n. ${order.num} del ${formatDateShort(order.date)}`
              : "Ordine fornitore Danea"}
          </DialogTitle>
          <DialogDescription>
            {order
              ? `Atec_PM · ${ORDER_STATUS_LABELS[order.orderStatus] ?? order.orderStatus} · Magazzino ${order.warehouse || "—"}`
              : "Lettura da Danea (Atec_PM)…"}
          </DialogDescription>
        </DialogHeader>

        {query.isError ? (
          <p className="text-sm text-destructive">
            {query.error instanceof Error
              ? query.error.message
              : "Lettura ordine non riuscita."}
          </p>
        ) : null}

        {order ? (
          <div className="min-h-0 flex-1 space-y-4 overflow-auto">
            <section className="rounded-md border bg-muted/30 p-3 text-sm">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="font-semibold">{order.supplierName}</div>
                  {order.supplierAddress ? (
                    <div>{order.supplierAddress}</div>
                  ) : null}
                  <div>
                    {[
                      order.supplierZip,
                      order.supplierCity,
                      order.supplierProvince ? `(${order.supplierProvince})` : "",
                    ]
                      .filter(Boolean)
                      .join(" ") || null}
                  </div>
                  {order.supplierVat ? (
                    <div className="text-muted-foreground">
                      P.IVA {order.supplierVat}
                    </div>
                  ) : null}
                </div>
                <div className="text-right">
                  {order.expectedDate ? (
                    <div>
                      <span className="text-muted-foreground">
                        Consegna prevista:{" "}
                      </span>
                      <span className="font-medium">
                        {formatDateShort(order.expectedDate)}
                      </span>
                    </div>
                  ) : null}
                </div>
              </div>
            </section>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Codice</TableHead>
                  <TableHead>Descrizione</TableHead>
                  <TableHead className="text-right">Qtà</TableHead>
                  <TableHead>UM</TableHead>
                  <TableHead className="text-right">Prezzo</TableHead>
                  <TableHead className="text-right">IVA %</TableHead>
                  <TableHead className="text-right">Importo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {order.rows.map((r, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-medium whitespace-nowrap">
                      {r.code}
                      {r.supplierCode && r.supplierCode !== r.code ? (
                        <div className="text-xs font-normal text-muted-foreground">
                          forn. {r.supplierCode}
                        </div>
                      ) : null}
                    </TableCell>
                    <TableCell className="max-w-[240px]">
                      {r.description}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.quantity}
                    </TableCell>
                    <TableCell>{r.unit}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(r.unitPrice)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.vatCode}
                    </TableCell>
                    <TableCell className="text-right font-medium tabular-nums">
                      {euro(r.netAmount)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            <section className="ml-auto w-full max-w-xs space-y-1 text-sm">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Totale netto</span>
                <span className="tabular-nums">{euro(order.totNet)}</span>
              </div>
              {order.vatSummary.map((v) => (
                <div key={v.vatCode} className="flex justify-between">
                  <span className="text-muted-foreground">
                    IVA {v.vatCode} su {euro(v.netAmount)}
                  </span>
                  <span className="tabular-nums">{euro(v.vatAmount)}</span>
                </div>
              ))}
              <div className="flex justify-between border-t pt-1 font-semibold">
                <span>Totale documento</span>
                <span className="tabular-nums">{euro(order.totDoc)}</span>
              </div>
            </section>

            {order.internalNote ? (
              <p className="text-xs text-muted-foreground">
                {order.internalNote}
              </p>
            ) : null}
          </div>
        ) : query.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento ordine…</p>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}
