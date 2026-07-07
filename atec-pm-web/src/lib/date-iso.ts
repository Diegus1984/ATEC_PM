// Helper di conversione ISO date-only (`yyyy-MM-dd`) ⇄ Date locale, senza
// scivolamenti di fuso (si costruisce la Date da anno/mese/giorno locali).
// Condivisi tra il DateField e le pagine che devono confrontare/limitare le date.

export function isoToDate(value: string | null | undefined): Date | undefined {
  if (!value) return undefined
  const [year, month, day] = value.slice(0, 10).split("-").map(Number)
  if (!year || !month || !day) return undefined
  return new Date(year, month - 1, day)
}

export function dateToIso(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, "0")
  const day = String(date.getDate()).padStart(2, "0")
  return `${year}-${month}-${day}`
}
