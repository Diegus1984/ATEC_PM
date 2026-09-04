/**
 * Il numero composto dell'offerta, calcolato lato client.
 *
 * <p>Il server lo restituisce già pronto in `composed`, ma la scheda ne ha bisogno **mentre
 * si digita**, prima di salvare: cambiando tipo o venditore l'anteprima deve seguire. Questa
 * è quindi una seconda copia della regola, e per questo ha un test accanto — è la copia che
 * si può disallineare da `SalesOfferRules.Composed` senza che nessuno se ne accorga.</p>
 */

/** Progressivo a tre cifre; stringa vuota se il numero non c'è (righe storiche). */
export function pad3(numero: number | null | undefined): string {
  if (numero === null || numero === undefined) return ""
  return String(numero).padStart(3, "0")
}

/**
 * `tipo + progressivo(3) + «-» + anno + «-» + sigla` → `S097-2026-EC`.
 * I pezzi mancanti spariscono senza rompere il resto: una bozza appena riservata ha solo
 * numero e anno.
 */
export function composeOfferNumber(
  tipo: string | null | undefined,
  numero: number | null | undefined,
  anno: number | null | undefined,
  venditore: string | null | undefined
): string {
  return `${tipo ?? ""}${pad3(numero)}-${anno ?? ""}-${venditore ?? ""}`
}

/**
 * La chance ammessa: 0–100 a passi di 10, oppure «non valutata».
 * `null` è un valore legittimo e diverso da 0 — «non l'ho ancora stimata» non è «zero».
 */
export const CHANCE_LEVELS: (number | null)[] = [
  null, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100,
]

export function chanceLabel(chance: number | null | undefined): string {
  return chance === null || chance === undefined ? "Non valutata" : `${chance}%`
}
