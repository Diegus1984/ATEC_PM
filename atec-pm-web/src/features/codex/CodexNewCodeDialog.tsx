import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Link2, Lock, Wand2 } from "lucide-react"

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
  fetchCodexPrefixes,
  releaseCodexReservation,
  reserveCodexNewCode,
  updateCodexNewCode,
} from "@/lib/api/codex"
import type { CodexListItem } from "@/lib/api/types"
import { decodeHtmlEntities } from "@/lib/format"

/** Codice scelto per la riga: generato dal sistema (prenotato) o codice originale. */
interface CodeChoice {
  code: string
  original: boolean
}

/**
 * Dialog di ricodifica: il codice NON si digita a mano (regola 21/07/2026).
 * L'operatore sceglie la famiglia, il sistema PRENOTA il prossimo codice
 * (regola Codex: famiglia + data odierna ggMMaa + progressivo del giorno, stessa
 * meccanica del generatore: più operatori in parallelo non ricevono mai lo stesso
 * codice) e l'operatore lo accetta con Salva. Annulla/chiusura liberano la
 * prenotazione (TTL 10 min come rete di sicurezza). Il server rifiuta qualunque
 * salvataggio senza prenotazione valida. In alternativa la voce «Codice originale»
 * (26/08/2026) assegna come codifica il codice storico della riga stessa, senza
 * ricodificare (niente prenotazione: il codice esiste già ed è suo).
 * Ruoli: ADMIN / PM / RESP_REPARTO.
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
  const [choice, setChoice] = React.useState<CodeChoice | null>(null)
  // Id prenotazione attiva (ref: serve anche nei cleanup senza re-render).
  const reservationRef = React.useRef<number | null>(null)

  // TUTTE le famiglie del generatore (#127, come il dialogo del Catalogo): la lista
  // la governa il server (/api/codex/prefixes — la 401 ritirata è già esclusa lì).
  const prefixesQuery = useQuery({
    queryKey: ["codex-prefixes"],
    queryFn: fetchCodexPrefixes,
    enabled: item != null,
  })
  const families = prefixesQuery.data ?? []

  const releaseReservation = React.useCallback(() => {
    const id = reservationRef.current
    if (id != null) {
      reservationRef.current = null
      // Fire-and-forget: se fallisce, la prenotazione scade da sola (TTL 10 min).
      void releaseCodexReservation(id).catch(() => {})
    }
  }, [])

  const clearChoice = React.useCallback(() => {
    releaseReservation()
    setChoice(null)
  }, [releaseReservation])

  React.useEffect(() => {
    if (item) {
      clearChoice()
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
      setChoice(null)
      onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  const reserve = async (family: string) => {
    setReserving(true)
    setError(null)
    try {
      clearChoice()
      const res = await reserveCodexNewCode(family)
      reservationRef.current = res.reservationId
      setChoice({ code: res.codice, original: false })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setReserving(false)
    }
  }

  const handleClose = () => {
    clearChoice()
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
      {/* max-w-lg: con tutte le famiglie del generatore (#127) i pulsanti sono 8+. */}
      <DialogContent className="sm:max-w-lg">
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
              {families.length === 0 && prefixesQuery.isLoading ? (
                <span className="text-xs text-muted-foreground">
                  Caricamento famiglie…
                </span>
              ) : (
                families.map((family) => (
                  <Button
                    key={family.codice}
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={reserving || saveMutation.isPending}
                    onClick={() => void reserve(family.codice)}
                  >
                    <Wand2 className="size-3.5" />
                    {family.codice} — {family.descrizione}
                  </Button>
                ))
              )}
            </div>
          </div>

          <div className="space-y-1.5">
            <p className="text-xs text-muted-foreground">
              Oppure assegna come codifica il codice originale della riga, senza
              ricodificare:
            </p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={reserving || saveMutation.isPending || !item.codice}
              onClick={() => {
                setError(null)
                releaseReservation()
                setChoice({ code: item.codice, original: true })
              }}
            >
              <Link2 className="size-3.5" />
              Codice originale{item.codice ? ` — ${item.codice}` : ""}
            </Button>
          </div>

          <div className="rounded-md border bg-muted/40 px-3 py-2.5">
            {choice ? (
              <div className="space-y-1">
                <p className="font-mono text-lg font-semibold tabular-nums">
                  {choice.code}
                </p>
                {choice.original ? (
                  <p className="flex items-center gap-1 text-xs text-primary">
                    <Link2 className="size-3" />
                    Codice originale — Salva lo conferma come codifica della riga.
                  </p>
                ) : (
                  <p className="flex items-center gap-1 text-xs text-emerald-600">
                    <Lock className="size-3" />
                    Prenotato per te — Salva per confermarlo, Annulla lo libera.
                  </p>
                )}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                Nessun codice scelto — genera dalla famiglia o usa il codice
                originale.
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
              disabled={!choice || saveMutation.isPending}
              onClick={() => choice && saveMutation.mutate(choice.code)}
            >
              Salva
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
