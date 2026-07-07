import type { MoMActionItem } from "@/lib/api/types"
import { isoToDate } from "@/lib/date-iso"
import { WEEKDAYS_SHORT, isRedDay } from "@/lib/it-holidays"

// Web equivalent dell'export del prototipo Gestione_MoM_v9: Stampa (A4 landscape,
// righe colorate per stato, giorno della settimana sotto le date, revisione in
// testata), Excel (.xls HTML-table), Word (.doc HTML) e CSV (separatore ';').

export interface MoMExportArgs {
  title: string
  tipoBadge: string
  meetingDate: string | null
  rev: number
  items: MoMActionItem[]
}

function statoLabel(item: MoMActionItem): string {
  if (item.status === "CLOSED") return "Chiusa"
  if (item.isCritical) return "Max priorità"
  if (item.status === "STANDBY") return "Stand by"
  return "Aperta"
}

function rowBgHex(item: MoMActionItem): string | null {
  if (item.status === "CLOSED") return "#C6EFCE"
  if (item.isCritical) return "#F6C7C7"
  if (item.status === "STANDBY") return "#FCF1C2"
  return null
}

function responsibleText(item: MoMActionItem): string {
  const names =
    item.responsibleNames.length > 0
      ? item.responsibleNames
      : [item.resp1Name, item.resp2Name, item.resp3Name].filter(Boolean)
  return names.join(", ")
}

function formatDate(value: string | null): string {
  if (!value) return ""
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleDateString("it-IT")
}

/** Data + giorno della settimana (rosso se festivo) per la stampa, come v9. */
function dowCellHtml(value: string | null): string {
  const date = isoToDate(value)
  if (!date) return ""
  const fest = isRedDay(date)
  return `${formatDate(value)}<span style="display:block;font-size:8.5px;font-weight:700;color:${
    fest ? "#C0392B" : "#27384a"
  }">${WEEKDAYS_SHORT[date.getDay()]}</span>`
}

function escapeHtml(value: string | null | undefined): string {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
}

