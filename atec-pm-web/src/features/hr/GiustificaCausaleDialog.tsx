import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { TriangleAlert } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { fetchHrGiustificaInfo, saveHrGiustifica } from "@/lib/api/hr"
import { HR_CAUSALE_LABEL, type HrCausale } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

/** Valore fittizio della voce «rimuovi»: Radix Select non accetta value="". */
const RIMUOVI = "__rimuovi__"

/** Ore all'italiana, senza decimali inutili: 8 → «8», 3,5 → «3,5». */
function ore(n: number): string {
  return n.toLocaleString("it-IT", { maximumFractionDigits: 1 })
}

interface GiustificaCausaleDialogProps {
  /** Giornata su cui si è cliccato; null = dialogo chiuso. */
  target: { employeeId: number; date: string } | null
  onOpenChange: (open: boolean) => void
  /** Chiamato dopo un salvataggio riuscito: la griglia si ricarica. */
  onSaved: () => void
}

/**
 * #132 — port del `CausaleDialog` del programma «Timbrature»: dice quante ore mancano
 * sulla giornata e fa scegliere la causale che le copre.
 *
 * Le regole non stanno qui: quante ore mancano e quali causali siano ammesse lo dice il
 * server (`GET /api/hr/calendar/giustifica`), che è anche quello che poi le riverifica al
 * salvataggio. Questo dialogo mostra quello che gli viene detto — comprese le ragioni per
 * cui una giornata NON si può giustificare (futura, festiva, già a posto, o assenza che
 * arriva da Ecos).
 */
export function GiustificaCausaleDialog({
  target,
  onOpenChange,
  onSaved,
}: GiustificaCausaleDialogProps) {
  const queryClient = useQueryClient()
  const [scelta, setScelta] = React.useState<string>(RIMUOVI)

  const infoQuery = useQuery({
    queryKey: ["hr-giustifica", target?.employeeId, target?.date],
    queryFn: () => fetchHrGiustificaInfo(target!.employeeId, target!.date),
    enabled: target != null,
  })

  const info = infoQuery.data

  // Si riparte sempre da «rimuovi causale», come faceva il dialogo originale
  // (`cmbCausale.SelectedIndex = 0`): la scelta è un gesto esplicito, mai un default.
  React.useEffect(() => {
    setScelta(RIMUOVI)
  }, [target?.employeeId, target?.date])

  const salva = useMutation({
    mutationFn: () =>
      saveHrGiustifica({
        employeeId: target!.employeeId,
        date: target!.date,
        causale: scelta === RIMUOVI ? "" : scelta,
      }),
    onSuccess: async () => {
      notifySuccess(scelta === RIMUOVI ? "Causale rimossa" : "Causale registrata")
      await queryClient.invalidateQueries({ queryKey: ["hr-giustifica"] })
      onSaved()
      onOpenChange(false)
    },
    onError: (err) => notifyError(err as Error),
  })

  // «Rimuovi» ha senso solo se c'è qualcosa da togliere; una causale nuova solo se c'è un buco.
  const puoConfermare =
    info != null &&
    info.blocco === "" &&
    (scelta === RIMUOVI ? info.puoRimuovere : info.oreMancanti > 0)

  return (
    <Dialog open={target != null} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Inserisci causale</DialogTitle>
          <DialogDescription>
            {info
              ? `${info.employeeName} — ${formatDateShort(info.date)}`
              : "Caricamento giornata…"}
          </DialogDescription>
        </DialogHeader>

        {infoQuery.isLoading ? (
          <p className="py-6 text-center text-sm text-muted-foreground">Caricamento…</p>
        ) : infoQuery.isError ? (
          <p className="py-6 text-center text-sm text-destructive">
            {(infoQuery.error as Error).message}
          </p>
        ) : info == null ? null : info.blocco !== "" ? (
          <div className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
            <TriangleAlert className="mt-0.5 size-4 shrink-0" />
            <span>{info.blocco}</span>
          </div>
        ) : (
          <div className="flex flex-col gap-4 py-1">
            <div className="grid grid-cols-3 gap-2 rounded-md border p-3 text-sm">
              <div className="flex flex-col">
                <span className="text-xs text-muted-foreground">Contratto</span>
                <span className="font-mono font-semibold">{ore(info.dailyHours)}h</span>
              </div>
              <div className="flex flex-col">
                <span className="text-xs text-muted-foreground">Lavorate</span>
                <span className="font-mono font-semibold">{ore(info.oreLavorate)}h</span>
              </div>
              <div className="flex flex-col">
                <span className="text-xs text-muted-foreground">Da giustificare</span>
                <span className="font-mono font-semibold text-primary">
                  {ore(info.oreMancanti)}h
                </span>
              </div>
            </div>

            {info.causaleCorrente !== "" && (
              <p className="text-xs text-muted-foreground">
                Oggi risulta{" "}
                <span className="font-semibold">
                  {HR_CAUSALE_LABEL[info.causaleCorrente as HrCausale] ??
                    info.causaleCorrente}
                </span>
                {info.oreCorrenti != null ? ` per ${ore(info.oreCorrenti)}h` : ""}.
              </p>
            )}

            <div className="flex flex-col gap-2">
              <Label htmlFor="causale-giustifica">Causale</Label>
              <Select value={scelta} onValueChange={setScelta}>
                <SelectTrigger id="causale-giustifica" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={RIMUOVI}>(rimuovi causale)</SelectItem>
                  {info.causali.map((c) => (
                    <SelectItem key={c} value={c}>
                      {HR_CAUSALE_LABEL[c]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {info.causali.length === 2 && (
                <p className="text-xs text-muted-foreground">
                  La giornata ha timbrature: si può solo completarla con un permesso o un
                  infortunio.
                </p>
              )}
            </div>

            <div className="flex items-baseline justify-between rounded-md bg-muted/50 px-3 py-2">
              <span className="text-sm font-medium">Ore</span>
              <span className="font-mono text-base font-bold text-primary">
                {scelta === RIMUOVI ? "—" : `${ore(info.oreMancanti)}h`}
              </span>
            </div>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Annulla
          </Button>
          <Button
            onClick={() => salva.mutate()}
            disabled={!puoConfermare || salva.isPending}
          >
            {salva.isPending ? "Salvataggio…" : "Conferma"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
