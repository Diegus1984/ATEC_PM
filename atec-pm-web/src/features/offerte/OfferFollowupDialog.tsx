import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { DateField } from "@/components/shared/date-field"
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
import { Textarea } from "@/components/ui/textarea"
import { addSalesOfferFollowup } from "@/lib/api/sales-offers"
import type { SalesOffer } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

/**
 * Annota un contatto col referente e, se serve, sposta il prossimo richiamo.
 *
 * <p>È il gesto più frequente del venditore — «richiamato il 5, decidono a fine mese» — e
 * per questo ha un dialogo suo invece di stare in fondo alla scheda: tre campi e via.</p>
 */
export function OfferFollowupDialog({
  open,
  offer,
  onClose,
  onSaved,
}: {
  open: boolean
  offer: SalesOffer
  onClose: () => void
  onSaved: () => void
}) {
  const [contactDate, setContactDate] = React.useState<string | null>(
    new Date().toISOString().slice(0, 10)
  )
  const [notes, setNotes] = React.useState("")
  const [nextContact, setNextContact] = React.useState<string | null>(
    offer.nextContact ? offer.nextContact.slice(0, 10) : null
  )

  const saveMutation = useMutation({
    mutationFn: () =>
      addSalesOfferFollowup(offer.id, { contactDate, notes, nextContact }),
    onSuccess: () => {
      notifySuccess("Contatto annotato")
      onSaved()
    },
    onError: (err: Error) => notifyError(err),
  })

  const vuoto = !notes.trim() && !nextContact

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Contatto col referente</DialogTitle>
          <DialogDescription>
            {offer.composed} — {offer.customerName || "senza cliente"}
            {offer.contactName ? ` · ${offer.contactName}` : ""}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label>Data del contatto</Label>
            <DateField value={contactDate} onChange={setContactDate} />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="followup-notes">Appunto</Label>
            <Textarea
              id="followup-notes"
              rows={3}
              placeholder="Richiamato, decidono a fine mese…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>

          <div className="space-y-1.5">
            <Label>Prossimo contatto</Label>
            <DateField value={nextContact} onChange={setNextContact} clearable />
            <p className="text-xs text-muted-foreground">
              {offer.nextContact
                ? `Adesso è fissato al ${formatDateShort(offer.nextContact)}.`
                : "Non ancora fissato."}{" "}
              Da qui nasce l'elenco «Da richiamare».
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={vuoto || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
