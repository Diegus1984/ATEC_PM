import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"

// Calcolo della Sintesi DDP, port delle Build* del client WPF (DdpSintesiPage.xaml.cs).
// Pure: ingredienti = righe DDP + causali (Conf. DDP) + aggregazioni (Aggregazioni DDP),
// con i fallback cablati quando A2..A8 non sono configurate.

export const DEFAULT_DELIVERED = ["CON", "COS", "DISP", "ASS", "MOD"]
export const DEFAULT_EXCL_MISSING = ["ANN", "SOSP", "SOST", "RAM"]

export interface BarRow {
  key: string
  label: string
  count: number
  pct: string
  fraction: number
  bg: string
  fg: string
}

export interface AvanzCard {
  label: string
  count: number
  pctLabel: string
  bg: string
  border: string
}

export interface Top10Row {
  item: DdpRowItem
  rank: number
  pctLabel: string
}

export interface MissingCell {
  text: string
  missing: boolean
}

export interface MissingRow {
  rowNo: number
  statoKey: string
  desc: string
  stato: MissingCell
  rif: MissingCell
  data: MissingCell
  dest: MissingCell
  costo: MissingCell
  flagColor: string
  missingLabel: string
}

export interface DdpKpis {
  totValue: number
  count: number
  datedCount: number
  overdue: number
  consegnato: number
  parziali: number
  finestra: string
}

export interface DdpSintesiModel {
  kpi: DdpKpis
  ripartizione: BarRow[]
  ripSub: string
  consegne: DdpRowItem[]
  consegneSub: string
  consegnato: DdpRowItem[]
  consegnatoSub: string
  top10: Top10Row[]
  destinazioni: BarRow[]
  destSub: string
  mancanti: MissingRow[]
  mancantiSub: string
  distinta: DdpRowItem[]
  avanzamento: AvanzCard[]
  avanzSub: string
  feedbackAcquisti: BarRow[]
  acqSub: string
  feedbackMagazzino: BarRow[]
  magSub: string
}

// ── Helper ──────────────────────────────────────────────────────

function todayIso(): string {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, "0")
  const day = String(now.getDate()).padStart(2, "0")
  return `${year}-${month}-${day}`
}

function dayOf(value: string | null): string | null {
  return value ? value.slice(0, 10) : null
}

function amount(row: DdpRowItem): number {
  return row.quantity * row.unitCost
}

function fmtDate(value: string | null): string {
  const day = dayOf(value)
  if (!day) return ""
  const [year, month, date] = day.split("-")
  return `${date}/${month}/${year}`
}

function pctLabel(fraction: number, total: number): string {
  // it-IT come il WPF (`{frac*100:0.#}%` → "12,3%"): virgola decimale, max 1 cifra.
  return total > 0
    ? `${(fraction * 100).toLocaleString("it-IT", {
        minimumFractionDigits: 0,
        maximumFractionDigits: 1,
      })}%`
    : "—"
}

function colorOf(hex: string | undefined, fallback: string): string {
  return hex && /^#[0-9a-fA-F]{6}$/.test(hex) ? hex : fallback
}

function lighten(hex: string, factor: number): string {
  const value = colorOf(hex, "#94A3B8").slice(1)
  const r = parseInt(value.slice(0, 2), 16)
  const g = parseInt(value.slice(2, 4), 16)
  const b = parseInt(value.slice(4, 6), 16)
  const mix = (channel: number) =>
    Math.round(channel + (255 - channel) * factor)
      .toString(16)
      .padStart(2, "0")
  return `#${mix(r)}${mix(g)}${mix(b)}`
}

function relativeLuminance(hex: string): number {
  const value = colorOf(hex, "#FFFFFF").slice(1)
  const channel = (component: number) => {
    const s = component / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  }
  const r = channel(parseInt(value.slice(0, 2), 16))
  const g = channel(parseInt(value.slice(2, 4), 16))
  const b = channel(parseInt(value.slice(4, 6), 16))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const la = relativeLuminance(a) + 0.05
  const lb = relativeLuminance(b) + 0.05
  return la > lb ? la / lb : lb / la
}

// Flag "dato mancante": rosso, o bianco quando il rosso sarebbe illeggibile sullo sfondo di stato.
function flagColor(bgHex: string | undefined): string {
  const bg = colorOf(bgHex, "#FFFFFF")
  return contrast("#FFFFFF", bg) > contrast("#C0392B", bg) ? "#FFFFFF" : "#C0392B"
}

// ── Costruzione ─────────────────────────────────────────────────

