export interface CommessaSection {
  key: string
  label: string
  /** Emoji come nel WPF (le altre sezioni); Dettagli usa 📈 in web. */
  icon?: string
  /** Visibile solo a PM/ADMIN (Prev vs Consuntivo). */
  economicsOnly?: boolean
}

/**
 * Le 8 sezioni fisse di ogni commessa, NELLO STESSO ORDINE del WPF
 * (`ProjectsPage.BuildTree`). Non riordinare: è la fedeltà richiesta.
 */
export const COMMESSA_SECTIONS: CommessaSection[] = [
  { key: "details", label: "Dettagli", icon: "📈" },
  { key: "cashflow", label: "Flusso di Cassa", icon: "💰" },
  {
    key: "budget_vs_actual",
    label: "Preventivo vs Consuntivo",
    icon: "📊",
    economicsOnly: true,
  },
  { key: "chat", label: "Chat", icon: "💬" },
  { key: "mom", label: "Verbali (MoM)", icon: "📝" },
  { key: "checklist", label: "Check list", icon: "✅" },
  { key: "milestones", label: "Milestone", icon: "📅" },
  { key: "sal", label: "SAL / Fatturazione", icon: "💶" },
  { key: "ddp_commercial", label: "DDP Commerciali", icon: "📋" },
  { key: "ddp_officina", label: "DDP Officina", icon: "🔧" },
  { key: "documents", label: "Documenti", icon: "📁" },
]
