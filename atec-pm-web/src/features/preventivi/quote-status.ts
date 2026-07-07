// Stati preventivo — etichette e colori fedeli ai converter WPF
// (QuoteStatusToBg/Fg/LabelConverter, dot delle ComboBoxItem in QuotesHomePage).

export interface QuoteStatusMeta {
  key: string
  label: string
  /** Pallino colorato nella combo. */
  dot: string
  /** Sfondo pill. */
  bg: string
  /** Testo pill. */
  fg: string
}

/** Stati selezionabili inline (NON include superseded/converted, gestiti a parte). */
export const QUOTE_STATUSES: QuoteStatusMeta[] = [
  { key: "draft", label: "In preparazione", dot: "#9CA3AF", bg: "#F3F4F6", fg: "#6B7280" },
  { key: "sent", label: "Preventivo inviato", dot: "#3B82F6", bg: "#DBEAFE", fg: "#1D4ED8" },
  { key: "negotiation", label: "In trattativa", dot: "#F59E0B", bg: "#FEF3C7", fg: "#D97706" },
  { key: "accepted", label: "Il cliente ha confermato", dot: "#10B981", bg: "#D1FAE5", fg: "#059669" },
  { key: "rejected", label: "Cliente non interessato", dot: "#EF4444", bg: "#FEE2E2", fg: "#DC2626" },
  { key: "expired", label: "Scaduto", dot: "#6B7280", bg: "#F3F4F6", fg: "#6B7280" },
]

const ALL_LABELS: Record<string, string> = {
  draft: "In preparazione",
  sent: "Preventivo inviato",
  negotiation: "In trattativa",
  accepted: "Il cliente ha confermato",
  rejected: "Cliente non interessato",
  expired: "Scaduto",
  superseded: "Superata",
  converted: "Convertito",
}

export function quoteStatusLabel(key: string): string {
  return ALL_LABELS[key] ?? key
}

export function quoteStatusMeta(key: string): QuoteStatusMeta | undefined {
  return QUOTE_STATUSES.find((s) => s.key === key)
}

/** Filtri stato della toolbar (Tutti + i 6 selezionabili + Convertito), come il WPF. */
export const QUOTE_STATUS_FILTERS: { value: string; label: string }[] = [
  { value: "", label: "Tutti" },
  ...QUOTE_STATUSES.map((s) => ({ value: s.key, label: s.label })),
  { value: "converted", label: "Convertito" },
]
