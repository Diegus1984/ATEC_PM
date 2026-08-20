import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { euro } from "@/lib/format"

import type { ExportTable } from "./ddp-export"
import {
  mancantiFooterText,
  type BarRow,
  type DdpSectionKey,
  type DdpSintesiModel,
} from "./ddp-sintesi-logic"
import {
  ddpRowToSintesiCells,
  fmtQty,
  sintesiTableHeaders,
} from "./ddp-sintesi-table"

/**
 * Costruzione delle tabelle di stampa della Sintesi DDP: **un solo punto** per la vista,
 * per la stampa di sezione e per la Stampa Aggregato, così il PDF non può divergere da
 * ciò che si vede. Le stampe ricostruiscono sempre l'insieme dalla sorgente (stesso
 * filtro, stesso ordinamento): un riordino manuale a video non finisce nel PDF.
 */
export interface PrintTableCtx {
  model: DdpSintesiModel
  officina: boolean
  statoLabel: (key: string) => string
  statusDefs: Map<string, DdpStatusItem>
  /** Filtro «righe accese»: le righe spente non si stampano. */
  onlyOn: <T extends { id: number }>(sectionKey: string, rows: T[]) => T[]
}

/** Chiavi stampabili: le sezioni di Avanzamento più le tabelle delle altre schede. */
export type PrintKey =
  | DdpSectionKey
  | "stato"
  | "rip"
  | "top10"
  | "dest"
  | "mancanti"
  | "distinta"
  | "acq"
  | "mag"

/** Riga segnaposto delle tabelle vuote: si stampa, ma non va contata come riga di dati. */
export const EMPTY_ROW = ["Nessuna riga."]

function barTable(title: string, bars: BarRow[], base: number): ExportTable {
  // La riga TOTALE riporta la somma delle barre stampate, non il totale della distinta:
  // A6/A7 coprono solo un sottoinsieme di stati e direbbero «100,0%» su un numero diverso.
  const sum = bars.reduce((acc, bar) => acc + bar.count, 0)
  const pct =
    base > 0
      ? `${((sum / base) * 100).toLocaleString("it-IT", {
          minimumFractionDigits: 1,
          maximumFractionDigits: 1,
        })}%`
      : "—"
  return {
    title,
    headers: ["Stato", "Descrizione", "N. Righe", "% sul totale"],
    rows: [
      ...bars.map((bar) => [bar.key, bar.label, String(bar.count), bar.pct]),
      ["", "TOTALE", String(sum), pct],
    ],
  }
}

export function sectionTitle(key: PrintKey, ctx: PrintTableCtx): string {
  const section = ctx.model.sezioni.find((item) => item.key === key)
  if (section) return section.title
  const titles: Partial<Record<PrintKey, string>> = {
    stato: "Stato DDP",
    rip: "Ripartizione per stato",
    top10: "Top 10 Costi",
    dest: "Destinazioni",
    mancanti: "Dati Mancanti",
    distinta: "Dati Distinta",
    acq: "Feedback Acquisti",
    mag: "Feedback Magazzino",
  }
  return titles[key] ?? String(key)
}

/** Righe effettivamente stampate per una chiave (serve ai contatori del dialogo). */
export function printableRowCount(key: PrintKey, ctx: PrintTableCtx): number {
  const section = ctx.model.sezioni.find((item) => item.key === key)
  if (section) return ctx.onlyOn(section.key, section.rows).length
  if (key === "mancanti") return ctx.onlyOn("mancanti", ctx.model.mancanti).length
  if (key === "top10") return ctx.model.top10.length
  if (key === "dest") return ctx.model.destinazioni.length
  if (key === "rip") return ctx.model.ripartizione.length
  if (key === "distinta") return ctx.model.distinta.length
  return 0
}

