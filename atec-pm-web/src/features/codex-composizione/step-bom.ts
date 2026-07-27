/**
 * Parser della distinta (BOM) da file STEP AP203 esportati da SolidWorks.
 *
 * Catena entità ISO 10303-21:
 *   NEXT_ASSEMBLY_USAGE_OCCURRENCE (padre, figlio) → PRODUCT_DEFINITION →
 *   PRODUCT_DEFINITION_FORMATION → PRODUCT (nome componente = nome file CAD).
 *
 * Il nome componente segue la convenzione ATEC `<codice> - <descrizione>`
 * (es. `301030823.001 - Vite TE M6x12`), da cui si estrae il codice Codex.
 */

export interface StepBomItem {
  /** Codice estratto dal prefisso del nome componente; null se il nome non inizia con un codice. */
  code: string | null
  /** Nome completo del componente nel modello CAD (senza suffisso configurazione). */
  name: string
  quantity: number
}

export interface StepBom {
  /** Codice della radice (l'assieme del file); null se il nome radice non inizia con un codice. */
  rootCode: string | null
  rootName: string
  /**
   * Soli figli DIRETTI della radice: le distinte dei sotto-compositi (es. i 5xx
   * dentro un 601) vivono nelle rispettive composizioni, non in quella del padre.
   */
  items: StepBomItem[]
}

/**
 * Decodifica le stringhe ISO 10303-21 (`\X2\00A0\X0` → carattere unicode) e normalizza
 * gli spazi: `\s` copre anche il non-breaking space (U+00A0) usato da SolidWorks nei nomi.
 */
function decodeStepString(value: string): string {
  return value
    .replace(/\\X2\\([0-9A-Fa-f]{4})\\X0\\/g, (_, hex: string) =>
      String.fromCharCode(parseInt(hex, 16))
    )
    .replace(/\s+/g, " ")
    .trim()
}

/** Rimuove il suffisso configurazione SolidWorks (`_0000`) dal nome componente. */
function cleanName(name: string): string {
  return name.replace(/_\d{4}$/, "").trim()
}

/** Estrae il codice dal prefisso del nome (12 cifre, punto opzionale prima delle ultime 3). */
function extractCode(name: string): string | null {
  const match = /^([1-7]\d{8}\.?\d{3})\b/.exec(name)
  return match ? match[1] : null
}

/** Confronto codici ignorando i punti (nel DB sono salvati senza). */
export function sameCode(a: string, b: string): boolean {
  return a.replace(/\./g, "") === b.replace(/\./g, "")
}

/**
 * Estrae la distinta dei figli diretti della radice da un file STEP.
 * Ritorna null se il testo non contiene una struttura assieme riconoscibile
 * (es. STEP di una singola parte, o file non STEP).
 */
export function parseStepBom(text: string): StepBom | null {
  // PRODUCT: #id = PRODUCT ( 'nome', ... )
  const products = new Map<string, string>()
  for (const m of text.matchAll(/#(\d+)\s*=\s*PRODUCT\s*\(\s*'([^']*)'/g)) {
    products.set(m[1], cleanName(decodeStepString(m[2])))
  }

  // FORMATION: #id = PRODUCT_DEFINITION_FORMATION[_…] ( '…', '…', #product … )
  const formations = new Map<string, string>()
  for (const m of text.matchAll(
    /#(\d+)\s*=\s*PRODUCT_DEFINITION_FORMATION\w*\s*\(\s*'[^']*'\s*,\s*'[^']*'\s*,\s*#(\d+)/g
  )) {
    formations.set(m[1], m[2])
  }

  // PRODUCT_DEFINITION: #id = PRODUCT_DEFINITION ( '…', '…', #formation, #ctx )
  const definitions = new Map<string, string>()
  for (const m of text.matchAll(
    /#(\d+)\s*=\s*PRODUCT_DEFINITION\s*\(\s*'[^']*'\s*,\s*'[^']*'\s*,\s*#(\d+)/g
  )) {
    definitions.set(m[1], m[2])
  }

  // NAUO: ( 'NAUOx', ' ', ' ', #padrePD, #figlioPD, $ )
  const occurrences: Array<[parent: string, child: string]> = []
  for (const m of text.matchAll(
    /NEXT_ASSEMBLY_USAGE_OCCURRENCE\s*\(\s*'[^']*'\s*,\s*'[^']*'\s*,\s*'[^']*'\s*,\s*#(\d+)\s*,\s*#(\d+)/g
  )) {
    occurrences.push([m[1], m[2]])
  }
  if (occurrences.length === 0) return null

  const nameOf = (definitionId: string): string => {
    const productId = formations.get(definitions.get(definitionId) ?? "")
    return products.get(productId ?? "") ?? ""
  }

  // Radice = una definizione che compare come padre ma mai come figlio.
  const childIds = new Set(occurrences.map(([, child]) => child))
  const rootId = occurrences.map(([parent]) => parent).find((id) => !childIds.has(id))
  if (!rootId) return null

  // Quantità = numero di occorrenze dello stesso componente sotto la radice.
  const counts = new Map<string, number>()
  for (const [parent, child] of occurrences) {
    if (parent !== rootId) continue
    const name = nameOf(child)
    if (!name) continue
    counts.set(name, (counts.get(name) ?? 0) + 1)
  }
  if (counts.size === 0) return null

  const rootName = nameOf(rootId)
  return {
    rootCode: extractCode(rootName),
    rootName,
    items: [...counts.entries()].map(([name, quantity]) => ({
      code: extractCode(name),
      name,
      quantity,
    })),
  }
}
