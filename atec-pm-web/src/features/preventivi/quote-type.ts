/** Etichetta reparto preventivo (SERVICE = reparto Service, non «servizio» generico). */
export function quoteTypeLabel(quoteType: string): string {
  return quoteType === "IMPIANTO" ? "Impianto" : "Service"
}

/** Badge compatto in griglia/dettaglio. */
export function quoteTypeBadge(quoteType: string): string {
  return quoteType === "IMPIANTO" ? "IMP" : "Service"
}
