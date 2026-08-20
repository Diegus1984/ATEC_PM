import { Printer } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { cn } from "@/lib/utils"

import { barWidthPercent } from "./ddp-sintesi-logic"
import type { DdpViewProps } from "./ddp-view-props"

/**
 * Scheda «Destinazioni»: quante righe di distinta vanno a ciascuna destinazione.
 * Base = `model.destinazioni`, già normalizzato (maiuscolo, spazi compattati) e
 * ordinato dal motore con «NON DEFINITA» sempre in fondo: qui non si riordina.
 * Il denominatore è il **numero di righe**, non il valore economico.
 */
export function DdpDestinazioniView(props: DdpViewProps) {
  const { model, printKey } = props

  const total = model.rows.length
  const empty = model.destinazioni.length === 0

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Destinazioni</h2>
          <p className="text-xs text-muted-foreground">{model.destSub}</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => printKey("dest")}>
          <Printer />
          Stampa PDF
        </Button>
      </div>

      {empty ? (
        <p className="text-sm text-muted-foreground">Nessuna riga.</p>
      ) : (
        <GridScroller className="rounded-lg border">
          <Table className="min-w-[620px] text-xs">
            <TableHeader className="bg-muted/50">
              <TableRow>
                <TableHead className="h-8">Destinazione</TableHead>
                <TableHead className="h-8 w-24 text-right">N. Righe</TableHead>
                <TableHead className="h-8 w-[280px]">% sul totale</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.destinazioni.map((bar) => (
                <TableRow
                  key={bar.label}
                  // Destinazione non valorizzata: è un dato da sistemare, si vede subito.
                  className={cn(
                    bar.isNonDef &&
                      "bg-red-50 font-semibold text-red-700 hover:bg-red-50 dark:bg-red-950/40 dark:text-red-400 dark:hover:bg-red-950/40"
                  )}
                >
                  <TableCell className="min-w-[220px] whitespace-normal">
                    {bar.label}
                  </TableCell>
                  <TableCell className="w-24 text-right tabular-nums">
                    {bar.count}
                  </TableCell>
                  <TableCell className="w-[280px]">
                    <div className="flex items-center gap-2">
                      <div className="h-2 min-w-[120px] flex-1 overflow-hidden rounded-full bg-muted">
                        <div
                          className="h-full rounded-full transition-[width] duration-500 ease-out"
                          style={{
                            width: `${barWidthPercent(bar.fraction, bar.count)}%`,
                            backgroundColor: bar.bg,
                          }}
                        />
                      </div>
                      <span className="w-12 shrink-0 text-right tabular-nums">
                        {bar.pct}
                      </span>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            {/* Riga di quadratura: le percentuali devono sommare a 100%. Senza barra. */}
            <TableFooter>
              <TableRow className="bg-muted/50 font-semibold">
                <TableCell>TOTALE</TableCell>
                <TableCell className="w-24 text-right tabular-nums">{total}</TableCell>
                <TableCell className="w-[280px] text-right tabular-nums">
                  {total > 0 ? "100,0%" : "—"}
                </TableCell>
              </TableRow>
            </TableFooter>
          </Table>
        </GridScroller>
      )}

      {empty ? null : (
        <p className="text-xs text-muted-foreground">
          Percentuali calcolate sul numero di righe, non sul valore.
        </p>
      )}
    </div>
  )
}
