import { MinusCircle, Printer, RotateCcw } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"

import { mancantiFooterText, type MissingCell } from "./ddp-sintesi-logic"
import type { DdpViewProps } from "./ddp-view-props"

/** Grigio del trattino «dato presente»: deve restare leggibile su ogni colore di stato. */
const OK_DASH_COLOR = "#94A3B8"

/**
 * Cella di controllo: se il dato manca si scrive il **nome del campo** in rosso scuro
 * su fondo rosa (contrasto fisso, indipendente dallo sfondo-stato della riga — altrimenti
 * su stati verdi con testo bianco il flag diventava bianco-su-rosa e spariva).
 * Se il dato c’è: trattino grigio.
 */
function MissingFlagCell({ cell }: { cell: MissingCell }) {
  if (!cell.missing) {
    return (
      <TableCell className="text-center" style={{ color: OK_DASH_COLOR }}>
        –
      </TableCell>
    )
  }
  return (
    <TableCell className="bg-red-100 text-center font-semibold text-red-800 dark:bg-red-950/50 dark:text-red-200">
      {cell.text}
    </TableCell>
  )
}

/**
 * Scheda «Dati Mancanti» (port della pagina omonima del prototipo V41): righe di distinta
 * con almeno uno dei 5 campi di controllo non valorizzato.
 * Lo spegnimento riga qui significa «l'ho già gestita»: la riga sparisce dall'elenco e dal
 * PDF, ma i conteggi del modello restano sul totale (regola del prototipo) — cala solo
 * «righe visualizzate».
 */
export function DdpMancantiView({
  model,
  statusDefs,
  statoLabel,
  rowOff,
  printKey,
}: DdpViewProps) {
  const confirm = useConfirm()

  const visible = model.mancanti.filter((row) => !rowOff.isOff("mancanti", row.id))
  const spente = model.mancanti.length - visible.length
  const { analyzed, excluded } = model.mancantiCounts

  const piede = mancantiFooterText(visible.length, spente, model.mancantiCounts)

  async function resetRighe() {
    const ok = await confirm({
      title: "Ripristinare tutte le righe spente?",
      description:
        "Le righe nascoste torneranno visibili in elenco e nelle stampe.",
      confirmLabel: "Ripristina",
      destructive: false,
    })
    if (!ok) return
    await rowOff.reset("mancanti")
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-card p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Dati Mancanti</h2>
          <p className="text-xs text-muted-foreground">
            Righe con dati mancanti nei campi controllati.
          </p>
          {/* L'elenco degli stati esclusi arriva dall'aggregazione A8 risolta: si mostra
              quello vero, configurato in «Aggregazioni DDP», mai una lista cablata. */}
          <p className="text-xs text-muted-foreground">
            Escluse dall'analisi le righe con stato:{" "}
            {[...model.sets.exclMissing].sort().join(", ") || "nessuno"} ({excluded} su{" "}
            {analyzed + excluded}).
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => printKey("mancanti")}>
          <Printer />
          Stampa PDF
        </Button>
      </div>

      {model.mancanti.length === 0 ? (
        <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-900 dark:border-emerald-900/50 dark:bg-emerald-950/40 dark:text-emerald-100">
          Nessuna riga con dati mancanti nei campi Stato, Rif. Danea, Data Prev. Cons.,
          Destinazione e Costo Unitario.
        </div>
      ) : visible.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Tutte le righe con dati mancanti sono state spente. Usa «Reset righe» per
          rivisualizzarle.
        </p>
      ) : (
        <GridScroller className="rounded-lg border">
          <Table className="min-w-[900px] text-xs">
            {/* Doppia intestazione: entrambe le righe restano ferme in alto (la seconda
                sotto la prima, alta 8 = h-8), altrimenti scorrendo si perde il gruppo. */}
            <TableHeader>
              <TableRow className="sticky top-0 z-20 bg-muted/50">
                <TableHead rowSpan={2} className="h-8 w-14 whitespace-nowrap">
                  Riga
                </TableHead>
                <TableHead rowSpan={2} className="h-8 w-32 whitespace-nowrap">
                  Stato
                </TableHead>
                <TableHead rowSpan={2} className="h-8 min-w-[220px]">
                  Descrizione
                </TableHead>
                <TableHead colSpan={5} className="h-8 text-center">
                  DATI MANCANTI
                </TableHead>
                <TableHead rowSpan={2} className="h-8 w-10" />
              </TableRow>
              <TableRow className="sticky top-8 z-20 bg-muted/50">
                <TableHead className="h-8 text-center">Stato</TableHead>
                <TableHead className="h-8 text-center">Rif. Danea</TableHead>
                <TableHead className="h-8 text-center">Data Prev. Cons.</TableHead>
                <TableHead className="h-8 text-center">Destinazione</TableHead>
                <TableHead className="h-8 text-center">Costo Unitario</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {visible.map((row) => {
                const def = statusDefs.get(row.statoKey)
                const style = def
                  ? { backgroundColor: def.colorBg, color: def.colorFg }
                  : undefined
                return (
                  <TableRow key={row.id} style={style}>
                    <TableCell className="tabular-nums">{row.rowNo}</TableCell>
                    {/* Stato non valorizzato: nessuna causale a DB, si mostra «ND». */}
                    <TableCell>{statoLabel(row.statoKey) || "ND"}</TableCell>
                    <TableCell className="min-w-[220px] whitespace-normal">
                      {row.desc}
                    </TableCell>
                    <MissingFlagCell cell={row.stato} />
                    <MissingFlagCell cell={row.rif} />
                    <MissingFlagCell cell={row.data} />
                    <MissingFlagCell cell={row.dest} />
                    <MissingFlagCell cell={row.costo} />
                    <TableCell className="w-10">
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        className="rounded-full"
                        title="Spegni riga (l'ho già gestita)"
                        onClick={() => rowOff.toggle("mancanti", row.id, true)}
                      >
                        <MinusCircle />
                      </Button>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </GridScroller>
      )}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">{piede}</p>
        {spente > 0 ? (
          <Button variant="outline" size="xs" onClick={() => void resetRighe()}>
            <RotateCcw />
            Reset righe
          </Button>
        ) : null}
      </div>
    </div>
  )
}
