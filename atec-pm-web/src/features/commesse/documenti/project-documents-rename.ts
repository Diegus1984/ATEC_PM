const INVALID_NAME_CHARS = /[\\/:*?"<>|]/

/** Separa nome file ed estensione (ultimo punto, come Path.GetExtension). */
export function splitFileName(fileName: string): {
  stem: string
  extension: string
} {
  const lastDot = fileName.lastIndexOf(".")
  if (lastDot <= 0) {
    return { stem: fileName, extension: "" }
  }
  return {
    stem: fileName.slice(0, lastDot),
    extension: fileName.slice(lastDot),
  }
}

/** Costruisce il nuovo nome file mantenendo l'estensione originale. */
export function buildFileRenameName(
  originalName: string,
  stemInput: string
): string | null {
  const stem = stemInput.trim()
  if (!stem || INVALID_NAME_CHARS.test(stem)) {
    return null
  }
  const { extension } = splitFileName(originalName)
  return `${stem}${extension}`
}
