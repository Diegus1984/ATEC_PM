// Le durate come le scrive il motore («8h 0m») e come si leggono a video («8h 00m»), più le
// etichette delle fasce CCNL: funzioni pure, condivise dal cartellino di una persona e dal
// «Controllo di ieri». Stanno fuori da celle-cartellino.tsx perché quel file esporta solo
// componenti (regola del fast refresh).

/** Le fasce della Circolare n. 12 del 23.12.2024 (colonna «Non a turni»). */
export const FASCE_LABELS: Record<string, string> = {
  A: "Straordinario diurno (20%)",
  B1: "Lavoro notturno fino alle 22 (25%)",
  B2: "Lavoro notturno oltre le 22 (35%)",
  C: "Festivo (55%)",
  D: "Festivo con riposo comp. (10%)",
  E: "Straord. festivo (55%)",
  F: "Straord. festivo con riposo comp. (35%)",
  G: "Straordinario notturno (50/60%)",
  H: "Notturno festivo (35%)",
  L: "Straord. notturno festivo (75%)",
  M: "Straord. nott. festivo con riposo comp. (55%)",
}

export function minutiDa(durata: string): number {
  const m = /^(\d+)h (\d+)m$/.exec(durata)
  return m ? Number(m[1]) * 60 + Number(m[2]) : 0
}

export function durata(minuti: number): string {
  return `${Math.floor(minuti / 60)}h ${String(minuti % 60).padStart(2, "0")}m`
}

/** «8h 0m» → «8h 00m»; «---» e vuoto restano com'erano. */
export function oreLeggibili(valore: string): string {
  const m = /^(\d+)h (\d+)m$/.exec(valore)
  return m ? `${m[1]}h ${m[2].padStart(2, "0")}m` : valore
}

export function isZero(valore: string): boolean {
  return !valore || valore === "---" || minutiDa(valore) === 0
}
