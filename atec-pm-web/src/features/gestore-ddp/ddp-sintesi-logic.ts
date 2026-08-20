import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { toDateOnly } from "@/lib/date-iso"

// Calcolo della Sintesi DDP, port delle Build* del client WPF (DdpSintesiPage.xaml.cs).
// Pure: ingredienti = righe DDP + causali (Conf. DDP) + aggregazioni (Aggregazioni DDP),
// con i fallback cablati quando A2..A8 non sono configurate.

// Chiusura positiva: dalla v75 (segnalazione #54) in officina sono CON (comprato fuori) e
// COS (costruito in casa); DISP resta sul commerciale e sulle righe chiuse prima.
export const DEFAULT_DELIVERED = ["DISP", "CON", "COS", "ASS"]
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
  /** Destinazioni: riga «NON DEFINITA» (o varianti), da evidenziare in rosso. */
  isNonDef?: boolean
}

/** Sezioni della scheda «Avanzamento» (port delle 8/10 tabelle del prototipo V41). */
export type DdpSectionKey =
  | "ver"
  | "chek"
  | "ro"
  | "do"
  | "dc"
  | "tab"
  | "par"
  | "rit"
  | "mit"
  | "del"
  | "ass"
  | "stop"

/**
 * Ordine canonico delle sezioni, diverso fra i due tipi di DDP come nel prototipo:
 * sulle Commerciali «Materiale in Ritardo» precede i parziali, sulle Officine è il contrario.
 * `dc` e `mit` sulle Commerciali compaiono solo se hanno righe (decisione di Diego 06/08/2026).
 * `chek` e `stop` compaiono solo se hanno righe (drill-down dalle card «Stati Avanzamento»).
 */
export const DDP_SECTION_ORDER: Record<"COMMERCIAL" | "OFFICINA", DdpSectionKey[]> = {
  OFFICINA: [
    "ver",
    "chek",
    "ro",
    "do",
    "dc",
    "tab",
    "par",
    "rit",
    "mit",
    "del",
    "ass",
    "stop",
  ],
  COMMERCIAL: [
    "ver",
    "chek",
    "ro",
    "do",
    "dc",
    "tab",
    "rit",
    "par",
    "mit",
    "del",
    "ass",
    "stop",
  ],
}

/** Sezioni presenti solo se hanno almeno una riga quando la DDP è commerciale. */
const OPTIONAL_ON_COMMERCIAL: DdpSectionKey[] = ["dc", "mit"]

/** Sezioni presenti solo se hanno almeno una riga (entrambi i tipi di DDP). */
const OPTIONAL_WHEN_EMPTY: DdpSectionKey[] = ["chek", "stop"]

export interface DdpAvanzSection {
  key: DdpSectionKey
  title: string
  rows: DdpRowItem[]
  /** Nota descrittiva sotto la tabella. */
  note: string
  /** Colora di rosso la data prevista quando è già passata. */
  dueRed: boolean
  emptyText: string
}

/** Finestra temporale (min/max/ampiezza) di un insieme di date. */
export interface DateWindow {
  /** Estremi in ISO date-only: la formattazione (corta a video, lunga in PDF) sta nella vista. */
  dalIso: string
  alIso: string
  dal: string
  al: string
  gg: number
  /** «dal gg/mm/aaaa al gg/mm/aaaa · N gg», oppure «n/d». */
  label: string
}

/**
 * Gli insiemi di stati effettivamente usati dal calcolo, già risolti dalle aggregazioni
 * configurabili con i loro fallback: le viste devono leggere questi, mai riscrivere elenchi.
 */
export interface DdpStateSets {
  /** A2 — consegnato / gestito. */
  delivered: Set<string>
  /** A3 — parzialmente consegnato. */
  parziale: Set<string>
  /** A8 — escluse dall'analisi «Dati Mancanti». */
  exclMissing: Set<string>
  /** A9 — escluse da totale e conteggi (card «DDP Stop»). */
  stop: Set<string>
  /** A2 ∪ A3 — card «Mat. a Magazzino». */
  magazzino: Set<string>
}

