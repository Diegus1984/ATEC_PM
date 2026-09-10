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
import {
  deleteHrPunch,
  sendHrAdjustment,
  sendHrDayToEcos,
  setHrEarlyEntry,
  setHrPunchDirection,
} from "@/lib/api/hr"
import type { HrDay, HrEcosTime, HrPunch } from "@/lib/api/types"
import { formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import {
  orariDaScrivere,
  orarioValido,
  riassuntoInvioEcos,
  scelteDaScrivere,
  versoTimbratura,
} from "./invio-ecos"
import { durataBreve } from "./ore"
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
        ? `Senza l'uscita non si possono contare le ore. Chiedi a ${chi} a che ora è uscito, scrivilo nella riga «Uscita» di «Orari su Ecos» e premi «Scrivi su Ecos»: la timbratura nasce su Ecos e qui.`
        : "Senza l'uscita non si possono contare le ore. Segnalalo a chi gestisce le presenze."
    if (st.label.startsWith("Manca l'entrata"))
      return canWrite
        ? `Ha timbrato solo l'uscita: la mattina non è passato dal lettore. Chiedi a ${chi} a che ora è arrivato, scrivilo nella riga «Entrata» di «Orari su Ecos» e premi «Scrivi su Ecos».`
        : "Risulta solo l'uscita: manca l'entrata del mattino. Segnalalo a chi gestisce le presenze."
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
  // Gli orari da scrivere su Ecos, decisi a mano (09/09/2026 sera): per timbratura e per la
  // pausa dedotta. Proposti con l'arrotondato del motore ogni volta che la giornata cambia.
  const [orari, setOrari] = React.useState<Record<number, string>>({})
  const [pausaOre, setPausaOre] = React.useState({ uscita: "", rientro: "" })
  // L'orario della timbratura che manca (l'uscita), scritto da HR senza motivo: vuoto finché
  // non lo scrive (Diego, 09/09/2026 sera).
  const [mancanteOra, setMancanteOra] = React.useState("")
  const proposta = React.useMemo(
    () => (giornata ? orariDaScrivere(giornata) : { righe: [], pausa: null, mancante: null }),
    [giornata]
  )
  React.useEffect(() => {
    if (!open) return
    setOrari(Object.fromEntries(proposta.righe.map((r) => [r.punchId, r.proposto])))
    setPausaOre(proposta.pausa ?? { uscita: "", rientro: "" })
    setMancanteOra("")
  }, [open, proposta])

  // Se manca l'uscita, la rettifica parte già impostata su «Uscita».
  // Cosa manca alla giornata: serve a partire col verso giusto nel modulo della rettifica.
  const manca: "OUT" | "IN" | null = giornata
    ? (() => {
        const st = statoGiornata(giornata)
        if (st.tone !== "bad") return null
        if (st.label.startsWith("Manca l'uscita")) return "OUT"
        if (st.label.startsWith("Manca l'entrata")) return "IN"
        return null
      })()
    : null

  React.useEffect(() => {
    if (!open) return
    setOra("")
    setVerso(manca ?? "IN")
    setMotivo("")
  }, [open, manca])

  const rettifica = useMutation({
    mutationFn: sendHrAdjustment,
    onSuccess: () => {
      notifySuccess("Rettifica registrata")
      onChanged()
      onOpenChange(false)
    },
    onError: (e) => notifyError((e as Error).message),
  })

  // L'entrata prima delle 8 vale solo se qualcuno la autorizza; finché nessuno decide la
  // giornata parte dalle 8 (Diego, 10/09/2026). Decidere rifà subito il conto della giornata.
  const anticipo = useMutation({
    mutationFn: (authorized: boolean) =>
      setHrEarlyEntry({ employeeId, workDate: giorno, authorized }),
    onSuccess: (messaggio) => {
      notifySuccess(messaggio || "Deciso.")
      onChanged()
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Non riuscito."),
  })

  // Il lettore a volte registra il gesto al contrario: il verso si corregge da qui, e la
  // correzione va prima su Ecos (Diego, 10/09/2026).
  const cambiaVerso = useMutation({
    mutationFn: setHrPunchDirection,
    onSuccess: (messaggio) => {
      notifySuccess(messaggio || "Verso corretto.")
      onChanged()
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Non riuscito."),
  })

  const elimina = useMutation({
    mutationFn: deleteHrPunch,
    onSuccess: () => {
      notifySuccess("Timbratura cancellata")
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

  const scelte = scelteDaScrivere(proposta.righe, orari, proposta.pausa, proposta.mancante, mancanteOra)

  async function inviaAEcos() {
    if (!giornata || scelte.totale === 0) return
    const times: HrEcosTime[] = scelte.timbrature.map((r) => ({
      punchId: r.punchId,
      direction: r.direction,
      time: orari[r.punchId] ?? r.proposto,
    }))
    if (scelte.conPausa) {
      times.push({ punchId: null, direction: "OUT", time: pausaOre.uscita, kind: "BREAK" })
      times.push({ punchId: null, direction: "IN", time: pausaOre.rientro, kind: "BREAK" })
    }
    if (scelte.conMancante && proposta.mancante) {
      times.push({ punchId: null, direction: proposta.mancante.direction, time: mancanteOra, kind: "MISSING" })
    }
    if (times.some((x) => !orarioValido(x.time))) {
      notifyError("C'è un orario non valido: scriverlo come 08:00.")
      return
    }
    // Il resoconto: cosa parte, riga per riga, con gli orari decisi qui.
    const righeTesto = scelte.timbrature.map((r) => {
      const scelto = orari[r.punchId] ?? r.proposto
      return r.suEcos
        ? `${versoTimbratura(r.direction)} ${r.suEcos} → ${scelto}`
        : `${versoTimbratura(r.direction)} ${scelto} (rettifica, nuova su Ecos)`
    })
    if (scelte.conPausa) righeTesto.push(`pausa ${pausaOre.uscita} → ${pausaOre.rientro} (nuove su Ecos)`)
    if (scelte.conMancante && proposta.mancante)
      righeTesto.push(`${versoTimbratura(proposta.mancante.direction)} mancante ${mancanteOra} (nuova su Ecos)`)
    const ok = await confirm({
      title: "Scrivere su Ecos questi orari?",
      description:
        `${employeeName || "Questa persona"}, ${giornoEsteso(giornata.workDate)}: ${righeTesto.join("; ")}.` +
        " Dopo la scrittura le timbrature qui sono uguali a Ecos; l'orario originale resta nel registro degli invii.",
      confirmLabel: `Scrivi su Ecos (${scelte.totale})`,
    })
    if (ok) invioEcos.mutate({ employeeId, workDate: giorno, times })
  }

  function inviaRettifica() {
    if (!ora || !giornata) return
    rettifica.mutate({
      employeeId,
      punchedAt: `${giorno}T${ora}:00`,
      direction: verso,
      // Il motivo non è più obbligatorio (Diego, 09/09/2026 sera): senza, resta scritto chi l'ha messa.
      reason: motivo.trim() || "Inserita da HR dal dettaglio della giornata",
    })
  }

  // Anche le timbrature di Ecos si cancellano (segnalazione #152, 09/09/2026): il server le
  // cancella PRIMA su Ecos (che le tiene come cancellate) e poi qui, e ricalcola la giornata.
  async function eliminaRiga(t: HrPunch) {
    const diEcos = t.source === "ECOS"
    const ok = await confirm({
      title: diEcos ? "Cancellare la timbratura anche su Ecos?" : "Eliminare la rettifica?",
      description: diEcos
        ? `${versoTimbratura(t.direction)} delle ${oraDa(t.punchedAt)}: viene cancellata su Ecos (che la tiene come cancellata) e poi qui, e la giornata si ricalcola. Se Ecos rifiuta, non cambia niente.`
        : "La giornata verrà ricalcolata senza questa timbratura. Il grezzo del rilevatore non si tocca.",
      confirmLabel: diEcos ? "Cancella su Ecos e qui" : "Elimina",
      destructive: true,
    })
    if (ok) elimina.mutate(t.id)
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
                  {canWrite ? (
                    <Select
                      value={t.direction === "IN" ? "IN" : "OUT"}
                      onValueChange={(v) =>
                        cambiaVerso.mutate({ punchId: t.id, direction: v as "IN" | "OUT" })
                      }
                      disabled={cambiaVerso.isPending}
                    >
                      <SelectTrigger
                        className="h-7 w-28"
                        aria-label={`Verso della timbratura delle ${oraDa(t.punchedAt)}`}
                        title="Se il lettore ha registrato il gesto al contrario, correggilo: la modifica va anche su Ecos"
                      >
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="IN">Entrata</SelectItem>
                        <SelectItem value="OUT">Uscita</SelectItem>
                      </SelectContent>
                    </Select>
                  ) : (
                    <span>{t.direction === "IN" ? "Entrata" : "Uscita"}</span>
                  )}
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
                  {canWrite && (t.source === "ADJUSTMENT" || (t.source === "ECOS" && Boolean(t.ecosStampId))) && (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="ml-auto"
                      disabled={elimina.isPending}
                      onClick={() => void eliminaRiga(t)}
                      title={t.source === "ECOS" ? "Cancella la timbratura (su Ecos e qui)" : "Elimina la rettifica"}
                    >
                      <Trash2 className="size-4" />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {canWrite && (giornata.earlyEntryMinutes ?? 0) > 0 && (
          <div className="space-y-2 rounded-md border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-medium">Entrata prima delle 8</p>
              {giornata.earlyEntryAuthorized === true ? (
                <Badge variant="default">AUTORIZZATA</Badge>
              ) : giornata.earlyEntryAuthorized === false ? (
                <Badge variant="outline">NON AUTORIZZATA</Badge>
              ) : (
                <Badge variant="destructive">DA DECIDERE</Badge>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              {`Ha timbrato ${durataBreve(giornata.earlyEntryMinutes ?? 0)} prima delle 8 (${
                giornata.normalized?.clockIn1 || giornata.raw?.clockIn1 || ""
              }). `}
              {giornata.earlyEntryAuthorized === true
                ? "Approvata: su Ecos e qui vale l'orario timbrato."
                : "La giornata parte dalle 8, e l'anticipo non entra nelle ore finché non lo approvi. La decisione scrive subito l'entrata su Ecos."}
            </p>
            <div className="flex justify-end gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={anticipo.isPending || giornata.earlyEntryAuthorized === false}
                onClick={() => anticipo.mutate(false)}
              >
                Rifiuta
              </Button>
              <Button
                size="sm"
                disabled={anticipo.isPending || giornata.earlyEntryAuthorized === true}
                onClick={() => anticipo.mutate(true)}
              >
                Approva
              </Button>
            </div>
          </div>
        )}

        {(proposta.righe.length > 0 || proposta.pausa || proposta.mancante) && (
          <div className="space-y-2 rounded-md border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-medium">Orari su Ecos</p>
              {scelte.totale > 0 ? (
                <Badge variant="secondary">Da scrivere: {scelte.totale}</Badge>
              ) : ecos.allineato ? (
                <Badge variant="outline">Allineato con Ecos</Badge>
              ) : null}
              {ecos.ultimoInvio && (
                <span className="text-xs text-muted-foreground">
                  Scritto il {formatDateTimeShort(ecos.ultimoInvio)}
                </span>
              )}
            </div>
            {/* Ogni riga: com'è su Ecos adesso e l'orario che ci andrà — proposto con
                l'arrotondato del motore, ma è HR a decidere (Diego, 09/09/2026 sera). */}
            <ul className="space-y-1 text-sm">
              {proposta.righe.map((r) => {
                const scelto = orari[r.punchId] ?? r.proposto
                const cambia = r.inviabile && !r.incerta && (r.suEcos == null || scelto !== r.suEcos)
                return (
                  <li key={r.punchId} className="flex flex-wrap items-center gap-2 tabular-nums">
                    <span className="w-14">{versoTimbratura(r.direction)}</span>
                    <span className="w-28 text-muted-foreground">
                      {r.suEcos ? `su Ecos ${r.suEcos}` : "non ancora su Ecos"}
                    </span>
                    <Input
                      type="time"
                      value={scelto}
                      onChange={(e) => setOrari((prev) => ({ ...prev, [r.punchId]: e.target.value }))}
                      disabled={!canWrite || !r.inviabile || r.incerta}
                      aria-label={`Orario su Ecos per ${versoTimbratura(r.direction).toLowerCase()}`}
                      className="h-8 w-28"
                    />
                    {r.tipo === "rettifica" && <Badge variant="default">RETTIFICA</Badge>}
                    {r.incerta ? (
                      <span className="text-xs text-destructive">
                        mandata senza risposta certa: verificare su Ecos
                      </span>
                    ) : !r.inviabile ? (
                      <span className="text-xs text-muted-foreground">non inviabile: cambierebbe giorno</span>
                    ) : cambia ? (
                      <span className="text-xs text-amber-700 dark:text-amber-400">da scrivere</span>
                    ) : (
                      <span className="text-xs text-muted-foreground">uguale</span>
                    )}
                  </li>
                )
              })}
              {proposta.pausa && (
                <li className="flex flex-wrap items-center gap-2 tabular-nums">
                  <span className="w-14">Pausa</span>
                  <span className="w-28 text-muted-foreground">non ancora su Ecos</span>
                  <Input
                    type="time"
                    value={pausaOre.uscita}
                    onChange={(e) => setPausaOre((prev) => ({ ...prev, uscita: e.target.value }))}
                    disabled={!canWrite}
                    aria-label="Uscita per la pausa"
                    className="h-8 w-28"
                  />
                  <span className="text-muted-foreground">→</span>
                  <Input
                    type="time"
                    value={pausaOre.rientro}
                    onChange={(e) => setPausaOre((prev) => ({ ...prev, rientro: e.target.value }))}
                    disabled={!canWrite}
                    aria-label="Rientro dalla pausa"
                    className="h-8 w-28"
                  />
                  <Badge variant="default">PAUSA</Badge>
                  <span className="text-xs text-amber-700 dark:text-amber-400">da scrivere</span>
                </li>
              )}
              {proposta.mancante && (
                // La timbratura che manca: si scrive qui, nella sua riga, senza motivo.
                <li className="flex flex-wrap items-center gap-2 tabular-nums">
                  <span className="w-14">{versoTimbratura(proposta.mancante.direction)}</span>
                  <span className="w-28 text-destructive">manca su Ecos</span>
                  <Input
                    type="time"
                    value={mancanteOra}
                    onChange={(e) => setMancanteOra(e.target.value)}
                    disabled={!canWrite}
                    aria-label={`Orario della ${versoTimbratura(proposta.mancante.direction).toLowerCase()} mancante`}
                    className="h-8 w-28"
                  />
                  <Badge variant="destructive">MANCANTE</Badge>
                  {scelte.conMancante ? (
                    <span className="text-xs text-amber-700 dark:text-amber-400">da scrivere</span>
                  ) : (
                    <span className="text-xs text-muted-foreground">scrivi l'orario, poi «Scrivi su Ecos»</span>
                  )}
                </li>
              )}
            </ul>
            <p className="text-xs text-muted-foreground">
              Proposto l'orario arrotondato dal motore: cambialo se serve. Su Ecos, e qui, va quello
              che scrivi; le rettifiche e la pausa nascono su Ecos come timbrature nuove. L'orario
              originale resta nel registro degli invii.
            </p>
            {canWrite && scelte.totale > 0 && (
              <div className="flex justify-end">
                <Button size="sm" disabled={invioEcos.isPending} onClick={() => void inviaAEcos()}>
                  <Send className="size-4" />
                  Scrivi su Ecos ({scelte.totale})
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
            <p className="text-sm font-medium">Aggiungi una timbratura</p>
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
              <Label htmlFor="rettifica-motivo">Motivo (facoltativo)</Label>
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
                disabled={!ora || rettifica.isPending}
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
