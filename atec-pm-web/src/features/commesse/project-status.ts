import {
  CircleCheck,
  CircleDashed,
  CirclePause,
  CirclePlay,
  CircleX,
  type LucideIcon,
} from "lucide-react"

/**
 * Stati commessa: **unico punto** per etichetta, icona e colore.
 * Prima erano duplicati in dialogo, albero, dashboard e dettaglio — con «Completata»
 * che nell'albero si chiamava «Chiusa». La famiglia di icone è coerente (cerchi), così
 * restano allineate sia nel menu a tendina sia nei badge delle griglie.
 */
export type ProjectStatus =
  | "DRAFT"
  | "ACTIVE"
  | "ON_HOLD"
  | "COMPLETED"
  | "CANCELLED"

export interface ProjectStatusMeta {
  value: ProjectStatus
  label: string
  icon: LucideIcon
  /** Colore di testo/icona (menu, badge outline). */
  className: string
  /** Bordo del badge outline, in tinta con il testo. */
  borderClassName: string
  /** Tinta piena per le intestazioni scure e i grafici (niente token CSS lì). */
  color: string
}

export const PROJECT_STATUS_META: ProjectStatusMeta[] = [
  {
    value: "DRAFT",
    label: "Bozza",
    icon: CircleDashed,
    className: "text-muted-foreground",
    borderClassName: "border-border",
    color: "#6B7280",
  },
  {
    value: "ACTIVE",
    label: "Attiva",
    icon: CirclePlay,
    className: "text-emerald-600 dark:text-emerald-400",
    borderClassName: "border-emerald-500/40",
    color: "#059669",
  },
  {
    value: "ON_HOLD",
    // #88: a video si chiama «Stand-by» (era «In pausa») — è la parola della segnalazione.
    label: "Stand-by",
    icon: CirclePause,
    className: "text-amber-600 dark:text-amber-400",
    borderClassName: "border-amber-500/40",
    color: "#D97706",
  },
  {
    value: "COMPLETED",
    label: "Completata",
    icon: CircleCheck,
    className: "text-primary",
    borderClassName: "border-primary/40",
    color: "#2563EB",
  },
  {
    value: "CANCELLED",
    label: "Annullata",
    icon: CircleX,
    className: "text-destructive",
    borderClassName: "border-destructive/40",
    color: "#DC2626",
  },
]

const BY_VALUE = new Map(PROJECT_STATUS_META.map((m) => [m.value, m]))

/** Stato sconosciuto (o vuoto) → si comporta come una bozza, senza rompere la UI. */
export function projectStatusMeta(status: string | null | undefined): ProjectStatusMeta {
  return BY_VALUE.get((status ?? "") as ProjectStatus) ?? PROJECT_STATUS_META[0]
}

/**
 * Transizioni consentite a partire dallo stato corrente (stessa regola del dialog
 * commessa / WPF). Completata e Annullata non hanno uscita.
 */
export const PROJECT_STATUS_TRANSITIONS: Record<string, readonly string[]> = {
  DRAFT: ["DRAFT", "ACTIVE", "CANCELLED"],
  ACTIVE: ["ACTIVE", "ON_HOLD", "COMPLETED", "CANCELLED"],
  ON_HOLD: ["ON_HOLD", "ACTIVE", "CANCELLED"],
  COMPLETED: ["COMPLETED"],
  CANCELLED: ["CANCELLED"],
}

/**
 * Stati selezionabili nel dialog / menu contestuale, dato lo stato attuale.
 * `canOverride` = chi ha la chiave «Opera su commesse sospese o chiuse» (#89): per lui
 * si aggiungono sempre i quattro stati della Dashboard — riaprire una Completata compreso.
 * «Annullata» resta il soft delete: non guadagna né perde uscite da qui.
 */
export function allowedProjectStatuses(
  current: string | null | undefined,
  canOverride = false
): string[] {
  if (!current) {
    return PROJECT_STATUS_META.map((item) => item.value)
  }
  const base =
    PROJECT_STATUS_TRANSITIONS[current] ?? PROJECT_STATUS_META.map((item) => item.value)
  if (canOverride && current !== "CANCELLED") {
    return [...new Set([...base, ...DASHBOARD_STATUS_META.map((m) => m.value)])]
  }
  return [...base]
}

/**
 * I quattro stati della colonna «Stato» cliccabile della Dashboard (#88): chi ha il permesso
 * («Opera su commesse sospese o chiuse») si muove liberamente fra questi — riaprire una chiusa
 * compreso, è scritto nella segnalazione. «Annullata» NON c'è: è il soft delete
 * dell'eliminazione, che ha il suo percorso con la conferma davanti.
 */
export const DASHBOARD_STATUS_META: ProjectStatusMeta[] = PROJECT_STATUS_META.filter(
  (m) => m.value !== "CANCELLED"
)
