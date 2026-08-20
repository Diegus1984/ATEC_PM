// Stampa / PDF del Gantt milestone. Porting fedele della funzione `printGantt`
// del prototipo (Gestione_Commesse_V31.html): genera un documento HTML
// autoconsistente in una nuova finestra, con la timeline auto-scalata per far
// rientrare l'intero diagramma in una pagina A3 orizzontale, e ne avvia la stampa.

import type { Milestone } from "@/lib/api/types"
import {
  addDays,
  diffDays,
  mondayOf,
  startOfDay,
} from "@/features/risorse/planner-logic"
import { isoWeek, weekLabel, weekTot, avgAvanz } from "@/features/milestones/milestone-utils"
import { isoToDate } from "@/lib/date-iso"
import { printHtml } from "@/lib/print-template"

// Sigle giorno settimana con lunedì in testa (dn = (getDay()+6)%7).
const DOW_MON = ["L", "M", "M", "G", "V", "S", "D"] as const
// Mesi abbreviati per le bande mensili della testata.
const MONTHS_SHORT = [
  "GEN", "FEB", "MAR", "APR", "MAG", "GIU",
  "LUG", "AGO", "SET", "OTT", "NOV", "DIC",
] as const

const ITALIAN_DAYS = ["Domenica", "Lunedì", "Martedì", "Mercoledì", "Giovedì", "Venerdì", "Sabato"] as const
const ITALIAN_MONTHS = [
  "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno",
  "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"
] as const

// Festività fisse (allineate a planner-logic) per la campitura dei giorni festivi.
const FIXED_HOLIDAYS: ReadonlyArray<[number, number]> = [
  [1, 1], [1, 6], [4, 25], [5, 1], [6, 2],
  [8, 15], [11, 1], [12, 8], [12, 25], [12, 26],
]

function isHolidayDay(d: Date): boolean {
  return FIXED_HOLIDAYS.some(([m, day]) => d.getMonth() + 1 === m && d.getDate() === day)
}

function escapeHtml(value: string | null | undefined): string {
  if (!value) return ""
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;")
}

function sundayOf(d: Date): Date {
  return addDays(mondayOf(d), 6)
}

/** Data breve gg/mm/aa per le colonne del pannello (compatta per la stampa). */
function fmtShort(iso: string | null): string {
  const d = isoToDate(iso)
  if (!d) return ""
  const dd = String(d.getDate()).padStart(2, "0")
  const mm = String(d.getMonth() + 1).padStart(2, "0")
  const yy = String(d.getFullYear()).slice(-2)
  return `${dd}/${mm}/${yy}`
}

function fmtItShort(iso: string | null): string {
  const d = isoToDate(iso)
  if (!d) return ""
  const dd = String(d.getDate()).padStart(2, "0")
  const mm = String(d.getMonth() + 1).padStart(2, "0")
  const yyyy = String(d.getFullYear())
  return `${dd}/${mm}/${yyyy}`
}

interface PrintColumn {
  key: string
  th: string
  width: number
  cell: (m: Milestone, nr: number) => string
}

/**
 * Numero di riga da stampare = posizione nell'elenco COMPLETO della commessa, esattamente
 * come la colonna «#» della griglia a video.
 *
 * Prima si usava la posizione fra le sole righe stampate: bastava nascondere una riga dal
 * Gantt perché la stampa ripartisse da 1..N e non fosse più confrontabile con la griglia.
 * Il prototipo conserva l'indice originale (`c.milestones.map((m,i)=>…).filter(…)`).
 */
function rowNumberMap(all: Milestone[]): Map<number, number> {
  const map = new Map<number, number>()
  all.forEach((m, i) => map.set(m.id, i + 1))
  return map
}

