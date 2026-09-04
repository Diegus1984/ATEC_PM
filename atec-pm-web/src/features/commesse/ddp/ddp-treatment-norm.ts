/** Normalizza descrizione trattamento (maiuscolo, spazi singoli) — analogo a destinazioni. */
export function normDdpTreatment(value: string | null | undefined): string {
  return String(value ?? "")
    .toUpperCase()
    .trim()
    .replace(/\s+/g, " ")
}