export interface AvanzCard {
  label: string
  count: number
  pctLabel: string
  bg: string
  border: string
  /** Sezione Avanzamento da aprire al click dalla scheda «Stato DDP». */
  sectionKey: DdpSectionKey
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
  /** Id della riga di distinta: chiave stabile (per React e per le righe spente). */
  id: number
  /** Numero riga: progressivo per i padri, con lettera (es. "3a") per i figli di composizione officina. */
  rowNo: number | string
  statoKey: string
  desc: string
  stato: MissingCell
  rif: MissingCell
  data: MissingCell
  dest: MissingCell
  costo: MissingCell
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
  /** Finestra consegne in forma scomposta (card «Attesa consegna» a 3 righe). */
  finestraWin: DateWindow
  /** Finestra di inserimento delle righe (su createdAt): «dal … al … · N gg». */
  insFinestra: string
  insFinestraWin: DateWindow
  /** Righe a magazzino = consegnate/gestite (A2) + parzialmente consegnate (A3). */
  magazzino: number
  /** Righe assegnate al montatore (ASS). */
  assegnato: number
  /** Righe in trattamento esterno (MIT). */
  trattamento: number
  /** Righe escluse dal totale € perché a stato chiuso (A9). */
  escluse: number
  /** Igiene dati: date di consegna implausibili (anno < 2015 o > 2100). */
  refusiDate: number
  /** Igiene dati: costo unitario a zero in stati che dovrebbero averlo. */
  costoZero: number
}

export interface DdpSintesiModel {
  kpi: DdpKpis
  /** Data di riferimento del calcolo: le celle la riusano per il rosso «in ritardo». */
  today: string
  /**
   * Righe «contabili»: per le DDP officina i padri di composizione (che hanno figli)
   * sono già esclusi. È la base di TUTTI i conteggi — mai partire da `rowsQuery.data`.
   */
  rows: DdpRowItem[]
  /** Gli insiemi di stati risolti dalle aggregazioni: le viste leggono questi. */
  sets: DdpStateSets
  parentIdsWithChildren: Set<number>
  /** Righe datate ancora da evadere (non A2, non A9), in ordine di data. */
  dated: DdpRowItem[]
  /** Righe senza data prevista ma in transito (IO/PAR/MIT). */
  noDateInTransitRows: DdpRowItem[]
  /** Righe datate con consegna prevista precedente a oggi. */
  overdueRows: DdpRowItem[]
  /** Sezioni della scheda «Avanzamento», già nell'ordine canonico del tipo di DDP. */
  sezioni: DdpAvanzSection[]
  ripartizione: BarRow[]
  ripSub: string
  consegne: DdpRowItem[]
  consegneSub: string
  consegnato: DdpRowItem[]
  consegnatoSub: string
  top10: Top10Row[]
  /** Subtotale delle 10 righe e totale della commessa (per le righe di piede). */
  top10Totals: { subtotal: number; total: number }
  destinazioni: BarRow[]
  destSub: string
  mancanti: MissingRow[]
  mancantiSub: string
  /** Conteggi dei Dati Mancanti: NON calano quando l'utente spegne una riga. */
  mancantiCounts: { withMissing: number; analyzed: number; excluded: number }
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

/**
 * Destinazione normalizzata: maiuscolo, spazi compattati, vuota → «NON DEFINITA».
 * Senza questa normalizzazione «Gruppo  Pompa» e «GRUPPO POMPA» finirebbero in due righe.
 */
export function normDest(value: string | null | undefined): string {
  const clean = (value ?? "").trim().toUpperCase().replace(/\s+/g, " ")
  return clean || "NON DEFINITA"
}

/** Riga di destinazione non valorizzata: intercetta anche varianti tipo «NON DESTINATA». */
export function isNonDefDest(dest: string): boolean {
  return /^NON\s*(DEF|DEST)/i.test(dest)
}

function emptyWindow(): DateWindow {
  return { dalIso: "", alIso: "", dal: "", al: "", gg: 0, label: "n/d" }
}

/** Finestra min/max su un insieme di date ISO (già date-only). */
function dateWindow(days: string[]): DateWindow {
  if (days.length === 0) return emptyWindow()
  const sorted = [...days].sort()
  const dal = sorted[0]
  const al = sorted[sorted.length - 1]
  const gg =
    Math.round((new Date(al).getTime() - new Date(dal).getTime()) / 86400000) + 1
  return {
    dalIso: dal,
    alIso: al,
    dal: fmtDate(dal),
    al: fmtDate(al),
    gg,
    label: `dal ${fmtDate(dal)} al ${fmtDate(al)} · ${gg} gg`,
  }
}

/** Ordine naturale per numero riga: "3" < "3a" < "10" (i figli officina hanno lettere). */
function byRowNumber(a: DdpRowItem, b: DdpRowItem): number {
  return String(a.rowNumber).localeCompare(String(b.rowNumber), undefined, {
    numeric: true,
  })
}

/** Per data prevista crescente, righe senza data in coda. */
function byDateThenNoDate(a: DdpRowItem, b: DdpRowItem): number {
  const da = toDateOnly(a.dateNeeded) ?? ""
  const db = toDateOnly(b.dateNeeded) ?? ""
  if (!da && !db) return byRowNumber(a, b)
  if (!da) return 1
  if (!db) return -1
  return da.localeCompare(db) || byRowNumber(a, b)
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
      const raw = statusKey ?? ""
      // Righe senza causale: a DB `item_status` è la stringa vuota e non esiste una voce
      // in anagrafica. Si mostrano come ND, così non compaiono badge muti nei grafici.
      const empty = !raw.trim()
      const key = empty ? "ND" : raw
      const fraction = total > 0 ? count / total : 0
      return {
        key,
        label: empty ? "Stato non valorizzato" : labelOf(key),
        count,
        fraction,
        pct: pctLabel(fraction, total),
        bg: bg(key),
        fg: fg(key),
      }
    })
}