export function buildPrintTable(key: PrintKey, ctx: PrintTableCtx): ExportTable {
  const { model, officina, statoLabel, statusDefs, onlyOn } = ctx
  const headers = [...sintesiTableHeaders(officina)]
  const fullRow = (row: DdpRowItem) => ddpRowToSintesiCells(row, officina, statoLabel)
  const rowColors = (rows: DdpRowItem[]) =>
    rows.map((row) => statusDefs.get(row.itemStatus)?.colorBg ?? null)

  const fullTable = (title: string, rows: DdpRowItem[]): ExportTable => ({
    title,
    headers,
    rows: rows.length ? rows.map(fullRow) : [EMPTY_ROW],
    rowColors: rows.length ? rowColors(rows) : undefined,
  })

  // Sezioni della scheda Avanzamento: sempre al netto delle righe spente.
  const section = model.sezioni.find((item) => item.key === key)
  if (section) {
    const rows = onlyOn(section.key, section.rows)
    return {
      ...fullTable(section.title, rows),
      title: `${section.title} — ${rows.length} rig${rows.length === 1 ? "a" : "he"}`,
    }
  }

  switch (key) {
    case "stato": {
      const kpi = model.kpi
      return {
        title: "Stato DDP",
        headers: ["Indicatore", "Valore"],
        rows: [
          ["Totale acquisti", euro(kpi.totValue)],
          ["Numero inserimenti", String(kpi.count)],
          ["Finestra inserimenti", kpi.insFinestra],
          ["Finestra consegne", kpi.finestra],
          ["Materiale parzialmente consegnato", String(kpi.parziali)],
          ["Materiale consegnato", String(kpi.consegnato)],
          ["Materiale a magazzino", String(kpi.magazzino)],
          ["In ordine / consegna", String(kpi.inConsegna)],
          ["Materiale in trattamento", String(kpi.trattamento)],
          ["Materiale assegnato", String(kpi.assegnato)],
          ["Materiale in ritardo", String(kpi.overdue)],
          ["Righe escluse dal totale (DDP stop)", String(kpi.escluse)],
        ],
      }
    }
    case "rip":
      return barTable("Ripartizione per stato", model.ripartizione, model.rows.length)
    case "acq":
      return barTable("Feedback Acquisti", model.feedbackAcquisti, model.rows.length)
    case "mag":
      return barTable("Feedback Magazzino", model.feedbackMagazzino, model.rows.length)
    case "top10": {
      const { subtotal, total } = model.top10Totals
      const pct = (value: number) =>
        total > 0
          ? `${((value / total) * 100).toLocaleString("it-IT", {
              minimumFractionDigits: 1,
              maximumFractionDigits: 1,
            })}%`
          : "—"
      return {
        title: "Top 10 Costi",
        headers: ["#", "Descrizione", "Qtà", "Fornitore", "Importo (€)", "% sul tot."],
        rows: [
          ...model.top10.map((row) => [
            String(row.rank),
            row.item.description,
            fmtQty(row.item.quantity),
            row.item.supplierName || "—",
            euro(row.item.unitCost == null ? null : row.item.quantity * row.item.unitCost),
            row.pctLabel,
          ]),
          ["", "Subtotale Top 10", "", "", euro(subtotal), pct(subtotal)],
          ["", "Totale commessa", "", "", euro(total), total > 0 ? "100,0%" : "—"],
        ],
      }
    }
    case "dest":
      return {
        title: "Destinazioni",
        headers: ["Destinazione", "N. Righe", "% sul totale"],
        rows: [
          ...model.destinazioni.map((bar) => [
            bar.label,
            String(bar.count),
            bar.pct,
          ]),
          [
            "TOTALE",
            String(model.rows.length),
            model.rows.length > 0 ? "100,0%" : "—",
          ],
        ],
      }
    case "mancanti": {
      const rows = onlyOn("mancanti", model.mancanti)
      return {
        // Stesso piede della vista: una sola formulazione dei conteggi.
        title: `Dati Mancanti — ${mancantiFooterText(
          rows.length,
          model.mancanti.length - rows.length,
          model.mancantiCounts
        )}`,
        headers: [
          "Riga",
          "Stato",
          "Descrizione",
          "Stato",
          "Rif. Danea",
          "Data Prev. Cons.",
          "Destinazione",
          "Costo Unitario",
        ],
        rows: rows.length
          ? rows.map((row) => [
              String(row.rowNo),
              // Le righe senza causale sono il difetto principale da segnalare: nel PDF
              // la cella non deve restare vuota.
              statoLabel(row.statoKey) || "ND",
              row.desc,
              row.stato.text,
              row.rif.text,
              row.data.text,
              row.dest.text,
              row.costo.text,
            ])
          : [EMPTY_ROW],
      }
    }
    case "distinta":
      return fullTable("Dati Distinta", model.distinta)
    default:
      return { title: String(key), headers: [], rows: [] }
  }
}
