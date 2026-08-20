// ── Matrice colori MoM (da «Matrice MoM.xlsx», 03/08/2026) ────────────────
//
//   Condizione   Colore riga
//   P1           Rosso pastello
//   P2           Arancione pastello
//   P3           Azzurro pastello
//   Stand by     Giallo pastello
//   Close        Verde pastello   → all'utente si mostra come «Chiusa»
//   Scadute      Viola pastello
//
// Precedenza (la prima che si applica vince):
//   Close → Stand by → Scadute → Max priorità → P1/P2/P3.
// «Max priorità» (bandierina) non è nella matrice: resta nella famiglia rossa
// di P1, con bordo più marcato, per non perdere la segnalazione.
//
// Tutta la matrice sta qui: è l'unico punto da toccare se un domani diventa
// configurabile da pagina impostazioni (come gli stati DDP).

export type MoMConditionKey =
  | "close"
  | "standby"
  | "overdue"
  | "critical"
  | "p1"
  | "p2"
  | "p3"

export interface MoMConditionStyle {
  /** Etichetta mostrata all'utente (colonna Priorità, export, legenda). */
  label: string
  /** Sfondo riga nel foglio, tinta pastello. */
  rowClass: string
  /** Pallino compatto (legende, elenchi). */
  dotClass: string
  /** Pillola con numero/testo (liste verbali). */
  pillClass: string
  /** Sfondo riga per export ed anteprima di stampa. */
  hex: string
  /** Colore testo per export ed anteprima di stampa. */
  inkHex: string
}

export const MOM_CONDITION_STYLE: Record<MoMConditionKey, MoMConditionStyle> = {
  close: {
    label: "Chiusa",
    rowClass:
      "bg-green-100/70 hover:bg-green-100 dark:bg-green-500/15 dark:hover:bg-green-500/25 shadow-[inset_0_0_0_1px_theme(colors.green.200),inset_3px_0_0_0_theme(colors.green.300)]",
    dotClass: "bg-green-500",
    pillClass:
      "bg-green-100 text-green-700 dark:bg-green-950/40 dark:text-green-300",
    hex: "#DCFCE7",
    inkHex: "#15803D",
  },
  standby: {
    label: "Stand by",
    rowClass:
      "bg-yellow-100/70 hover:bg-yellow-100 dark:bg-yellow-500/15 dark:hover:bg-yellow-500/25 shadow-[inset_0_0_0_1px_theme(colors.yellow.200),inset_3px_0_0_0_theme(colors.yellow.400)]",
    dotClass: "bg-yellow-400",
    pillClass:
      "bg-yellow-100 text-yellow-700 dark:bg-yellow-950/40 dark:text-yellow-300",
    hex: "#FEF9C3",
    inkHex: "#A16207",
  },
  overdue: {
    label: "Scaduta",
    rowClass:
      "bg-purple-100/70 hover:bg-purple-100 dark:bg-purple-500/15 dark:hover:bg-purple-500/25 shadow-[inset_0_0_0_1px_theme(colors.purple.200),inset_3px_0_0_0_theme(colors.purple.300)]",
    dotClass: "bg-purple-500",
    pillClass:
      "bg-purple-100 text-purple-700 dark:bg-purple-950/40 dark:text-purple-300",
    hex: "#F3E8FF",
    inkHex: "#7E22CE",
  },
  critical: {
    label: "Max priorità",
    rowClass:
      "bg-red-100 hover:bg-red-200/70 dark:bg-red-500/25 dark:hover:bg-red-500/35 shadow-[inset_0_0_0_1px_theme(colors.red.300),inset_4px_0_0_0_theme(colors.red.500)]",
    dotClass: "bg-red-500",
    pillClass: "bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300",
    hex: "#FECACA",
    inkHex: "#B91C1C",
  },
  p1: {
    label: "P1",
    rowClass:
      "bg-red-100/70 hover:bg-red-100 dark:bg-red-500/15 dark:hover:bg-red-500/25 shadow-[inset_0_0_0_1px_theme(colors.red.200),inset_3px_0_0_0_theme(colors.red.300)]",
    dotClass: "bg-red-500",
    pillClass: "bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300",
    hex: "#FEE2E2",
    inkHex: "#B91C1C",
  },
  p2: {
    label: "P2",
    rowClass:
      "bg-orange-100/70 hover:bg-orange-100 dark:bg-orange-500/15 dark:hover:bg-orange-500/25 shadow-[inset_0_0_0_1px_theme(colors.orange.200),inset_3px_0_0_0_theme(colors.orange.300)]",
    dotClass: "bg-orange-500",
    pillClass:
      "bg-orange-100 text-orange-700 dark:bg-orange-950/40 dark:text-orange-300",
    hex: "#FFEDD5",
    inkHex: "#C2410C",
  },
  p3: {
    label: "P3",
    rowClass:
      "bg-sky-100/70 hover:bg-sky-100 dark:bg-sky-500/15 dark:hover:bg-sky-500/25 shadow-[inset_0_0_0_1px_theme(colors.sky.200),inset_3px_0_0_0_theme(colors.sky.300)]",
    dotClass: "bg-sky-500",
    pillClass: "bg-sky-100 text-sky-700 dark:bg-sky-950/40 dark:text-sky-300",
    hex: "#E0F2FE",
    inkHex: "#0369A1",
  },
}

/** Condizione da priorità numerica (1–3), con fallback su P2. */
export function priorityConditionKey(priorita: number): MoMConditionKey {
  if (priorita === 1) return "p1"
  if (priorita === 3) return "p3"
  return "p2"
}

/**
 * Condizione della riga secondo la matrice: `overdue` va passato dal chiamante
 * (dipende dalla data di oggi, vedi `isOverdue`).
 */
export function momConditionKey(item: {
  priorita: number
  status: string
  isCritical: boolean
  isOverdue?: boolean
}): MoMConditionKey {
  if (item.status === "CLOSED") return "close"
  if (item.status === "STANDBY") return "standby"
  if (item.isOverdue) return "overdue"
  if (item.isCritical) return "critical"
  return priorityConditionKey(item.priorita)
}
