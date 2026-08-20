/** Formattazioni della Trasferta che non sono importi (giorni, ore). */

/** Numero all'italiana senza decimali forzati: `6` → «6», `4.5` → «4,5», null → «—». */
export function plainNum(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return "—"
  return String(Math.round(value * 100) / 100).replace(".", ",")
}
