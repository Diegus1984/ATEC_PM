// Formattazioni condivise ATEC PM. Tieni qui i formatter riusati tra le pagine
// (importi, ecc.) così l'aspetto resta coerente ovunque.

/**
 * Formatta un importo in euro: cultura it-IT, 2 decimali, simbolo € in coda.
 * Es. `6.07` → `"6,07 €"`, `265.96` → `"265,96 €"`.
 * Valori nullish o NaN → `"—"`.
 */
export function euro(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) {
    return "—"
  }
  return `${value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} €`
}
