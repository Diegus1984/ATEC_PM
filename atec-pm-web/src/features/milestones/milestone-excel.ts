// Export Excel del Gantt milestone in SpreadsheetML 2003 (.xls leggibile da Excel).
//
// Porting di `exportViewExcel` del prototipo Gestione_Commesse_V32. Perché SpreadsheetML
// e non una tabella HTML rinominata come fanno gli altri export .xls dell'app: qui servono
// celle COLORATE (le barre del Gantt sono celle campite, una per giorno), bande mesi/settimane
// mergiate e il blocco riquadri sul pannello di sinistra — cose che un .html rinominato
// non porta dentro Excel.

import type { Milestone } from "@/lib/api/types"
import { downloadFile, fileStamp, safeFileName } from "@/lib/download"
import {
  addDays,
  diffDays,
  mondayOf,
  startOfDay,
  toIso,
} from "@/features/risorse/planner-logic"
import { isoWeek, weekLabel, weekTot } from "@/features/milestones/milestone-utils"
import { isoToDate } from "@/lib/date-iso"

const MESI = [
  "GENNAIO", "FEBBRAIO", "MARZO", "APRILE", "MAGGIO", "GIUGNO",
  "LUGLIO", "AGOSTO", "SETTEMBRE", "OTTOBRE", "NOVEMBRE", "DICEMBRE",
]
const DOW = ["Lu", "Ma", "Me", "Gi", "Ve", "Sa", "Do"]

/** Larghezza (in punti) delle colonne del pannello, per id colonna del Gantt. */
const PANEL_WIDTH: Record<string, number> = {
  nr: 24,
  descrizione: 200,
  wInizio: 38,
  dataInizio: 62,
  wFine: 38,
  dataFine: 62,
  wTot: 34,
  avanzamento: 96,
  note: 120,
}

function esc(value: unknown): string {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
}

function cell(value: unknown, style?: string, type: "String" | "Number" = "String"): string {
  const s = style ? ` ss:StyleID="${style}"` : ""
  return `<Cell${s}><Data ss:Type="${type}">${esc(value)}</Data></Cell>`
}

function mergedCell(value: unknown, style: string, mergeAcross: number): string {
  const m = mergeAcross > 0 ? ` ss:MergeAcross="${mergeAcross}"` : ""
  return `<Cell ss:StyleID="${style}"${m}><Data ss:Type="String">${esc(value)}</Data></Cell>`
}

function fmtIt(iso: string | null | undefined): string {
  if (!iso) return ""
  const p = iso.slice(0, 10).split("-")
  return p.length === 3 ? `${p[2]}/${p[1]}/${p[0]}` : ""
}

function sundayOf(d: Date): Date {
  return addDays(mondayOf(d), 6)
}

export interface ExportGanttExcelOptions {
  projectCode?: string
  projectTitle?: string
  /** Nome della composizione esportata (es. «Vista Cliente»), finisce in testata e nel nome file. */
  viewName: string
  /** Righe da esportare, già filtrate secondo la composizione. */
  milestones: Milestone[]
  /** Numero di riga da stampare per id: la stessa numerazione della griglia (buchi inclusi). */
  rowNumbers: Map<number, number>
  /** Colonne del pannello sinistro visibili, nell'ordine, con etichetta. */
  panelColumns: { id: string; label: string }[]
  /** Se includere il diagramma (una colonna per giorno). */
  showTimeline: boolean
  filterFrom?: string | null
  filterTo?: string | null
  onError?: (message: string) => void
}

/**
 * Genera e scarica il workbook. Restituisce `false` (e chiama `onError`) se non c'è
 * nulla da esportare, così il chiamante può avvisare invece di scaricare un file vuoto.
 */
