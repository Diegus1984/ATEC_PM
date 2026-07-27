import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { toDateOnly } from "@/lib/date-iso"

// Calcolo della Sintesi DDP, port delle Build* del client WPF (DdpSintesiPage.xaml.cs).
// Pure: ingredienti = righe DDP + causali (Conf. DDP) + aggregazioni (Aggregazioni DDP),
// con i fallback cablati quando A2..A8 non sono configurate.

// Matrice stati v7: DISP assorbe CON/COS/SPED; il vecchio MOD è confluito in RAM.
export const DEFAULT_DELIVERED = ["DISP", "ASS"]
export const DEFAULT_EXCL_MISSING = ["ANN", "SOSP", "SOST", "RAM"]
// Righe SENZA data prevista ma comunque "in consegna" (ordine emesso / in lavorazione):
// contate nel KPI Materiale in Consegna come nel prototipo V30 (INCONS_NODATE).
export const DEFAULT_IN_TRANSIT_NODATE = ["IO", "PAR", "MIT"]
// Stati in cui un costo unitario a zero è plausibile (non ancora quotato/ordinato)
// o irrilevante (riga chiusa): esclusi dal check igiene "costo zero".
export const COST_ZERO_OK = ["ANN", "SOSP", "SOST", "RAM", "ND", "RO", "VER", "DO"]

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
  /** Numero riga: progressivo per i padri, con lettera (es. "3a") per i figli di composizione officina. */
  rowNo: number | string
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
  /** Righe senza data prevista in stato ordine/lavorazione (IO, PAR, MIT). */
  noDateInTransit: number
  /** Materiale in Consegna = righe datate da evadere + senza-data in transito. */
  inConsegna: number
  overdue: number
  consegnato: number
  parziali: number
  finestra: string
  /** Igiene dati: date di consegna implausibili (anno < 2015 o > 2100). */
  refusiDate: number
  /** Igiene dati: costo unitario a zero in stati che dovrebbero averlo. */
  costoZero: number
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

function amount(row: DdpRowItem): number {
  return row.quantity * row.unitCost
}

