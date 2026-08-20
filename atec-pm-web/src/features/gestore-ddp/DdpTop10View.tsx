import * as React from "react"
import { Printer } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { euro } from "@/lib/format"

import { DdpRowsTable } from "./DdpRowsTable"
import { fmtQty } from "./ddp-sintesi-table"
import type { DdpViewProps } from "./ddp-view-props"

/**
 * Percentuale delle righe di piede. Il modello espone `pctLabel` solo sulle singole
 * righe della Top 10: qui si applica la stessa formula della stampa
 * (`ddp-print-tables.ts`), così video e PDF non possono divergere di un decimale.
 */
function pctOnTotal(value: number, total: number): string {
  return total > 0
    ? `${((value / total) * 100).toLocaleString("it-IT", {
        minimumFractionDigits: 1,
        maximumFractionDigits: 1,
      })}%`
    : "—"
}

/**
 * Scheda «Top 10 Costi»: le dieci righe a maggior impatto economico della distinta.
 * Base = `model.top10`, già ordinato per importo decrescente dal motore
 * (`quantity × unitCost`): qui non si riordina e non si ricalcola nulla.
 */
export function DdpTop10View(props: DdpViewProps) {
  const { model, officina, statusDefs, statoLabel, visibleCols, printKey } = props

  // Le 6 colonne compatte del prototipo bastano per leggere l'impatto; la distinta
  // completa resta a un clic per chi deve controllare rif. Danea, note o destinazione.
  const [fullCols, setFullCols] = React.useState(false)

  const { subtotal, total } = model.top10Totals
  const empty = model.top10.length === 0

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold">Top 10 Costi</h2>
          <p className="text-xs text-muted-foreground">
            Le righe che pesano di più sul totale acquisti della commessa.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <Label className="text-xs font-normal text-muted-foreground">
            <Checkbox
              checked={fullCols}
              onCheckedChange={(checked) => setFullCols(checked === true)}
            />
            Colonne complete
          </Label>
          <Button variant="outline" size="sm" onClick={() => printKey("top10")}>
            <Printer />
            Stampa PDF
          </Button>
        </div>
      </div>

      {empty ? (
        <p className="text-sm text-muted-foreground">Nessuna riga.</p>
      ) : fullCols ? (
        // Stesse dieci righe, tutte le colonne della distinta (filtrate dal menu «Colonne»).
        <DdpRowsTable
          rows={model.top10.map((row) => row.item)}
          statusDefs={statusDefs}
          statoLabel={statoLabel}
          officina={officina}
          visibleCols={visibleCols}
        />
      ) : (
        <GridScroller className="rounded-lg border">
          <Table className="min-w-[760px] text-xs">
            <TableHeader className="bg-muted/50">
              <TableRow>
                <TableHead className="h-8 w-12">#</TableHead>
                <TableHead className="h-8">Descrizione</TableHead>
                <TableHead className="h-8 text-right">Qtà</TableHead>
                <TableHead className="h-8">Fornitore</TableHead>
                <TableHead className="h-8 text-right">Importo (€)</TableHead>
                <TableHead className="h-8 text-right">% sul tot.</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.top10.map((row) => (
                <TableRow key={row.item.id}>
                  <TableCell className="w-12">
                    <span className="inline-flex size-6 items-center justify-center rounded-lg bg-primary text-[11px] font-bold tabular-nums text-primary-foreground">
                      {row.rank}
                    </span>
                  </TableCell>
                  <TableCell className="min-w-[320px] whitespace-normal">
                    {row.item.description}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {fmtQty(row.item.quantity)}
                  </TableCell>
                  <TableCell>{row.item.supplierName || "—"}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {euro(row.item.unitCost == null ? null : row.item.quantity * row.item.unitCost)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {row.pctLabel}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            {/* Piede non ordinabile: subtotale delle dieci righe e totale della commessa. */}
            <TableFooter>
              <TableRow className="bg-muted/50 font-semibold">
                <TableCell />
                <TableCell>Subtotale Top 10</TableCell>
                <TableCell />
                <TableCell />
                <TableCell className="text-right tabular-nums">
                  {euro(subtotal)}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {pctOnTotal(subtotal, total)}
                </TableCell>
              </TableRow>
              <TableRow className="bg-muted/50 font-semibold">
                <TableCell />
                <TableCell>Totale commessa</TableCell>
                <TableCell />
                <TableCell />
                <TableCell className="text-right tabular-nums">
                  {euro(total)}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {total > 0 ? "100,0%" : "—"}
                </TableCell>
              </TableRow>
            </TableFooter>
          </Table>
        </GridScroller>
      )}

      {empty ? null : (
        <p className="text-xs text-muted-foreground">
          Percentuali calcolate sul totale acquisti della commessa (escluse le righe a
          stato chiuso).
        </p>
      )}
    </div>
  )
}
