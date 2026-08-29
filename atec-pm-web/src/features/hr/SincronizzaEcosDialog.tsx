import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { DownloadCloud, RefreshCw } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
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
import { fetchHrStatus, importHrPunches, resyncHrMonth } from "@/lib/api/hr"
import type { HrImportResult } from "@/lib/api/types"
import { formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

const MESI = [
  "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno",
  "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre",
]

interface SincronizzaEcosDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Mese aperto nella pagina: è quello proposto per la sincronizzazione mirata. */
  anno: number
  mese: number
  /**
   * Chiamato a import finito: cartellino, calendario e quadratura si ricaricano, e la
   * pagina riceve i codici Ecos rimasti senza dipendente (il banner del «Collega Ecos»).
   */
  onImported: (esito: HrImportResult | null) => void
}

/**
 * La pagina «Sincronizzazione ECOS Agile» dell'originale, in un dialogo
 * (PIANO-HR-PORT-ORIGINALE.md, voci 5, 8 e 11):
 *
 * <ul>
 *   <li><b>l'avanzamento a video</b> — barra e log, port del `txtLog` di `SyncEcosPage`;</li>
 *   <li><b>la sincronizzazione di un mese scelto</b>, che è la strada per rifare un mese:
 *   si <i>riscarica</i> invece di cancellare (il «Cancella Mese» dell'originale qui non
 *   esiste apposta — il grezzo è append-only e le giornate si rigenerano da sole);</li>
 *   <li><b>l'ultima lettura badge</b>, accanto all'ultima sincronizzazione.</li>
 * </ul>
 *
 * <p>🪤 L'avanzamento vive <b>in memoria</b> nel server: se il servizio si riavvia a metà
 * import lo stato si azzera. Qui si riconosce (nessun inizio registrato dopo che si era
 * visto girare) e lo si dice, invece di restare a girare per sempre.</p>
 */