export function buildRipartizioneBars(
  counts: { statusKey: string; count: number }[],
  statusDefs: Map<string, DdpStatusItem>
): BarRow[] {
  const total = counts.reduce((sum, entry) => sum + entry.count, 0)
  const labelOf = (key: string) => statusDefs.get(key)?.label ?? key
  const bg = (key: string) => colorOf(statusDefs.get(key)?.colorBg, "#CCCCCC")
  const fg = (key: string) => colorOf(statusDefs.get(key)?.colorFg, "#000000")

  return [...counts]
    .sort((a, b) => b.count - a.count || a.statusKey.localeCompare(b.statusKey))
    .map(({ statusKey, count }) => {
      const key = statusKey ?? ""
      const fraction = total > 0 ? count / total : 0
      return {
        key,
        label: labelOf(key),
        count,
        fraction,
        pct: pctLabel(fraction, total),
        bg: bg(key),
        fg: fg(key),
      }
    })
}

export interface SintesiInputs {
  rows: DdpRowItem[]
  statusDefs: Map<string, DdpStatusItem>
  aggSets: Map<string, Set<string>>
}

export function buildSintesiModel({
  rows,
  statusDefs,
  aggSets,
}: SintesiInputs): DdpSintesiModel {
  const delivered = aggSets.get("A2")?.size
    ? aggSets.get("A2")!
    : new Set(DEFAULT_DELIVERED)
  const exclMissing = aggSets.get("A8") ?? new Set(DEFAULT_EXCL_MISSING)
  const a3 = aggSets.get("A3") ?? new Set(["PAR"])
  // Stati «esclusi da totale/conteggi» (aggregazione A9): fuori dal totale € e dai conteggi
  // di consegna/ritardo, e sono la card «DDP STOP».
  const excludedStates = aggSets.get("A9") ?? new Set<string>()

  const total = rows.length
  const today = todayIso()
  const labelOf = (key: string) => statusDefs.get(key)?.label ?? key
  const bg = (key: string) => colorOf(statusDefs.get(key)?.colorBg, "#CCCCCC")
  const fg = (key: string) => colorOf(statusDefs.get(key)?.colorFg, "#000000")

  // ── KPI ──
  const totValue = rows.reduce(
    (sum, row) => (excludedStates.has(row.itemStatus) ? sum : sum + amount(row)),
    0
  )
  const dated = rows.filter(
    (row) =>
      dayOf(row.dateNeeded) &&
      !delivered.has(row.itemStatus) &&
      !excludedStates.has(row.itemStatus)
  )
  const overdue = dated.filter((row) => (dayOf(row.dateNeeded) ?? "") < today)
  let finestra = "n/d"
  if (dated.length > 0) {
    const days = dated.map((row) => dayOf(row.dateNeeded)!).sort()
    const min = days[0]
    const max = days[days.length - 1]
    const span =
      Math.round(
        (new Date(max).getTime() - new Date(min).getTime()) / 86400000
      ) + 1
    finestra = `dal ${fmtDate(min)} al ${fmtDate(max)} · ${span} gg`
  }
  const kpi: DdpKpis = {
    totValue,
    count: total,
    datedCount: dated.length,
    overdue: overdue.length,
    consegnato: rows.filter((row) => delivered.has(row.itemStatus)).length,
    parziali: rows.filter((row) => a3.has(row.itemStatus)).length,
    finestra,
  }

  // ── Ripartizione per stato ──
  const ripGroups = new Map<string, number>()
  for (const row of rows)
    ripGroups.set(row.itemStatus ?? "", (ripGroups.get(row.itemStatus ?? "") ?? 0) + 1)
  const ripartizione: BarRow[] = buildRipartizioneBars(
    Array.from(ripGroups.entries()).map(([statusKey, count]) => ({ statusKey, count })),
    statusDefs
  )
  const ripSub = `${total} righe · ${ripartizione.length} stati presenti`

  // ── Materiale in Consegna / Consegnato ──
  const consegne = [...dated].sort((a, b) =>
    (dayOf(a.dateNeeded) ?? "").localeCompare(dayOf(b.dateNeeded) ?? "")
  )
  const consegneSub = `Orizzonte temporale delle consegne ancora da evadere. Escluse le righe già consegnate o gestite (${[
    ...delivered,
  ]
    .sort()
    .join(", ")}).`

  const consegnato = rows
    .filter((row) => delivered.has(row.itemStatus))
    .sort((a, b) => a.rowNumber - b.rowNumber)
  const consegnatoSub = `${consegnato.length} righe di materiale consegnato o gestito (${[
    ...delivered,
  ]
    .sort()
    .join(", ")}).`

  // ── Top 10 costi ──
  const top10: Top10Row[] = [...rows]
    .sort((a, b) => amount(b) - amount(a))
    .slice(0, 10)
    .map((item, index) => ({
      item,
      rank: index + 1,
      pctLabel: pctLabel(totValue > 0 ? amount(item) / totValue : 0, totValue),
    }))

  // ── Destinazioni ──
  const destGroups = new Map<string, number>()
  for (const row of rows) {
    const name = row.destination?.trim() ? row.destination.trim() : "NON DEFINITA"
    destGroups.set(name, (destGroups.get(name) ?? 0) + 1)
  }
  const destinazioni: BarRow[] = Array.from(destGroups.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([name, count]) => {
      const fraction = total > 0 ? count / total : 0
      const nd = name === "NON DEFINITA"
      return {
        key: "",
        label: name,
        count,
        fraction,
        pct: pctLabel(fraction, total),
        bg: nd ? "#C0392B" : "#2563EB",
        fg: "#FFFFFF",
      }
    })
  const destSub = `${total} righe · ${destinazioni.length} destinazioni`

  // ── Dati mancanti ──
  const mancanti: MissingRow[] = []
  let analyzed = 0
  for (const row of rows) {
    const status = row.itemStatus ?? ""
    if (exclMissing.has(status)) continue
    analyzed++
    const mStato = !status.trim() || status === "ND"
    const mRif = !row.daneaRef?.trim()
    const mData = !row.dateNeeded
    const mDest = !row.destination?.trim()
    const mCosto = row.unitCost === 0
    if (!(mStato || mRif || mData || mDest || mCosto)) continue
    const cell = (missing: boolean, label: string): MissingCell => ({
      text: missing ? label : "–",
      missing,
    })
    const stato = cell(mStato, "Stato")
    const rif = cell(mRif, "Rif. Danea")
    const data = cell(mData, "Data prev.")
    const dest = cell(mDest, "Destinazione")
    const costo = cell(mCosto, "Costo")
    mancanti.push({
      rowNo: row.rowNumber,
      statoKey: status,
      desc: row.description,
      stato,
      rif,
      data,
      dest,
      costo,
      flagColor: flagColor(statusDefs.get(status)?.colorBg),
      missingLabel: [stato, rif, data, dest, costo]
        .filter((c) => c.missing)
        .map((c) => c.text)
        .join(", "),
    })
  }
  const excluded = rows.length - analyzed
  const mancantiSub = `${mancanti.length} righe con almeno un dato mancante su ${analyzed} analizzate (${excluded} escluse per stato)`

  // ── Stati Avanzamento (A5, 8 card) ──
  const buckets: { label: string; states: string[] }[] = [
    { label: "VERIFICARE", states: ["VER"] },
    { label: "CHECK", states: ["CHEK"] },
    { label: "DA ORDINARE", states: ["DO"] },
    { label: "RICH. OFF.", states: ["RO"] },
    { label: "IN ORDINE", states: ["IO"] },
    {
      label: "DDP STOP",
      // Deriva dagli stati «esclusi da totale» (A9), non da un array fisso; fallback storico.
      states: excludedStates.size
        ? [...excludedStates]
        : ["ANN", "SOSP", "RAM", "SOST"],
    },
    { label: "SPED-MOD", states: ["SPED", "MOD"] },
    { label: "ASSEGNATO", states: ["ASS"] },
  ]
  const avanzamento: AvanzCard[] = buckets.map((bucket) => {
    const count = rows.filter((row) => bucket.states.includes(row.itemStatus)).length
    const fraction = total > 0 ? count / total : 0
    const base = colorOf(statusDefs.get(bucket.states[0])?.colorBg, "#94A3B8")
    return {
      label: bucket.label,
      count,
      pctLabel: `${pctLabel(fraction, total)} su Tot.`,
      bg: lighten(base, 0.86),
      border: lighten(base, 0.5),
    }
  })
  const avanzSub = `${buckets.length} stati di avanzamento · ${total} righe`

  // ── Feedback (A6 Acquisti / A7 Magazzino) ──
  const feedback = (code: string): { rows: BarRow[]; sub: string } => {
    const set = aggSets.get(code) ?? new Set<string>()
    let sum = 0
    const ordered = [...set].sort((a, b) => {
      const sa = statusDefs.get(a)?.sortOrder ?? Number.MAX_SAFE_INTEGER
      const sb = statusDefs.get(b)?.sortOrder ?? Number.MAX_SAFE_INTEGER
      return sa - sb || a.localeCompare(b)
    })
    const result = ordered.map((key) => {
      const count = rows.filter((row) => row.itemStatus === key).length
      sum += count
      const fraction = total > 0 ? count / total : 0
      return {
        key,
        label: labelOf(key),
        count,
        fraction,
        pct: pctLabel(fraction, total),
        bg: bg(key),
        fg: fg(key),
      }
    })
    return { rows: result, sub: `${set.size} stati · ${sum} righe` }
  }
  const acq = feedback("A6")
  const mag = feedback("A7")

  return {
    kpi,
    ripartizione,
    ripSub,
    consegne,
    consegneSub,
    consegnato,
    consegnatoSub,
    top10,
    destinazioni,
    destSub,
    mancanti,
    mancantiSub,
    distinta: rows,
    avanzamento,
    avanzSub,
    feedbackAcquisti: acq.rows,
    acqSub: acq.sub,
    feedbackMagazzino: mag.rows,
    magSub: mag.sub,
  }
}

export function barWidthPercent(fraction: number, count: number): number {
  if (count <= 0) return 0
  return Math.max(3, fraction * 100)
}
