import * as React from "react"
import { useMutation, useQueries } from "@tanstack/react-query"
import { TriangleAlert } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { bulkCloseSalesOffers, fetchSalesOffers } from "@/lib/api/sales-offers"
import { notifyError, notifySuccess } from "@/lib/toast"

import { salesOfferStatusLabel } from "./sales-offer-status"

/**
 * Chiusura in blocco delle offerte ancora aperte degli anni passati.
 *
 * <p><b>Perché serve davvero.</b> Nel vecchio Excel le offerte perse non venivano quasi mai
 * chiuse: sullo storico importato ci sono 1.364 «aperte» e appena 7 «perse». Finché non si
 * passa di qui, «quante ne sono entrate» risponde 95,7% sugli anni chiusi — un numero che non
 * vuol dire niente.</p>
 *
 * <p>Il conteggio di quante righe si stanno per toccare è <b>vero</b>, non una stima: si
 * chiedono al server i totali delle aperte anno per anno. Un'operazione che ne cambia un
 * migliaio non si conferma alla cieca.</p>
 */
export function BulkCloseDialog({
  open,
  onClose,
  onDone,
}: {
  open: boolean
  onClose: () => void
  onDone: () => void
}) {
  const annoCorrente = new Date().getFullYear()
  const anniChiusi = React.useMemo(() => {
    const anni: number[] = []
    for (let a = annoCorrente - 1; a >= 2022; a--) anni.push(a)
    return anni
  }, [annoCorrente])

  const [finoA, setFinoA] = React.useState(anniChiusi[0] ?? annoCorrente - 1)
  const [stato, setStato] = React.useState("persa")

  // Un conteggio per anno: la somma di quelli fino all'anno scelto è esattamente ciò che
  // l'operazione toccherà (il server chiude `status='aperta' AND year <= finoA`).
  const conteggi = useQueries({
    queries: anniChiusi.map((a) => ({
      queryKey: ["sales-offers-aperte-anno", a],
      queryFn: () => fetchSalesOffers({ year: a, status: "aperta", pageSize: 1 }),
      enabled: open,
    })),
  })

  const caricando = conteggi.some((q) => q.isLoading)
  const quante = anniChiusi.reduce(
    (somma, a, i) => (a <= finoA ? somma + (conteggi[i]?.data?.total ?? 0) : somma),
    0
  )

  const mutation = useMutation({
    mutationFn: () => bulkCloseSalesOffers({ untilYear: finoA, status: stato }),
    onSuccess: (righe) => {
      notifySuccess(`${righe} offerte chiuse come «${salesOfferStatusLabel(stato)}»`)
      onDone()
    },
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Chiudi le offerte degli anni passati</DialogTitle>
          <DialogDescription>
            Le offerte ancora «aperte» degli anni già conclusi passano tutte allo stato scelto.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label>Fino all'anno</Label>
              <Select value={String(finoA)} onValueChange={(v) => setFinoA(Number(v))}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {anniChiusi.map((a) => (
                    <SelectItem key={a} value={String(a)}>
                      {a} e precedenti
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Diventano</Label>
              <Select value={stato} onValueChange={setStato}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="persa">Perse</SelectItem>
                  <SelectItem value="sospesa">Sospese</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <p className="flex items-start gap-2 rounded-lg border border-amber-500/50 bg-amber-500/5 px-3 py-2 text-sm">
            <TriangleAlert className="mt-0.5 size-4 shrink-0 text-amber-600" />
            <span>
              {caricando ? (
                "Conto quante offerte verrebbero chiuse…"
              ) : (
                <>
                  Verranno chiuse <b>{quante} offerte</b> come «
                  {salesOfferStatusLabel(stato)}». Ogni cambio finisce nel registro modifiche,
                  quindi si può ricostruire — ma non c'è un pulsante per annullare.
                </>
              )}
            </span>
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            variant="destructive"
            onClick={() => mutation.mutate()}
            disabled={mutation.isPending || caricando || quante === 0}
          >
            Chiudi {quante > 0 ? `${quante} offerte` : ""}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
