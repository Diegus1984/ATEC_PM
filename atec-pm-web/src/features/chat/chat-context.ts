import type { ChatListItem } from "@/lib/api/types"
import { isCommessaCode } from "@/lib/project-code"

/**
 * Contesto di una chat (#78), con la stessa regola delle barre laterali PM: codice che
 * comincia con la data = commessa vera, codice libero (INTERNA, SERVICE…) = altra attività,
 * nessuna commessa = chat libera.
 */
export type ChatGroupKey = "projects" | "other" | "none"

export const CHAT_GROUP_LABELS: Record<ChatGroupKey, string> = {
  projects: "Commesse",
  other: "Altre Attività",
  none: "Senza commessa",
}

export function groupOfChat(chat: ChatListItem): ChatGroupKey {
  if (chat.projectId == null) return "none"
  return isCommessaCode(chat.projectCode) ? "projects" : "other"
}

/** Etichetta del contenitore: «C20260814.001 · Linea di montaggio» oppure «Senza commessa». */
export function chatContextLabel(chat: ChatListItem): string {
  if (chat.projectId == null) return "Senza commessa"
  if (!chat.projectCode) return "Commessa"
  return chat.projectTitle
    ? `${chat.projectCode} · ${chat.projectTitle}`
    : chat.projectCode
}
