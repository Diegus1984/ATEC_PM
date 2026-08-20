import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { MessageCircle } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { getActiveChatId } from "@/features/chat/active-chat"
import { chatContextLabel } from "@/features/chat/chat-context"
import { NOTIFICATIONS_BADGE_KEY } from "@/features/notifications/useNotificationsBadge"
import { fetchChatInbox } from "@/lib/api/project-chat"
import type { ChatMessageAlert } from "@/lib/api/types"
import { useChatInboxHub } from "@/lib/signalr/use-chat-inbox-hub"
import { toast } from "@/lib/toast"
import { cn } from "@/lib/utils"

import {
  CHAT_INBOX_BADGE_KEY,
  CHAT_INBOX_LIST_KEY,
  useChatInboxBadge,
} from "./useChatInboxBadge"

function fmtWhen(value: string | null): string {
  if (!value) return ""
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return ""
  return d.toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  })
}

/** Campanella messaggi in header: le chat di tutte le commesse in cui sei dentro. */
export function ChatInboxBell() {
  const [open, setOpen] = React.useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { unreadCount } = useChatInboxBadge()
  const onChange = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_BADGE_KEY })
    void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_LIST_KEY })
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_BADGE_KEY })
  }, [queryClient])

  /**
   * Il riquadro discreto a ogni messaggio ricevuto (#78). Sta qui perché la campanella è
   * l'unico pezzo di chat presente in ogni pagina, e usa la connessione che ha già aperto:
   * un componente a parte avrebbe voluto una seconda presa sull'hub per lo stesso evento.
   * Se la conversazione è già aperta sotto gli occhi, niente riquadro.
   */
  const onMessage = React.useCallback(
    (alert: ChatMessageAlert) => {
      void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_BADGE_KEY })
      void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_LIST_KEY })
      if (getActiveChatId() === alert.chatId) return

      const contesto = alert.contextLabel ? ` · ${alert.contextLabel}` : ""
      toast(`${alert.senderName} — ${alert.chatTitle}${contesto}`, {
        description: alert.preview,
        action: {
          label: "Apri",
          onClick: () => navigate(`/chat?chat=${alert.chatId}`),
        },
      })
    },
    [navigate, queryClient]
  )

  useChatInboxHub(onChange, { onMessage })

  const listQuery = useQuery({
    queryKey: CHAT_INBOX_LIST_KEY,
    queryFn: () => fetchChatInbox(),
    enabled: open,
  })

  const items = listQuery.data ?? []
  const badgeText = unreadCount > 99 ? "99+" : String(unreadCount)

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative"
          aria-label="Chat"
        >
          <MessageCircle />
          {unreadCount > 0 ? (
            <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-amber-500 px-1 text-[10px] font-bold leading-none text-white">
              {badgeText}
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-96 p-0">
        <div className="border-b px-4 py-2.5 text-sm font-semibold">
          Chat
          {unreadCount > 0 ? (
            <span className="ml-1 font-normal text-muted-foreground text-xs">
              ({unreadCount} non letti)
            </span>
          ) : null}
        </div>
        <div className="max-h-[26rem] overflow-y-auto">
          {listQuery.isLoading ? (
            <p className="px-4 py-8 text-center text-sm text-muted-foreground">
              Caricamento…
            </p>
          ) : items.length === 0 ? (
            <p className="px-4 py-8 text-center text-sm text-muted-foreground">
              Nessuna chat.
            </p>
          ) : (
            items.map((chat) => (
              <button
                key={chat.id}
                type="button"
                className={cn(
                  "flex w-full flex-col gap-0.5 border-b px-4 py-2.5 text-left last:border-b-0 hover:bg-accent",
                  chat.unreadCount > 0 && "bg-accent/40"
                )}
                // Sempre alla pagina Chat: dalla #78 una conversazione può non avere commessa,
                // e l'indirizzo /commesse/{id}/chat non saprebbe dove andare.
                onClick={() => {
                  setOpen(false)
                  navigate(`/chat?chat=${chat.id}`)
                }}
              >
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-medium">{chat.title}</span>
                  {chat.unreadCount > 0 ? (
                    <span className="ml-auto rounded-full bg-amber-500 px-1.5 text-[10px] font-semibold text-white">
                      {chat.unreadCount > 99 ? "99+" : chat.unreadCount}
                    </span>
                  ) : null}
                </div>
                <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                  <span className="truncate">{chatContextLabel(chat)}</span>
                  {chat.lastMessageAt ? (
                    <span className="ml-auto shrink-0">
                      {fmtWhen(chat.lastMessageAt)}
                    </span>
                  ) : null}
                </div>
                <span className="truncate text-xs text-muted-foreground">
                  {chat.lastMessagePreview || "Nessun messaggio"}
                </span>
              </button>
            ))
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}
