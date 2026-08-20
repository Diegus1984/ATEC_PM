import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"

import { ActivityCatalogEditor } from "./ActivityCatalogEditor"

/**
 * Anagrafica attività richiamata dal form commessa (blocco 7). Serve al caso concreto:
 * si sta creando una commessa, manca una voce nell'elenco da precaricare e senza questo
 * bisognava annullare, andare in `/anagrafica-attivita` e ricominciare da capo.
 * Alla chiusura il chiamante ricarica il catalogo e riallinea la selezione — le voci
 * aggiunte qui tornano già spuntate.
 */
export function ActivityCatalogDialog({
  open,
  onClose,
}: {
  open: boolean
  onClose: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Anagrafica attività</DialogTitle>
          <DialogDescription>
            Voci standard precaricate alla creazione di una commessa. Le modifiche
            valgono per tutti; le commesse già create non vengono toccate.
          </DialogDescription>
        </DialogHeader>

        <ActivityCatalogEditor />

        <DialogFooter>
          <Button onClick={onClose}>Chiudi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
