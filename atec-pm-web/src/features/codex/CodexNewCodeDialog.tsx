import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Lock, Wand2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  releaseCodexReservation,
  reserveCodexNewCode,
  updateCodexNewCode,
} from "@/lib/api/codex"
import type { CodexListItem } from "@/lib/api/types"
import { decodeHtmlEntities } from "@/lib/format"

/** Famiglie della nuova codifica (ampliamento Codex 21/07/2026): 201/211/221. */
const NEW_CODE_FAMILIES = [
  { prefix: "201", label: "Generici" },
  { prefix: "211", label: "Elettrici" },
  { prefix: "221", label: "Pneumatici" },
]

/**
 * Dialog di ricodifica: il codice NON si digita a mano (regola 21/07/2026).
 * L'operatore sceglie la famiglia, il sistema PRENOTA il prossimo codice
 * (regola Codex: famiglia + data odierna ggMMaa + progressivo del giorno, stessa
 * meccanica del generatore: più operatori in parallelo non ricevono mai lo stesso
 * codice) e l'operatore lo accetta con Salva. Annulla/chiusura liberano la
 * prenotazione (TTL 10 min come rete di sicurezza). Il server rifiuta qualunque
 * salvataggio senza prenotazione valida. Ruoli: ADMIN / PM / RESP_REPARTO.
 */
export function CodexNewCodeDialog({
  item,
  onClose,
  onSaved,
}: {
  item: CodexListItem | null
  onClose: () => void
  onSaved: () => void
}) {
  const confirm = useConfirm()
  const [error, setError] = React.useState<string | null>(null)
  const [reserving, setReserving] = React.useState(false)
  const [reservedCode, setReservedCode] = React.useState<string | null>(null)
  // Id prenotazione attiva (ref: serve anche nei cleanup senza re-render).
  const reservationRef = React.useRef<number | null>(null)

  const releaseCurrent = React.useCallback(() => {
    const id = reservationRef.current
    if (id != null) {
      reservationRef.current = null
      setReservedCode(null)
      // Fire-and-forget: se fallisce, la prenotazione scade da sola (TTL 10 min).
      void releaseCodexReservation(id).catch(() => {})
    }
  }, [])

  React.useEffect(() => {
    if (item) {
      releaseCurrent()
      setError(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item?.id])

  const saveMutation = useMutation({
    mutationFn: (value: string) =>
      updateCodexNewCode(item!.id, value, reservationRef.current),
    onSuccess: () => {
      // Il server ha già liberato la prenotazione insieme al salvataggio.
      reservationRef.current = null
      setReservedCode(null)
      onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  const reserve = async (family: string) => {
    setReserving(true)
    setError(null)
    try {
      releaseCurrent()
      const res = await reserveCodexNewCode(family)
      reservationRef.current = res.reservationId
      setReservedCode(res.codice)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setReserving(false)
    }
  }

  const handleClose = () => {
    releaseCurrent()
    onClose()
  }

  const handleRemove = async () => {
    const ok = await confirm({
      title: "Rimuovi codice nuovo",
      description: `Rimuovere il codice nuovo ${item?.codiceNuovo} da ${item?.codice}?\nLa riga torna "non ricodificata".`,
      confirmLabel: "Rimuovi",
    })
    if (ok) saveMutation.mutate("")
  }

  if (!item) return null

  return (
    <Dialog open onOpenChange={(next) => !next && handleClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Codice nuovo — {item.codice}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">
            <span
              className="block max-w-full truncate"
              title={decodeHtmlEntities(item.descr)}
            >
              {decodeHtmlEntities(item.descr) || "(senza descrizione)"}
            </span>
          </p>

          {item.codiceNuovo ? (
            <p className="text-sm">
              Codice nuovo attuale:{" "}
              <span className="font-mono font-medium tabular-nums">
                {item.codiceNuovo}
              </span>
            </p>
          ) : null}

          <div className="space-y-1.5">
            <p className="text-xs text-muted-foreground">
              Il codice è generato dal sistema (famiglia + data + progressivo):
              scegli la famiglia, poi conferma con Salva.
            </p>
            <div className="flex flex-wrap gap-2">
              {NEW_CODE_FAMILIES.map((family) => (
                <Button
                  key={family.prefix}
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reserving || saveMutation.isPending}
                  onClick={() => void reserve(family.prefix)}
                >
                  <Wand2 className="size-3.5" />
                  {family.prefix} {family.label}
                </Button>
              ))}
            </div>
          </div>

          <div className="rounded-md border bg-muted/40 px-3 py-2.5">
            {reservedCode ? (
              <div className="space-y-1">
                <p className="font-mono text-lg font-semibold tabular-nums">
                  {reservedCode}
                </p>
                <p className="flex items-center gap-1 text-xs text-emerald-600">
                  <Lock className="size-3" />
                  Prenotato per te — Salva per confermarlo, Annulla lo libera.
                </p>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                Nessun codice generato — scegli una famiglia qui sopra.
              </p>
            )}
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter className="gap-2 sm:justify-between">
          <div>
            {item.codiceNuovo ? (
              <Button
                type="button"
                variant="outline"
                className="text-destructive"
                disabled={saveMutation.isPending}
                onClick={() => void handleRemove()}
              >
                Rimuovi
              </Button>
            ) : null}
          </div>
          <div className="flex gap-2">
            <Button variant="outline" onClick={handleClose}>
              Annulla
            </Button>
            <Button
              disabled={!reservedCode || saveMutation.isPending}
              onClick={() => reservedCode && saveMutation.mutate(reservedCode)}
            >
              Salva
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
