import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { fetchHrSicknessProtocol, setHrSicknessProtocol } from "@/lib/api/hr"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
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

import { ProtocolloScelta } from "./protocollo-scelta"

/**
 * Il protocollo della mutua di una giornata già segnata malattia. Si apre dal calendario, e
 * serve per i due casi che il dialogo della causale non copre (Diego, 10/09/2026): il
 * certificato che arriva dopo, e le malattie che vengono da Ecos, dove la causale qui non si
 * tocca. Un certificato copre più giorni, quindi si propone l'ultimo numero della persona.
 */
export function ProtocolloMutuaDialog({
  target,
  onOpenChange,
  onSaved,
}: {
  /** Giornata su cui si è cliccato; null = dialogo chiuso. */
  target: { employeeId: number; date: string } | null
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}) {
  const query = useQuery({
    queryKey: ["hr-protocollo", target?.employeeId, target?.date],
    queryFn: () => fetchHrSicknessProtocol(target!.employeeId, target!.date),
    enabled: target != null,
  })
  const info = query.data

  const [numero, setNumero] = React.useState("")
  React.useEffect(() => {
    if (info) setNumero(info.current || info.last || "")
  }, [info])

  const salva = useMutation({
    mutationFn: () =>
      setHrSicknessProtocol({
        employeeId: target!.employeeId,
        date: target!.date,
        protocol: numero.trim(),
      }),
    onSuccess: (messaggio) => {
      notifySuccess(messaggio || "Salvato.")
      onSaved()
      onOpenChange(false)
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Salvataggio non riuscito."),
  })

  const bloccato = info != null && info.blocco !== ""

  return (
    <Dialog open={target != null} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Protocollo della mutua</DialogTitle>
          <DialogDescription>
            {info
              ? `${info.employeeName} — ${formatDateShort(info.date.slice(0, 10))}`
              : target
                ? formatDateShort(target.date)
                : ""}
          </DialogDescription>
        </DialogHeader>

        {query.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : query.error ? (
          <p className="text-sm text-destructive">{(query.error as Error).message}</p>
        ) : bloccato ? (
          <p className="text-sm text-muted-foreground">{info!.blocco}</p>
        ) : (
          <div className="space-y-3">
            <ProtocolloScelta
              ultimo={info?.last ?? ""}
              ultimoGiorno={info?.lastDate ?? null}
              valore={numero}
              onValore={setNumero}
            />
            {info?.current && (
              <p className="text-xs text-muted-foreground">
                Su questa giornata c'è già il protocollo {info.current}. Svuota il campo per
                toglierlo.
              </p>
            )}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Annulla
          </Button>
          <Button disabled={bloccato || salva.isPending} onClick={() => salva.mutate()}>
            {salva.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Il campo del numero, quando il dialogo serve solo a leggere. */
export function ProtocolloSolaLettura({ numero }: { numero: string }) {
  return (
    <div className="space-y-1">
      <Label>Protocollo</Label>
      <Input value={numero} readOnly className="font-mono tabular-nums" />
    </div>
  )
}
