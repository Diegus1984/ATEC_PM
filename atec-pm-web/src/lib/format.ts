// Formattazioni condivise ATEC PM. Tieni qui i formatter riusati tra le pagine
// (importi, ecc.) così l'aspetto resta coerente ovunque.

/**
 * Formatta un importo in euro: 2 decimali, virgola decimale, punto delle
 * migliaia SEMPRE presente, simbolo € in coda.
 * Es. `6.07` → `"6,07 €"`, `4000` → `"4.000,00 €"`, `265.96` → `"265,96 €"`.
 * Valori nullish o NaN → `"—"`.
 *
 * Formattazione manuale (niente toLocaleString): il CLDR it-IT ha
 * minimumGroupingDigits=2 e ometterebbe il punto per 1.000–9.999.
 */
export function euro(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) {
    return "—"
  }
  const negativo = value < 0
  const [interi, decimali] = Math.abs(value).toFixed(2).split(".")
  const raggruppati = interi.replace(/\B(?=(\d{3})+(?!\d))/g, ".")
  return `${negativo ? "-" : ""}${raggruppati},${decimali} €`
}

/**
 * Percentuale all'italiana con 2 decimali e simbolo attaccato: `18.42` → `"18,42%"`,
 * `-3.1` → `"-3,10%"`. Nullish o NaN → `"—"`: una percentuale non calcolabile non è «0%».
 */
export function percent(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) {
    return "—"
  }
  return `${value.toFixed(2).replace(".", ",")}%`
}

/** Numero con 2 decimali all'italiana, senza simbolo (per input e celle calcolate). */
export function fmt2(value: number): string {
  return value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

/**
 * Parsing numerico tollerante al formato italiano. Vuoto o non numerico → 0.
 * Se c'è la virgola, i punti sono separatori delle migliaia (`1.234,50` → 1234.5);
 * senza virgola il punto resta decimale (`1234.5` → 1234.5), come da sempre.
 * Ignora spazi ed eventuale simbolo €, così incollare un importo formattato funziona.
 */
export function parseDecimal(value: string | null | undefined): number {
  const raw = (value ?? "").replace(/[\s€]/g, "")
  if (!raw) return 0
  const normalized = raw.includes(",")
    ? raw.replace(/\./g, "").replace(",", ".")
    : raw
  const n = Number(normalized)
  return Number.isFinite(n) ? n : 0
}

/** Testo di cella: trattino quando il valore è vuoto o solo spazi. */
export function dash(value: string | null | undefined): string {
  return value && value.trim() ? value : "—"
}

/**
 * Formattazione canonica codice Codex / ATEC:
 * rimuove tutti i punti e inserisce un solo punto prima delle ultime 3 cifre.
 * Es. `101170426001` → `101170426.001`.
 */
export function formatCodexCode(codice: string | null | undefined): string {
  if (!codice) return ""
  const raw = codice.replace(/\./g, "").trim()
  if (raw.length > 3) {
    return `${raw.slice(0, raw.length - 3)}.${raw.slice(raw.length - 3)}`
  }
  return raw
}

/** Ore con al più un decimale, all'italiana e senza unità (es. `7,5`). */
export function fmtHours(hours: number): string {
  return hours.toLocaleString("it-IT", { maximumFractionDigits: 1 })
}

/** Dimensione file leggibile (B / KB / MB). */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/**
 * Decodifica le entità HTML nei testi importati (es. le descrizioni del Codex
 * remoto contengono `&#8217;` al posto dell'apostrofo). Testo puro in ingresso e
 * in uscita: l'eventuale markup viene reso come testo, mai interpretato.
 */
const entityTextarea =
  typeof document !== "undefined" ? document.createElement("textarea") : null

export function decodeHtmlEntities(text: string | null | undefined): string {
  const value = text ?? ""
  if (!value.includes("&") || !entityTextarea) return value
  entityTextarea.innerHTML = value.replace(/</g, "&lt;")
  return entityTextarea.value
}
