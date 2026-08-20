import type { DdpStatusItem } from "@/lib/api/types"

import type { PrintKey } from "./ddp-print-tables"
import type { DdpSintesiModel } from "./ddp-sintesi-logic"
import type { DdpRowOffApi } from "./use-ddp-row-off"

/** Schede della Sintesi DDP (port delle 5 pagine del prototipo V41 + Dati Distinta). */
export type DdpTabKey =
  | "stato"
  | "avanz"
  | "top10"
  | "dest"
  | "mancanti"
  | "distinta"

/** Tutto ciò che le viste ricevono dal contenitore: nessuna vista fa query per conto suo. */
export interface DdpViewProps {
  model: DdpSintesiModel
  officina: boolean
  statusDefs: Map<string, DdpStatusItem>
  statoLabel: (key: string) => string
  /** Visibilità colonne dal menu «Colonne» della pagina. */
  visibleCols: Record<string, boolean>
  rowOff: DdpRowOffApi
  /** Stampa PDF di una singola tabella. */
  printKey: (key: PrintKey) => void
  /** Naviga a un'altra scheda, opzionalmente aprendo una sezione. */
  goTo: (tab: DdpTabKey, sectionId?: string) => void
  /** Sezione da aprire all'ingresso nella scheda (drill-down dalle card). */
  focusSection?: string
  code: string
  reportHeader: string
  collapsedParentIds: Set<number>
  parentIdsWithChildren: Set<number>
  toggleParentCollapse: (id: number) => void
}
