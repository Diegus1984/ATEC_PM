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
  type SalCashAmount,
  type SalCashFlowTotals,
} from "./sal-economics"

/**
 * Le sole voci di `SalCashFlowTotals` che sono un importo Netto/Con IVA: dalla #134 il
 * tipo porta anche un contatore (`ordiniEsclusi`), che una card non saprebbe disegnare.
 * Scritto come tipo derivato e non come elenco a mano: la prossima voce si sistema da sé.
 */
type SalCardKey = {
  [K in keyof SalCashFlowTotals]: SalCashFlowTotals[K] extends SalCashAmount ? K : never
}[keyof SalCashFlowTotals]

// Analisi Economica SAL (Fase 5 v10, rinominata dalla #134 — prima «Cash Flow»):
// 5 totali globali su tutte le commesse ATTIVE, ciascuno Netto e Con IVA. Dati da
// GET /api/sal/economics (solo PM/ADMIN, il server risponde 403 agli altri ruoli).
// Aggiornamento real-time gestito da SalPage tramite SignalR (GlobalSalChanged).

const CARDS: {
  key: SalCardKey
  title: string
  accent: string
  note?: string | ((totals: SalCashFlowTotals) => string)
}[] = [
  {
    key: "ordini",
    title: "Totale Ordini commesse Attive",
    accent: "border-l-sky-600",
    // #134: le commesse col SAL chiuso escono dal portafoglio ordini. Il perché sta
    // scritto sulla card: un totale che cala senza spiegazione sembra un difetto.
    note: (totals) =>
      totals.ordiniEsclusi > 0
        ? `Escluse ${totals.ordiniEsclusi} commesse col SAL chiuso (incasso al 100%).`
        : "Escluse le commesse col SAL chiuso (incasso al 100%): oggi nessuna.",
  },
  {
    key: "incassate",
    title: "Totale Fatture Incassate",
    accent: "border-l-emerald-600",
  },
  {
    key: "emesse",
    title: "Fatture Emesse (Attesa Incasso)",
    accent: "border-l-amber-500",
    note: (totals) => `Totale emesso ad oggi: ${euro(totals.totaleEmesso.netto)} (incluse incassate)`,
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
    note: "Corrisponde a Fatture in attesa + Totale da Fatturare.",
  },
]

/** Apre una finestra con la SOLA tabella dei totali e ne lancia la stampa. */
function printAnalisiEconomica(totals: SalCashFlowTotals): void {
  const body = CARDS.map((c) => {
    const v = totals[c.key]
    const noteText = typeof c.note === "function" ? c.note(totals) : c.note
    const note = noteText
      ? `<div style="font-size:9px;color:#666">${noteText}</div>`
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
    title: "Analisi Economica SAL — commesse attive",
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
        Caricamento analisi economica SAL...
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
          onClick={() => printAnalisiEconomica(totals)}
        >
          <Printer className="size-3.5 mr-1.5" />
          Stampa PDF
        </Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        {CARDS.map((c) => {
          const v = totals[c.key]
          const noteText = typeof c.note === "function" ? c.note(totals) : c.note
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
                {noteText && (
                  <p className="mt-1 text-[11px] leading-snug text-muted-foreground">
                    {noteText}
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
