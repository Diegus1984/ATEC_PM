// Riepilogo, export Excel e stampa delle righe del Prospetto SAL.
//
// Stavano dentro `SalProspettoView`; sono qui perché le usano anche le due viste
// «Warning Fatturazione SAL» e «Warning incasso fattura», che sono lo stesso elenco
// filtrato alle sole righe in allarme.

import { downloadXlsx } from "@/lib/api/export"
import type { SalProspettoRow } from "@/lib/api/types"
import { formatDateFull } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { printHtml } from "@/lib/print-template"

import { salProspettoAlertLabel } from "./sal-prospetto-alert"
import { buildProspettoExcelTable } from "./sal-prospetto-excel"

/** Contatori del sommario segnalazioni (calcolati dagli alert delle righe). */
export interface AlertCounters {
  total: number
  warn: number
  pre: number
  incasso: number
  attesa: number
}

export function countAlerts(rows: SalProspettoRow[]): AlertCounters {
  const c: AlertCounters = { total: rows.length, warn: 0, pre: 0, incasso: 0, attesa: 0 }
  for (const r of rows) {
    if (r.alert === "warn") c.warn++
    else if (r.alert === "pre") c.pre++
    else if (r.alert === "incasso") c.incasso++
    else if (r.alert === "attesa") c.attesa++
  }
  return c
}

/**
 * Scarica le righe in Excel vero (`.xlsx`): numeri, percentuali e date come valori, filtro su
 * ogni colonna (#150). Il CSV di prima scriveva `27122.172` e Excel italiano lo leggeva
 * come ventisette milioni.
 */
export async function downloadProspettoExcel(
  rows: SalProspettoRow[],
  canSeeEconomics: boolean,
  options?: { fileName?: string; sheetName?: string }
): Promise<void> {
  await downloadXlsx(
    options?.fileName ?? "prospetto-sal.xlsx",
    buildProspettoExcelTable(rows, canSeeEconomics, options?.sheetName)
  )
}

/** Riga di sommario in chiaro, usata come sottotitolo della stampa. */
export function prospettoSummaryText(counters: AlertCounters): string {
  return (
    `${counters.total} ipotesi monitorate · ${counters.warn} scadute di fatturazione · ` +
    `${counters.pre} pre-warning · ${counters.incasso} fatture non incassate · ` +
    `${counters.attesa} emesse in attesa di incasso`
  )
}

/** Apre una finestra con la SOLA tabella e ne lancia la stampa (evita di stampare tutta l'app). */
export function printProspetto(
  rows: SalProspettoRow[],
  canSeeEconomics: boolean,
  options?: { title?: string; subtitle?: string }
): void {
  const esc = (s: string): string =>
    s.replace(/[&<>]/g, (c) => (c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"))
  // Larghezze fisse (table-layout: fixed): senza, «Step SAL» e «Cliente» si prendono
  // tutto lo spazio e le colonne data vanno a capo, sbilanciando la pagina.
  const cols: { label: string; width: string; align?: "right" | "center" }[] = [
    { label: "Segnalazione", width: "9%" },
    { label: "Commessa", width: "9%" },
    { label: "Cliente", width: "16%" },
    { label: "Scad.", width: "4%", align: "center" },
    { label: "Step SAL", width: canSeeEconomics ? "22%" : "31%" },
    { label: "%", width: "5%", align: "right" },
    { label: "Condizione", width: "10%" },
  ]
  if (canSeeEconomics) cols.push({ label: "Importo", width: "9%", align: "right" })
  cols.push(
    { label: "Ipotesi Fatturazione", width: "8%", align: "center" },
    { label: "Data Prevista Saldo", width: "8%", align: "center" }
  )

  const counters = countAlerts(rows)
  const body = rows
    .map((r) => {
      const cells = [
        salProspettoAlertLabel(r.alert),
        r.code,
        r.cliente || "—",
        `${r.ord}°`,
        r.step || "—",
        r.perc != null ? `${r.perc}%` : "—",
        r.condizione || "—",
      ]
      if (canSeeEconomics) cells.push(euro(r.importo))
      cells.push(formatDateFull(r.dataFatt) || "—")
      cells.push(formatDateFull(r.dataSaldo) || "—")
      return `<tr>${cells
        .map((c, i) => {
          const col = cols[i]
          const cls = [
            col.align === "right" ? "num" : col.align === "center" ? "mid" : "",
            col.align ? "nw" : "",
          ]
            .filter(Boolean)
            .join(" ")
          return `<td${cls ? ` class="${cls}"` : ""}>${esc(String(c))}</td>`
        })
        .join("")}</tr>`
    })
    .join("")

  const customStyles = `
    table{border-collapse:collapse;width:100%;table-layout:fixed;font-size:10px}
    col{}
    th,td{border:1px solid #ccc;padding:4px 6px;text-align:left;vertical-align:top;
          overflow-wrap:break-word}
    th{background:#f3f4f6;font-size:9px;text-transform:uppercase;letter-spacing:.03em}
    td.num{text-align:right}
    td.mid{text-align:center}
    td.nw{white-space:nowrap}
    /* Righe intere e intestazione ripetuta a ogni pagina */
    thead{display:table-header-group}
    tr{page-break-inside:avoid}
    tbody tr:nth-child(even){background:#fafafa}
  `

  const contentHtml = `
    <table>
      <colgroup>${cols.map((c) => `<col style="width:${c.width}">`).join("")}</colgroup>
      <thead>
        <tr>${cols
          .map(
            (c) =>
              `<th${c.align === "right" ? ' style="text-align:right"' : c.align === "center" ? ' style="text-align:center"' : ""}>${c.label}</th>`
          )
          .join("")}</tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `

  printHtml({
    title: options?.title ?? "Prospetto SAL — controllo fatturazione e incassi",
    subtitle: options?.subtitle ?? prospettoSummaryText(counters),
    contentHtml,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}
