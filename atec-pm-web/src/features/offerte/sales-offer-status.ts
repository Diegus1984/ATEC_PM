/**
 * Gli stati del Registro Offerte — etichette e pallino colorato.
 *
 * <p>Sono i cinque dell'applicativo che il registro sostituisce, non i sette dei Preventivi:
 * il registro racconta l'**esito commerciale** (l'abbiamo presa? persa?), il preventivo
 * racconta a che punto è il documento. Vedi `quote-status.ts` per l'altro elenco.</p>
 */

export interface SalesOfferStatusMeta {
  key: string
  label: string
  /** Classe Tailwind del pallino, come vuole `StatusDot`. */
  dot: string
}

export const SALES_OFFER_STATUSES: SalesOfferStatusMeta[] = [
  { key: "bozza", label: "Bozza", dot: "bg-zinc-400" },
  { key: "aperta", label: "Aperta", dot: "bg-blue-500" },
  { key: "presa", label: "Presa", dot: "bg-emerald-500" },
  { key: "persa", label: "Persa", dot: "bg-red-500" },
  { key: "sospesa", label: "Sospesa", dot: "bg-amber-500" },
]

export function salesOfferStatusLabel(key: string): string {
  return SALES_OFFER_STATUSES.find((s) => s.key === key)?.label ?? key
}

export function salesOfferStatusDot(key: string): string {
  return SALES_OFFER_STATUSES.find((s) => s.key === key)?.dot ?? "bg-muted"
}

/** Filtri stato della barra: «Tutti» più i cinque. */
export const SALES_OFFER_STATUS_FILTERS: { value: string; label: string }[] = [
  { value: "", label: "Tutti gli stati" },
  ...SALES_OFFER_STATUSES.map((s) => ({ value: s.key, label: s.label })),
]
