import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ChatComposer } from "@/features/commesse/chat/ChatComposer"
import { ChatMessageAttachment } from "@/features/commesse/chat/ChatMessageAttachment"
import { ChatMessageBody } from "@/features/commesse/chat/ChatMessageBody"
import { ChatParticipantsPopover } from "@/features/commesse/chat/ChatParticipantsPopover"
import { fetchMoMEmployees } from "@/lib/api/mom"
import {
  createChat,
  deleteChat,
  deleteChatMessage,
  fetchChatMessages,
  fetchChatParticipants,
  fetchChats,
  markChatRead,
  sendChatMessage,
  sendChatMessageWithAttachment,
} from "@/lib/api/project-chat"
import type { ChatListItem, ChatMessage } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { useProjectChatHub } from "@/lib/signalr/use-project-chat-hub"
import { cn } from "@/lib/utils"

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
  const role = getSession()?.user.userRole
  return role === "ADMIN" || role === "PM"
}

export function ProjectChat({ projectId }: { projectId: number }) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [selected, setSelected] = React.useState<number | null>(null)
  const [newOpen, setNewOpen] = React.useState(false)
  const messagesEndRef = React.useRef<HTMLDivElement>(null)

  const chatsQuery = useQuery({
    queryKey: ["project-chats", projectId],
    queryFn: () => fetchChats(projectId),
    enabled: projectId > 0,
  })
  const chats = React.useMemo(() => chatsQuery.data ?? [], [chatsQuery.data])
  const selectedChat = chats.find((c) => c.id === selected) ?? null

  const participantsQuery = useQuery({
    queryKey: ["project-chat-participants", selected],
    queryFn: () => fetchChatParticipants(selected as number),
    enabled: selected != null,
  })

  const currentEmployeeId = getSession()?.user.employeeId
  const mentionCandidates = React.useMemo(
    () =>
      (participantsQuery.data ?? []).filter(
        (p) => p.employeeId !== currentEmployeeId
      ),
    [participantsQuery.data, currentEmployeeId]
  )

  useProjectChatHub(projectId, (change) => {
    void queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    if (selected != null && change.chatId === selected) {
      void queryClient.invalidateQueries({
        queryKey: ["project-chat-messages", selected],
      })
      void queryClient.invalidateQueries({
        queryKey: ["project-chat-participants", selected],
      })
    }
  })

  React.useEffect(() => {
    if (selected == null && chats.length > 0) setSelected(chats[0].id)
  }, [chats, selected])

  const messagesQuery = useQuery({
    queryKey: ["project-chat-messages", selected],
    queryFn: () => fetchChatMessages(selected as number),
    enabled: selected != null,
  })

  React.useEffect(() => {
    if (selected == null) return
    markChatRead(selected)
      .then(() =>
        queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
      )
      .catch(() => undefined)
  }, [selected, projectId, queryClient])

  const messages = messagesQuery.data ?? []

  React.useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" })
  }, [messages, selected])

  async function invalidateChatData() {
    await queryClient.invalidateQueries({
      queryKey: ["project-chat-messages", selected],
    })
    await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
  }

  const sendMutation = useMutation({
    mutationFn: (text: string) => sendChatMessage(selected as number, text),
    onSuccess: () => void invalidateChatData(),
    onError: (err: Error) => notifyError(err),
  })

  const attachMutation = useMutation({
    mutationFn: (file: File) =>
      sendChatMessageWithAttachment(selected as number, file),
    onSuccess: () => void invalidateChatData(),
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
      await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    },
    onError: (err: Error) => notifyError(err),
  })

  async function handleDeleteChat(chat: ChatListItem) {
    const ok = await confirm({
      title: "Elimina chat",
      description: `Eliminare la chat "${chat.title}" e tutti i suoi messaggi?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteChatMutation.mutate(chat.id)
  }

  let lastDate = ""

  return (
    <div className="grid h-[62vh] grid-cols-[260px_1fr] gap-3">
      <div className="flex min-h-0 flex-col rounded-lg border">
        <div className="border-b p-2">
          <Button size="sm" className="w-full" onClick={() => setNewOpen(true)}>
            <Plus />
            Nuova chat
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-1">
          {chatsQuery.isLoading ? (
            <p className="p-3 text-sm text-muted-foreground">Caricamento…</p>
          ) : chats.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">Nessuna chat.</p>
          ) : (
            chats.map((chat) => (
              <button
                key={chat.id}
                type="button"
                className={cn(
                  "group flex w-full flex-col gap-0.5 rounded-md px-2 py-1.5 text-left hover:bg-accent",
                  selected === chat.id && "bg-accent"
                )}
                onClick={() => setSelected(chat.id)}
              >
                <div className="flex items-center gap-1">
                  <span className="flex-1 truncate text-sm font-medium">
                    {chat.title}
                  </span>
                  {chat.unreadCount > 0 ? (
                    <span className="rounded-full bg-amber-500 px-1.5 text-[10px] font-semibold text-white">
                      {chat.unreadCount > 99 ? "99+" : chat.unreadCount}
                    </span>
                  ) : null}
                </div>
                <div className="flex items-center gap-2 text-[10px] text-muted-foreground">
                  <span>
                    {chat.participantCount} partecip. · {chat.messageCount} msg
                  </span>
                  {chat.lastMessageAt ? (
                    <span className="ml-auto shrink-0">
                      {fmtListTime(chat.lastMessageAt)}
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
      </div>

      <div className="flex min-h-0 flex-col rounded-lg border">
        {selected == null ? (
          <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
            Seleziona o crea una chat.
          </div>
        ) : (
          <>
            <div className="flex items-center gap-2 border-b px-3 py-2">
              <span className="min-w-0 flex-1 truncate text-sm font-semibold">
                {selectedChat?.title ?? "Chat"}
              </span>
              {selectedChat ? (
                <ChatParticipantsPopover
                  chatId={selected}
                  projectId={projectId}
                  participantCount={selectedChat.participantCount}
                />
              ) : null}
              {selectedChat ? (
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

            <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3">
              {messages.length === 0 ? (
                <p className="text-center text-sm text-muted-foreground">
                  Nessun messaggio. Scrivi il primo.
                </p>
              ) : (
                messages.map((m) => {
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
                            m.isMine
                              ? "bg-primary text-primary-foreground"
                              : "bg-muted"
                          )}
                        >
                          {!m.isMine ? (
                            <p className="text-[11px] font-semibold opacity-80">
                              {m.employeeName}
                            </p>
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
              <div ref={messagesEndRef} />
            </div>

            <ChatComposer
              disabled={selected == null}
              mentionCandidates={mentionCandidates}
              sending={sendMutation.isPending}
              uploading={attachMutation.isPending}
              onSend={(text) => sendMutation.mutate(text)}
              onAttach={(file) => attachMutation.mutate(file)}
            />
          </>
        )}
      </div>

      <NewChatDialog
        open={newOpen}
        projectId={projectId}
        onClose={() => setNewOpen(false)}
        onCreated={async (id) => {
          setNewOpen(false)
          setSelected(id)
          await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
        }}
      />
    </div>
  )
}

function NewChatDialog({
  open,
  projectId,
  onClose,
  onCreated,
}: {
  open: boolean
  projectId: number
  onClose: () => void
  onCreated: (chatId: number) => void
}) {
  const [title, setTitle] = React.useState("")
  const [participants, setParticipants] = React.useState<number[]>([])

  const employeesQuery = useQuery({
    queryKey: ["mom-employees"],
    queryFn: fetchMoMEmployees,
    enabled: open,
  })

  React.useEffect(() => {
    if (open) {
      setTitle("")
      setParticipants([])
    }
  }, [open])

  const createMutation = useMutation({
    mutationFn: () =>
      createChat({ projectId, title: title.trim(), participantIds: participants }),
    onSuccess: (id) => onCreated(id),
    onError: (err: Error) => notifyError(err),
  })

  const employees = employeesQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuova chat</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div className="grid gap-1.5">
            <Label>Titolo</Label>
            <Input
              value={title}
              autoFocus
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Oggetto della discussione"
            />
          </div>
          <div className="grid gap-1.5">
            <Label>Partecipanti</Label>
            <div className="grid max-h-48 grid-cols-2 gap-1.5 overflow-y-auto rounded-md border p-2">
              {employees.map((e) => (
                <label key={e.id} className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={participants.includes(e.id)}
                    onCheckedChange={(v) =>
                      setParticipants((prev) =>
                        v ? [...prev, e.id] : prev.filter((x) => x !== e.id)
                      )
                    }
                  />
                  <span className="truncate">{e.name}</span>
                </label>
              ))}
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => createMutation.mutate()}
            disabled={!title.trim() || createMutation.isPending}
          >
            Crea
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