export function exportGanttViewExcel(opts: ExportGanttExcelOptions): boolean {
  const {
    projectCode,
    projectTitle,
    viewName,
    milestones,
    rowNumbers,
    panelColumns,
    showTimeline,
    filterFrom,
    filterTo,
    onError,
  } = opts

  const fail = (msg: string) => {
    onError?.(msg)
    return false
  }

  if (milestones.length === 0) return fail("Nessuna riga da esportare")
  if (panelColumns.length === 0 && !showTimeline)
    return fail("Nessuna colonna da esportare: accendi almeno una colonna o il diagramma")

  // Intervallo temporale: dalle sole righe esportate, poi ristretto dal filtro Dal/Al.
  const dates: string[] = []
  milestones.forEach((m) => {
    if (m.dataInizio) dates.push(m.dataInizio.slice(0, 10))
    if (m.dataFine) dates.push(m.dataFine.slice(0, 10))
  })
  dates.sort()

  let days: Date[] = []
  let start = startOfDay(new Date())
  if (showTimeline) {
    if (dates.length === 0) return fail("Nessuna data da esportare")
    start = mondayOf(isoToDate(dates[0])!)
    let end = sundayOf(addDays(isoToDate(dates[dates.length - 1])!, 7))
    const today = startOfDay(new Date())
    if (today < start) start = mondayOf(today)
    if (today > end) end = sundayOf(today)
    if (filterFrom) {
      const f = mondayOf(isoToDate(filterFrom)!)
      if (f > start) start = f
    }
    if (filterTo) {
      const t = sundayOf(isoToDate(filterTo)!)
      if (t < end) end = t
    }
    if (end < start) end = sundayOf(start)
    const total = diffDays(end, start) + 1
    days = Array.from({ length: total }, (_, i) => addDays(start, i))
  }

  const todayIso = toIso(startOfDay(new Date()))
  const nPanel = panelColumns.length
  const totalCols = nPanel + (showTimeline ? days.length : 0)

  // ── Larghezze colonne ──
  let colsXml = ""
  panelColumns.forEach((c) => {
    colsXml += `<Column ss:Width="${PANEL_WIDTH[c.id] ?? 60}"/>`
  })
  if (showTimeline) days.forEach(() => (colsXml += `<Column ss:Width="13"/>`))

  // ── Testata ──
  const titolo = `Diagramma di Gantt — ${projectCode ?? ""}${projectTitle ? " — " + projectTitle : ""}`
  const periodo = showTimeline
    ? `Periodo ${fmtIt(toIso(days[0]))}–${fmtIt(toIso(days[days.length - 1]))} · `
    : ""
  let rowsXml =
    `<Row><Cell ss:StyleID="sTitle" ss:MergeAcross="${Math.max(0, totalCols - 1)}">` +
    `<Data ss:Type="String">${esc(titolo)}</Data></Cell></Row>` +
    `<Row><Cell ss:StyleID="sMeta" ss:MergeAcross="${Math.max(0, totalCols - 1)}">` +
    `<Data ss:Type="String">${esc(`${viewName} · ${periodo}Documento del ${fmtIt(todayIso)}`)}</Data></Cell></Row>` +
    `<Row></Row>`

  // ── Bande mesi e settimane ──
  if (showTimeline) {
    let r = `<Row ss:Height="16">`
    for (let i = 0; i < nPanel; i++)
      r += `<Cell ss:StyleID="sHdrBlank"><Data ss:Type="String"></Data></Cell>`
    let mi = 0
    while (mi < days.length) {
      const md = days[mi]
      let span = 0
      while (
        mi + span < days.length &&
        days[mi + span].getMonth() === md.getMonth() &&
        days[mi + span].getFullYear() === md.getFullYear()
      )
        span++
      r += mergedCell(`${MESI[md.getMonth()]} ${md.getFullYear()}`, "sMonth", span - 1)
      mi += span
    }
    rowsXml += r + `</Row>`

    r = `<Row ss:Height="13">`
    for (let i = 0; i < nPanel; i++)
      r += `<Cell ss:StyleID="sHdrBlank"><Data ss:Type="String"></Data></Cell>`
    let wi = 0
    while (wi < days.length) {
      const wd = days[wi]
      let span = 0
      const monday = mondayOf(wd).getTime()
      while (wi + span < days.length && mondayOf(days[wi + span]).getTime() === monday) span++
      r += mergedCell(`W${String(isoWeek(wd)).padStart(2, "0")}`, "sWeek", span - 1)
      wi += span
    }
    rowsXml += r + `</Row>`
  }

  // ── Intestazioni colonne + giorni ──
  let hr = `<Row ss:Height="26">`
  panelColumns.forEach((c) => (hr += cell(c.label, "sHdr")))
  if (showTimeline)
    days.forEach((d) => {
      const dn = (d.getDay() + 6) % 7
      const we = dn >= 5
      const isToday = toIso(d) === todayIso
      hr += cell(`${DOW[dn]} ${d.getDate()}`, isToday ? "sDayT" : we ? "sDayWe" : "sDay")
    })
  rowsXml += hr + `</Row>`

  // ── Righe attività ──
  milestones.forEach((m) => {
    const av = typeof m.avanzamento === "number" ? m.avanzamento : null
    const full = av === 100
    let r = `<Row ss:Height="16">`

    panelColumns.forEach((c) => {
      switch (c.id) {
        case "nr":
          r += cell(rowNumbers.get(m.id) ?? 0, "pC", "Number")
          break
        case "descrizione":
          r += cell(m.descrizione || "—", full ? "pDescDone" : "pDesc")
          break
        case "wInizio":
          r += cell(weekLabel(m.dataInizio) || "—", "pC")
          break
        case "dataInizio":
          r += cell(fmtIt(m.dataInizio) || "—", "pC")
          break
        case "wFine":
          r += cell(weekLabel(m.dataFine) || "—", "pC")
          break
        case "dataFine":
          r += cell(fmtIt(m.dataFine) || "—", "pC")
          break
        case "wTot": {
          const t = weekTot(m.dataInizio, m.dataFine)
          r += t === "" ? cell("—", "pC") : cell(t, "pC", "Number")
          break
        }
        case "avanzamento": {
          // Barra a blocchi: Excel non ha barre di avanzamento nelle celle, i blocchi
          // pieni/vuoti si leggono uguale e restano testo copiabile.
          const filled = Math.round((av ?? 0) / 10)
          const blocks = "█".repeat(filled) + "░".repeat(10 - filled)
          r += cell(blocks + (av == null ? "" : `  ${av}%`), full ? "pAvDone" : "pAv")
          break
        }
        case "note":
          r += cell(m.note || "", "pNote")
          break
        default:
          r += cell("", "pC")
      }
    })

    if (showTimeline) {
      // Confine dell'avanzamento dentro la barra: la parte già fatta è più scura.
      let doneEnd: Date | null = null
      const bs = m.dataInizio ? startOfDay(isoToDate(m.dataInizio)!) : null
      const be = m.dataFine ? startOfDay(isoToDate(m.dataFine)!) : null
      if (bs && be && av && av > 0 && !full) {
        const len = diffDays(be, bs) + 1
        doneEnd = addDays(bs, Math.max(0, Math.round((len * av) / 100) - 1))
      }
      days.forEach((d) => {
        const dn = (d.getDay() + 6) % 7
        const we = dn >= 5
        const isToday = toIso(d) === todayIso
        let style = isToday ? "cToday" : we ? "cWe" : "cEmpty"
        if (bs && be && d >= bs && d <= be) {
          style = full ? "cDone" : doneEnd && d <= doneEnd ? "cBarDark" : "cBar"
        }
        r += `<Cell ss:StyleID="${style}"><Data ss:Type="String"></Data></Cell>`
      })
    }

    rowsXml += r + `</Row>`
  })

  const styles = `
   <Style ss:ID="Default" ss:Name="Normal"><Alignment ss:Vertical="Center"/><Font ss:FontName="Calibri" ss:Size="9" ss:Color="#27384A"/></Style>
   <Style ss:ID="sTitle"><Font ss:Bold="1" ss:Size="13" ss:Color="#243340"/></Style>
   <Style ss:ID="sMeta"><Font ss:Size="9" ss:Color="#5A6B7A"/></Style>
   <Style ss:ID="sHdr"><Font ss:Bold="1" ss:Size="8" ss:Color="#5A6B7A"/><Alignment ss:Horizontal="Left" ss:Vertical="Bottom" ss:WrapText="1"/><Interior ss:Color="#EAF1F9" ss:Pattern="Solid"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D1DFEC"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D1DFEC"/></Borders></Style>
   <Style ss:ID="sHdrBlank"><Interior ss:Color="#EAF1F9" ss:Pattern="Solid"/></Style>
   <Style ss:ID="sMonth"><Font ss:Bold="1" ss:Size="8" ss:Color="#2F6098"/><Alignment ss:Horizontal="Left"/><Interior ss:Color="#EAF1F9" ss:Pattern="Solid"/><Borders><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#C9D6E5"/></Borders></Style>
   <Style ss:ID="sWeek"><Font ss:Size="7" ss:Color="#33639B"/><Alignment ss:Horizontal="Center"/><Interior ss:Color="#E9F1FA" ss:Pattern="Solid"/><Borders><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#DDE6F1"/></Borders></Style>
   <Style ss:ID="sDay"><Font ss:Size="7" ss:Color="#788896"/><Alignment ss:Horizontal="Center"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#D1DFEC"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#EEF2F7"/></Borders></Style>
   <Style ss:ID="sDayWe"><Font ss:Size="7" ss:Color="#788896"/><Alignment ss:Horizontal="Center"/><Interior ss:Color="#E8EDF3" ss:Pattern="Solid"/><Borders><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#EEF2F7"/></Borders></Style>
   <Style ss:ID="sDayT"><Font ss:Size="7" ss:Bold="1" ss:Color="#C26A3C"/><Alignment ss:Horizontal="Center"/><Interior ss:Color="#FFD966" ss:Pattern="Solid"/></Style>
   <Style ss:ID="pC"><Alignment ss:Horizontal="Center"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="pDesc"><Font ss:Bold="1"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="pDescDone"><Font ss:Bold="1" ss:Color="#3E8E63"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="pAv"><Font ss:Color="#2E75B6"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="pAvDone"><Font ss:Color="#3E8E63" ss:Bold="1"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="pNote"><Font ss:Color="#5A6B7A"/><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#E4ECF5"/></Borders></Style>
   <Style ss:ID="cEmpty"><Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Color="#EEF2F7"/><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#EEF2F7"/></Borders></Style>
   <Style ss:ID="cWe"><Interior ss:Color="#E8EDF3" ss:Pattern="Solid"/><Borders><Border ss:Position="Right" ss:LineStyle="Continuous" ss:Color="#EEF2F7"/></Borders></Style>
   <Style ss:ID="cToday"><Interior ss:Color="#FFD966" ss:Pattern="Solid"/></Style>
   <Style ss:ID="cBar"><Interior ss:Color="#2E75B6" ss:Pattern="Solid"/></Style>
   <Style ss:ID="cBarDark"><Interior ss:Color="#1F5A92" ss:Pattern="Solid"/></Style>
   <Style ss:ID="cDone"><Interior ss:Color="#3E8E63" ss:Pattern="Solid"/></Style>`

  // Il blocco riquadri tiene fermo il pannello di sinistra mentre si scorrono i giorni:
  // senza, un Gantt lungo diventa illeggibile appena si scorre.
  const freeze = showTimeline
    ? `<FreezePanes/><FrozenNoSplit/><SplitVertical>${nPanel}</SplitVertical>` +
      `<LeftColumnRightPane>${nPanel}</LeftColumnRightPane><ActivePane>1</ActivePane>`
    : ""

  const xml = `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
 xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
 <Styles>${styles}
 </Styles>
 <Worksheet ss:Name="Gantt">
  <Table>${colsXml}
${rowsXml}
  </Table>
  <WorksheetOptions xmlns="urn:schemas-microsoft-com:office:excel">
   ${freeze}
  </WorksheetOptions>
 </Worksheet>
</Workbook>`

  downloadFile(
    `Gantt_${safeFileName(projectCode || "commessa")}_${safeFileName(viewName)}_${fileStamp()}.xls`,
    xml,
    "application/vnd.ms-excel"
  )
  return true
}
