// ── Dialog «Nuova Richiesta d'Offerta»: conferma articoli, note, crea la gara ──

import * as React from "react"
import { FileCheck2 } from "lucide-react"

import { Badge } from "@/components/ui/badge"
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
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { createPurchaseRfq } from "@/lib/api/purchase-rfqs"
import type { AcquistiInboxItem } from "@/lib/api/types"
import { notifyError, notifyInfo } from "@/lib/toast"

export function CreateRfqDialog({
  items,
  onClose,
  onCreated,
}: {
  /** Righe da mettere in gara (già filtrate su «Da ordinare»); null = dialog chiuso. */
  items: AcquistiInboxItem[] | null
  onClose: () => void
  /** Riceve gli id delle RDO create: il server le spezza una per commessa. */
  onCreated: (createdIds: number[]) => void
}) {
  const [description, setDescription] = React.useState("")
  const [notes, setNotes] = React.useState("")
  const [submitting, setSubmitting] = React.useState(false)

  // Oggetto precompilato con la commessa all'apertura; le note ripartono vuote.
  React.useEffect(() => {
    if (!items || items.length === 0) return
    setDescription(`Richiesta offerta — Commessa ${items[0]?.projectCode || ""}`)
    setNotes("")
  }, [items])

  const handleSubmit = async () => {
    if (!items || items.length === 0) return
    setSubmitting(true)
    try {
      const bomItemIds = items.map((i) => i.id)
      const atecCode = items.find((i) => i.atecCode)?.atecCode || "GENERICO"

      const createdIds = await createPurchaseRfq({
        atecCode,
        description,
        notes,
        bomItemIds,
      })

      notifyInfo(`RDO creata con successo! (${createdIds.length} commessa/e)`)
      onCreated(createdIds)
    } catch (err) {
      notifyError(
        `Errore creazione RDO: ${err instanceof Error ? err.message : String(err)}`
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open={!!items} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-lg font-bold">
            <FileCheck2 className="h-5 w-5 text-primary" />
            Nuova Richiesta d'Offerta (RDO)
          </DialogTitle>
          <DialogDescription className="text-xs">
            Conferma gli articoli e le note per creare la gara e richiedere i preventivi ai
            fornitori.
          </DialogDescription>
        </DialogHeader>

        {items && (
          <div className="space-y-4 py-2 text-xs">
            <div>
              <Label className="font-semibold text-foreground">
                Articoli Inclusi nella RDO ({items.length})
              </Label>
              <div className="mt-1 max-h-48 overflow-y-auto space-y-1.5 border rounded p-2 bg-muted/40">
                {items.map((item) => (
                  <div
                    key={item.id}
                    className="flex justify-between items-start text-xs p-1.5 border rounded bg-card"
                  >
                    <div className="space-y-0.5">
                      <div className="font-semibold text-foreground">{item.description}</div>
                      <div className="text-[11px] text-muted-foreground flex items-center gap-2">
                        <span>
                          Cod. Fornitore:{" "}
                          <strong className="font-mono text-foreground">
                            {item.partNumber || "N.D."}
                          </strong>
                        </span>
                        <span>•</span>
                        <span>
                          Rif. ATEC:{" "}
                          <strong className="font-mono text-foreground">
                            {item.atecCode || "N.D."}
                          </strong>
                        </span>
                      </div>
                    </div>
                    <Badge variant="outline" className="font-mono shrink-0 ml-2">
                      {item.quantity} {item.unit || "pz"}
                    </Badge>
                  </div>
                ))}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="rfq-descr" className="font-semibold">
                Descrizione / Oggetto RDO
              </Label>
              <Input
                id="rfq-descr"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Es. Richiesta offerta per alimentatori 24V"
                className="h-9 text-xs"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="rfq-notes" className="font-semibold">
                Note / Requisiti aggiuntivi (opzionale)
              </Label>
              <Textarea
                id="rfq-notes"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Specificare qui tempi di consegna richiesti, condizioni o note per il fornitore..."
                className="text-xs h-20"
              />
            </div>
          </div>
        )}

        <DialogFooter className="gap-2 sm:gap-0">
          <Button variant="outline" size="sm" onClick={onClose}>
            Annulla
          </Button>
          <Button
            size="sm"
            onClick={() => void handleSubmit()}
            disabled={submitting}
            className="gap-1"
          >
            <FileCheck2 className="h-4 w-4" />
            {submitting ? "Creazione in corso..." : "Conferma e Crea RDO"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
