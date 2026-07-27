import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Printer, RefreshCw } from "lucide-react"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { printHtml } from "@/lib/print-template"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ApiError } from "@/lib/api/client"
import { fetchSalEconomics } from "@/lib/api/sal"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"
import {
  cashFlowTotals,
  type SalCashFlowTotals,
} from "./sal-economics"

// Cash Flow SAL (Fase 5 v10): 5 totali globali su tutte le commesse ATTIVE,
// ciascuno Netto e Con IVA. Dati da GET /api/sal/economics (solo PM/ADMIN,
// il server risponde 403 agli altri ruoli).

const CARDS: {
  key: keyof SalCashFlowTotals
  title: string
  accent: string
  note?: string
}[] = [
  {
    key: "ordini",
    title: "Totale Ordini commesse Attive",
    accent: "border-l-sky-600",
  },
  {
    key: "incassate",
    title: "Totale Fatture Incassate",
    accent: "border-l-emerald-600",
  },
  {
    key: "emesse",
    title: "Totale Fatture Emesse",
    accent: "border-l-amber-500",
  },
  {
    key: "daFatturare",
    title: "Totale da Fatturare",
    accent: "border-l-slate-400",
  },
  {
    key: "avere",
    title: "Totale Avere",
    accent: "border-l-primary",
    note: "Corrisponde a Totale Fatture Emesse + Totale da Fatturare.",
  },
]

/** Apre una finestra con la SOLA tabella dei totali e ne lancia la stampa. */
function printCashFlow(totals: SalCashFlowTotals): void {
  const body = CARDS.map((c) => {
    const v = totals[c.key]
    const note = c.note
      ? `<div style="font-size:9px;color:#666">${c.note}</div>`
      : ""
    return (
      `<tr><td>${c.title}${note}</td>` +
      `<td style="text-align:right">${euro(v.netto)}</td>` +
      `<td style="text-align:right">${euro(v.conIva)}</td></tr>`
    )
  }).join("")

  const customStyles = `
    table{border-collapse:collapse;width:100%;font-size:11px}
    th,td{border:1px solid #ccc;padding:5px 8px;text-align:left}th{background:#f3f4f6}
  `

  const contentHtml = `
    <table>
      <thead>
        <tr>
          <th>Voce</th>
          <th style="text-align:right">Netto</th>
          <th style="text-align:right">Con IVA</th>
        </tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `

  printHtml({
    title: "Cash Flow SAL — commesse attive",
    subtitle: `Situazione al ${formatDateShort(new Date())}`,
    contentHtml,
    orientation: "portrait",
    paperSize: "A4",
    customStyles,
  })
}

// Le card di sintesi: il grafico Analisi è reso da SalPage subito sotto questa vista.
export function SalCashFlowView() {
  const query = useQuery({
    queryKey: ["sal", "economics"],
    queryFn: fetchSalEconomics,
    // Vista aggregata cross-commessa: refresh automatico come il Prospetto SAL.
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  const totals = React.useMemo(
    () => (query.data ? cashFlowTotals(query.data) : null),
    [query.data]
  )

  if (query.isLoading) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-muted-foreground justify-center py-12">
        <RefreshCw className="size-4 animate-spin" />
        Caricamento cash flow SAL...
      </div>
    )
  }

  if (query.isError) {
    const message =
      query.error instanceof ApiError && query.error.status === 403
        ? "Dati economici riservati ai ruoli PM/ADMIN."
        : (query.error as Error).message
    return <PageErrorAlert message={message} />
  }

  const data = query.data
  if (!data || !totals || (data.headers.length === 0 && data.rows.length === 0)) {
    return (
      <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
        <p className="text-sm font-medium text-muted-foreground">
          Nessun dato SAL sulle commesse attive
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-end gap-2">
        <Button
          variant="outline"
          size="sm"
          className="h-8"
          onClick={() => printCashFlow(totals)}
        >
          <Printer className="size-3.5 mr-1.5" />
          Stampa PDF
        </Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        {CARDS.map((c) => {
          const v = totals[c.key]
          return (
            <Card key={c.key} size="sm" className={cn("border-l-4", c.accent)}>
              <CardHeader className="pb-0">
                <CardTitle className="text-[13px] leading-snug">{c.title}</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-1.5">
                <div className="flex items-baseline justify-between gap-2">
                  <span className="text-xs text-muted-foreground">Netto</span>
                  <span className="font-mono text-base font-semibold tabular-nums">
                    {euro(v.netto)}
                  </span>
                </div>
                <div className="flex items-baseline justify-between gap-2">
                  <span className="text-xs text-muted-foreground">Con IVA</span>
                  <span className="font-mono text-sm font-medium tabular-nums text-muted-foreground">
                    {euro(v.conIva)}
                  </span>
                </div>
                {c.note && (
                  <p className="mt-1 text-[11px] leading-snug text-muted-foreground">
                    {c.note}
                  </p>
                )}
              </CardContent>
            </Card>
          )
        })}
      </div>
    </div>
  )
}
