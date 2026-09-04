/** Normalizza descrizione destinazione (maiuscolo, spazi singoli) — come nel prototipo demo V1. */
export function normDdpDestination(value: string | null | undefined): string {
  return String(value ?? "")
    .toUpperCase()
    .trim()
    .replace(/\s+/g, " ")
}
