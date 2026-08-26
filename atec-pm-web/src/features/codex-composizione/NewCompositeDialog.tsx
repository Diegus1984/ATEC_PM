import * as React from "react"

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
import {
  confirmCodexReservation,
  releaseCodexReservation,
  reserveCodexCode,
} from "@/lib/api/codex"
import type { CodexGeneratedCode } from "@/lib/api/types"

/**
 * Crea un nuovo composito (501/511/601/701) senza uscire dalla pagina Composizione:
 * serve quando la famiglia è vuota — «Nessun composito 511» — e il gruppo va inventato
 * adesso. Stesso flusso reserve → descrizione → confirm del pannello «Genera Codice»
 * della pagina Codex, ma con la famiglia già decisa dal Tipo selezionato: qui non ha
 * senso far scegliere il prefisso, si sta compilando la distinta di QUEL tipo.
 *
 * Il codice viene PRENOTATO all'apertura (così due persone che creano un 511 nello
 * stesso momento non ricevono lo stesso progressivo) e RILASCIATO se si annulla o si
 * chiude: senza il rilascio il progressivo del giorno resterebbe bruciato per 10 minuti.
 */
export function NewCompositeDialog({
  open,
  typeCode,
  typeLabel,
  onCreated,
  onClose,
}: {
  open: boolean
  typeCode: string
  typeLabel: string
  onCreated: (created: CodexGeneratedCode) => void
  onClose: () => void
}) {
  const [descr, setDescr] = React.useState("")
  const [reservation, setReservation] = React.useState<{
    id: number
    code: string
  } | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  // Ref e non stato: serve al cleanup, che deve vedere il valore corrente e non
  // quello catturato alla prima render.
  const reservationRef = React.useRef<number | null>(null)
  reservationRef.current = reservation?.id ?? null

  // Prenotazione all'apertura.
  React.useEffect(() => {
    if (!open) return
    let annullato = false
    setDescr("")
    setError(null)
    setReservation(null)
    setBusy(true)
    void reserveCodexCode(typeCode)
      .then((res) => {
        if (annullato) {
          // Dialog già chiuso mentre la chiamata era in volo: la prenotazione
          // appena nata non la vedrebbe più nessuno, va rilasciata subito.
          void releaseCodexReservation(res.reservationId).catch(() => undefined)
          return
        }
        setReservation({ id: res.reservationId, code: res.codice })
      })
      .catch((err: Error) => {
        if (!annullato) setError(err.message)
      })
      .finally(() => {
        if (!annullato) setBusy(false)
      })
    return () => {
      annullato = true
    }
  }, [open, typeCode])

  function releaseAndClose() {
    const id = reservationRef.current
    reservationRef.current = null
    setReservation(null)
    if (id != null) void releaseCodexReservation(id).catch(() => undefined)
    onClose()
  }

  async function confirm() {
    const testo = descr.trim()
    if (!reservation || busy || testo.length === 0) return
    setBusy(true)
    setError(null)
    try {
      const created = await confirmCodexReservation(reservation.id, testo)
      reservationRef.current = null
      setReservation(null)
      onCreated(created)
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && releaseAndClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Nuovo composito {typeCode}</DialogTitle>
          <DialogDescription>
            {typeLabel}. Il codice lo assegna il sistema (famiglia + data + progressivo):
            scrivi solo la descrizione.
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-3">
          <div className="flex items-center gap-2 text-sm">
            <span className="text-muted-foreground">Codice assegnato:</span>
            <span className="font-mono font-bold text-primary">
              {reservation?.code ?? (busy ? "prenotazione…" : "—")}
            </span>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="new-composite-descr">Descrizione</Label>
            <Input
              id="new-composite-descr"
              autoFocus
              value={descr}
              placeholder="Es. Colonnina luminosa 3 luci"
              onChange={(event) => setDescr(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  event.preventDefault()
                  void confirm()
                }
              }}
            />
          </div>

          {error ? (
            <p className="text-sm text-destructive" role="alert">
              {error}
            </p>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={releaseAndClose}>
            Annulla
          </Button>
          <Button
            disabled={busy || reservation == null || descr.trim().length === 0}
            onClick={() => void confirm()}
          >
            Crea
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
