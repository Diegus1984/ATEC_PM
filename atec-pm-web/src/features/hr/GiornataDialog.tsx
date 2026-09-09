import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Send, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { deleteHrAdjustment, sendHrAdjustment, sendHrDayToEcos } from "@/lib/api/hr"
import type { HrDay } from "@/lib/api/types"
import { formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { riassuntoInvioEcos, versoTimbratura } from "./invio-ecos"
import { StatoGiornata, statoGiornata } from "./stato-giornata"

function oraDa(iso: string): string {
  const d = new Date(iso)
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`
}

/** «Mercoledì 12 agosto 2026», col nome del giorno in maiuscolo. */
function giornoEsteso(iso: string): string {
  const testo = new Date(iso).toLocaleDateString("it-IT", {
    weekday: "long",
    day: "numeric",
    month: "long",
    year: "numeric",
  })
  return testo.charAt(0).toUpperCase() + testo.slice(1)
}

/** Un orario del giorno: in grande quello che vale, sotto quello timbrato (sempre, anche se uguale). */
function RiquadroOra({
  etichetta,
  valore,
  timbrato,
}: {
  etichetta: string
  valore: string
  timbrato: string
}) {
  const mancante = valore === "??:??"
  const vuoto = !valore
  const conTimbrata = !vuoto && !mancante && Boolean(timbrato)
  return (
    <div
      className={cn(
        "rounded-md border px-2.5 py-2",
        mancante && "border-destructive/60 bg-destructive/10"
      )}
    >
      <p className="text-xs text-muted-foreground">{etichetta}</p>
      <p
        className={cn(
          "text-lg font-bold leading-tight tabular-nums",
          mancante && "text-sm text-destructive",
          vuoto && "text-muted-foreground"
        )}
      >
        {mancante ? "Non timbrata" : vuoto ? "—" : valore}
      </p>
      <p className="text-xs text-muted-foreground tabular-nums">
        {conTimbrata ? `timbrato ${timbrato}` : " "}
      </p>
    </div>
  )
}

/** Cosa dire a chi apre la giornata, in base allo stato letto dal motore. */
function spiegazione(g: HrDay, nome: string, canWrite: boolean): string | null {
  const st = statoGiornata(g)
  const chi = nome.split(" ")[0] || "il dipendente"
  if (st.tone === "bad") {
    if (st.label.startsWith("Nessuna timbratura"))
      return canWrite
        ? `Giorno lavorativo senza nessuna timbratura e senza assenza registrata su Ecos: da chiarire con ${chi}. Se era assente, l'assenza va registrata su Ecos; se ha lavorato, inserisci qui le timbrature con il motivo.`
        : "Giorno lavorativo senza timbrature e senza assenza registrata. Segnalalo a chi gestisce le presenze."
    if (st.label.startsWith("Manca l'uscita"))
      return canWrite
        ? `Senza l'uscita non si possono contare le ore del pomeriggio. Chiedi a ${chi} a che ora è uscito e inserisci l'orario qui sotto: la timbratura originale resta, la correzione si aggiunge con il tuo nome.`
        : "Senza l'uscita non si possono contare le ore del pomeriggio. Segnalalo a chi gestisce le presenze."
    return canWrite
      ? "Le timbrature di questo giorno non tornano: guardale qui sotto e, se serve, aggiungi quella che manca con il motivo."
      : "Le timbrature di questo giorno non tornano. Segnalalo a chi gestisce le presenze."
  }
  if (st.tone === "warn" && st.label.startsWith("Uscita non timbrata"))
    return "L'uscita non è stata timbrata: il motore ha contato la giornata fino alle 17:00. Se l'orario vero è diverso, aggiungi la rettifica."
  if (st.label === "Giornata in corso") return "La giornata è ancora aperta: le ore si contano alla fine."
  return null
}

/**
 * Dettaglio di una giornata: i quattro orari in grande con sotto l'ora timbrata, una frase
 * che dice cosa fare, le timbrature come stanno su Ecos (Ecos + rettifiche) e, per chi
 * ha la scrittura, la rettifica. La timbratura originale resta SEMPRE — la rettifica è una
 * riga in più con autore e motivo, e solo le rettifiche si possono togliere.
 */
