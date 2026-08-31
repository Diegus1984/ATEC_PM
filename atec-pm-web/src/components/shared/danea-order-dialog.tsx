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
import { GridScroller } from "@/components/shared/grid-scroller"
import { fetchDaneaOrder, fetchDaneaOrderByRef } from "@/lib/api/danea-orders"
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
  daneaRef = null,
  onClose,
}: {
  idDoc: number | null
  /**
   * Rif. Danea scritto a mano (es. «123/26»): si usa quando l'IdDoc non c'è —
   * il server cerca per numero, prima nell'archivio attuale e poi nel VECCHIO
   * (siamo in migrazione: gli ordini storici stanno ancora lì).
   */
  daneaRef?: string | null
  onClose: () => void
}) {
  const aperto = idDoc != null || !!daneaRef
  const query = useQuery({
    queryKey: ["danea-order", idDoc, daneaRef],
    queryFn: () =>
      idDoc != null ? fetchDaneaOrder(idDoc) : fetchDaneaOrderByRef(daneaRef!),
    enabled: aperto,
    // «Non trovato» e «rif malformato» sono esiti deterministici: il retry
    // rifarebbe solo l'intera doppia scansione Firebird (nuovo + vecchio).
    retry: false,
  })
  const order = query.data

  return (
    <Dialog open={aperto} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-3 overflow-hidden sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <FileCheck2 className="size-5 text-teal-700" />
            {order
              ? `Ordine fornitore n. ${order.num} del ${formatDateShort(order.date)}`
              : "Ordine fornitore Danea"}
          </DialogTitle>
          <DialogDescription>
            {/* Il nome leggibile dell'archivio è «Danea»: «Atec_PM» da solo si
                confonde col programma ATEC PM (resta tra parentesi nel caricamento). */}
            {order
              ? `${order.archivio === "VECCHIO" ? "Danea — VECCHIO archivio (Srl-2020-2021)" : "Danea"} · ${ORDER_STATUS_LABELS[order.orderStatus] ?? order.orderStatus} · Magazzino ${order.warehouse || "—"}`
              : "Lettura da Danea…"}
          </DialogDescription>
          {order?.archivio === "VECCHIO" ? (
            <p className="rounded border border-amber-500/60 bg-amber-500/10 px-2 py-1 text-xs text-amber-700 dark:text-amber-400">
              Questo ordine sta nel VECCHIO archivio Danea: non è ancora stato
              migrato in Atec_PM.
            </p>
          ) : order?.ambiguoConVecchio ? (
            <p className="rounded border border-amber-500/60 bg-amber-500/10 px-2 py-1 text-xs text-amber-700 dark:text-amber-400">
              Attenzione: un ordine con lo stesso numero esiste anche nel VECCHIO
              archivio Danea. Questo è quello dell'archivio attuale — controlla che
              il fornitore sia quello che ti aspetti.
            </p>
          ) : null}
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

            <GridScroller className="rounded-md border">
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
            </GridScroller>

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
