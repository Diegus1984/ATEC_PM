import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Check,
  CheckCheck,
  ChevronRight,
  LogOut,
  Plus,
  Reply,
  Search,
  Trash2,
  Users,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Input } from "@/components/ui/input"
import { ChatComposer } from "@/features/commesse/chat/ChatComposer"
import { ChatMessageAttachment } from "@/features/commesse/chat/ChatMessageAttachment"
import { ChatMessageBody } from "@/features/commesse/chat/ChatMessageBody"
import { ChatParticipantsPopover } from "@/features/commesse/chat/ChatParticipantsPopover"
import { CHAT_INBOX_BADGE_KEY } from "@/features/commesse/chat/useChatInboxBadge"
import { NewChatDialog } from "@/features/chat/NewChatDialog"
import { setActiveChatId } from "@/features/chat/active-chat"
import {
  CHAT_GROUP_LABELS,
  chatContextLabel,
  groupOfChat,
  type ChatGroupKey,
} from "@/features/chat/chat-context"
import { fetchRealEmployees } from "@/lib/api/employees"
import {
  deleteChat,
  deleteChatMessage,
  fetchChatInbox,
  fetchChatMessages,
  fetchChatParticipants,
  fetchChats,
  leaveChat,
  markChatRead,
  sendChatMessage,
  sendChatMessageWithAttachment,
} from "@/lib/api/project-chat"
import type {
  ChatChange,
  ChatListItem,
  ChatMessage,
  ChatTyping,
} from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { useChatInboxHub } from "@/lib/signalr/use-chat-inbox-hub"
import { useProjectChatHub } from "@/lib/signalr/use-project-chat-hub"
import { cn } from "@/lib/utils"

/**
 * Dove sta girando la chat: dentro una commessa (tab «Chat» del dettaglio) oppure nella
 * pagina globale del menu principale, che le mostra tutte divise per contesto.
 */
export type ChatScope =
  | { kind: "project"; projectId: number; label?: string }
  | { kind: "all" }

/** Chiave della lista «tutte le mie chat»: sotto `chat-inbox`, così una sola invalidazione le prende entrambe. */
export const CHAT_ALL_LIST_KEY = ["chat-inbox", "tutte"] as const

const CHAT_ALL_LIMIT = 500

function fmtTime(value: string): string {
  const d = new Date(value)
  return Number.isNaN(d.getTime())
    ? ""
    : d.toLocaleString("it-IT", { hour: "2-digit", minute: "2-digit" })
}

function fmtListTime(value: string | null): string {
  if (!value) return ""
  const d = new Date(value)
  return Number.isNaN(d.getTime())
    ? ""
    : d.toLocaleString("it-IT", {
        day: "2-digit",
        month: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
      })
}

function fmtDateSeparator(value: string): string {
  const d = new Date(value)
  return Number.isNaN(d.getTime())
    ? ""
    : d.toLocaleDateString("it-IT", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
      })
}

function canDeleteMessage(message: ChatMessage): boolean {
  if (message.isMine) return true
  // I messaggi ALTRUI li tocca solo chi modera la chat.
  return canWriteFeature("action.moderate_chat")
}

function mergeMessages(older: ChatMessage[], latest: ChatMessage[]): ChatMessage[] {
  const byId = new Map<number, ChatMessage>()
  for (const m of older) byId.set(m.id, m)
  for (const m of latest) byId.set(m.id, m)
  return [...byId.values()].sort((a, b) => a.id - b.id)
}

function ReadTicks({ message }: { message: ChatMessage }) {
  if (!message.isMine) return null
  const allRead =
    message.otherParticipantCount > 0 &&
    message.readByCount >= message.otherParticipantCount
  const someRead = message.readByCount > 0
  if (allRead || someRead) {
    return (
      <CheckCheck
        className={cn("size-3 shrink-0", allRead ? "opacity-100" : "opacity-70")}
        aria-label={allRead ? "Letto da tutti" : "Letto"}
      />
    )
  }
  return <Check className="size-3 shrink-0 opacity-70" aria-label="Inviato" />
}

