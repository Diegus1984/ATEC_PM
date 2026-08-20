// Stampa PDF del foglio SAL di una singola commessa (dialogo di stampa browser).

import { fmtSalPct } from "@/features/sal/SalIncassoProgress"
import type { SalBundle, SalRow } from "@/lib/api/types"
import { formatDateFull } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { escapeHtml, printHtml } from "@/lib/print-template"

import { salDataSaldo, salGgSaldoValue, salIsPagata } from "./sal-utils"

function statoFattLabel(stato: string): string {
  if (stato === "daEmettere") return "Da emettere"
  if (stato === "emessa") return "Emessa"
  return stato?.trim() || "—"
}

function dash(s: string | null | undefined): string {
  const t = (s ?? "").trim()
  return t || "—"
}

function rowAmounts(
  row: SalRow,
  valore: number | null | undefined
): { importo: number | null; iva: number | null; totIva: number | null } {
  if (valore == null || row.perc == null) {
    return { importo: null, iva: null, totIva: null }
  }
  const importo = valore * (Number(row.perc) / 100)
  const iva = importo * ((row.ivaPerc ?? 0) / 100)
  return { importo, iva, totIva: importo + iva }
}

/** Apre la finestra di stampa con il piano SAL della commessa. */
export function printSalSheet(
  bundle: SalBundle,
  options: {
    projectCode: string
    projectTitle?: string
    canSeeEconomics: boolean
  }
): void {
  const { header, rows } = bundle
  const { projectCode, projectTitle, canSeeEconomics } = options

  const totalPerc = rows.reduce((acc, r) => acc + Number(r.perc ?? 0), 0)
  const paidPerc = rows.reduce(
    (acc, r) => acc + (salIsPagata(r.pagamento) ? Number(r.perc ?? 0) : 0),
    0
  )

  type Col = { label: string; width: string; align?: "right" | "center" }
  const cols: Col[] = [
    { label: "#", width: "3%", align: "center" },
  ]
  if (canSeeEconomics) cols.push({ label: "IVA", width: "6%", align: "right" })
  cols.push({ label: "% IVA", width: "4%", align: "center" })
  if (canSeeEconomics) cols.push({ label: "Tot. + IVA", width: "7%", align: "right" })
  cols.push(
    { label: "Data Prev. Saldo", width: "7%", align: "center" },
    { label: "GG Saldo", width: "4%", align: "center" },
    { label: "Step SAL", width: canSeeEconomics ? "14%" : "22%" },
    { label: "N° Fattura", width: "7%" },
    { label: "Conto SAP", width: "7%" },
    { label: "% SAL", width: "4%", align: "center" },
    { label: "Condizioni", width: "8%" }
  )
  if (canSeeEconomics) cols.push({ label: "Importo", width: "7%", align: "right" })
  cols.push(
    { label: "Ipotesi Fatt.", width: "7%", align: "center" },
    { label: "Stato Fatt.", width: "7%" },
    { label: "Pagamento", width: "7%" },
    { label: "Data Incasso", width: "7%", align: "center" },
    { label: "Note", width: canSeeEconomics ? "8%" : "12%" }
  )

  let sumImporto = 0
  let sumIva = 0
  let hasEconomics = false

  const body = rows
    .map((r, index) => {
      const { importo, iva, totIva } = rowAmounts(r, header.valore)
      if (importo != null) {
        sumImporto += importo
        sumIva += iva ?? 0
        hasEconomics = true
      }
      const dataSaldo = salDataSaldo(r.dataFatt, r.ggSaldo)
      const cells: string[] = [String(index + 1).padStart(2, "0")]
      if (canSeeEconomics) cells.push(euro(iva))
      cells.push(r.ivaPerc != null ? `${r.ivaPerc}%` : "—")
      if (canSeeEconomics) cells.push(euro(totIva))
      cells.push(
        formatDateFull(dataSaldo) || "—",
        String(salGgSaldoValue(r.ggSaldo)),
        dash(r.step),
        dash(r.nFatt),
        dash(r.contoSap),
        r.perc != null ? `${r.perc}%` : "—",
        dash(r.condizione)
      )
      if (canSeeEconomics) cells.push(euro(importo))
      cells.push(
        formatDateFull(r.dataFatt) || "—",
        statoFattLabel(r.stato),
        dash(r.pagamento),
        formatDateFull(r.dataPagamento) || "—",
        dash(r.note)
      )
      return `<tr>${cells
        .map((c, i) => {
          const col = cols[i]
          const cls = [
            col.align === "right" ? "num" : col.align === "center" ? "mid" : "",
            col.align ? "nw" : "",
          ]
            .filter(Boolean)
            .join(" ")
          return `<td${cls ? ` class="${cls}"` : ""}>${escapeHtml(c)}</td>`
        })
        .join("")}</tr>`
    })
    .join("")

  const footerCells: string[] = [""]
  if (canSeeEconomics) footerCells.push(hasEconomics ? euro(sumIva) : "—")
  footerCells.push("")
  if (canSeeEconomics) footerCells.push(hasEconomics ? euro(sumImporto + sumIva) : "—")
  footerCells.push("", "", "Totali", "", "", fmtSalPct(totalPerc) + "%", "")
  if (canSeeEconomics) footerCells.push(hasEconomics ? euro(sumImporto) : "—")
  footerCells.push("", "", "", "", "")

  const footer =
    rows.length > 0
      ? `<tr class="tot">${footerCells
          .map((c, i) => {
            const col = cols[i]
            const cls = [
              col?.align === "right" ? "num" : col?.align === "center" ? "mid" : "",
              col?.align ? "nw" : "",
            ]
              .filter(Boolean)
              .join(" ")
            return `<td${cls ? ` class="${cls}"` : ""}>${escapeHtml(c)}</td>`
          })
          .join("")}</tr>`
      : ""

  const emptyMsg =
    rows.length === 0
      ? `<p class="empty">Nessun pagamento SAL pianificato per questa commessa.</p>`
      : ""

  const customStyles = `
    table{border-collapse:collapse;width:100%;table-layout:fixed;font-size:9px}
    th,td{border:1px solid #ccc;padding:3px 5px;text-align:left;vertical-align:top;
          overflow-wrap:break-word}
    th{background:#f3f4f6;font-size:8px;text-transform:uppercase;letter-spacing:.03em}
    td.num{text-align:right}
    td.mid{text-align:center}
    td.nw{white-space:nowrap}
    thead{display:table-header-group}
    tr{page-break-inside:avoid}
    tbody tr:nth-child(even){background:#fafafa}
    tr.tot td{background:#f3f4f6;font-weight:700}
    .empty{margin:24px 0;text-align:center;color:#788896;font-size:12px}
  `

  const contentHtml = `
    ${emptyMsg}
    ${
      rows.length > 0
        ? `<table>
      <colgroup>${cols.map((c) => `<col style="width:${c.width}">`).join("")}</colgroup>
      <thead>
        <tr>${cols
          .map(
            (c) =>
              `<th${c.align === "right" ? ' style="text-align:right"' : c.align === "center" ? ' style="text-align:center"' : ""}>${c.label}</th>`
          )
          .join("")}</tr>
      </thead>
      <tbody>${body}${footer}</tbody>
    </table>`
        : ""
    }
  `

  const meta: { label: string; value: string | number }[] = [
    { label: "Cliente", value: header.customerName || "—" },
    { label: "PO - Ordine", value: header.po || "—" },
    { label: "Rif. Offerta", value: header.rifOfferta || "—" },
  ]
  if (canSeeEconomics) {
    meta.push({ label: "Importo ordine", value: euro(header.valore) })
  }
  meta.push(
    { label: "% SAL pianificata", value: `${fmtSalPct(totalPerc)}%` },
    { label: "% incassata", value: `${fmtSalPct(paidPerc)}%` }
  )

  const titlePlain = projectTitle
    ? `SAL — ${projectCode} · ${projectTitle}`
    : `SAL — ${projectCode}`

  printHtml({
    title: titlePlain,
    titleHtml: projectTitle
      ? `SAL — <span class="code">${escapeHtml(projectCode)}</span> · ${escapeHtml(projectTitle)}`
      : `SAL — <span class="code">${escapeHtml(projectCode)}</span>`,
    subtitle: "Piano di fatturazione a stati d'avanzamento",
    meta,
    contentHtml,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}
