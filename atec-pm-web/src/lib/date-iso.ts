// Helper di conversione ISO date-only (`yyyy-MM-dd`) ⇄ Date locale, senza
// scivolamenti di fuso (si costruisce la Date da anno/mese/giorno locali).
// Condivisi tra il DateField e le pagine che devono confrontare/limitare le date.

export function isoToDate(value: string | null | undefined): Date | undefined {
  if (!value) return undefined
  const [year, month, day] = value.slice(0, 10).split("-").map(Number)
  if (!year || !month || !day) return undefined
  return new Date(year, month - 1, day)
}

/**
 * Porzione date-only (`yyyy-MM-dd`) di un valore ISO: taglia l'eventuale orario.
 * I confronti tra date nell'app si fanno sempre su questa forma, mai su Date.
 */
export function toDateOnly(value: string | null | undefined): string | null {
  return value ? value.slice(0, 10) : null
}

export function dateToIso(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, "0")
  const day = String(date.getDate()).padStart(2, "0")
  return `${year}-${month}-${day}`
}

/**
 * Formato data breve della UI: gg/mm/aa (anno a 2 cifre per compattezza).
 * Standard per griglie e campi; gli export documentali restano a 4 cifre.
 * Accetta Date o stringa (ISO date-only/datetime o comunque parsabile).
 */
export function formatDateShort(value: Date | string | null | undefined): string {
  if (!value) return ""
  const date = value instanceof Date ? value : isoToDate(value) ?? new Date(value)
  if (!date || Number.isNaN(date.getTime())) return ""
  return date.toLocaleDateString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "2-digit",
  })
}

/** Come `formatDateShort`, ma con il trattino al posto della stringa vuota:
 *  è la forma usata dalle celle di griglia. */
export function formatDateOrDash(value: Date | string | null | undefined): string {
  return formatDateShort(value) || "—"
}
