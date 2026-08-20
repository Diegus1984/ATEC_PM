import { useEffect, useRef, useState } from "react"
import { useQuery } from "@tanstack/react-query"

import { fetchChatInboxBadge } from "@/lib/api/project-chat"

const INTERVAL_BASE = 30_000

export const CHAT_INBOX_BADGE_KEY = ["chat-inbox-badge"] as const
export const CHAT_INBOX_LIST_KEY = ["chat-inbox"] as const

export function useChatInboxBadge() {
  const [intervalMs, setIntervalMs] = useState(INTERVAL_BASE)
  const lastCount = useRef(0)

  const query = useQuery({
    queryKey: CHAT_INBOX_BADGE_KEY,
    queryFn: fetchChatInboxBadge,
    refetchInterval: intervalMs,
    refetchIntervalInBackground: false,
  })

  const updatedAt = query.dataUpdatedAt
  useEffect(() => {
    if (query.data === undefined) return
    const count = query.data
    setIntervalMs(count > lastCount.current ? 15_000 : INTERVAL_BASE)
    lastCount.current = count
  }, [updatedAt, query.data])

  return { unreadCount: query.data ?? 0 }
}