function safeFileName(value: string): string {
  const cleaned = value.replace(/[\\/:*?"<>|]/g, "_").trim()
  return cleaned || "verbale"
}

function download(name: string, content: string, type: string): void {
  const blob = new Blob([content], { type })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

const HEADERS = [
  "#",
  "Attività",
  "Descrizione",
  "Azione",
  "Priorità",
  "Responsabili",
  "Data check avanz.",
  "Data close",
  "Stato",
]

function rowCells(item: MoMActionItem, index: number): string[] {
  return [
    String(index + 1),
    item.attivita,
    item.descrizione ?? "",
    item.azione ?? "",
    String(item.priorita),
    responsibleText(item),
    formatDate(item.dataCheck),
    formatDate(item.dataClose),
    statoLabel(item),
  ]
}

const PRI_HEX: Record<number, { bg: string; ink: string }> = {
  1: { bg: "#FFD0D6", ink: "#9C0006" },
  2: { bg: "#FFEDB0", ink: "#8A5A00" },
  3: { bg: "#C9EFD2", ink: "#0B6B2E" },
}

function buildTable(items: MoMActionItem[], withDow: boolean): string {
  const head = HEADERS.map(
    (header) =>
      `<th style="background:#CFE3F6;color:#2F6098;border:0.5px solid #A9C3DE;padding:4px 6px;font-weight:bold;text-align:left">${escapeHtml(
        header
      )}</th>`
  ).join("")

  const body = items
    .map((item, index) => {
      const bg = rowBgHex(item)
      const style = bg ? ` style="background:${bg}"` : ""
      const pri = PRI_HEX[item.priorita] ?? PRI_HEX[2]
      const cells = rowCells(item, index)
        .map((cell, cellIndex) => {
          let cellStyle =
            "border:0.5px solid #ccc;padding:3px 6px;vertical-align:top;white-space:pre-wrap"
          let html = escapeHtml(cell)
          if (cellIndex === 4)
            cellStyle += `;background:${pri.bg};color:${pri.ink};text-align:center;font-weight:bold`
          if (withDow && cellIndex === 6) html = dowCellHtml(item.dataCheck)
          if (withDow && cellIndex === 7) html = dowCellHtml(item.dataClose)
          return `<td style="${cellStyle}">${html}</td>`
        })
        .join("")
      return `<tr${style}>${cells}</tr>`
    })
    .join("")

  return `<table style="border-collapse:collapse;width:100%;font-family:'Segoe UI',Arial,sans-serif;font-size:11px"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`
}

function metaLine(args: MoMExportArgs): string {
  const parts = [args.tipoBadge]
  if (args.meetingDate) parts.push(`Riunione: ${formatDate(args.meetingDate)}`)
  parts.push(`Rev. ${args.rev}`)
  return parts.join(" · ")
}

export function exportMoMExcel(args: MoMExportArgs): void {
  const html = `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel"><head><meta charset="utf-8"></head><body><h3>${escapeHtml(
    `${args.title} · ${metaLine(args)}`
  )}</h3>${buildTable(args.items, false)}</body></html>`
  download(
    `MoM_${safeFileName(args.title)}.xls`,
    html,
    "application/vnd.ms-excel"
  )
}

export function exportMoMWord(args: MoMExportArgs): void {
  const html = `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word"><head><meta charset="utf-8"><style>table{border-collapse:collapse;width:100%}</style></head><body><h2 style="color:#2F6098;margin:6px 0">${escapeHtml(
    args.title
  )}</h2><p style="color:#555;font-size:11px;margin:0 0 10px">${escapeHtml(
    `${metaLine(args)} · Focus Azioni`
  )}</p>${buildTable(args.items, false)}</body></html>`
  download(`MoM_${safeFileName(args.title)}.doc`, html, "application/msword")
}

export function exportMoMCsv(args: MoMExportArgs): void {
  const quote = (value: string) =>
    `"${String(value ?? "")
      .replace(/"/g, '""')
      .replace(/\r?\n/g, " / ")}"`
  const lines = [
    HEADERS.slice(1).map(quote).join(";"),
    ...args.items.map((item, index) =>
      rowCells(item, index).slice(1).map(quote).join(";")
    ),
  ]
  // BOM esplicito: Excel riconosce l'UTF-8 solo con il byte order mark in testa.
  download(
    `MoM_${safeFileName(args.title)}.csv`,
    "﻿" + lines.join("\r\n"),
    "text/csv;charset=utf-8"
  )
}

export function printMoM(args: MoMExportArgs): void {
  const html = `<!doctype html><html><head><meta charset="utf-8"><title>${escapeHtml(
    `MoM ${args.title}`
  )}</title><style>@page{size:A4 landscape;margin:12mm}body{font-family:'Segoe UI',Arial,sans-serif;font-size:11px;margin:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}h2{font-size:16px;margin:0 0 2px;color:#2F6098}p.meta{color:#666;margin:0 0 10px}span.rev{background:#2F6098;color:#fff;border-radius:999px;padding:2px 10px;font-weight:bold;font-size:11px}table{page-break-inside:auto}tr{page-break-inside:avoid}thead{display:table-header-group}</style></head><body><h2>${escapeHtml(
    args.title
  )}</h2><p class="meta">${escapeHtml(args.tipoBadge)}${
    args.meetingDate
      ? ` · Riunione: ${escapeHtml(formatDate(args.meetingDate))}`
      : ""
  } · <span class="rev">Rev. ${args.rev}</span> · MoM Focus Azioni</p>${buildTable(
    args.items,
    true
  )}</body></html>`

  const win = window.open("", "_blank")
  if (!win) return
  win.document.write(html)
  win.document.close()
  win.focus()
  win.setTimeout(() => {
    win.print()
  }, 250)
}
