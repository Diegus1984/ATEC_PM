export interface MentionSegment {
  type: "text" | "mention"
  value: string
}

/** Spezza il testo in segmenti testo / @menzione (stessa logica del client WPF). */
export function splitMessageMentions(message: string): MentionSegment[] {
  const segments: MentionSegment[] = []
  let i = 0

  while (i < message.length) {
    const atIdx = message.indexOf("@", i)
    if (atIdx < 0) {
      segments.push({ type: "text", value: message.slice(i) })
      break
    }

    if (atIdx > i) {
      segments.push({ type: "text", value: message.slice(i, atIdx) })
    }

    const nameStart = atIdx + 1
    let spaceCount = 0
    let nameEnd = nameStart
    while (nameEnd < message.length) {
      if (message[nameEnd] === " ") spaceCount++
      if (
        spaceCount >= 2 ||
        (spaceCount >= 1 &&
          nameEnd > nameStart + 1 &&
          !/[A-Za-zÀ-ÿ']/.test(message[nameEnd]) &&
          message[nameEnd] !== " ")
      ) {
        break
      }
      nameEnd++
    }

    segments.push({ type: "mention", value: message.slice(atIdx, nameEnd) })
    i = nameEnd
  }

  return segments
}

/** Filtro attivo dopo `@` prima del cursore; `null` se nessuna menzione in corso. */
export function getActiveMentionFilter(
  text: string,
  caretPos: number
): string | null {
  const atIndex = text.lastIndexOf("@", Math.max(0, caretPos - 1))
  if (atIndex < 0 || atIndex >= caretPos) return null

  const filter = text.slice(atIndex + 1, caretPos).toLowerCase()
  if (filter.includes(" ") && filter.split(" ").length > 2) return null

  return filter
}

export function insertMentionAtCaret(
  text: string,
  caretPos: number,
  employeeName: string
): { text: string; caret: number } {
  const atIndex = text.lastIndexOf("@", Math.max(0, caretPos - 1))
  if (atIndex < 0) return { text, caret: caretPos }

  const before = text.slice(0, atIndex)
  const after = caretPos < text.length ? text.slice(caretPos) : ""
  const mention = `@${employeeName} `
  const nextText = before + mention + after

  return { text: nextText, caret: before.length + mention.length }
}

export function isImageAttachment(fileName: string): boolean {
  const ext = fileName.split(".").pop()?.toLowerCase() ?? ""
  return ["jpg", "jpeg", "png", "gif", "bmp", "webp"].includes(ext)
}
