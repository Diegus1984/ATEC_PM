// Export/stampa della Sintesi DDP (port di BtnExportExcel/BtnPrint del WPF):
// Excel = HTML-table aperto da Excel (.xls); Stampa = finestra HTML + window.print().

import { printHtml, escapeHtml } from "@/lib/print-template"

export interface ExportTable {
  title: string
  headers: string[]
  rows: string[][]
  /** Colore di sfondo per riga (es. colore stato DDP). */
  rowColors?: (string | null)[]
}

function safeFileName(value: string): string {
  const cleaned = value.replace(/[\\/:*?"<>|]/g, "_").trim()
  return cleaned || "export"
}

function tableHtml(table: ExportTable, headerBg: string): string {
  const head = table.headers
    .map(
      (header) =>
        `<th style="background:${headerBg};border:0.5px solid #ccc;padding:4px 6px;font-weight:bold;text-align:left">${escapeHtml(
          header
        )}</th>`
    )
    .join("")
  const body = table.rows
    .map((cells, index) => {
      const color = table.rowColors?.[index]
      const style = color ? ` style="background:${color}"` : ""
      const tds = cells
        .map(
          (cell) =>
            `<td style="border:0.5px solid #ccc;padding:3px 6px;vertical-align:top">${escapeHtml(
              cell
            )}</td>`
        )
        .join("")
      return `<tr${style}>${tds}</tr>`
    })
    .join("")
  return `<table style="border-collapse:collapse;font-family:'Segoe UI',Arial,sans-serif;font-size:11px"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`
}

export function exportDdpExcel(fileBase: string, table: ExportTable): void {
  const html = `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel"><head><meta charset="utf-8"></head><body><h3>${escapeHtml(
    table.title
  )}</h3>${tableHtml(table, "#CFE3F6")}</body></html>`
  const blob = new Blob([html], { type: "application/vnd.ms-excel" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")
  link.href = url
  link.download = `DDP_${safeFileName(fileBase)}.xls`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export function printDdpTables(
  title: string,
  subtitle: string,
  tables: ExportTable[]
): void {
  const sections = tables
    .map(
      (table) =>
        `<h3 style="font-size:13px;margin:16px 0 4px">${escapeHtml(
          table.title
        )}</h3>${tableHtml(table, "#dcdcdc")}`
    )
    .join("")

  const customStyles = `
    table{border-collapse:collapse;width:100%}
    td,th{border:0.5px solid #bbb;padding:3px 6px;text-align:left;vertical-align:top}
  `

  printHtml({
    title,
    subtitle,
    contentHtml: sections,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}
