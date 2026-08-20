/**
 * Scarica un contenuto generato dal client come file.
 *
 * Esisteva copiato in una mezza dozzina di punti (export CSV, .xls, PDF…): qui sta
 * una volta sola. `revokeObjectURL` è differito perché Safari annulla il download
 * se l'URL viene revocato nello stesso task del click.
 */
export function downloadFile(
  fileName: string,
  content: BlobPart,
  mime: string
): void {
  const blob = new Blob([content], { type: mime })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** Nome file sicuro: solo lettere, cifre, `_` e `-`. */
export function safeFileName(value: string): string {
  return String(value)
    .replace(/[^A-Za-z0-9_-]+/g, "_")
    .replace(/^_+|_+$/g, "")
}

/** Timestamp `aaaammgg` per i nomi dei file esportati. */
export function fileStamp(date = new Date()): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, "0")
  const d = String(date.getDate()).padStart(2, "0")
  return `${y}${m}${d}`
}
