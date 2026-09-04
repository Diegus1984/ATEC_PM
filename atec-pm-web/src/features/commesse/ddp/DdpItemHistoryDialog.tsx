import { useQuery } from "@tanstack/react-query"
import { History } from "lucide-react"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { fetchDdpItemEvents, type DdpItemKind } from "@/lib/api/ddp-events"
import { formatDateShort } from "@/lib/date-iso"

interface Props {
  open: boolean
  onOpenChange: (open: boolean) => void
  kind: DdpItemKind
  itemId: number | null
  /** Codice/descrizione mostrati in intestazione, per capire di quale riga si tratta. */
  itemLabel?: string
}

/**
 * Cronistoria di una riga di distinta: quando è passata da uno stato all'altro e per mano
 * di chi. Sostituisce l'idea di una colonna-data per ogni stato — qui c'è tutto, comprese
 * le voci ricostruite dalle date che il programma già registrava prima di questa funzione.
 */
export function DdpItemHistoryDialog({
  open,
  onOpenChange,
  kind,
  itemId,
  itemLabel,
}: Props) {
  const query = useQuery({
    queryKey: ["ddp-item-events", kind, itemId],
    queryFn: () => fetchDdpItemEvents(kind, itemId as number),
    enabled: open && itemId != null,
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <History className="size-4" />
            Cronistoria della riga
          </DialogTitle>
          <DialogDescription>
            {itemLabel ? itemLabel : "Passaggi di stato registrati per questa riga."}
          </DialogDescription>
        </DialogHeader>

        {query.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : query.error ? (
          <p className="text-sm text-destructive">
            {(query.error as Error).message}
          </p>
        ) : !query.data || query.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Nessun passaggio registrato: la riga non ha ancora cambiato stato da
            quando è attiva la cronistoria.
          </p>
        ) : (
          <ol className="max-h-[60vh] space-y-3 overflow-auto pr-1">
            {query.data.map((evento) => (
              <li key={evento.id} className="flex gap-3">
                <div className="flex flex-col items-center pt-1">
                  <span
                    className="size-3 shrink-0 rounded-full border"
                    style={{ backgroundColor: evento.toColorBg ?? "var(--muted)" }}
                  />
                  <span className="mt-1 w-px flex-1 bg-border" />
                </div>
                <div className="min-w-0 flex-1 pb-1">
                  <div className="flex flex-wrap items-baseline gap-x-2">
                    <span className="font-medium">{evento.toLabel}</span>
                    {evento.fromLabel ? (
                      <span className="text-xs text-muted-foreground">
                        da {evento.fromLabel}
                      </span>
                    ) : null}
                  </div>
                  <div className="text-sm text-muted-foreground">
                    {formatDateShort(evento.changedAt)}
                    {" · "}
                    {new Date(evento.changedAt).toLocaleTimeString("it-IT", {
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                    {evento.changedBy ? ` · ${evento.changedBy}` : ""}
                    {evento.origin === "SISTEMA" ? " · automatico" : ""}
                    {evento.origin === "RICOSTR" ? " · data dedotta" : ""}
                  </div>
                  {evento.note ? (
                    <div className="text-xs text-muted-foreground">{evento.note}</div>
                  ) : null}
                </div>
              </li>
            ))}
          </ol>
        )}
      </DialogContent>
    </Dialog>
  )
}