// Definizione colonne del pannello sinistro (id allineati a SIDEBAR_COLUMNS del Gantt).
const ALL_COLUMNS: PrintColumn[] = [
  { key: "nr", th: "Nr", width: 26, cell: (_m, nr) => `<td class="ix">${nr}</td>` },
  {
    key: "descrizione",
    th: "Attività / Descrizione",
    width: 200,
    cell: (m) =>
      `<td class="ds ${m.avanzamento === 100 ? "dn" : ""} ${m.evidenza ? "hl" : ""}">${escapeHtml(m.descrizione || "—")}</td>`,
  },
  { key: "wInizio", th: "W.\nInizio", width: 42, cell: (m) => `<td class="ce">${weekLabel(m.dataInizio) || "—"}</td>` },
  { key: "dataInizio", th: "Data\nInizio", width: 60, cell: (m) => `<td class="ce">${fmtShort(m.dataInizio) || "—"}</td>` },
  { key: "wFine", th: "W.\nFine", width: 42, cell: (m) => `<td class="ce">${weekLabel(m.dataFine) || "—"}</td>` },
  { key: "dataFine", th: "Data\nFine", width: 60, cell: (m) => `<td class="ce">${fmtShort(m.dataFine) || "—"}</td>` },
  {
    key: "wTot",
    th: "W.\nTot",
    width: 40,
    cell: (m) => {
      const t = weekTot(m.dataInizio, m.dataFine)
      return `<td class="ce">${t === "" ? "—" : t}</td>`
    },
  },
  {
    key: "avanzamento",
    th: "Avanz.",
    width: 50,
    cell: (m) => {
      const av = typeof m.avanzamento === "number" ? m.avanzamento : null
      return `<td class="ce">${av == null ? "—" : av + "%"}</td>`
    },
  },
  { key: "note", th: "Note", width: 130, cell: (m) => `<td class="nt">${escapeHtml(m.note || "")}</td>` },
]

export interface PrintMilestoneGanttOptions {
  /** Codice commessa (testata documento). */
  projectCode?: string
  /** Descrizione/titolo commessa (testata documento). */
  projectTitle?: string
  /** Milestone già filtrate: attive e visibili nell'ordine di visualizzazione. */
  milestones: Milestone[]
  /** Elenco COMPLETO della commessa nell'ordine della griglia: serve solo a numerare le righe
   *  con lo stesso «Nr» che si vede a video (vedi {@link rowNumberMap}). */
  allMilestones: Milestone[]
  /** Id colonne del pannello sinistro attualmente visibili (sottoinsieme di ALL_COLUMNS). */
  visibleColumnIds: string[]
  /** Se mostrare la timeline (parte destra) — normalmente sempre true per il Gantt. */
  showTimeline?: boolean
  /** Filtro data «Dal» (yyyy-MM-dd) per limitare l'intervallo rappresentato. */
  filterFrom?: string | null
  /** Filtro data «Al» (yyyy-MM-dd) per limitare l'intervallo rappresentato. */
  filterTo?: string | null
  /** Callback opzionale per notificare errori all'utente (es. popup bloccati). */
  onError?: (message: string) => void
}

/**
 * Genera e stampa il Gantt milestone su una pagina A3 orizzontale, replicando la
 * resa del prototipo: pannello colonne a sinistra + timeline (mesi/settimane/giorni)
 * con barre e riempimento avanzamento a destra, il tutto scalato per rientrare
 * nella pagina.
 */
