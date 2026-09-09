import * as React from "react"

import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Textarea } from "@/components/ui/textarea"

/** Una mail pronta da spedire. */
export interface MessaggioMail {
  /**
   * Il destinatario (l'id dipendente). Non è la chiave del selettore: dal «Controllo di
   * ieri» la stessa persona può ricevere due mail (due giornate), e il selettore va per
   * posizione nell'elenco.
   */
  id: number
  nome: string
  email?: string | null
  subject: string
  body: string
}

interface AnteprimaMailDialogProps {
  /** Le mail da mostrare; null = dialogo chiuso. */
  messaggi: MessaggioMail[] | null
  titolo: string
  /** Riga sotto il titolo: chi verrà scritto, chi è senza email, chi era già stato chiesto. */
  descrizione?: string
  confermaLabel: string
  inviando?: boolean
  onConferma: () => void
  onOpenChange: (open: boolean) => void
}

/**
 * L'anteprima integrale della mail prima di spedirla (PIANO-HR-PORT-ORIGINALE.md, voce 4):
 * port di `ConfirmDialog.ShowEmail` del programma «Timbrature».
 *
 * <p>Un dialogo solo, usato sia dal sollecito della giornata (una mail) sia da quello
 * mensile del Calendario (N mail): là il riepilogo dei destinatari resta — dice a colpo
 * d'occhio quanti sono e chi è senza indirizzo — e qui si aggiunge il testo per intero,
 * perché un sollecito sbagliato lo legge una persona.</p>
 *
 * <p>Con più destinatari il testo si sfoglia con il selettore: il corpo cambia da persona a
 * persona (i giorni mancanti sono i suoi), quindi mostrarne uno solo sarebbe una bugia.</p>
 */
export function AnteprimaMailDialog({
  messaggi,
  titolo,
  descrizione,
  confermaLabel,
  inviando = false,
  onConferma,
  onOpenChange,
}: AnteprimaMailDialogProps) {
  // La scelta è la POSIZIONE nell'elenco, non l'id del dipendente: due mail alla stessa
  // persona (due giornate del Controllo di ieri) devono restare due voci distinte.
  const [scelto, setScelto] = React.useState<number>(0)

  // Ogni volta che si riapre si riparte dalla prima mail.
  const quanti = messaggi?.length ?? 0
  React.useEffect(() => {
    setScelto(0)
  }, [messaggi])

  const corrente = messaggi?.[scelto] ?? messaggi?.[0] ?? null

  const opzioni: LookupComboboxOption<number>[] = React.useMemo(
    () =>
      (messaggi ?? []).map((m, indice) => ({
        id: indice,
        name: m.nome,
        hint: m.email ?? "senza email",
      })),
    [messaggi]
  )

  return (
    <Dialog open={messaggi != null} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] flex-col sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{titolo}</DialogTitle>
          {descrizione && <DialogDescription>{descrizione}</DialogDescription>}
        </DialogHeader>

        {corrente == null ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nessuna mail da mostrare.
          </p>
        ) : (
          <div className="flex min-h-0 flex-1 flex-col gap-3">
            {quanti > 1 && (
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground">
                  {quanti} mail — anteprima di:
                </span>
                <LookupCombobox<number>
                  options={opzioni}
                  value={scelto}
                  onValueChange={(v) => setScelto(v ?? 0)}
                  placeholder="Scegli la mail"
                  className="w-72"
                />
              </div>
            )}

            <div className="space-y-1 rounded-lg border bg-muted/30 p-3 text-sm">
              <p>
                <span className="font-semibold text-muted-foreground">A: </span>
                {corrente.email ? (
                  <span>
                    {corrente.nome} &lt;{corrente.email}&gt;
                  </span>
                ) : (
                  <span className="text-destructive">
                    {corrente.nome} — nessun indirizzo email
                  </span>
                )}
              </p>
              <p>
                <span className="font-semibold text-muted-foreground">Oggetto: </span>
                <span className="font-medium">{corrente.subject}</span>
              </p>
            </div>

            <Textarea
              readOnly
              value={corrente.body}
              className="min-h-64 flex-1 resize-none font-mono text-xs"
            />
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Annulla
          </Button>
          <Button onClick={onConferma} disabled={inviando || corrente == null}>
            {inviando ? "Invio…" : confermaLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
