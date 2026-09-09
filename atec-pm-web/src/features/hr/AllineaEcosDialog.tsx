import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { CloudUpload } from "lucide-react"

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
import { fetchHrEcosPlan, sendHrDayToEcos } from "@/lib/api/hr"
import type { HrEcosPlannedOp } from "@/lib/api/types"
import { formatDateFull } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { oreLeggibili } from "./ore"

/** La giornata di una persona da allineare su Ecos. */
export interface AllineaEcosTarget {
  employeeId: number
  date: string
}

/**
 * «Allinea Ecos» (09/09/2026, Diego dal «Controllo di ieri»): la giornata calcolata a video —
 * così si conferma che le ore sono giuste — e, riga per riga, cosa si scriverà su Ecos: gli
 * orari arrotondati al posto di quelli timbrati, le rettifiche e la pausa pranzo dedotta come
 * timbrature nuove, ciò che resta fuori e perché. Niente parte senza «Scrivi su Ecos».
 * Il resoconto lo compone il server (`GET /api/hr/ecos/send-day/plan`): quello che si legge
 * è quello che parte.
 */
export function AllineaEcosDialog({
  target,
  onOpenChange,
  onChanged,
}: {
  target: AllineaEcosTarget | null
  onOpenChange: (open: boolean) => void
  /** Dopo una scrittura riuscita (anche parziale): le pagine rileggono. */
  onChanged: () => void
}) {
  const pianoQuery = useQuery({
    queryKey: ["hr-ecos-plan", target?.employeeId, target?.date],
    queryFn: () => fetchHrEcosPlan(target!.employeeId, target!.date),
    enabled: target != null,
  })
  const piano = pianoQuery.data

  const scrivi = useMutation({
    mutationFn: sendHrDayToEcos,
    onSuccess: (esito) => {
      if (esito.success) notifySuccess(esito.message)
      else notifyError(esito.message)
      onChanged()
      // Con un fallimento parziale si resta aperti: il resoconto si rilegge e dice cosa manca.
      if (esito.success) onOpenChange(false)
      else void pianoQuery.refetch()
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Scrittura su Ecos non riuscita."),
  })

  const scrivono = React.useMemo(
    () => (piano?.operations ?? []).filter((o) => o.kind === "UPDATE" || o.kind === "INSERT" || o.kind === "INSERT_BREAK"),
    [piano]
  )
  const fuori = React.useMemo(
    () => (piano?.operations ?? []).filter((o) => o.kind === "SKIP" || o.kind === "UNCERTAIN" || o.kind === "MISSING"),
    [piano]
  )

  return (
    <Dialog open={target != null} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Allinea Ecos alla giornata calcolata</DialogTitle>
          <DialogDescription>
            {piano
              ? `${piano.employeeName}, ${formatDateFull(piano.workDate)}. Prima di scrivere: controlla che le ore calcolate siano giuste, poi conferma.`
              : target
                ? `Giornata del ${formatDateFull(target.date)}.`
                : ""}
          </DialogDescription>
        </DialogHeader>

        {pianoQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : pianoQuery.error ? (
          <p className="text-sm text-destructive">{(pianoQuery.error as Error).message}</p>
        ) : piano ? (
          <div className="space-y-3">
            {/* La giornata calcolata: quello che Ecos dovrà avere. */}
            <div className="rounded-md border bg-muted/30 p-3 text-sm">
              <p className="mb-1 font-medium">Giornata calcolata</p>
              <div className="grid grid-cols-4 gap-2 tabular-nums">
                <Orario etichetta="Entrata" valore={piano.clockIn1} />
                <Orario etichetta="Uscita" valore={piano.clockOut1} />
                <Orario etichetta="Entrata" valore={piano.clockIn2} />
                <Orario etichetta="Uscita" valore={piano.clockOut2} />
              </div>
              <p className="mt-2 text-muted-foreground">
                Ore <span className="font-medium text-foreground">{oreLeggibili(piano.regularHours) || "—"}</span>
                {piano.overtime && !/^0h 0+m$/.test(piano.overtime) && (
                  <>
                    {" "}· straordinario <span className="font-medium text-foreground">{oreLeggibili(piano.overtime)}</span>
                  </>
                )}
                {" "}· pausa <span className="font-medium text-foreground">{oreLeggibili(piano.breakTime) || "—"}</span>
                {piano.note && <span className="ml-2 text-xs">({piano.note})</span>}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                L'asterisco segna un orario dedotto dal motore, non timbrato.
              </p>
            </div>

            {scrivono.length > 0 ? (
              <div className="space-y-1">
                <p className="text-sm font-medium">
                  Su Ecos si {scrivono.length === 1 ? "scriverà questa riga" : `scriveranno ${scrivono.length} righe`}
                </p>
                <ul className="space-y-1 text-sm">
                  {scrivono.map((o, i) => (
                    <RigaOperazione key={i} op={o} />
                  ))}
                </ul>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Niente da scrivere su Ecos.</p>
            )}

            {fuori.length > 0 && (
              <div className="space-y-1">
                <p className="text-sm font-medium text-muted-foreground">Resta fuori</p>
                <ul className="space-y-1 text-sm">
                  {fuori.map((o, i) => (
                    <RigaOperazione key={i} op={o} />
                  ))}
                </ul>
              </div>
            )}

            {piano.message && (
              <p className={cn("text-sm", piano.canSend ? "text-muted-foreground" : "text-amber-700 dark:text-amber-400")}>
                {piano.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              L'ora timbrata resta qui e nel registro degli invii. Le timbrature nuove nascono su
              Ecos con la nota «ATEC PM» e qui diventano timbrature di Ecos a tutti gli effetti.
            </p>
          </div>
        ) : null}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Annulla
          </Button>
          <Button
            disabled={!piano?.canSend || scrivi.isPending}
            onClick={() => {
              if (target) scrivi.mutate({ employeeId: target.employeeId, workDate: target.date })
            }}
          >
            <CloudUpload className="size-4" />
            {scrivi.isPending
              ? "Scrivo…"
              : piano && piano.toWrite > 0
                ? `Scrivi su Ecos (${piano.toWrite})`
                : "Scrivi su Ecos"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function Orario({ etichetta, valore }: { etichetta: string; valore: string }) {
  const mancante = valore === "??:??"
  return (
    <div className="leading-tight">
      <span className="block text-[11px] text-muted-foreground">{etichetta}</span>
      <span className={cn("font-semibold", mancante && "text-destructive")}>
        {mancante ? "Non timbrata" : valore || "—"}
      </span>
    </div>
  )
}

const ETICHETTA_KIND: Record<HrEcosPlannedOp["kind"], { testo: string; variant: "default" | "secondary" | "outline" | "destructive" }> = {
  UPDATE: { testo: "MODIFICA", variant: "secondary" },
  INSERT: { testo: "NUOVA · RETTIFICA", variant: "default" },
  INSERT_BREAK: { testo: "NUOVA · PAUSA", variant: "default" },
  SKIP: { testo: "NON INVIABILE", variant: "outline" },
  UNCERTAIN: { testo: "DA VERIFICARE", variant: "destructive" },
  MISSING: { testo: "MANCANTE", variant: "destructive" },
}

function RigaOperazione({ op }: { op: HrEcosPlannedOp }) {
  const e = ETICHETTA_KIND[op.kind]
  return (
    <li className="flex flex-wrap items-center gap-2">
      <Badge variant={e.variant} className="w-32 justify-center">
        {e.testo}
      </Badge>
      <span className="tabular-nums font-medium">{op.label}</span>
      {op.detail && <span className="text-xs text-muted-foreground">— {op.detail}</span>}
    </li>
  )
}
