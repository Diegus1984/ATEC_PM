/**
 * Codici commessa: riconoscimento e ordinamento. Vive qui (e non dentro un
 * modulo) perché lo usano più sezioni per separare le commesse vere da quelle di
 * servizio a codice libero (INTERNA, SERVICE _ SANGRATO…).
 *
 * Segnalazione #79 — unica regola ovunque:
 * - «Commesse»: codice tipo `C260814_TESTO`, `C260814_900`, `C260814_900_1`
 *   (prefisso `C` + 6/8 cifre data), ordinate crescenti per data/codice;
 * - «Altre Attività»: tutto il resto (testo libero), ordine alfabetico.
 */

/** Etichette di sezione (#79) — usare queste ovunque, niente sinonimi. */
export const COMMESSE_SECTION_LABEL = "Commesse"
export const ALTRE_ATTIVITA_SECTION_LABEL = "Altre Attività"

/**
 * Data (`aaaammgg`) letta dal codice commessa, `""` se il codice non ce l'ha.
 * Riconosce i due formati in uso: `C{aammgg}…` (es. `C240318_146`, dal vecchio
 * gestionale) e `C{aaaammgg}.{NNN}` (es. `C20260731.001`, quello attuale).
 */
export function projectCodeDate(code: string): string {
  const digits = /^C(\d{6}|\d{8})(?!\d)/.exec(code)?.[1]
  if (!digits) return ""
  return digits.length === 6 ? `20${digits}` : digits
}

/** True se il codice è una commessa «vera» (non un'attività a testo libero). */
export function isCommessaCode(code: string): boolean {
  return projectCodeDate(code) !== ""
}

/** Ordina le commesse **in ordine crescente** di data/codice (`projectCodeDate`). */
export function compareProjectCodes(a: string, b: string): number {
  const dateA = projectCodeDate(a)
  const dateB = projectCodeDate(b)
  return dateA === dateB ? a.localeCompare(b, "it") : dateA.localeCompare(dateB)
}

/**
 * Partiziona una lista in Commesse / Altre Attività con l'ordinamento #79.
 * `getCode` legge il codice; `getSortLabel` (opz.) l'etichetta per l'alfabetico.
 */
export function partitionCommesseAltreAttivita<T>(
  items: T[],
  getCode: (item: T) => string,
  getSortLabel: (item: T) => string = getCode
): { commesse: T[]; altreAttivita: T[] } {
  const commesse: T[] = []
  const altreAttivita: T[] = []
  for (const item of items) {
    if (isCommessaCode(getCode(item))) commesse.push(item)
    else altreAttivita.push(item)
  }
  commesse.sort((a, b) => compareProjectCodes(getCode(a), getCode(b)))
  altreAttivita.sort((a, b) =>
    getSortLabel(a).localeCompare(getSortLabel(b), "it")
  )
  return { commesse, altreAttivita }
}