/** Riga della lista chat: titolo, pallino arancione dei non letti, contesto e anteprima. */
function ChatListRow({
  chat,
  selected,
  showContext,
  canDelete,
  onSelect,
  onAddParticipants,
  onLeave,
  onDelete,
}: {
  chat: ChatListItem
  selected: boolean
  showContext: boolean
  canDelete: boolean
  onSelect: () => void
  onAddParticipants: () => void
  onLeave: () => void
  onDelete: () => void
}) {
  const canLeave = chat.isParticipant

  // 🪤 «Aggiungi partecipanti…» apre un POPOVER, non un dialogo, ed è l'unica voce di menu del
  // progetto a farlo. Differenza che si paga: chiudendosi, il menu rimette il fuoco dove stava
  // prima del tasto destro (il FocusScope di Radix, allo smontaggio). Quel `focusin` arriva a
  // pannello GIÀ aperto, e il Popover — che è non modale — lo legge come «hai interagito fuori»
  // e si richiude nello stesso istante: all'utente sembra che la voce non faccia niente
  // (BUG-016). Gli altri due comandi si salvano solo perché l'AlertDialog della conferma mette
  // il veto su `onInteractOutside` per conto suo.
  // Qui il ripristino del fuoco si sopprime SOLO quando è stata quella voce ad aprire qualcosa:
  // chiudendo il menu con Esc o scegliendo le altre voci, il fuoco torna alla riga come sempre.
  const apreIlPannelloPartecipanti = React.useRef(false)

  const row = (
    <button
      type="button"
      className={cn(
        "group flex w-full flex-col gap-0.5 rounded-md px-2 py-1.5 text-left hover:bg-accent",
        selected && "bg-accent"
      )}
      onClick={onSelect}
    >
      <div className="flex items-center gap-1">
        <span className="flex-1 truncate text-sm font-medium">{chat.title}</span>
        {chat.unreadCount > 0 ? (
          <span className="rounded-full bg-amber-500 px-1.5 text-[10px] font-semibold text-white">
            {chat.unreadCount > 99 ? "99+" : chat.unreadCount}
          </span>
        ) : null}
      </div>
      {showContext ? (
        <span className="truncate text-[10px] font-medium text-muted-foreground">
          {chatContextLabel(chat)}
        </span>
      ) : null}
      <div className="flex items-center gap-2 text-[10px] text-muted-foreground">
        <span>
          {chat.participantCount} partecip. · {chat.messageCount} msg
        </span>
        {chat.lastMessageAt ? (
          <span className="ml-auto shrink-0">{fmtListTime(chat.lastMessageAt)}</span>
        ) : null}
      </div>
      <span className="truncate text-xs text-muted-foreground">
        {chat.lastMessagePreview || "Nessun messaggio"}
      </span>
    </button>
  )

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{row}</ContextMenuTrigger>
      <ContextMenuContent
        className="min-w-48"
        onCloseAutoFocus={(event) => {
          if (!apreIlPannelloPartecipanti.current) return
          apreIlPannelloPartecipanti.current = false
          event.preventDefault()
        }}
      >
        <ContextMenuItem
          onSelect={() => {
            apreIlPannelloPartecipanti.current = true
            onAddParticipants()
          }}
        >
          <Users />
          Aggiungi partecipanti…
        </ContextMenuItem>
        {canLeave ? (
          <ContextMenuItem onSelect={onLeave}>
            <LogOut />
            Esci dalla chat
          </ContextMenuItem>
        ) : null}
        {canDelete ? (
          <>
            <ContextMenuSeparator />
            <ContextMenuItem variant="destructive" onSelect={onDelete}>
              <Trash2 />
              Cancella
            </ContextMenuItem>
          </>
        ) : null}
      </ContextMenuContent>
    </ContextMenu>
  )
}