export function SincronizzaEcosDialog({
  open,
  onOpenChange,
  anno,
  mese,
  onImported,
}: SincronizzaEcosDialogProps) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()

  const [annoScelto, setAnnoScelto] = React.useState(anno)
  const [meseScelto, setMeseScelto] = React.useState(mese)
  const [interrotto, setInterrotto] = React.useState(false)
  const eraInCorso = React.useRef(false)

  React.useEffect(() => {
    if (!open) return
    setAnnoScelto(anno)
    setMeseScelto(mese)
    setInterrotto(false)
    eraInCorso.current = false
  }, [open, anno, mese])

  const aggiorna = (esito: HrImportResult | null) => {
    void queryClient.invalidateQueries({ queryKey: ["hr-status"] })
    onImported(esito)
  }

  // Le due mutation stanno PRIMA della query apposta: il loro `isPending` accende il
  // polling dell'avanzamento. Il POST dell'import è sincrono — il server risponde a lavoro
  // finito — quindi aspettare che sia lo stato a dire «running» non funzionerebbe mai:
  // nessuno lo riletterebbe fino alla risposta, e barra e log comparirebbero a cose fatte.
  const importa = useMutation({
    mutationFn: (full: boolean) => importHrPunches(full),
    onSuccess: (esito) => {
      notifySuccess(esito.message)
      aggiorna(esito)
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Import non riuscito."),
  })

  const sincronizzaMese = useMutation({
    mutationFn: () => resyncHrMonth(annoScelto, meseScelto),
    onSuccess: (esito) => {
      notifySuccess(esito.message)
      aggiorna(esito)
    },
    onError: (e) =>
      notifyError(e instanceof Error ? e.message : "Sincronizzazione del mese non riuscita."),
  })

  const inAttesa = importa.isPending || sincronizzaMese.isPending

  const statoQuery = useQuery({
    queryKey: ["hr-status"],
    queryFn: fetchHrStatus,
    enabled: open,
    // Mentre l'import gira la pagina si aggiorna da sola, come la barra dell'originale.
    refetchInterval: (query) =>
      inAttesa || query.state.data?.progress?.running ? 1500 : false,
  })

  const stato = statoQuery.data
  const avanzamento = stato?.progress
  const inCorso = avanzamento?.running === true

  // Il servizio riavviato a metà import: lo stato in memoria è sparito.
  React.useEffect(() => {
    if (inCorso) {
      eraInCorso.current = true
      setInterrotto(false)
      return
    }
    if (eraInCorso.current && avanzamento != null && avanzamento.startedAt == null) {
      setInterrotto(true)
      eraInCorso.current = false
    }
  }, [inCorso, avanzamento])

  const occupato = inCorso || inAttesa

  async function reimportaTutto() {
    const ok = await confirm({
      title: "Reimportare tutto lo storico da Ecos?",
      description:
        "Si riscarica ogni timbratura dall'inizio e si rimette in pari il calcolo. " +
        "Serve dopo aver collegato una persona nuova: le sue timbrature passate erano " +
        "state scartate. Può richiedere qualche minuto.",
      confirmLabel: "Reimporta tutto",
      destructive: false,
    })
    if (ok) importa.mutate(true)
  }

  // L'anno proposto arriva dal mese aperto in pagina: se qualcuno è andato indietro oltre
  // la finestra dei cinque anni, quell'anno deve comparire lo stesso — altrimenti la
  // tendina resta vuota e si sincronizza un anno che sullo schermo non è scritto.
  const anni = React.useMemo(() => {
    const corrente = new Date().getFullYear()
    const base = Array.from({ length: 5 }, (_, i) => corrente + 1 - i)
    return base.includes(annoScelto) ? base : [...base, annoScelto].sort((a, b) => b - a)
  }, [annoScelto])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] flex-col sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Sincronizzazione Ecos</DialogTitle>
          <DialogDescription>
            Scarica le timbrature e rimette in pari i cartellini.
          </DialogDescription>
        </DialogHeader>

        <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto">
          {/* Stato: quando è stata l'ultima volta */}
          <div className="space-y-1 rounded-lg border bg-muted/30 p-3 text-sm">
            <p>
              <span className="font-semibold text-muted-foreground">Ultima sync: </span>
              {stato?.lastImport ? formatDateTimeShort(stato.lastImport) : "mai"}
              {stato?.lastResult ? ` — ${stato.lastResult}` : ""}
            </p>
            <p>
              <span className="font-semibold text-muted-foreground">Badge: </span>
              {stato?.lastBadgeRead
                ? formatDateTimeShort(stato.lastBadgeRead)
                : "mai sincronizzati"}
            </p>
            {stato && !stato.configured && (
              <p className="text-amber-600 dark:text-amber-500">
                Credenziali Ecos non configurate: l'import è fermo.
              </p>
            )}
          </div>

          {/* Import dal cursore */}
          <div className="flex flex-wrap items-center gap-2">
            <Button
              size="sm"
              disabled={occupato || stato?.configured === false}
              onClick={() => importa.mutate(false)}
            >
              <DownloadCloud className="mr-1 size-3.5" />
              Importa le novità
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={occupato || stato?.configured === false}
              onClick={() => void reimportaTutto()}
            >
              <RefreshCw className="mr-1 size-3.5" />
              Reimporta tutto
            </Button>
          </div>

          {/* Un mese scelto */}
          <div className="space-y-2 rounded-lg border p-3">
            <Label className="text-xs uppercase tracking-wider text-muted-foreground">
              Sincronizza un mese
            </Label>
            <p className="text-xs text-muted-foreground">
              Riscarica il mese intero da Ecos e ricalcola le giornate, comprese le
              timbrature cancellate là. Non sposta il segnaposto dell'import automatico.
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <Select
                value={String(meseScelto)}
                onValueChange={(v) => setMeseScelto(Number(v))}
              >
                <SelectTrigger className="w-36">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {MESI.map((nome, i) => (
                    <SelectItem key={nome} value={String(i + 1)}>
                      {nome}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select
                value={String(annoScelto)}
                onValueChange={(v) => setAnnoScelto(Number(v))}
              >
                <SelectTrigger className="w-24">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {anni.map((a) => (
                    <SelectItem key={a} value={String(a)}>
                      {a}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button
                variant="outline"
                size="sm"
                disabled={occupato || stato?.configured === false}
                onClick={() => sincronizzaMese.mutate()}
              >
                {sincronizzaMese.isPending ? "Sincronizzo…" : "Sincronizza il mese"}
              </Button>
            </div>
          </div>

          {/* Avanzamento */}
          {avanzamento && (avanzamento.running || avanzamento.log.length > 0) && (
            <div className="space-y-2">
              <div className="flex items-center justify-between text-xs">
                <span className="font-medium">{avanzamento.title}</span>
                <span className="text-muted-foreground">{avanzamento.phase}</span>
              </div>
              <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
                <div
                  className={cn(
                    "h-full rounded-full bg-primary transition-all duration-500",
                    avanzamento.running && "animate-pulse"
                  )}
                  style={{ width: `${Math.min(100, Math.max(0, avanzamento.percent))}%` }}
                />
              </div>
              <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                <span>scaricate {avanzamento.downloaded}</span>
                <span>nuove {avanzamento.added}</span>
                <span>aggiornate {avanzamento.updated}</span>
                <span>rimosse {avanzamento.removed}</span>
                <span>giornate ricalcolate {avanzamento.daysRecalculated}</span>
              </div>
              <pre className="max-h-56 overflow-auto rounded-lg border bg-muted/30 p-2 font-mono text-[11px] leading-relaxed">
                {avanzamento.log.join("\n")}
              </pre>
            </div>
          )}

          {interrotto && (
            <p className="rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
              Import non più in corso: il servizio si è riavviato mentre girava e
              l'avanzamento è andato perso. I dati già scritti restano; per completare,
              rilancia l'import.
            </p>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