/**
 * Aggregazione configurata da «Aggregazioni DDP»: se manca **o è vuota** si usa il fallback
 * cablato, così una configurazione incompleta non svuota la pagina.
 */
export function aggOf(
  aggSets: Map<string, Set<string>>,
  code: string,
  fallback: string[]
): Set<string> {
  const set = aggSets.get(code)
  return set && set.size ? set : new Set(fallback)
}

/**
 * Mappa un codice stato alla sezione Avanzamento del drill-down (Stato DDP → Avanzamento).
 * Ordine: stati monovalenti e ASS prima di A2, così ASS non finisce in «Magazzino».
 */
export function sectionKeyForStatus(
  status: string,
  sets: DdpStateSets
): DdpSectionKey | undefined {
  switch (status) {
    case "VER":
      return "ver"
    case "CHEK":
      return "chek"
    case "RO":
      return "ro"
    case "DO":
      return "do"
    case "DC":
      return "dc"
    case "IO":
      return "tab"
    case "MIT":
      return "mit"
    case "ASS":
      return "ass"
    default:
      break
  }
  if (sets.parziale.has(status)) return "par"
  if (sets.stop.has(status)) return "stop"
  if (sets.delivered.has(status)) return "del"
  return undefined
}

/**
 * Le sezioni della scheda «Avanzamento», nell'ordine canonico del tipo di DDP.
 * Nessun elenco di stati è cablato: si legge tutto dalle aggregazioni A2/A3/A9.
 */
