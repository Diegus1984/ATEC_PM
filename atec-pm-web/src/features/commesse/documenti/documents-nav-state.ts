// ── Documenti di commessa: posizione corrente nell'history state ───────────
//
// La cartella aperta (e l'eventuale file di cui mostrare l'anteprima) vivono
// nell'history state di React Router, non nella querystring: l'URL resta
// `/commesse/:id/documents` senza esporre il percorso delle cartelle, mentre
// avanti/indietro del browser continuano a navigare tra le cartelle e il
// refresh mantiene la posizione (il browser conserva l'history state).
//
// I vecchi link con `?path=` / `?preview=` restano validi: vengono letti come
// fallback e ripuliti dall'URL al primo render (vedi `ProjectDocuments`).

const DOC_PATH_KEY = "docPath"
const DOC_PREVIEW_KEY = "docPreview"

export interface DocumentsNav {
  /** Cartella corrente, relativa alla radice documenti della commessa. */
  docPath: string
  /** File di cui aprire l'anteprima (consumato una volta sola). */
  docPreview: string | null
}

function asRecord(state: unknown): Record<string, unknown> {
  return state && typeof state === "object"
    ? (state as Record<string, unknown>)
    : {}
}

/** Legge la posizione corrente dallo state, con fallback ai query param legacy. */
export function readDocumentsNav(
  state: unknown,
  search: URLSearchParams
): DocumentsNav {
  const raw = asRecord(state)
  const path = raw[DOC_PATH_KEY]
  const preview = raw[DOC_PREVIEW_KEY]
  return {
    docPath: typeof path === "string" ? path : search.get("path") ?? "",
    docPreview: typeof preview === "string" ? preview : search.get("preview"),
  }
}

/**
 * History state da passare a `navigate`: sostituisce la posizione documenti
 * conservando le altre chiavi già presenti (es. `fromGlobal`).
 */
export function withDocumentsNav(
  state: unknown,
  nav: { docPath?: string; docPreview?: string | null }
): Record<string, unknown> {
  const next = { ...asRecord(state) }
  delete next[DOC_PATH_KEY]
  delete next[DOC_PREVIEW_KEY]
  if (nav.docPath) {
    next[DOC_PATH_KEY] = nav.docPath
  }
  if (nav.docPreview) {
    next[DOC_PREVIEW_KEY] = nav.docPreview
  }
  return next
}

/** `true` se l'URL porta ancora i vecchi parametri dei Documenti. */
export function hasLegacyDocumentsParams(search: URLSearchParams): boolean {
  return search.has("path") || search.has("preview")
}

/** Querystring senza i vecchi parametri dei Documenti (gli altri restano). */
export function stripLegacyDocumentsParams(search: URLSearchParams): string {
  const next = new URLSearchParams(search)
  next.delete("path")
  next.delete("preview")
  return next.toString()
}