export function printMilestoneGantt(opts: PrintMilestoneGanttOptions): void {
  const {
    projectCode,
    projectTitle,
    milestones,
    allMilestones,
    visibleColumnIds,
    showTimeline = true,
    filterFrom,
    filterTo,
    onError,
  } = opts

  const rowNr = rowNumberMap(allMilestones)

  const fail = (msg: string) => {
    if (onError) onError(msg)
  }

  const timelineVisible = showTimeline
  const cols = ALL_COLUMNS.filter((c) => visibleColumnIds.includes(c.key))
  if (cols.length === 0 && !timelineVisible) {
    fail("Nessuna colonna da stampare")
    return
  }

  // Intervallo temporale dalle sole righe visibili (con eventuali filtri Dal/Al).
  const dates: string[] = []
  milestones.forEach((m) => {
    if (m.dataInizio) dates.push(m.dataInizio.slice(0, 10))
    if (m.dataFine) dates.push(m.dataFine.slice(0, 10))
  })
  dates.sort()
  if (dates.length === 0 && !filterFrom && !filterTo) {
    fail("Nessuna data da rappresentare")
    return
  }

  const minIso = filterFrom || dates[0] || null
  const maxIso = filterTo || dates[dates.length - 1] || null
  if (!minIso || !maxIso) {
    fail("Nessuna data da rappresentare")
    return
  }

  let start = mondayOf(isoToDate(minIso)!)
  // La settimana in più serve solo quando la fine è dedotta dalle attività: se «Al» l'ha
  // scelto l'utente, la finestra si chiude alla domenica di quella data.
  let end = filterTo
    ? sundayOf(isoToDate(maxIso)!)
    : sundayOf(addDays(isoToDate(maxIso)!, 7))
  const today = startOfDay(new Date())
  // Include sempre oggi nell'intervallo, salvo quando escluso da un filtro esplicito.
  if (!filterFrom && today < start) start = mondayOf(today)
  if (!filterTo && today > end) end = sundayOf(today)
  if (end < start) end = sundayOf(start)

  const totalDays = diffDays(end, start) + 1
  const days: Date[] = []
  for (let i = 0; i < totalDays; i++) days.push(addDays(start, i))

  // Dimensionamento: pannello a larghezze fisse, timeline sullo spazio rimanente.
  const panelW = cols.reduce((s, c) => s + c.width, 0)
  const TARGET = 1490 // ~ A3 orizzontale, margini 10mm
  const timelineW = timelineVisible ? Math.max(300, TARGET - panelW) : 0
  let dw = 0
  let timeW = 0
  if (timelineVisible) {
    dw = Math.max(1.6, Math.min(18, timelineW / totalDays))
    timeW = Math.round(totalDays * dw)
  }
  const tableW = panelW + (timelineVisible ? timeW : 0)
  const colgroup =
    "<colgroup>" +
    cols.map((c) => `<col style="width:${c.width}px">`).join("") +
    (timelineVisible ? `<col style="width:${timeW}px">` : "") +
    "</colgroup>"

  // Bande testata: mesi, settimane, giorni.
  let months = ""
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
    months += `<div class="m" style="width:${span * dw}px">${MONTHS_SHORT[md.getMonth()]} ${md.getFullYear()}</div>`
    mi += span
  }

  let weeks = ""
  let wi = 0
  while (wi < days.length) {
    const wd = days[wi]
    let span = 0
    const wo = mondayOf(wd).getTime()
    while (wi + span < days.length && mondayOf(days[wi + span]).getTime() === wo) span++
    weeks += `<div class="w" style="width:${span * dw}px">W${String(isoWeek(wd)).padStart(2, "0")}</div>`
    wi += span
  }

  const todayTime = today.getTime()
  let dayHdr = ""
  days.forEach((d) => {
    const dn = (d.getDay() + 6) % 7
    const we = dn >= 5
    const t = d.getTime() === todayTime
    let lbl = ""
    if (dw >= 11) lbl = `<span class="dw">${DOW_MON[dn]}</span>${d.getDate()}`
    else if (dw >= 5.5) lbl = `${d.getDate()}`
    dayHdr += `<div class="dh ${we ? "we" : ""} ${t ? "t" : ""}" style="width:${dw}px">${lbl}</div>`
  })

  // Righe.
  let rows = ""
  milestones.forEach((m) => {
    const av = typeof m.avanzamento === "number" ? m.avanzamento : null
    const full = av === 100
    const tds = cols.map((c) => c.cell(m, rowNr.get(m.id) ?? 0)).join("")

    let timeCell = ""
    if (timelineVisible) {
      let cells = ""
      days.forEach((d) => {
        const dn = (d.getDay() + 6) % 7
        const we = dn >= 5
        const ho = isHolidayDay(d)
        const t = d.getTime() === todayTime
        cells += `<div class="d ${we ? "we" : ""} ${ho ? "ho" : ""} ${t ? "t" : ""}" style="width:${dw}px"></div>`
      })
      let bar = ""
      if (m.dataInizio && m.dataFine) {
        const bs = startOfDay(isoToDate(m.dataInizio)!)
        const be = startOfDay(isoToDate(m.dataFine)!)
        const off = diffDays(bs, start)
        const len = diffDays(be, bs) + 1
        if (len > 0 && off + len > 0 && off < totalDays) {
          const left = Math.max(0, off) * dw
          const width = Math.max(
            dw * 0.8,
            (Math.min(totalDays, off + len) - Math.max(0, off)) * dw - 1
          )
          // Barra piena, senza riempimento proporzionale all'avanzamento: sulla barra
          // non deve comparire nessun riferimento alla percentuale (resta la colonna
          // «Avanz.» della tabella). Il verde è lo stato «completata», non una quota.
          bar = `<div class="bar ${full ? "done" : ""}" style="left:${left}px;width:${width}px"></div>`
        }
      }
      timeCell = `<td class="tl"><div class="trow" style="width:${timeW}px">${cells}<div class="bt">${bar}</div></div></td>`
    }
    rows += `<tr>${tds}${timeCell}</tr>`
  })

  const headCells =
    cols.map((c) => `<th>${c.th}</th>`).join("") +
    (timelineVisible
      ? `<th class="tl"><div class="bands"><div class="mrow" style="width:${timeW}px">${months}</div><div class="wrow" style="width:${timeW}px">${weeks}</div><div class="drow" style="width:${timeW}px">${dayHdr}</div></div></th>`
      : "")

  // Titolo in due versioni: testo semplice (fallback) e HTML con il codice evidenziato.
  const title = projectCode
    ? `${projectCode}${projectTitle ? " — " + projectTitle : ""}`
    : "Milestones"
  const titleHtml = projectCode
    ? `<span class="code">${escapeHtml(projectCode)}</span>${projectTitle ? " — " + escapeHtml(projectTitle) : ""}`
    : "Milestones"

  const customStyles = `
    table{border-collapse:collapse; width:${tableW}px; table-layout:fixed} th,td{border:1px solid #D9E1EC; padding:2px 5px; font-size:9px; vertical-align:middle}
    th{background:#EAF1F9; color:#5A6B7A; text-transform:uppercase; font-size:8px; white-space:pre-line; vertical-align:bottom}
    td.ix{text-align:right;color:#9AA7B4;font-family:'JetBrains Mono',monospace} td.ds{font-weight:600; white-space:normal; word-break:break-word; line-height:1.2} td.ds.dn{color:#3E8E63} td.ds.hl{color:#8E1B2E;font-weight:700} td.ce{text-align:center;font-family:'JetBrains Mono',monospace;white-space:nowrap} td.nt{color:#5A6B7A; white-space:normal; word-break:break-word; line-height:1.2}
    td.tl{padding:0; overflow:hidden} .trow{position:relative; height:18px; display:flex} .d{height:18px;border-right:1px solid #EEF2F7; flex:0 0 auto} .d.we{background:#E8EDF3} .d.ho{background:#F3E1E4} .d.t{background:#FFD966}
    .bt{position:absolute;inset:0} .bar{position:absolute;top:3px;height:12px;background:#2E75B6;border-radius:3px;box-shadow:0 1px 1px rgba(31,90,146,.25)} .bar.done{background:#3E8E63}
    .bands{display:flex;flex-direction:column} .mrow,.wrow{display:flex} .m{font-size:8px;font-weight:800;text-transform:uppercase;color:#2F6098;background:#EAF1F9;border-right:1px solid #C9D6E5;padding:2px 4px;overflow:hidden;white-space:nowrap;flex:0 0 auto}
    .w{font-family:'JetBrains Mono',monospace;font-size:7px;font-weight:700;color:#33639B;background:#E9F1FA;border-right:1px solid #DDE6F1;text-align:center;overflow:hidden;flex:0 0 auto}
    th.tl{padding:0; vertical-align:bottom}
    .drow{display:flex; border-bottom:1.5px solid #C9D6E5}
    .dh{font-family:'JetBrains Mono',monospace; font-size:6px; line-height:1.05; text-align:center; color:#5A6B7A; background:#fff; border-right:1px solid #EEF2F7; padding:1px 0; overflow:hidden; white-space:nowrap; flex:0 0 auto}
    .dh .dw{display:block; font-size:5px; font-weight:700; text-transform:uppercase; color:#788896}
    .dh.we{background:#E8EDF3} .dh.t{background:#FFD966; color:#27384A}
    tr:nth-child(even) td.ds, tr:nth-child(even) td.ce, tr:nth-child(even) td.ix, tr:nth-child(even) td.nt{background:#F6FAFE}
  `

  const contentHtml = `
    <table>
      ${colgroup}
      <thead><tr>${headCells}</tr></thead>
      <tbody>${rows}</tbody>
    </table>
  `

  // Testata ridotta al solo logo + commessa (#71): dal foglio spariscono il
  // sottotitolo «GANTT MILESTONES», i metadati (conteggio Milestones e la nota
  // «Settimane ISO 8601») e la legenda dei colori. Restano titolo, tabella e piè.
  printHtml({
    title,
    titleHtml,
    contentHtml,
    orientation: "landscape",
    paperSize: "A3",
    customStyles,
  })
}

