import type { MoMActionItem } from "@/lib/api/types"
import { formatDateFull, isoToDate } from "@/lib/date-iso"
import { WEEKDAYS_SHORT, isRedDay } from "@/lib/it-holidays"
import { printHtml, escapeHtml } from "@/lib/print-template"

import { isOverdue, todayIso } from "./mom-detail-shared"
import {
  MOM_CONDITION_STYLE,
  momConditionKey,
  priorityConditionKey,
  type MoMConditionKey,
} from "./mom-palette"

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

/** Condizione della riga secondo la matrice colori (vedi `mom-palette`). */
function conditionOf(item: MoMActionItem): MoMConditionKey {
  return momConditionKey({ ...item, isOverdue: isOverdue(item, todayIso()) })
}

function statoLabel(item: MoMActionItem): string {
  const key = conditionOf(item)
  return key === "p1" || key === "p2" || key === "p3"
    ? "Aperta"
    : MOM_CONDITION_STYLE[key].label
}

/** Sfondo riga di stampa/export: stesse tinte pastello del foglio. */
function rowBgHex(item: MoMActionItem): string | null {
  return MOM_CONDITION_STYLE[conditionOf(item)].hex
}

function responsibleText(item: MoMActionItem): string {
  const names =
    item.responsibleNames.length > 0
      ? item.responsibleNames
      : [item.resp1Name, item.resp2Name, item.resp3Name].filter(Boolean)
  return names.join(", ")
}

function formatDate(value: string | null): string {
  // Export documentale: gg/mm/aaaa dal helper di casa (regola: mai toLocaleDateString nudo).
  return formatDateFull(value)
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
  "Data chiusura",
  "Stato",
]

/**
 * Contenuto della colonna Priorità: la priorità P1–P3, oppure lo stato scelto
 * (Stand by / Close) quando è impostato — come nel foglio a video.
 */
function priorityCellKey(item: MoMActionItem): MoMConditionKey {
  if (item.status === "CLOSED") return "close"
  if (item.status === "STANDBY") return "standby"
  return priorityConditionKey(item.priorita)
}

function rowCells(item: MoMActionItem, index: number): string[] {
  return [
    String(index + 1),
    item.attivita,
    item.descrizione ?? "",
    item.azione ?? "",
    MOM_CONDITION_STYLE[priorityCellKey(item)].label,
    responsibleText(item),
    formatDate(item.dataCheck),
    formatDate(item.dataClose),
    statoLabel(item),
  ]
}

/**
 * Attività, Descrizione e Azione (colonne 1–3) sono campi da 40 caratteri con
 * riporto automatico a capo: in stampa mantengono la stessa larghezza del
 * foglio a video invece di allargarsi con il testo.
 */
const TEXT_COL_INDEXES = new Set([1, 2, 3])
const TEXT_COL_STYLE = ";width:40ch;max-width:40ch"

function buildTable(items: MoMActionItem[], withDow: boolean): string {
  const head = HEADERS.map(
    (header, index) =>
      `<th style="background:#CFE3F6;color:#2F6098;border:0.5px solid #A9C3DE;padding:4px 6px;font-weight:bold;text-align:left${
        TEXT_COL_INDEXES.has(index) ? TEXT_COL_STYLE : ""
      }">${escapeHtml(header)}</th>`
  ).join("")

  const body = items
    .map((item, index) => {
      const bg = rowBgHex(item)
      const style = bg ? ` style="background:${bg}"` : ""
      const pri = MOM_CONDITION_STYLE[priorityCellKey(item)]
      const cells = rowCells(item, index)
        .map((cell, cellIndex) => {
          let cellStyle =
            "border:0.5px solid #ccc;padding:3px 6px;vertical-align:top;white-space:pre-wrap"
          let html = escapeHtml(cell)
          if (TEXT_COL_INDEXES.has(cellIndex)) cellStyle += TEXT_COL_STYLE
          if (cellIndex === 4)
            cellStyle += `;background:${pri.hex};color:${pri.inkHex};text-align:center;font-weight:bold`
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
  const subtitle = `${escapeHtml(args.tipoBadge)}${
    args.meetingDate
      ? ` · Riunione: ${escapeHtml(formatDate(args.meetingDate))}`
      : ""
  } · Rev. ${args.rev} · MoM Focus Azioni`

  const customStyles = `
    table{page-break-inside:auto; border-collapse:collapse; width:100%}
    tr{page-break-inside:avoid}
    thead{display:table-header-group}
    td,th{border:0.5px solid #bbb;padding:3px 6px;text-align:left;vertical-align:top}
  `

  printHtml({
    title: args.title,
    subtitle,
    contentHtml: buildTable(args.items, true),
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}
