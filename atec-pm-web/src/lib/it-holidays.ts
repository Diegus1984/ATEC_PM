// Festività italiane + giorni "rossi" (weekend/festivi) per le date del foglio MoM
// (giorno della settimana sotto le date, rosso se festivo — prototipo Gestione_MoM_v9).

export const WEEKDAYS_SHORT = ["dom", "lun", "mar", "mer", "gio", "ven", "sab"]

/** Domenica di Pasqua (algoritmo di Gauss/Meeus). */
export function easterSunday(year: number): Date {
  const a = year % 19
  const b = Math.floor(year / 100)
  const c = year % 100
  const d = Math.floor(b / 4)
  const e = b % 4
  const f = Math.floor((b + 8) / 25)
  const g = Math.floor((b - f + 1) / 3)
  const h = (19 * a + b - d - g + 15) % 30
  const i = Math.floor(c / 4)
  const k = c % 4
  const l = (32 + 2 * e + 2 * i - h - k) % 7
  const m = Math.floor((a + 11 * h + 22 * l) / 451)
  const month = Math.floor((h + l - 7 * m + 114) / 31)
  const day = ((h + l - 7 * m + 114) % 31) + 1
  return new Date(year, month - 1, day)
}

const FIXED_HOLIDAYS = new Set([
  "1-1", // Capodanno
  "1-6", // Epifania
  "4-25", // Liberazione
  "5-1", // Festa del lavoro
  "6-2", // Repubblica
  "8-15", // Ferragosto
  "11-1", // Ognissanti
  "12-8", // Immacolata
  "12-25", // Natale
  "12-26", // S. Stefano
])

/** Festività nazionale italiana (fisse + lunedì dell'Angelo). */
export function isItHoliday(date: Date): boolean {
  if (FIXED_HOLIDAYS.has(`${date.getMonth() + 1}-${date.getDate()}`)) return true
  const easter = easterSunday(date.getFullYear())
  const easterMonday = new Date(easter)
  easterMonday.setDate(easter.getDate() + 1)
  return (
    date.getMonth() === easterMonday.getMonth() &&
    date.getDate() === easterMonday.getDate()
  )
}

/** Giorno "rosso": weekend o festività nazionale. */
export function isRedDay(date: Date): boolean {
  const weekday = date.getDay()
  return weekday === 0 || weekday === 6 || isItHoliday(date)
}