export function buildAvanzSections(input: {
  rows: DdpRowItem[]
  officina: boolean
  aggSets: Map<string, Set<string>>
  /** Righe datate da evadere + senza data in transito (la sezione «tab»). */
  inConsegna: DdpRowItem[]
  overdueRows: DdpRowItem[]
}): DdpAvanzSection[] {
  const { rows, officina, aggSets, inConsegna, overdueRows } = input
  const delivered = aggOf(aggSets, "A2", DEFAULT_DELIVERED)
  const a3 = aggOf(aggSets, "A3", ["PAR"])
  const excluded = aggOf(aggSets, "A9", DEFAULT_EXCL_MISSING)
  const list = (statuses: Set<string> | string) =>
    rows.filter((row) =>
      typeof statuses === "string"
        ? row.itemStatus === statuses
        : statuses.has(row.itemStatus)
    )
  const join = (set: Set<string>) => [...set].sort().join(", ")
  const n = (count: number) => `${count} rig${count === 1 ? "a" : "he"}`

  const build: Record<DdpSectionKey, () => DdpAvanzSection> = {
    ver: () => {
      const list_ = list("VER").sort(byRowNumber)
      return {
        key: "ver",
        title: "da Verificare",
        rows: list_,
        note: `${n(list_.length)} in stato VER: da verificare se disponibili a magazzino.`,
        dueRed: false,
        emptyText: "Nessun materiale da verificare.",
      }
    },
    chek: () => {
      const list_ = list("CHEK").sort(byRowNumber)
      return {
        key: "chek",
        title: "Check",
        rows: list_,
        note: `${n(list_.length)} in stato CHEK: in attesa di check/controllo.`,
        dueRed: false,
        emptyText: "Nessuna riga in check.",
      }
    },
    ro: () => {
      const list_ = list("RO").sort(byRowNumber)
      return {
        key: "ro",
        title: "Richieste di Offerta",
        rows: list_,
        note: `${n(list_.length)} in stato RO, ancora da chiudere a livello di quotazione.`,
        dueRed: false,
        emptyText: "Nessuna richiesta di offerta.",
      }
    },
    do: () => {
      const list_ = list("DO").sort(byRowNumber)
      return {
        key: "do",
        title: "Da Ordinare",
        rows: list_,
        note: `${n(list_.length)} in stato DO: quotate, in attesa di emissione ordine.`,
        dueRed: false,
        emptyText: "Nessun materiale da ordinare.",
      }
    },
    dc: () => {
      const list_ = list("DC").sort(byRowNumber)
      return {
        key: "dc",
        title: "Da Costruire",
        rows: list_,
        note: `${n(list_.length)} in stato DC: da costruire internamente.`,
        dueRed: false,
        emptyText: "Nessun materiale da costruire.",
      }
    },
    tab: () => ({
      key: "tab",
      title: "In Ordine / Consegna — IO / PAR",
      rows: inConsegna,
      // Il titolo è quello storico del prototipo, ma l'insieme è più largo: va spiegato.
      note: `${n(inConsegna.length)} ancora da evadere: tutte quelle con data prevista${
        excluded.size ? ` (escluse le righe a stato chiuso: ${join(excluded)})` : ""
      }, più quelle senza data in stato ${DEFAULT_IN_TRANSIT_NODATE.join("/")}. Escluso il materiale già consegnato o gestito (${join(
        delivered
      )}).`,
      dueRed: true,
      emptyText: "Nessuna riga in ordine o in consegna.",
    }),
    par: () => {
      const list_ = list(a3).sort(byDateThenNoDate)
      return {
        key: "par",
        title: "Materiale Parzialmente Consegnato",
        rows: list_,
        note: `${n(list_.length)} in stato ${join(a3)}: consegnate o costruite solo in parte.`,
        dueRed: true,
        emptyText: "Nessun materiale parzialmente consegnato.",
      }
    },
    rit: () => ({
      key: "rit",
      title: "Materiale in Ritardo",
      rows: overdueRows,
      note: `${n(overdueRows.length)} con data di consegna prevista precedente a oggi.`,
      dueRed: true,
      emptyText: "Nessuna riga in ritardo di consegna.",
    }),
    mit: () => {
      const list_ = list("MIT").sort(byDateThenNoDate)
      return {
        key: "mit",
        title: "Materiale in Trattamento",
        rows: list_,
        note: `${n(list_.length)} in stato MIT: materiale presso il fornitore di trattamento.`,
        dueRed: true,
        emptyText: "Nessun materiale in trattamento.",
      }
    },
    del: () => {
      const list_ = list(delivered).sort(byRowNumber)
      return {
        key: "del",
        title: "Materiale a Magazzino",
        rows: list_,
        // I parziali (A3) restano fuori: hanno la loro sezione. La card KPI invece li include.
        note: `${n(list_.length)} di materiale consegnato o gestito (${join(
          delivered
        )}). I parzialmente consegnati sono nella sezione dedicata.`,
        dueRed: false,
        emptyText: "Nessun materiale consegnato o gestito.",
      }
    },
    ass: () => {
      const list_ = list("ASS").sort(byRowNumber)
      return {
        key: "ass",
        title: "Materiale Assegnato",
        rows: list_,
        note: `${n(list_.length)} in stato ASS: già assegnate al montaggio.`,
        dueRed: false,
        emptyText: "Nessun materiale assegnato.",
      }
    },
    stop: () => {
      const list_ = list(excluded).sort(byRowNumber)
      return {
        key: "stop",
        title: "DDP Stop",
        rows: list_,
        note: `${n(list_.length)} escluse dai conteggi (stati ${join(excluded)}).`,
        dueRed: false,
        emptyText: "Nessuna riga in DDP stop.",
      }
    },
  }

  return DDP_SECTION_ORDER[officina ? "OFFICINA" : "COMMERCIAL"]
    .map((key) => build[key]())
    .filter((section) => {
      if (OPTIONAL_WHEN_EMPTY.includes(section.key) && section.rows.length === 0) {
        return false
      }
      return (
        officina ||
        !OPTIONAL_ON_COMMERCIAL.includes(section.key) ||
        section.rows.length > 0
      )
    })
}

