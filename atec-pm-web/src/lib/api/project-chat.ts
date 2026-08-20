import { apiDelete, apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import { getSession } from "@/lib/auth/session"
import type {
  ApiResponse,
  ChatInboxBadge,
  ChatListItem,
  ChatMessagesPage,
  ChatParticipant,
} from "@/lib/api/types"

const MAX_ATTACHMENT_BYTES = 20 * 1024 * 1024

function apiBase(): string {
  return import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? ""
}

function authHeaders(): Headers {
  const headers = new Headers()
  const token = getSession()?.token
  if (token) headers.set("Authorization", `Bearer ${token}`)
  return headers
}

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result
      if (typeof result !== "string") {
        reject(new Error("Lettura file fallita"))
        return
      }
      const base64 = result.includes(",") ? result.split(",")[1] : result
      resolve(base64)
    }
    reader.onerror = () => reject(new Error("Lettura file fallita"))
    reader.readAsDataURL(file)
  })
}

export async function fetchChats(projectId: number): Promise<ChatListItem[]> {
  const r = await apiGet<ApiResponse<ChatListItem[]>>(
    `/api/chat/project/${projectId}`
  )
  return unwrapApi(r)
}

/**
 * Chat in cui sono dentro, di qualsiasi commessa (e senza commessa). `limit` alto per la
 * pagina «Chat» del menu, che le vuole tutte; la campanella si tiene il valore di default.
 */
export async function fetchChatInbox(limit?: number): Promise<ChatListItem[]> {
  const query = limit ? `?limit=${limit}` : ""
  const r = await apiGet<ApiResponse<ChatListItem[]>>(`/api/chat/inbox${query}`)
  return unwrapApi(r)
}

export async function fetchChatInboxBadge(): Promise<number> {
  const r = await apiGet<ApiResponse<ChatInboxBadge>>("/api/chat/inbox/badge")
  return unwrapApi(r).unreadCount
}

export async function fetchChatMessages(
  chatId: number,
  beforeId = 0,
  limit = 50
): Promise<ChatMessagesPage> {
  const r = await apiGet<ApiResponse<ChatMessagesPage>>(
    `/api/chat/${chatId}/messages?beforeId=${beforeId}&limit=${limit}`
  )
  return unwrapApi(r)
}

export async function fetchChatParticipants(
  chatId: number
): Promise<ChatParticipant[]> {
  const r = await apiGet<ApiResponse<ChatParticipant[]>>(
    `/api/chat/${chatId}/participants`
  )
  return unwrapApi(r)
}

export async function createChat(req: {
  /** null = voce «— Nessuna —» della tendina commessa/attività. */
  projectId: number | null
  title: string
  participantIds: number[]
}): Promise<number> {
  const r = await apiPost<ApiResponse<number>>("/api/chat", req)
  return unwrapApi(r)
}

export async function sendChatMessage(
  chatId: number,
  message: string,
  replyToMessageId?: number | null
): Promise<number> {
  const r = await apiPost<ApiResponse<number>>(`/api/chat/${chatId}/messages`, {
    chatId,
    message,
    replyToMessageId: replyToMessageId ?? null,
  })
  return unwrapApi(r)
}

export async function sendChatMessageWithAttachment(
  chatId: number,
  file: File,
  message?: string,
  replyToMessageId?: number | null
): Promise<number> {
  if (file.size > MAX_ATTACHMENT_BYTES) {
    throw new Error("File troppo grande (max 20 MB).")
  }

  const fileData = await fileToBase64(file)
  const r = await apiPost<ApiResponse<number>>(
    `/api/chat/${chatId}/messages/with-attachment`,
    {
      chatId,
      message: message?.trim() || `📎 ${file.name}`,
      fileName: file.name,
      fileData,
      replyToMessageId: replyToMessageId ?? null,
    }
  )
  return unwrapApi(r)
}

export async function fetchChatAttachmentBlob(messageId: number): Promise<Blob> {
  const response = await fetch(
    `${apiBase()}/api/chat/messages/${messageId}/attachment`,
    { headers: authHeaders() }
  )
  if (!response.ok) {
    throw new Error("Impossibile caricare l'allegato")
  }
  return response.blob()
}

export async function downloadChatAttachment(
  messageId: number,
  fileName: string
): Promise<void> {
  const blob = await fetchChatAttachmentBlob(messageId)
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}

export async function deleteChatMessage(messageId: number): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(
    `/api/chat/messages/${messageId}`
  )
  unwrapApi(r)
}

export async function deleteChat(chatId: number): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(`/api/chat/${chatId}`)
  unwrapApi(r)
}

export async function leaveChat(chatId: number): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>(`/api/chat/${chatId}/leave`, {})
  unwrapApi(r)
}

export async function addChatParticipant(
  chatId: number,
  employeeId: number
): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>(
    `/api/chat/${chatId}/participants`,
    employeeId
  )
  unwrapApi(r)
}

export async function removeChatParticipant(
  chatId: number,
  employeeId: number
): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(
    `/api/chat/${chatId}/participants/${employeeId}`
  )
  unwrapApi(r)
}

export async function markChatRead(chatId: number): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>(`/api/chat/${chatId}/mark-read`, {})
  unwrapApi(r)
}
