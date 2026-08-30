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

/** Normalizzazione del codice ATEC che decide il gruppo RDO: UNICA copia, usata sia
 *  dall'anteprima nel render sia da handleSubmit, così non possono divergere. */
function codiceAtecEffettivo(item: AcquistiInboxItem): string {
  return (item.atecCode || "").replace(/\./g, "").trim()
}

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

  // Anteprima VERITIERA: gli stessi gruppi che handleSubmit creerà (una RDO per
  // codice ATEC, righe senza codice escluse), mostrati PRIMA della conferma invece
  // che scoperti dai toast dopo.
  const { numRdo, senzaCodice } = React.useMemo(() => {
    const codici = new Set<string>()
    let esclusi = 0
    for (const item of items ?? []) {
      const chiave = codiceAtecEffettivo(item)
      if (chiave) codici.add(chiave)
      else esclusi += 1
    }
    return { numRdo: codici.size, senzaCodice: esclusi }
  }, [items])

  const handleSubmit = async () => {
    if (!items || items.length === 0) return
    setSubmitting(true)
    try {
      // Una gara = UN articolo. Prima si prendeva il codice ATEC della PRIMA riga e ci si
      // infilavano dentro tutte le altre: articoli diversi nella stessa gara, fornitori
      // invitati che c'entravano con un pezzo solo, e in aggiudicazione la riga di un altro
      // pezzo veniva riscritta come quella del vincitore. Ora si raggruppa per codice ATEC
      // e nasce una RDO per gruppo; le righe senza codice restano fuori (vedi sotto).
      const gruppi = new Map<string, number[]>()
      const senzaCodice: string[] = []
      for (const item of items) {
        const chiave = codiceAtecEffettivo(item)
        // Senza codice ATEC il server non sa chi invitare: cercherebbe i fornitori con
        // `WHERE atec_code = 'GENERICO'` e non ne troverebbe nessuno, creando una RDO MUTA
        // — zero offerte, nessuna mail possibile, e le righe bloccate dentro perché per la
        // guardia anti-doppione risultano già in gara. Meglio non crearla e dirlo.
        if (!chiave) {
          senzaCodice.push(item.partNumber || item.description || `#${item.id}`)
          continue
        }
        const ids = gruppi.get(chiave)
        if (ids) ids.push(item.id)
        else gruppi.set(chiave, [item.id])
      }

      if (gruppi.size === 0) {
        notifyError(
          "Nessuna riga ha il codice ATEC: senza, non si sa quali fornitori invitare. " +
            "Assegna il codice dalla colonna «Cod. ATEC» e riprova."
        )
        return
      }

      // Un gruppo che fallisce non deve fermare gli altri, e soprattutto non deve far
      // saltare `onCreated`: senza quello le liste non si aggiornano e le RDO gia create
      // restano invisibili, mentre al secondo tentativo la guardia anti-doppione le
      // rifiuta tutte («righe gia in RDO non annullate»). Cosi invece i gruppi riusciti
      // si vedono e quelli falliti restano ritentabili, perche le loro righe sono libere.
      const createdIds: number[] = []
      const falliti: string[] = []
      for (const [atecCode, bomItemIds] of gruppi) {
        try {
          const ids = await createPurchaseRfq({
            atecCode,
            description,
            notes,
            bomItemIds,
          })
          createdIds.push(...ids)
        } catch (err) {
          falliti.push(
            `${atecCode}: ${err instanceof Error ? err.message : String(err)}`
          )
        }
      }

      const escluse = senzaCodice.length
        ? ` ${senzaCodice.length} righe senza Cod. ATEC NON sono state messe in gara: ${senzaCodice.slice(0, 3).join(", ")}${senzaCodice.length > 3 ? " …" : ""}.`
        : ""
      if (createdIds.length > 0) {
        notifyInfo(
          (gruppi.size > 1
            ? `${createdIds.length} RDO create su ${gruppi.size} articoli selezionati.`
            : `RDO creata con successo! (${createdIds.length} commessa/e)`) + escluse
        )
        onCreated(createdIds)
      }
      if (falliti.length > 0) {
        // `escluse` va ripetuto qui: se TUTTI i gruppi falliscono il toast di successo non
        // esce, e le righe senza Cod. ATEC sparirebbero dalla gara senza dirlo a nessuno.
        notifyError(
          `${falliti.length} gare non create: ${falliti.slice(0, 2).join(" · ")}${falliti.length > 2 ? " …" : ""}` +
            (createdIds.length === 0 ? escluse : "")
        )
      }
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
            {numRdo > 1
              ? "Conferma gli articoli e le note per creare le gare e richiedere i preventivi ai fornitori."
              : "Conferma gli articoli e le note per creare la gara e richiedere i preventivi ai fornitori."}
          </DialogDescription>
        </DialogHeader>

        {items && (
          <div className="space-y-4 py-2 text-xs">
            <div
              className={
                numRdo === 0
                  ? "rounded border border-amber-500/60 bg-amber-500/5 p-2 font-medium text-amber-700 dark:text-amber-400"
                  : "rounded border bg-muted/40 p-2 font-medium text-foreground"
              }
            >
              {numRdo === 0
                ? "Nessuna riga ha il Cod. ATEC: non verrà creata alcuna RDO. Assegna i codici dall'Inbox Acquisti (icona catena) e riprova."
                : numRdo === 1
                  ? "Verrà creata 1 richiesta d'offerta (RDO)."
                  : `Verranno create ${numRdo} richieste d'offerta (RDO): una per ogni codice ATEC.`}
              {numRdo > 0 && senzaCodice > 0 && (
                <span className="font-normal text-amber-700 dark:text-amber-400">
                  {/* «dalla gara/dalle gare» segue il numero di RDO, non di righe. */}
                  {`${senzaCodice === 1 ? " 1 riga senza Cod. ATEC resterà fuori" : ` ${senzaCodice} righe senza Cod. ATEC resteranno fuori`} ${numRdo === 1 ? "dalla gara" : "dalle gare"}.`}
                </span>
              )}
            </div>
            <div>
              <Label className="font-semibold text-foreground">
                Articoli selezionati ({items.length})
              </Label>
              <div className="mt-1 max-h-48 overflow-y-auto space-y-1.5 border rounded p-2 bg-muted/40">
                {items.map((item) => {
                  const fuoriGara = !codiceAtecEffettivo(item)
                  return (
                    <div
                      key={item.id}
                      className={
                        fuoriGara
                          ? "text-xs p-1.5 border rounded border-amber-500/60 bg-amber-500/5"
                          : "text-xs p-1.5 border rounded bg-card"
                      }
                    >
                      <div className="flex justify-between items-start">
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
                      {fuoriGara && (
                        <div className="mt-1 text-[11px] font-medium text-amber-700 dark:text-amber-400">
                          Questa riga NON entrerà in gara: assegna prima il Cod. ATEC (icona
                          catena nell'Inbox Acquisti).
                        </div>
                      )}
                    </div>
                  )
                })}
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
            disabled={submitting || numRdo === 0}
            title={
              numRdo === 0
                ? "Nessuna riga ha il Cod. ATEC: non c'è nulla da mettere in gara."
                : undefined
            }
            className="gap-1"
          >
            <FileCheck2 className="h-4 w-4" />
            {submitting
              ? "Creazione in corso..."
              : numRdo > 1
                ? `Conferma e Crea ${numRdo} RDO`
                : "Conferma e Crea RDO"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