export interface SintesiInputs {
  rows: DdpRowItem[]
  statusDefs: Map<string, DdpStatusItem>
  aggSets: Map<string, Set<string>>
  /** Data di riferimento ISO (default: oggi). Un solo orologio per modello e celle. */
  today?: string
}

export function buildSintesiModel({
  rows: rawRows,
  statusDefs,
  aggSets,
  today = todayIso(),
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

  // Aggregazione mancante O vuota ⇒ fallback cablato (vedi aggOf).
  const delivered = aggOf(aggSets, "A2", DEFAULT_DELIVERED)
  const exclMissing = aggOf(aggSets, "A8", DEFAULT_EXCL_MISSING)
  const a3 = aggOf(aggSets, "A3", ["PAR"])
  // Stati «esclusi da totale/conteggi» (aggregazione A9): fuori dal totale € e dai conteggi
  // di consegna/ritardo, e sono la card «DDP STOP».
  const excludedStates = aggOf(aggSets, "A9", DEFAULT_EXCL_MISSING)

  const total = rows.length
  const labelOf = (key: string) => statusDefs.get(key)?.label ?? key
  const bg = (key: string) => colorOf(statusDefs.get(key)?.colorBg, "#CCCCCC")
  const fg = (key: string) => colorOf(statusDefs.get(key)?.colorFg, "#000000")

  // ── KPI ──
  const totValue = rows.reduce(
    (sum, row) => (excludedStates.has(row.itemStatus) ? sum : sum + amount(row)),
    0
  )
  // Ordinate qui una volta sola: consegne, ritardi e le loro stampe ereditano l'ordine
  // per data crescente senza doverlo riapplicare (e senza rischiare due ordini diversi).
  const dated = rows
    .filter(
      (row) =>
        toDateOnly(row.dateNeeded) &&
        !delivered.has(row.itemStatus) &&
        !excludedStates.has(row.itemStatus)
    )
    .sort(byDateThenNoDate)
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
  const finestraWin = dateWindow(
    dated.map((row) => toDateOnly(row.dateNeeded)!)
  )
  // Finestra di inserimento: il gestionale non ha la data d'ordine del prototipo,
  // si usa la data di creazione della riga (stessa colonna «Data» della distinta).
  const insFinestraWin = dateWindow(
    rows
      .map((row) => toDateOnly(row.createdAt))
      .filter((day): day is string => !!day)
  )
  const parzialiRows = rows.filter((row) => a3.has(row.itemStatus))
  const deliveredRows = rows.filter((row) => delivered.has(row.itemStatus))
  const magazzinoSet = new Set([...delivered, ...a3])
  const kpi: DdpKpis = {
    totValue,
    count: total,
    datedCount: dated.length,
    noDateInTransit: noDateInTransit.length,
    inConsegna: dated.length + noDateInTransit.length,
    overdue: overdue.length,
    consegnato: deliveredRows.length,
    parziali: parzialiRows.length,
    finestra: finestraWin.label,
    finestraWin,
    insFinestra: insFinestraWin.label,
    insFinestraWin,
    // Card «Mat. a Magazzino»: consegnato + parzialmente consegnato (A2 ∪ A3), come il
    // prototipo. La tabella omonima invece mostra solo A2 — differenza voluta, dichiarata
    // nella nota di sezione (decisione di Diego 06/08/2026).
    magazzino: rows.filter((row) => magazzinoSet.has(row.itemStatus)).length,
    assegnato: rows.filter((row) => row.itemStatus === "ASS").length,
    trattamento: rows.filter((row) => row.itemStatus === "MIT").length,
    escluse: rows.filter((row) => excludedStates.has(row.itemStatus)).length,
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
  const ripSub = `${total} righe d'ordine · ${ripartizione.length} stati presenti`

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
  // Stessa base economica del totale: le righe a stato chiuso (A9) restano fuori sia dal
  // rank sia dal subtotale, altrimenti una riga annullata di grosso importo occuperebbe il
  // primo posto e il subtotale supererebbe il totale commessa.
  const top10: Top10Row[] = rows
    .filter((row) => !excludedStates.has(row.itemStatus))
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
    const name = normDest(row.destination)
    destGroups.set(name, (destGroups.get(name) ?? 0) + 1)
  }
  const destinazioni: BarRow[] = Array.from(destGroups.entries())
    .map(([name, count]) => ({ name, count, nd: isNonDefDest(name) }))
    // Le destinazioni non valorizzate vanno SEMPRE in fondo; poi conteggio DESC e
    // alfabetico come spareggio esplicito (l'ordine dev'essere riproducibile).
    .sort(
      (a, b) =>
        Number(a.nd) - Number(b.nd) ||
        b.count - a.count ||
        a.name.localeCompare(b.name, "it")
    )
    .map(({ name, count, nd }) => {
      const fraction = total > 0 ? count / total : 0
      return {
        key: "",
        label: name,
        count,
        fraction,
        pct: pctLabel(fraction, total),
        bg: nd ? "#C0392B" : "#2563EB",
        fg: "#FFFFFF",
        isNonDef: nd,
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
    // A DB il costo unitario è DECIMAL con default 0: «vuoto» e «zero» non si distinguono.
    // Criterio unico: tutto ciò che non è maggiore di zero è un dato mancante.
    const mCosto = !(row.unitCost > 0)
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
      id: row.id,
      rowNo: row.rowNumber,
      statoKey: status,
      desc: row.description,
      stato,
      rif,
      data,
      dest,
      costo,
      missingLabel: [stato, rif, data, dest, costo]
        .filter((c) => c.missing)
        .map((c) => c.text)
        .join(", "),
    })
  }
  const excluded = rows.length - analyzed
  const mancantiSub = `${mancanti.length} righe con almeno un dato mancante su ${analyzed} analizzate (${excluded} escluse per stato)`
  const mancantiCounts = {
    withMissing: mancanti.length,
    analyzed,
    excluded,
  }

  // ── Stati Avanzamento (A5, 8 card) ──
  // Ordine del prototipo V41, tradotto sulla matrice stati v7: «Sped-Mod» non esiste più
  // (SPED/MOD assorbiti da DISP/RAM), al suo posto compaiono «In Ordine» e «Da Costruire».
  // `sectionKey` collega ogni card alla sezione Avanzamento del drill-down (#44).
  const allBuckets: {
    label: string
    states: string[]
    sectionKey: DdpSectionKey
    optional?: boolean
  }[] = [
    { label: "VERIFICARE", states: ["VER"], sectionKey: "ver" },
    { label: "CHECK", states: ["CHEK"], sectionKey: "chek" },
    { label: "RICH. OFF.", states: ["RO"], sectionKey: "ro" },
    { label: "DA ORDINARE", states: ["DO"], sectionKey: "do" },
    // DC e MIT: sempre sulle Officine, sulle Commerciali solo se hanno righe.
    { label: "DA COSTRUIRE", states: ["DC"], sectionKey: "dc", optional: !officina },
    // «In Ordine» (solo IO) → sezione «In Ordine / Consegna» (insieme più ampio).
    { label: "IN ORDINE", states: ["IO"], sectionKey: "tab" },
    { label: "TRATTAMENTO", states: ["MIT"], sectionKey: "mit", optional: !officina },
    // Deriva dagli stati «esclusi da totale» (A9), non da un array fisso.
    { label: "DDP STOP", states: [...excludedStates], sectionKey: "stop" },
    { label: "MAT. A MAGAZZINO", states: [...magazzinoSet], sectionKey: "del" },
    { label: "ASSEGNATO", states: ["ASS"], sectionKey: "ass" },
  ]
  const buckets = allBuckets.filter(
    (bucket) =>
      !bucket.optional ||
      rows.some((row) => bucket.states.includes(row.itemStatus))
  )
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
      sectionKey: bucket.sectionKey,
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

  // ── Sezioni della scheda Avanzamento ──
  const sezioni = buildAvanzSections({
    rows,
    officina,
    aggSets,
    inConsegna: consegne,
    overdueRows: overdue,
  })

  return {
    kpi,
    today,
    rows,
    sets: {
      delivered,
      parziale: a3,
      exclMissing,
      stop: excludedStates,
      magazzino: magazzinoSet,
    },
    parentIdsWithChildren,
    dated,
    noDateInTransitRows: noDateInTransit,
    overdueRows: overdue,
    sezioni,
    ripartizione,
    ripSub,
    consegne,
    consegneSub,
    consegnato,
    consegnatoSub,
    top10,
    top10Totals: {
      subtotal: top10.reduce((sum, row) => sum + amount(row.item), 0),
      total: totValue,
    },
    destinazioni,
    destSub,
    mancanti,
    mancantiSub,
    mancantiCounts,
    distinta: distintaRows,
    avanzamento,
    avanzSub,
    feedbackAcquisti: acq.rows,
    acqSub: acq.sub,
    feedbackMagazzino: mag.rows,
    magSub: mag.sub,
  }
}

/**
 * Piede della tabella «Dati Mancanti»: **una sola formulazione** per la vista e per il PDF.
 * I conteggi del modello (con dato mancante / analizzate / escluse) restano sul totale:
 * spegnendo una riga cala solo «righe visualizzate».
 */
export function mancantiFooterText(
  visible: number,
  spente: number,
  counts: { withMissing: number; analyzed: number; excluded: number }
): string {
  return (
    `${visible} righe visualizzate` +
    (spente > 0 ? ` · ${spente} ${spente === 1 ? "riga spenta" : "righe spente"}` : "") +
    ` · ${counts.withMissing} con almeno un dato mancante su ${counts.analyzed} analizzate` +
    ` (${counts.excluded} escluse per stato).`
  )
}

export function barWidthPercent(fraction: number, count: number): number {
  if (count <= 0) return 0
  return Math.max(3, fraction * 100)
}