export interface PrintMilestoneTableOptions {
  projectCode?: string
  projectTitle?: string
  projectSub?: string
  /** Righe effettivamente stampate (attive e visibili). */
  milestones: Milestone[]
  /** Elenco COMPLETO della commessa, usato solo per numerare le righe come nella griglia. */
  allMilestones: Milestone[]
  onError?: (message: string) => void
}

/**
 * Genera e stampa la tabella delle Milestone su una pagina A4 orizzontale,
 * replicando fedelmente il layout e lo stile del prototipo (Gestione_Commesse_V31.html).
 */
export function printMilestoneTable(opts: PrintMilestoneTableOptions): void {
  const {
    projectCode,
    projectTitle,
    projectSub,
    milestones,
    allMilestones,
  } = opts

  const now = new Date()
  const rowNr = rowNumberMap(allMilestones)
  // Conteggio, media e periodo si riferiscono alle SOLE righe stampate (come il prototipo):
  // prima la media arrivava da `allMilestones`, quindi comprendeva anche le righe nascoste
  // dal Gantt e il valore in testata non corrispondeva al foglio.
  const avg = avgAvanz(milestones)

  // Calcolo intervallo date pianificato
  const dates: string[] = []
  milestones.forEach((m) => {
    if (m.dataInizio) dates.push(m.dataInizio.slice(0, 10))
    if (m.dataFine) dates.push(m.dataFine.slice(0, 10))
  })
  dates.sort()
  const rangeTxt = dates.length
    ? `${fmtItShort(dates[0])} – ${fmtItShort(dates[dates.length - 1])}`
    : "—"

  const rows = milestones
    .map((m) => {
      const av = typeof m.avanzamento === "number" ? m.avanzamento : null
      const full = av === 100
      const tot = weekTot(m.dataInizio, m.dataFine)
      const barFill = av || 0
      return `<tr>
        <td class="ix">${rowNr.get(m.id) ?? 0}</td>
        <td class="ds ${full ? "dn" : ""} ${m.evidenza ? "hl" : ""}">${escapeHtml(m.descrizione || "—")}</td>
        <td class="ce">${weekLabel(m.dataInizio) || "—"}</td>
        <td class="ce">${fmtItShort(m.dataInizio) || "—"}</td>
        <td class="ce">${weekLabel(m.dataFine) || "—"}</td>
        <td class="ce">${fmtItShort(m.dataFine) || "—"}</td>
        <td class="ce">${tot === "" ? "—" : tot}</td>
        <td class="av">
          <div class="pb ${full ? "f" : ""}"><span style="width:${barFill}%"></span></div>
          <em class="${full ? "dn" : ""}">${av == null ? "—" : av + "%"}</em>
        </td>
        <td class="nt">${escapeHtml(m.note || "")}</td>
      </tr>`
    })
    .join("")

  const docTitle = projectCode
    ? `${projectCode}${projectTitle ? " — " + projectTitle : ""}`
    : "Milestones di Commessa"
  const docTitleHtml = projectCode
    ? `<span class="code">${escapeHtml(projectCode)}</span>${
        projectTitle ? " — " + escapeHtml(projectTitle) : ""
      }`
    : "Milestones di Commessa"

  const dayName = ITALIAN_DAYS[now.getDay()]
  const dateStr = `${now.getDate()} ${ITALIAN_MONTHS[now.getMonth()]} ${now.getFullYear()}`

  const customStyles = `
    table{width:100%; border-collapse:collapse}
    th{background:#EAF1F9; border:1px solid #D1DFEC; padding:6px 7px; font-size:9px; text-transform:uppercase; letter-spacing:.03em; color:#5A6B7A; text-align:left}
    td{border:1px solid #E4ECF5; padding:5px 7px; vertical-align:middle}
    td.ix{ text-align:right; color:#A5B3C2; font-family:'JetBrains Mono',monospace; width:24px}
    td.ce{ text-align:center; font-family:'JetBrains Mono',monospace; white-space:nowrap }
    td.ds{ font-weight:600; width:24% }
    td.ds.dn{ color:#3E7A52 }
    td.ds.hl{ color:#8E1B2E; font-weight:700 }
    td.nt{ color:#5A6B7A; width:18% }
    td.av{ width:120px }
    .pb{height:9px; border-radius:5px; background:#EAF1F9; border:1px solid #D1DFEC; overflow:hidden; display:inline-block; width:74px; vertical-align:middle}
    .pb span{display:block; height:100%; background:#9CC1E6}
    .pb.f span{background:#6FB287}
    td.av em{font-style:normal; font-family:'JetBrains Mono',monospace; font-weight:600; margin-left:6px; font-size:10px}
    td.av em.dn{color:#3E7A52; font-weight:700}
    tr:nth-child(even) td{background:#F6FAFE}
  `

  const contentHtml = `
    <table>
      <thead><tr><th>#</th><th>Attività / Descrizione</th><th>W. Inizio</th><th>Data Inizio</th><th>W. Fine</th><th>Data Fine</th><th>W. Tot</th><th>Avanzamento</th><th>Note</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
  `

  printHtml({
    title: docTitle,
    titleHtml: docTitleHtml,
    subtitle: projectSub ? `Milestones di Commessa — ${projectSub}` : "Milestones di Commessa",
    meta: [
      { label: "Milestones", value: milestones.length },
      { label: "Avanzamento medio", value: avg == null ? "—" : avg + "%" },
      { label: "Periodo pianificato", value: rangeTxt },
      { label: "Data documento", value: `${dayName} ${dateStr}` }
    ],
    contentHtml,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}
