import { ChevronDown, ChevronRight } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { formatCodexCode } from "@/lib/format"
import { cn } from "@/lib/utils"

import {
  DUE_DATE_CELL_INDEX,
  ddpRowToSintesiCells,
  isOverdueDate,
  sintesiTableHeaders,
} from "./ddp-sintesi-table"

export interface RowOffControls {
  isOff: (rowId: number) => boolean
  toggle: (rowId: number, off: boolean) => void
}

/**
 * Tabella righe della Sintesi DDP (distinta completa, colonne pilotate dal menu «Colonne»).
 * Le righe «spente» restano visibili in grigio: escono solo dalle stampe.
 */
export function DdpRowsTable({
  rows,
  statusDefs,
  statoLabel,
  officina,
  visibleCols,
  markOverdue,
  today,
  emptyText = "Nessuna riga.",
  rowOff,
  collapsedParentIds,
  parentIdsWithChildren,
  toggleParentCollapse,
}: {
  rows: DdpRowItem[]
  statusDefs: Map<string, DdpStatusItem>
  statoLabel: (key: string) => string
  officina: boolean
  /** Visibilità per etichetta di colonna (menu «Colonne» della pagina). */
  visibleCols: Record<string, boolean>
  /** Colora di rosso la data prevista già passata. */
  markOverdue?: boolean
  /** Data di riferimento del modello (`model.today`): serve solo con `markOverdue`. */
  today?: string
  emptyText?: string
  /** Se passato, compare la casella «riga accesa/spenta» in testa a ogni riga. */
  rowOff?: RowOffControls
  collapsedParentIds?: Set<number>
  parentIdsWithChildren?: Set<number>
  toggleParentCollapse?: (id: number) => void
}) {
  const headers = sintesiTableHeaders(officina)
  // Indici delle colonne accese: celle e intestazioni sono allineate per posizione
  // (vedi ddpRowToSintesiCells), quindi basta filtrare gli indici.
  const shownIdx = headers
    .map((_, index) => index)
    .filter((index) => visibleCols[headers[index]] ?? true)
  if (rows.length === 0) {
    return <p className="text-sm text-muted-foreground">{emptyText}</p>
  }

  let visibleRows = rows
  if (officina && collapsedParentIds) {
    visibleRows = rows.filter((row) => {
      if (row.parentOfficinaItemId != null) {
        return !collapsedParentIds.has(row.parentOfficinaItemId)
      }
      return true
    })
  }

  return (
    <GridScroller className="rounded-lg border">
      <Table className="min-w-[1400px] text-xs">
        <TableHeader className="bg-muted/50">
          <TableRow>
            {rowOff ? <TableHead className="h-8 w-9" /> : null}
            {shownIdx.map((index) => (
              <TableHead key={headers[index]} className="h-8 whitespace-nowrap">
                {headers[index]}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {visibleRows.map((row) => {
            const def = statusDefs.get(row.itemStatus)
            const off = rowOff?.isOff(row.id) ?? false
            const style = def
              ? { backgroundColor: def.colorBg, color: def.colorFg }
              : undefined
            // A video le date stanno a 2 cifre d'anno; il 4 cifre resta a stampe ed export.
            const cells = ddpRowToSintesiCells(
              row,
              officina,
              statoLabel,
              formatDateShort
            )
            const overdue =
              markOverdue && !!today && isOverdueDate(row.dateNeeded, today)
            return (
              <TableRow key={row.id} style={style} className={cn(off && "opacity-40")}>
                {rowOff ? (
                  <TableCell className="w-9">
                    <Checkbox
                      checked={!off}
                      aria-label={off ? "Riga spenta" : "Riga accesa"}
                      title={
                        off
                          ? "Riga esclusa dalle stampe: clicca per riaccenderla"
                          : "Escludi la riga dalle stampe"
                      }
                      onCheckedChange={(checked) =>
                        rowOff.toggle(row.id, checked !== true)
                      }
                    />
                  </TableCell>
                ) : null}
                {shownIdx.map((index) => {
                  const cell = cells[index]
                  if (officina && index === 0) {
                    const isChild = row.parentOfficinaItemId != null
                    return (
                      <TableCell
                        key={index}
                        className={cn(
                          isChild
                            ? "pl-3 italic font-normal opacity-70"
                            : "opacity-80 tabular-nums font-semibold",
                          "whitespace-nowrap"
                        )}
                      >
                        {row.rowNumber}
                      </TableCell>
                    )
                  }
                  if (
                    officina &&
                    index === 3 &&
                    parentIdsWithChildren &&
                    toggleParentCollapse &&
                    collapsedParentIds
                  ) {
                    const isChild = row.parentOfficinaItemId != null
                    const hasChildren = parentIdsWithChildren.has(row.id)
                    const isCollapsed = collapsedParentIds.has(row.id)
                    return (
                      <TableCell key={index} className="whitespace-nowrap">
                        <span className="flex items-center gap-1 font-semibold">
                          {isChild ? (
                            <span
                              className="mr-1 select-none text-muted-foreground"
                              title={`Componente di composizione (${row.compositionQty ?? 1} per padre)`}
                            >
                              ↳
                            </span>
                          ) : hasChildren ? (
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation()
                                toggleParentCollapse(row.id)
                              }}
                              className="mr-1 inline-flex size-5 items-center justify-center rounded hover:bg-muted"
                            >
                              {isCollapsed ? (
                                <ChevronRight className="size-4" strokeWidth={2.5} />
                              ) : (
                                <ChevronDown className="size-4" strokeWidth={2.5} />
                              )}
                            </button>
                          ) : null}
                          {formatCodexCode(row.partNumber) || "—"}
                        </span>
                      </TableCell>
                    )
                  }
                  if (index === 4) {
                    return (
                      <TableCell
                        key={index}
                        className="min-w-[280px] max-w-[420px] p-2"
                      >
                        <span
                          className="block line-clamp-2 whitespace-normal break-words leading-snug"
                          title={typeof cell === "string" ? cell : undefined}
                        >
                          {cell || "—"}
                        </span>
                      </TableCell>
                    )
                  }
                  return (
                    <TableCell
                      key={index}
                      className={cn(
                        index === headers.length - 2
                          ? "min-w-[160px]"
                          : "whitespace-nowrap",
                        index === 0 || index === 5 ? "tabular-nums" : undefined,
                        index >= headers.length - 2 ? "tabular-nums" : undefined,
                        // Ritardo: solo colore sulla cella, il testo resta pulito.
                        overdue &&
                          index === DUE_DATE_CELL_INDEX &&
                          "font-semibold text-red-700 dark:text-red-400"
                      )}
                    >
                      {cell}
                    </TableCell>
                  )
                })}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </GridScroller>
  )
}