function fmtDate(value: string | null): string {
  const day = toDateOnly(value)
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
  rows: rawRows,
  statusDefs,
  aggSets,
}: SintesiInputs): DdpSintesiModel {
  const officina = rawRows.some((r) => r.ddpType === "OFFICINA")

  const parentIdsWithChildren = new Set<number>()
  if (officina) {
    for (const item of rawRows) {
      if (item.parentOfficinaItemId != null) {
        parentIdsWithChildren.add(item.parentOfficinaItemId)
      }
    }
  }

  const rows = officina
    ? rawRows.filter((r) => !parentIdsWithChildren.has(r.id))
    : rawRows

  let distintaRows: DdpRowItem[] = rawRows
  if (officina) {
    const parents = rawRows.filter((it) => it.parentOfficinaItemId == null)
    const children = rawRows.filter((it) => it.parentOfficinaItemId != null)

    const childrenMap: Record<number, typeof children> = {}
    children.forEach((child) => {
      const pid = child.parentOfficinaItemId!
      childrenMap[pid] = childrenMap[pid] ?? []
      childrenMap[pid].push(child)
    })

    Object.keys(childrenMap).forEach((pidStr) => {
      const pid = Number(pidStr)
      childrenMap[pid].sort((a, b) =>
        (a.partNumber || "").localeCompare(b.partNumber || "", undefined, {
          numeric: true,
          sensitivity: "base",
        })
      )
    })

    const sortedList: typeof rawRows = []
    parents.forEach((parent) => {
      sortedList.push(parent)
      const pChildren = childrenMap[parent.id]
      if (pChildren) {
        sortedList.push(...pChildren)
        delete childrenMap[parent.id]
      }
    })

    Object.values(childrenMap).forEach((orphans) => {
      sortedList.push(...orphans)
    })

    const parentToChildrenLookup: Record<number, typeof children> = {}
    rawRows.forEach((child) => {
      if (child.parentOfficinaItemId != null) {
        const pid = child.parentOfficinaItemId
        parentToChildrenLookup[pid] = parentToChildrenLookup[pid] ?? []
        parentToChildrenLookup[pid].push(child)
      }
    })

    let parentCount = 0
    distintaRows = sortedList.map((item) => {
      let displayIndex: string | number = "•"
      if (item.parentOfficinaItemId == null) {
        parentCount++
        displayIndex = parentCount
      }

      let unitCost = item.unitCost
      const hasChildren = parentIdsWithChildren.has(item.id)
      if (hasChildren) {
        const itemChildren = parentToChildrenLookup[item.id] ?? []
        unitCost = itemChildren.reduce(
          (sum, child) => sum + child.unitCost * (child.compositionQty ?? 1),
          0
        )
      }
      const totalCost = unitCost * item.quantity

      return {
        ...item,
        rowNumber: displayIndex,
        unitCost,
        totalCost,
      } as DdpRowItem
    })
  }

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
      toDateOnly(row.dateNeeded) &&
      !delivered.has(row.itemStatus) &&
      !excludedStates.has(row.itemStatus)
  )
  const overdue = dated.filter((row) => (toDateOnly(row.dateNeeded) ?? "") < today)
  // Senza data ma con ordine emesso / in lavorazione: contano come "in consegna"
  // anche se restano fuori dalla finestra temporale (basata sulle sole righe datate).
  const noDateSet = new Set(DEFAULT_IN_TRANSIT_NODATE)
  const noDateInTransit = rows.filter(
    (row) =>
      !toDateOnly(row.dateNeeded) &&
      noDateSet.has(row.itemStatus) &&
      !excludedStates.has(row.itemStatus)
  )
  // Igiene dati: refusi di battitura sulle date e costi assenti dove non dovrebbero.
  const costZeroOk = new Set(COST_ZERO_OK)
  const refusiDate = dated.filter((row) => {
    const year = Number((toDateOnly(row.dateNeeded) ?? "").slice(0, 4))
    return year > 0 && (year < 2015 || year > 2100)
  }).length
  const costoZero = rows.filter(
    (row) =>
      !(row.unitCost > 0) &&
      !costZeroOk.has(row.itemStatus) &&
      !excludedStates.has(row.itemStatus)
  ).length
  let finestra = "n/d"
  if (dated.length > 0) {
    const days = dated.map((row) => toDateOnly(row.dateNeeded)!).sort()
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
    noDateInTransit: noDateInTransit.length,
    inConsegna: dated.length + noDateInTransit.length,
    overdue: overdue.length,
    consegnato: rows.filter((row) => delivered.has(row.itemStatus)).length,
    parziali: rows.filter((row) => a3.has(row.itemStatus)).length,
    finestra,
    refusiDate,
    costoZero,
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
  // Righe datate in ordine di consegna + righe senza data in transito (IO/PAR/MIT) in coda.
  const consegne = [
    ...[...dated].sort((a, b) =>
      (toDateOnly(a.dateNeeded) ?? "").localeCompare(toDateOnly(b.dateNeeded) ?? "")
    ),
    ...noDateInTransit,
  ]
  const consegneSub = `Consegne ancora da evadere: righe con data prevista${
    noDateInTransit.length > 0
      ? ` + ${noDateInTransit.length} senza data in stato ${DEFAULT_IN_TRANSIT_NODATE.join("/")}`
      : ""
  }. Escluse le righe già consegnate o gestite (${[...delivered]
    .sort()
    .join(", ")}).`

  const consegnato = rows
    .filter((row) => delivered.has(row.itemStatus))
    .sort((a, b) =>
      // Ordinamento naturale: rowNumber può essere "3a" (figli officina) → "3" < "3a" < "10"
      String(a.rowNumber).localeCompare(String(b.rowNumber), undefined, {
        numeric: true,
      })
    )
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
    // v7: SPED/MOD eliminati (assorbiti da DISP/RAM); la card mostra il trattamento esterno.
    { label: "TRATTAMENTO", states: ["MIT"] },
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
    distinta: distintaRows,
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