export function ChatWorkspace({
  scope,
  initialChatId,
}: {
  scope: ChatScope
  initialChatId?: number | null
}) {
  const isAll = scope.kind === "all"
  const projectId = scope.kind === "project" ? scope.projectId : null

  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [selected, setSelected] = React.useState<number | null>(
    initialChatId && initialChatId > 0 ? initialChatId : null
  )
  const [newOpen, setNewOpen] = React.useState(false)
  const [participantsOpen, setParticipantsOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")
  const [searchOpen, setSearchOpen] = React.useState(false)
  const [listFilter, setListFilter] = React.useState("")
  const [closedGroups, setClosedGroups] = React.useState<ChatGroupKey[]>([])
  const [replyTo, setReplyTo] = React.useState<ChatMessage | null>(null)
  const [older, setOlder] = React.useState<ChatMessage[]>([])
  const [olderHasMore, setOlderHasMore] = React.useState<boolean | null>(null)
  const [loadingMore, setLoadingMore] = React.useState(false)
  const [typing, setTyping] = React.useState<{ name: string; until: number } | null>(null)
  const messagesEndRef = React.useRef<HTMLDivElement>(null)
  const listRef = React.useRef<HTMLDivElement>(null)

  React.useEffect(() => {
    if (initialChatId && initialChatId > 0) setSelected(initialChatId)
  }, [initialChatId])

  const chatsQuery = useQuery({
    queryKey: isAll ? CHAT_ALL_LIST_KEY : ["project-chats", projectId],
    queryFn: () =>
      isAll ? fetchChatInbox(CHAT_ALL_LIMIT) : fetchChats(projectId as number),
    enabled: isAll || (projectId ?? 0) > 0,
  })
  const chats = React.useMemo(() => chatsQuery.data ?? [], [chatsQuery.data])
  const selectedChat = chats.find((c) => c.id === selected) ?? null

  const participantsQuery = useQuery({
    queryKey: ["project-chat-participants", selected],
    queryFn: () => fetchChatParticipants(selected as number),
    enabled: selected != null,
  })

  // Il «@» elenca TUTTI i colleghi, non i soli partecipanti (#78): serve proprio a tirare
  // dentro chi non era stato messo alla creazione — ci pensa il server, all'invio.
  const employeesQuery = useQuery({
    queryKey: ["employees-real"],
    queryFn: fetchRealEmployees,
  })

  const currentEmployeeId = getSession()?.user.employeeId
  const participantIds = React.useMemo(
    () => (participantsQuery.data ?? []).map((p) => p.employeeId),
    [participantsQuery.data]
  )
  const mentionCandidates = React.useMemo(
    () =>
      (employeesQuery.data ?? [])
        .filter((e) => e.id !== currentEmployeeId)
        .map((e) => ({ id: e.id, employeeId: e.id, employeeName: e.name })),
    [employeesQuery.data, currentEmployeeId]
  )

  // Chi guarda quale chat: lo legge la campanella per non aprire il riquadro di un messaggio
  // che sta già arrivando sotto gli occhi.
  React.useEffect(() => {
    setActiveChatId(selected)
    return () => setActiveChatId(null)
  }, [selected])

  const onChatChange = React.useCallback(
    (change: ChatChange) => {
      void queryClient.invalidateQueries({ queryKey: ["chat-inbox"] })
      void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_BADGE_KEY })
      if (projectId != null) {
        void queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
      }

      // 🪤 «read» è il segnaposto di lettura di qualcuno: cambia i contatori, NON i messaggi.
      // Rileggerli qui rifaceva scattare il «segna come letto», che tornava a notificare —
      // il giro infinito che rendeva la chat lenta (#78).
      if (change.action === "read") return

      // I messaggi si ricaricano solo per la conversazione aperta: gli altri eventi
      // aggiornano la lista e basta.
      if (change.chatId === selected) {
        void queryClient.invalidateQueries({
          queryKey: ["project-chat-messages", selected],
        })
        void queryClient.invalidateQueries({
          queryKey: ["project-chat-participants", selected],
        })
      }
    },
    [projectId, queryClient, selected]
  )

  const onTyping = React.useCallback(
    (event: ChatTyping) => {
      if (event.chatId !== selected) return
      if (event.employeeId === currentEmployeeId) return
      setTyping({ name: event.employeeName, until: Date.now() + 2500 })
    },
    [currentEmployeeId, selected]
  )

  // Dentro la commessa si ascolta il gruppo della commessa (che porta anche il «sta
  // scrivendo»); nella pagina globale il gruppo inbox, che copre tutte le commesse.
  const { sendTyping } = useProjectChatHub(projectId, onChatChange, onTyping)
  useChatInboxHub(onChatChange, { enabled: isAll })

  const lastTypingSent = React.useRef(0)
  const handleTyping = React.useCallback(() => {
    if (selected == null || projectId == null) return
    const now = Date.now()
    if (now - lastTypingSent.current < 800) return
    lastTypingSent.current = now
    sendTyping(selected)
  }, [projectId, selected, sendTyping])

  React.useEffect(() => {
    if (!typing) return
    const wait = typing.until - Date.now()
    const t = window.setTimeout(() => setTyping(null), Math.max(0, wait))
    return () => window.clearTimeout(t)
  }, [typing])

  React.useEffect(() => {
    if (selected != null) return
    if (initialChatId && chats.some((c) => c.id === initialChatId)) {
      setSelected(initialChatId)
      return
    }
    if (chats.length > 0) setSelected(chats[0].id)
  }, [chats, selected, initialChatId])

  React.useEffect(() => {
    setOlder([])
    setOlderHasMore(null)
    setReplyTo(null)
    setSearch("")
    setTyping(null)
  }, [selected])

  const messagesQuery = useQuery({
    queryKey: ["project-chat-messages", selected],
    queryFn: () => fetchChatMessages(selected as number),
    enabled: selected != null,
  })

  const latest = React.useMemo(
    () => messagesQuery.data?.messages ?? [],
    [messagesQuery.data]
  )
  const lastMessageId = latest.length > 0 ? latest[latest.length - 1].id : 0

  // Si segna letto quando si apre la chat e quando ne arriva uno nuovo — non a ogni rilettura
  // dei messaggi, o si rientra nel giro infinito descritto sopra.
  React.useEffect(() => {
    if (selected == null) return
    markChatRead(selected)
      .then(() => {
        void queryClient.invalidateQueries({ queryKey: ["chat-inbox"] })
        void queryClient.invalidateQueries({ queryKey: CHAT_INBOX_BADGE_KEY })
        void queryClient.invalidateQueries({ queryKey: ["notifications-badge"] })
        if (projectId != null) {
          void queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
        }
      })
      .catch(() => undefined)
  }, [selected, projectId, queryClient, lastMessageId])

  const messages = React.useMemo(() => mergeMessages(older, latest), [older, latest])
  const hasMore = olderHasMore ?? messagesQuery.data?.hasMore ?? false

  const q = search.trim().toLowerCase()
  const visibleMessages = React.useMemo(
    () =>
      q
        ? messages.filter(
            (m) =>
              m.message.toLowerCase().includes(q) ||
              m.employeeName.toLowerCase().includes(q)
          )
        : messages,
    [messages, q]
  )

  React.useEffect(() => {
    if (q) return
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" })
  }, [latest, selected, q])

  // ── Lista di sinistra: filtro + (nella pagina globale) sezioni per contesto ──
  const filteredChats = React.useMemo(() => {
    const term = listFilter.trim().toLowerCase()
    if (!term) return chats
    return chats.filter((c) =>
      `${c.title} ${chatContextLabel(c)} ${c.lastMessagePreview}`
        .toLowerCase()
        .includes(term)
    )
  }, [chats, listFilter])

  const groups = React.useMemo(() => {
    const buckets: Record<ChatGroupKey, ChatListItem[]> = {
      projects: [],
      other: [],
      none: [],
    }
    for (const chat of filteredChats) buckets[groupOfChat(chat)].push(chat)

    // Dentro «Commesse» e «Altre attività» le chat restano vicine per contenitore, così la
    // suddivisione «per commessa» si vede a colpo d'occhio.
    for (const key of ["projects", "other"] as const) {
      buckets[key].sort((a, b) => {
        const byContext = a.projectCode.localeCompare(b.projectCode, "it")
        if (byContext !== 0) return byContext
        return (b.lastMessageAt ?? "").localeCompare(a.lastMessageAt ?? "")
      })
    }

    return (["projects", "other", "none"] as ChatGroupKey[]).map((key) => ({
      key,
      label: CHAT_GROUP_LABELS[key],
      chats: buckets[key],
    }))
  }, [filteredChats])

  async function loadOlder() {
    if (selected == null || loadingMore || !hasMore || messages.length === 0) return
    setLoadingMore(true)
    const el = listRef.current
    const prevHeight = el?.scrollHeight ?? 0
    try {
      const page = await fetchChatMessages(selected, messages[0].id)
      setOlder((prev) => mergeMessages(page.messages, prev))
      setOlderHasMore(page.hasMore)
      requestAnimationFrame(() => {
        if (!el) return
        el.scrollTop = el.scrollHeight - prevHeight
      })
    } catch (err) {
      notifyError(err instanceof Error ? err : "Impossibile caricare i messaggi")
    } finally {
      setLoadingMore(false)
    }
  }

  async function invalidateChatData() {
    await queryClient.invalidateQueries({
      queryKey: ["project-chat-messages", selected],
    })
    await queryClient.invalidateQueries({
      queryKey: ["project-chat-participants", selected],
    })
    await queryClient.invalidateQueries({ queryKey: ["chat-inbox"] })
    await queryClient.invalidateQueries({ queryKey: CHAT_INBOX_BADGE_KEY })
    if (projectId != null) {
      await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    }
  }

  const sendMutation = useMutation({
    mutationFn: (text: string) =>
      sendChatMessage(selected as number, text, replyTo?.id),
    onSuccess: () => {
      setReplyTo(null)
      void invalidateChatData()
    },
    onError: (err: Error) => notifyError(err),
  })

  const attachMutation = useMutation({
    mutationFn: (file: File) =>
      sendChatMessageWithAttachment(selected as number, file, undefined, replyTo?.id),
    onSuccess: () => {
      setReplyTo(null)
      void invalidateChatData()
    },
    onError: (err: Error) => notifyError(err),
  })

  const deleteMsgMutation = useMutation({
    mutationFn: deleteChatMessage,
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ["project-chat-messages", selected],
      }),
    onError: (err: Error) => notifyError(err),
  })

  const deleteChatMutation = useMutation({
    mutationFn: deleteChat,
    onSuccess: async () => {
      setSelected(null)
      await invalidateChatData()
    },
    onError: (err: Error) => notifyError(err),
  })

  const leaveMutation = useMutation({
    mutationFn: leaveChat,
    onSuccess: async () => {
      setSelected(null)
      await invalidateChatData()
    },
    onError: (err: Error) => notifyError(err),
  })

  async function handleDeleteChat(chat: ChatListItem) {
    const ok = await confirm({
      title: "Elimina chat",
      description: `Eliminare la chat "${chat.title}" e tutti i suoi messaggi per tutti?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteChatMutation.mutate(chat.id)
  }

  async function handleLeaveChat(chat: ChatListItem) {
    const ok = await confirm({
      title: "Esci dalla chat",
      description: `Uscire da "${chat.title}"? Non riceverai più i messaggi. Gli altri restano.`,
      confirmLabel: "Esci",
    })
    if (ok) leaveMutation.mutate(chat.id)
  }

  function handleSelectChat(chatId: number) {
    setSelected(chatId)
    setParticipantsOpen(false)
  }

  function handleAddParticipants(chat: ChatListItem) {
    setSelected(chat.id)
    setParticipantsOpen(true)
  }

  // Chi modera la chat elimina qualsiasi chat; chi l'ha creata elimina la propria anche
  // senza la chiave (l'OR era già così: toglierlo sarebbe una restrizione, non una parità).
  function canDeleteChat(chat: ChatListItem): boolean {
    return (
      canWriteFeature("action.moderate_chat") ||
      chat.createdById === currentEmployeeId
    )
  }

  const canDeleteSelected =
    selectedChat != null && canDeleteChat(selectedChat)

  let lastDate = ""
  const typingVisible = typing && typing.until > Date.now() ? typing : null

  // Titolo della conversazione: oltre al nome della chat, il contenitore — commessa, altra
  // attività o «Senza commessa» (#78).
  const headerContext = selectedChat
    ? chatContextLabel(selectedChat)
    : scope.kind === "project"
      ? (scope.label ?? "")
      : ""

  return (
    <div className="grid h-full min-h-0 grid-cols-[280px_1fr] gap-0">
      <div className="flex min-h-0 flex-col border-r">
        <div className="space-y-2 border-b p-2">
          <Button size="sm" className="w-full" onClick={() => setNewOpen(true)}>
            <Plus />
            Nuova chat
          </Button>
          {isAll ? (
            <Input
              value={listFilter}
              onChange={(e) => setListFilter(e.target.value)}
              placeholder="Cerca chat, commessa…"
              className="h-8"
            />
          ) : null}
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-1">
          {chatsQuery.isLoading ? (
            <p className="p-3 text-sm text-muted-foreground">Caricamento…</p>
          ) : filteredChats.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">Nessuna chat.</p>
          ) : isAll ? (
            groups.map((group) => {
              const open = !closedGroups.includes(group.key)
              return (
                <div key={group.key} className="mb-1">
                  <button
                    type="button"
                    aria-expanded={open}
                    className="flex w-full items-center gap-1.5 rounded-md px-2 py-1 text-left text-xs font-semibold text-muted-foreground hover:bg-accent"
                    onClick={() =>
                      setClosedGroups((prev) =>
                        prev.includes(group.key)
                          ? prev.filter((k) => k !== group.key)
                          : [...prev, group.key]
                      )
                    }
                  >
                    <ChevronRight
                      className={cn(
                        "size-3.5 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
                        open && "rotate-90"
                      )}
                    />
                    <span className="truncate">{group.label}</span>
                    <span className="ml-auto shrink-0 tabular-nums">
                      {group.chats.length}
                    </span>
                  </button>
                  <Collapsible open={open}>
                    <div className="pl-1">
                      {group.chats.length === 0 ? (
                        <p className="px-2 py-1 text-xs text-muted-foreground">
                          Nessuna chat.
                        </p>
                      ) : (
                        group.chats.map((chat) => (
                          <ChatListRow
                            key={chat.id}
                            chat={chat}
                            selected={selected === chat.id}
                            showContext={group.key !== "none"}
                            canDelete={canDeleteChat(chat)}
                            onSelect={() => handleSelectChat(chat.id)}
                            onAddParticipants={() => handleAddParticipants(chat)}
                            onLeave={() => void handleLeaveChat(chat)}
                            onDelete={() => void handleDeleteChat(chat)}
                          />
                        ))
                      )}
                    </div>
                  </Collapsible>
                </div>
              )
            })
          ) : (
            filteredChats.map((chat) => (
              <ChatListRow
                key={chat.id}
                chat={chat}
                selected={selected === chat.id}
                showContext={false}
                canDelete={canDeleteChat(chat)}
                onSelect={() => handleSelectChat(chat.id)}
                onAddParticipants={() => handleAddParticipants(chat)}
                onLeave={() => void handleLeaveChat(chat)}
                onDelete={() => void handleDeleteChat(chat)}
              />
            ))
          )}
        </div>
      </div>

      <div className="flex min-h-0 flex-col">
        {selected == null ? (
          <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
            Seleziona o crea una chat.
          </div>
        ) : (
          <>
            <div className="flex items-center gap-2 border-b px-3 py-2">
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold">
                  Chat — {selectedChat?.title ?? ""}
                </p>
                {headerContext ? (
                  <p className="truncate text-[11px] text-muted-foreground">
                    {headerContext}
                  </p>
                ) : null}
              </div>
              <Button
                variant="ghost"
                size="icon-sm"
                className="text-muted-foreground"
                aria-label="Cerca in conversazione"
                onClick={() => setSearchOpen((v) => !v)}
              >
                <Search />
              </Button>
              {selectedChat ? (
                <ChatParticipantsPopover
                  chatId={selected}
                  projectId={selectedChat.projectId}
                  participantCount={selectedChat.participantCount}
                  open={participantsOpen}
                  onOpenChange={setParticipantsOpen}
                />
              ) : null}
              {selectedChat?.isParticipant ? (
                <Button
                  variant="ghost"
                  size="icon-sm"
                  className="text-muted-foreground hover:text-foreground"
                  onClick={() => void handleLeaveChat(selectedChat)}
                  aria-label="Esci dalla chat"
                >
                  <LogOut />
                </Button>
              ) : null}
              {selectedChat && canDeleteSelected ? (
                <Button
                  variant="ghost"
                  size="icon-sm"
                  className="text-muted-foreground hover:text-destructive"
                  onClick={() => void handleDeleteChat(selectedChat)}
                  aria-label="Elimina chat"
                >
                  <Trash2 />
                </Button>
              ) : null}
            </div>

            {searchOpen ? (
              <div className="border-b px-3 py-1.5">
                <Input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Cerca in questa conversazione"
                  className="h-8"
                  autoFocus
                />
                {q ? (
                  <p className="mt-1 text-[11px] text-muted-foreground">
                    {visibleMessages.length} risultat
                    {visibleMessages.length === 1 ? "o" : "i"}
                  </p>
                ) : null}
              </div>
            ) : null}

            <div ref={listRef} className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3">
              {hasMore && !q ? (
                <div className="flex justify-center py-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={loadingMore}
                    onClick={() => void loadOlder()}
                  >
                    {loadingMore ? "Caricamento…" : "Messaggi precedenti"}
                  </Button>
                </div>
              ) : null}
              {visibleMessages.length === 0 ? (
                <p className="text-center text-sm text-muted-foreground">
                  {q ? "Nessun risultato." : "Nessun messaggio. Scrivi il primo."}
                </p>
              ) : (
                visibleMessages.map((m) => {
                  const dateStr = fmtDateSeparator(m.createdAt)
                  const showDate = dateStr !== lastDate
                  if (showDate) lastDate = dateStr

                  return (
                    <React.Fragment key={m.id}>
                      {showDate ? (
                        <div className="flex justify-center py-1">
                          <span className="rounded-full bg-muted px-3 py-0.5 text-[10px] text-muted-foreground">
                            {dateStr}
                          </span>
                        </div>
                      ) : null}
                      <div
                        className={cn(
                          "flex gap-2",
                          m.isMine ? "justify-end" : "justify-start"
                        )}
                      >
                        {!m.isMine ? (
                          <Avatar size="sm" className="mt-1 size-6">
                            <AvatarFallback className="text-[9px]">
                              {m.employeeInitials}
                            </AvatarFallback>
                          </Avatar>
                        ) : null}
                        <div
                          className={cn(
                            "group max-w-[75%] rounded-lg px-3 py-1.5 text-sm",
                            m.isMine ? "bg-primary text-primary-foreground" : "bg-muted"
                          )}
                        >
                          {!m.isMine ? (
                            <p className="text-[11px] font-semibold opacity-80">
                              {m.employeeName}
                            </p>
                          ) : null}
                          {m.replyToMessageId ? (
                            <div
                              className={cn(
                                "mb-1 rounded border-l-2 px-2 py-0.5 text-[11px] opacity-80",
                                m.isMine
                                  ? "border-primary-foreground/50 bg-primary-foreground/10"
                                  : "border-primary/40 bg-background/60"
                              )}
                            >
                              <p className="truncate font-semibold">
                                {m.replyToEmployeeName}
                              </p>
                              <p className="truncate">{m.replyToPreview}</p>
                            </div>
                          ) : null}
                          <ChatMessageBody message={m.message} isMine={m.isMine} />
                          {m.hasAttachment && m.attachmentName ? (
                            <ChatMessageAttachment
                              messageId={m.id}
                              attachmentName={m.attachmentName}
                              isMine={m.isMine}
                            />
                          ) : null}
                          <div className="mt-0.5 flex items-center gap-2">
                            <span className="text-[10px] opacity-70">
                              {fmtTime(m.createdAt)}
                            </span>
                            <ReadTicks message={m} />
                            <button
                              type="button"
                              className="ml-auto text-[10px] opacity-0 transition group-hover:opacity-100"
                              onClick={() => setReplyTo(m)}
                            >
                              <span className="inline-flex items-center gap-0.5">
                                <Reply className="size-3" />
                                Rispondi
                              </span>
                            </button>
                            {canDeleteMessage(m) ? (
                              <button
                                type="button"
                                className="text-[10px] opacity-0 transition group-hover:opacity-100"
                                onClick={() => deleteMsgMutation.mutate(m.id)}
                              >
                                Elimina
                              </button>
                            ) : null}
                          </div>
                        </div>
                      </div>
                    </React.Fragment>
                  )
                })
              )}
              {typingVisible ? (
                <p className="px-2 text-xs text-muted-foreground">
                  {typingVisible.name} sta scrivendo…
                </p>
              ) : null}
              <div ref={messagesEndRef} />
            </div>

            <ChatComposer
              disabled={selected == null}
              mentionCandidates={mentionCandidates}
              participantIds={participantIds}
              sending={sendMutation.isPending}
              uploading={attachMutation.isPending}
              replyTo={
                replyTo
                  ? {
                      employeeName: replyTo.isMine ? "Tu" : replyTo.employeeName,
                      preview: replyTo.message,
                    }
                  : null
              }
              onClearReply={() => setReplyTo(null)}
              onSend={(text) => sendMutation.mutate(text)}
              onAttach={(file) => attachMutation.mutate(file)}
              onTyping={handleTyping}
            />
          </>
        )}
      </div>

      <NewChatDialog
        open={newOpen}
        defaultProjectId={projectId}
        lockProject={scope.kind === "project"}
        onClose={() => setNewOpen(false)}
        onCreated={async (id) => {
          setNewOpen(false)
          setSelected(id)
          await invalidateChatData()
        }}
      />
    </div>
  )
}
