// Stampa/PDF delle viste priorità della Check list (finestra HTML + window.print()):
// dal dialogo di stampa del browser si sceglie "Salva come PDF".

import { escapeHtml, printHtml } from "@/lib/print-template"
import type { ChecklistItem, ChecklistStatus } from "@/lib/api/types"
import type { ChecklistRowContainer } from "@/features/checklist/checklist-shared"
import { daysFromToday, priorityMeta, sortChecklistItems } from "@/features/checklist/checklist-utils"
import { formatDateFull } from "@/lib/date-iso"

export interface ChecklistPrintRow {
  item: ChecklistItem
  container: ChecklistRowContainer
}

const STATUS_LABEL: Record<ChecklistStatus, string> = {
  OPEN: "Aperta",
  STANDBY: "Standby",
  CLOSED: "Chiusa",
}

/** Date a 4 cifre: sui documenti stampati non si usa il formato breve della griglia. */
function printDate(value: string | null): string {
  return formatDateFull(value) || "—"
}

function timeLeftLabel(dueDate: string | null): string {
  const n = daysFromToday(dueDate)
  if (n === null) return "—"
  if (n === 0) return "Oggi"
  return n < 0 ? `${n} gg` : `+${n} gg`
}

/** Commessa/gruppo su più righe, come la cella a video. */
function containerHtml(container: ChecklistRowContainer): string {
  const lines: string[] = []
  if (container.code) lines.push(`<b>${escapeHtml(container.code)}</b>`)
  if (container.customer) lines.push(escapeHtml(container.customer))
  if (container.title) lines.push(escapeHtml(container.title))
  if (lines.length === 0) lines.push(escapeHtml(container.label))
  return lines.join("<br>")
}

const PRINT_STYLES = `
  table { border-collapse: collapse; width: 100% }
  th, td { border: 0.5px solid #C6D3E0; padding: 4px 6px; text-align: left; vertical-align: top }
  th { background: #EAF1F9; font-size: 9px; text-transform: uppercase; letter-spacing: .05em; color: #4A5C6E }
  td { font-size: 10px }
  tr { page-break-inside: avoid }
  thead { display: table-header-group }
  .num { width: 26px; text-align: center; color: #788896 }
  .cont { width: 130px }
  .date, .left, .stat { white-space: nowrap }
  .critical td { background: #FDECEC }
  .critical .flag { color: #B42318; font-weight: 700 }
  .overdue { color: #B42318; font-weight: 700 }
  .today { color: #B54708; font-weight: 700 }
  .closed td { color: #8A97A4; text-decoration: line-through }
  .empty { padding: 14px; text-align: center; color: #788896; border: 1px dashed #C6D3E0 }
`

/**
 * Stampa una tabella di attività (usata dalle due sezioni P0: del giorno / critiche).
 * Le righe sono ordinate come a video (scadenza crescente, critiche in testa, chiuse in fondo).
 */
export function printChecklistRows({
  title,
  subtitle,
  rows,
}: {
  title: string
  subtitle: string
  rows: ChecklistPrintRow[]
}): void {
  const containerById = new Map(rows.map((r) => [r.item.id, r.container]))
  const sorted = sortChecklistItems(
    rows.map((r) => r.item),
    "date"
  )

  const body = sorted
    .map((item, index) => {
      const container = containerById.get(item.id)
      const days = daysFromToday(item.dueDate)
      const leftClass = days === null ? "" : days < 0 ? " overdue" : days === 0 ? " today" : ""
      const rowClass = [
        item.isCritical ? "critical" : "",
        item.status === "CLOSED" ? "closed" : "",
      ]
        .filter(Boolean)
        .join(" ")
      return `<tr${rowClass ? ` class="${rowClass}"` : ""}>
        <td class="num">${index + 1}</td>
        <td class="cont">${container ? containerHtml(container) : "—"}</td>
        <td>${escapeHtml(item.description)}${item.isCritical ? ' <span class="flag">· CRITICA</span>' : ""}</td>
        <td class="date">${printDate(item.dueDate)}</td>
        <td class="left${leftClass}">${timeLeftLabel(item.dueDate)}</td>
        <td class="stat">${STATUS_LABEL[item.status]}</td>
      </tr>`
    })
    .join("")

  const contentHtml =
    sorted.length === 0
      ? `<p class="empty">Nessuna attività da stampare.</p>`
      : `<table>
          <thead>
            <tr>
              <th class="num">#</th>
              <th class="cont">Commessa / Gruppo</th>
              <th>Attività</th>
              <th class="date">Scadenza</th>
              <th class="left">Tempo residuo</th>
              <th class="stat">Stato</th>
            </tr>
          </thead>
          <tbody>${body}</tbody>
        </table>`

  const critical = sorted.filter((i) => i.isCritical).length
  const closed = sorted.filter((i) => i.status === "CLOSED").length

  printHtml({
    title,
    subtitle: `Check list · Priorità ${priorityMeta(0).code} — ${priorityMeta(0).name}`,
    meta: [
      { label: "Selezione", value: subtitle },
      { label: "Attività", value: sorted.length },
      { label: "Critiche", value: critical },
      { label: "Chiuse", value: closed },
    ],
    contentHtml,
    orientation: "portrait",
    paperSize: "A4",
    customStyles: PRINT_STYLES,
  })
}