export function GiornataDialog({
  open,
  onOpenChange,
  giornata,
  employeeId,
  employeeName,
  canWrite,
  onChanged,
  azioni,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  giornata: HrDay | null
  employeeId: number
  employeeName: string
  canWrite: boolean
  onChanged: () => void
  /** Comandi della giornata decisi dalla pagina (email al dipendente, rilettura da Ecos). */
  azioni?: React.ReactNode
}) {
  const confirm = useConfirm()
  const [ora, setOra] = React.useState("")
  const [verso, setVerso] = React.useState<"IN" | "OUT">("IN")
  const [motivo, setMotivo] = React.useState("")

  // Se manca l'uscita, la rettifica parte già impostata su «Uscita».
  const mancaUscita = giornata
    ? (() => {
        const st = statoGiornata(giornata)
        return st.tone === "bad" && st.label.startsWith("Manca l'uscita")
      })()
    : false

  React.useEffect(() => {
    if (!open) return
    setOra("")
    setVerso(mancaUscita ? "OUT" : "IN")
    setMotivo("")
  }, [open, mancaUscita])

  const rettifica = useMutation({
    mutationFn: sendHrAdjustment,
    onSuccess: () => {
      notifySuccess("Rettifica registrata")
      onChanged()
      onOpenChange(false)
    },
    onError: (e) => notifyError((e as Error).message),
  })

  const elimina = useMutation({
    mutationFn: deleteHrAdjustment,
    onSuccess: () => {
      notifySuccess("Rettifica eliminata")
      onChanged()
      onOpenChange(false)
    },
    onError: (e) => notifyError((e as Error).message),
  })

  // «Invia a Ecos»: gli orari arrotondati sovrascrivono su Ecos quelli timbrati. L'esito
  // arriva sempre come dato: un fallimento parziale si legge nel registro, non in un errore.
  const invioEcos = useMutation({
    mutationFn: sendHrDayToEcos,
    onSuccess: (esito) => {
      if (esito.success) notifySuccess(esito.message)
      else notifyError(esito.message)
      onChanged()
    },
    onError: (e) => notifyError((e as Error).message),
  })

  if (!giornata) return null
  const giorno = giornata.workDate.slice(0, 10)
  const stato = statoGiornata(giornata)
  const frase = spiegazione(giornata, employeeName, canWrite)
  const ecos = riassuntoInvioEcos(giornata)

  async function inviaAEcos() {
    if (!giornata || !ecos.daScrivere) return
    const modifiche = ecos.daInviare.length - ecos.daInserire.length
    const pezzi = [
      modifiche > 0 ? `${modifiche} con l'orario arrotondato al posto di quello timbrato` : "",
      ecos.daInserire.length > 0
        ? `${ecos.daInserire.length} rettifiche inserite su Ecos come timbrature nuove`
        : "",
      // La pausa dedotta (09/09/2026): su Ecos nasce come due strisciate vere.
      ecos.pausaDaInserire
        ? `la pausa pranzo dedotta dal motore (${giornata.clockOut1.replace("*", "")} e ${giornata.clockIn2.replace("*", "")}) inserita su Ecos come timbrature nuove`
        : "",
    ].filter(Boolean)
    const ok = await confirm({
      title: "Scrivere su Ecos la giornata calcolata?",
      description:
        `${employeeName || "Questa persona"}, ${giornoEsteso(giornata.workDate)}: ${pezzi.join("; ")}.` +
        " L'ora timbrata resta qui e nel registro degli invii.",
      confirmLabel: "Scrivi su Ecos",
    })
    if (ok) invioEcos.mutate({ employeeId, workDate: giorno })
  }

  function inviaRettifica() {
    if (!ora || !motivo.trim() || !giornata) return
    rettifica.mutate({
      employeeId,
      punchedAt: `${giorno}T${ora}:00`,
      direction: verso,
      reason: motivo.trim(),
    })
  }

  async function eliminaRiga(id: number) {
    const ok = await confirm({
      title: "Eliminare la rettifica?",
      description:
        "La giornata verrà ricalcolata senza questa timbratura. Il grezzo del rilevatore non si tocca.",
      confirmLabel: "Elimina",
      destructive: true,
    })
    if (ok) elimina.mutate(id)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        className="sm:max-w-xl"
        // La finestra di conferma (useConfirm) è un portale fuori da questo dialogo: per
        // Radix un clic lì dentro è «fuori», e chiudeva anche la giornata. Con «Invia a
        // Ecos» il dialogo deve restare aperto per mostrare l'esito e il registro (08/09).
        onInteractOutside={(e) => {
          const target = e.detail.originalEvent.target as Element | null
          if (target?.closest?.('[role="alertdialog"]')) e.preventDefault()
        }}
      >
        <DialogHeader>
          <DialogTitle>{giornoEsteso(giornata.workDate)}</DialogTitle>
          <DialogDescription className="flex flex-wrap items-center gap-2">
            {employeeName && <span>{employeeName}</span>}
            <StatoGiornata stato={stato} />
          </DialogDescription>
        </DialogHeader>

        {(giornata.hasData || giornata.punches.length > 0) && (
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <RiquadroOra etichetta="Entrata" valore={giornata.clockIn1} timbrato={giornata.raw.clockIn1} />
            <RiquadroOra etichetta="Uscita" valore={giornata.clockOut1} timbrato={giornata.raw.clockOut1} />
            <RiquadroOra etichetta="Entrata" valore={giornata.clockIn2} timbrato={giornata.raw.clockIn2} />
            <RiquadroOra etichetta="Uscita" valore={giornata.clockOut2} timbrato={giornata.raw.clockOut2} />
          </div>
        )}

        {frase && <p className="rounded-md bg-muted px-3 py-2 text-sm">{frase}</p>}

        {giornata.note && stato.tone !== "bad" && stato.tone !== "info" && (
          <p className="text-xs text-muted-foreground">Nota del calcolo: {giornata.note}</p>
        )}

        <div className="space-y-1">
          <p className="text-sm font-medium">Timbrature su Ecos</p>
          {giornata.punches.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nessuna timbratura.</p>
          ) : (
            <ul className="space-y-1">
              {giornata.punches.map((t) => (
                <li
                  key={t.id}
                  className="flex items-center gap-2 rounded-md border px-2 py-1 text-sm"
                >
                  <span className="tabular-nums font-medium">{oraDa(t.punchedAt)}</span>
                  <span>{t.direction === "IN" ? "Entrata" : "Uscita"}</span>
                  <Badge variant={t.source === "ADJUSTMENT" ? "default" : "outline"}>
                    {t.source === "ADJUSTMENT" ? "RETTIFICA" : t.source}
                  </Badge>
                  {t.reason && (
                    <span
                      className="min-w-0 flex-1 truncate text-xs text-muted-foreground"
                      title={`${t.reason}${t.createdBy ? ` — ${t.createdBy}` : ""}`}
                    >
                      {t.reason}
                      {t.createdBy ? ` — ${t.createdBy}` : ""}
                    </span>
                  )}
                  {canWrite && t.source === "ADJUSTMENT" && (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="ml-auto"
                      disabled={elimina.isPending}
                      onClick={() => void eliminaRiga(t.id)}
                      title="Elimina la rettifica"
                    >
                      <Trash2 className="size-4" />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {(ecos.diEcos > 0 || ecos.pausaDaInserire) && (
          <div className="space-y-2 rounded-md border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-medium">Orari arrotondati su Ecos</p>
              {ecos.daScrivere ? (
                <Badge variant="secondary">
                  Da inviare: {ecos.daInviare.length + (ecos.pausaDaInserire ? 2 : 0)}
                </Badge>
              ) : ecos.allineato ? (
                <Badge variant="outline">Allineato con Ecos</Badge>
              ) : null}
              {ecos.ultimoInvio && (
                <span className="text-xs text-muted-foreground">
                  Inviato il {formatDateTimeShort(ecos.ultimoInvio)}
                </span>
              )}
            </div>
            {ecos.daInviare.length > 0 && (
              <ul className="space-y-0.5 text-sm">
                {ecos.daInviare.map((t) => (
                  <li key={t.id} className="flex items-center gap-2 tabular-nums">
                    <span className="w-14">{versoTimbratura(t.direction)}</span>
                    <span className="text-muted-foreground">{oraDa(t.ecosPunchedAt ?? t.punchedAt)}</span>
                    <span className="text-muted-foreground">{t.ecosInsert ? "nuova su Ecos alle" : "diventa"}</span>
                    <span className="font-medium">{t.roundedAt ? oraDa(t.roundedAt) : "—"}</span>
                    {t.ecosInsert && <Badge variant="default">RETTIFICA</Badge>}
                  </li>
                ))}
              </ul>
            )}
            {ecos.pausaDaInserire && (
              <p className="text-sm tabular-nums">
                Pausa pranzo dedotta dal motore: su Ecos nascono{" "}
                <span className="font-medium">
                  uscita {giornata.clockOut1.replace("*", "")} e rientro {giornata.clockIn2.replace("*", "")}
                </span>{" "}
                <Badge variant="default">PAUSA</Badge>
              </p>
            )}
            {ecos.nonInviabili > 0 && (
              <p className="text-xs text-muted-foreground">
                {ecos.nonInviabili === 1
                  ? "Una timbratura non è inviabile: l'arrotondamento cambierebbe giorno."
                  : `${ecos.nonInviabili} timbrature non sono inviabili: l'arrotondamento cambierebbe giorno.`}
              </p>
            )}
            {ecos.incerte > 0 && (
              <p className="text-xs text-destructive">
                {ecos.incerte === 1
                  ? "Una rettifica è stata mandata a Ecos senza risposta certa: verificare su Ecos. Non riparte da sola; se là non c'è, togliere la rettifica e rifarla."
                  : `${ecos.incerte} rettifiche sono state mandate a Ecos senza risposta certa: verificare su Ecos. Non ripartono da sole; se là non ci sono, togliere le rettifiche e rifarle.`}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Su Ecos l'orario timbrato viene sovrascritto con quello arrotondato e le rettifiche
              nascono come timbrature nuove. L'ora timbrata resta qui e nel registro degli invii.
            </p>
            {canWrite && ecos.daScrivere && (
              <div className="flex justify-end">
                <Button size="sm" disabled={invioEcos.isPending} onClick={() => void inviaAEcos()}>
                  <Send className="size-4" />
                  Scrivi su Ecos
                </Button>
              </div>
            )}
            {giornata.ecosSends.length > 0 && (
              <div className="space-y-1">
                <p className="text-xs font-medium">Registro invii</p>
                <ul className="space-y-0.5 text-xs">
                  {giornata.ecosSends.map((invio) => (
                    <li
                      key={invio.id}
                      className={cn(
                        "flex flex-wrap items-center gap-x-2 tabular-nums",
                        invio.outcome !== "OK" && "text-destructive"
                      )}
                    >
                      <span>{formatDateTimeShort(invio.sentAt)}</span>
                      <span>
                        {versoTimbratura(invio.direction)} {oraDa(invio.punchedAt)}{" "}
                        {invio.previousTime ? "diventa" : "nuova su Ecos alle"} {oraDa(invio.sentTime)}
                      </span>
                      <span>
                        {invio.outcome === "OK" ? (invio.previousTime ? "inviata" : "inserita") : "errore"}
                      </span>
                      {invio.sentBy && <span className="text-muted-foreground">{invio.sentBy}</span>}
                      {invio.outcome !== "OK" && invio.message && (
                        <span className="basis-full truncate" title={invio.message}>
                          {invio.message}
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}

        {canWrite && (
          <div className="space-y-2 rounded-md border p-3">
            <p className="text-sm font-medium">
              {mancaUscita ? "Inserisci l'uscita mancante" : "Aggiungi una timbratura"}
            </p>
            <div className="flex items-end gap-2">
              <div className="space-y-1">
                <Label htmlFor="rettifica-ora">Ora</Label>
                <Input
                  id="rettifica-ora"
                  type="time"
                  value={ora}
                  onChange={(e) => setOra(e.target.value)}
                  className="w-28"
                />
              </div>
              <div className="space-y-1">
                <Label>Entrata o uscita</Label>
                <Select value={verso} onValueChange={(v) => setVerso(v as "IN" | "OUT")}>
                  <SelectTrigger className="w-32">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="IN">Entrata</SelectItem>
                    <SelectItem value="OUT">Uscita</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-1">
              <Label htmlFor="rettifica-motivo">Motivo (obbligatorio)</Label>
              <Textarea
                id="rettifica-motivo"
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
                placeholder="Es. uscita non timbrata, giustificata dal responsabile"
                rows={2}
              />
              {/* Il motivo resta scritto nel cartellino e lo legge chiunque lo gestisca:
                  la causale sanitaria non deve finirci (piano §8). */}
              <p className="text-xs text-muted-foreground">
                Scrivi il motivo organizzativo. Mai causali sanitarie o dati di salute.
              </p>
            </div>
            <div className="flex justify-end">
              <Button
                size="sm"
                disabled={!ora || !motivo.trim() || rettifica.isPending}
                onClick={inviaRettifica}
              >
                Registra la timbratura
              </Button>
            </div>
          </div>
        )}

        {azioni && <DialogFooter className="sm:justify-start">{azioni}</DialogFooter>}
      </DialogContent>
    </Dialog>
  )
}
