import * as React from "react"
import { Paperclip, Send, X } from "lucide-react"

import {
  getActiveMentionFilter,
  insertMentionAtCaret,
} from "@/features/commesse/chat/chat-mentions"
import { Button } from "@/components/ui/button"
import { notifyError } from "@/lib/toast"
import { Textarea } from "@/components/ui/textarea"
import type { ChatParticipant } from "@/lib/api/types"
import { cn } from "@/lib/utils"

const MAX_ATTACHMENT_BYTES = 20 * 1024 * 1024

export type ChatReplyTarget = {
  employeeName: string
  preview: string
}

export function ChatComposer({
  disabled,
  mentionCandidates,
  participantIds,
  sending,
  uploading,
  replyTo,
  onClearReply,
  onSend,
  onAttach,
  onTyping,
}: {
  disabled?: boolean
  /** TUTTI i colleghi, non solo chi è già nella chat: menzionarne uno lo fa entrare (#78). */
  mentionCandidates: ChatParticipant[]
  /** Chi è già dentro: serve solo a marcare gli altri nella tendina del «@». */
  participantIds?: number[]
  sending?: boolean
  uploading?: boolean
  replyTo?: ChatReplyTarget | null
  onClearReply?: () => void
  onSend: (text: string) => void
  onAttach: (file: File) => void
  onTyping?: () => void
}) {
  const [draft, setDraft] = React.useState("")
  const [caret, setCaret] = React.useState(0)
  const [mentionOpen, setMentionOpen] = React.useState(false)
  const [mentionIndex, setMentionIndex] = React.useState(0)
  const [dragging, setDragging] = React.useState(false)
  const textareaRef = React.useRef<HTMLTextAreaElement>(null)
  const fileRef = React.useRef<HTMLInputElement>(null)

  const filter = getActiveMentionFilter(draft, caret)
  const filtered =
    filter != null
      ? mentionCandidates.filter((p) =>
          p.employeeName.toLowerCase().includes(filter)
        )
      : []

  React.useEffect(() => {
    const open = filter != null && filtered.length > 0
    setMentionOpen(open)
    if (open) setMentionIndex(0)
  }, [filter, filtered.length])

  function syncCaret() {
    const pos = textareaRef.current?.selectionStart ?? draft.length
    setCaret(pos)
  }

  function insertMention(participant: ChatParticipant) {
    const next = insertMentionAtCaret(draft, caret, participant.employeeName)
    setDraft(next.text)
    setCaret(next.caret)
    setMentionOpen(false)
    requestAnimationFrame(() => {
      const el = textareaRef.current
      if (!el) return
      el.focus()
      el.setSelectionRange(next.caret, next.caret)
    })
  }

  function submit() {
    const text = draft.trim()
    if (!text || disabled || sending || uploading) return
    onSend(text)
    setDraft("")
    setMentionOpen(false)
  }

  function takeFile(file: File | undefined | null) {
    if (!file) return
    if (file.size > MAX_ATTACHMENT_BYTES) {
      notifyError("File troppo grande (max 20 MB).")
      return
    }
    onAttach(file)
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    takeFile(file)
  }

  function handlePaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const items = e.clipboardData?.items
    if (!items) return
    for (const item of items) {
      if (item.kind === "file") {
        const file = item.getAsFile()
        if (file) {
          e.preventDefault()
          takeFile(file)
          return
        }
      }
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault()
    setDragging(false)
    takeFile(e.dataTransfer.files?.[0])
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (mentionOpen && filtered.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault()
        setMentionIndex((i) => Math.min(i + 1, filtered.length - 1))
        return
      }
      if (e.key === "ArrowUp") {
        e.preventDefault()
        setMentionIndex((i) => Math.max(i - 1, 0))
        return
      }
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault()
        insertMention(filtered[mentionIndex])
        return
      }
      if (e.key === "Escape") {
        e.preventDefault()
        setMentionOpen(false)
        return
      }
    }

    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault()
      submit()
    }
  }

  const busy = sending || uploading

  return (
    <div
      className={cn("relative border-t p-2", dragging && "bg-accent/40")}
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={handleDrop}
    >
      {dragging ? (
        <p className="pointer-events-none absolute inset-0 z-20 flex items-center justify-center text-sm font-medium">
          Rilascia per allegare
        </p>
      ) : null}

      {replyTo ? (
        <div className="mb-1.5 flex items-start gap-2 rounded-md border-l-2 border-primary bg-muted/60 px-2 py-1">
          <div className="min-w-0 flex-1">
            <p className="truncate text-[11px] font-semibold">{replyTo.employeeName}</p>
            <p className="truncate text-[11px] text-muted-foreground">{replyTo.preview}</p>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="shrink-0"
            aria-label="Annulla risposta"
            onClick={onClearReply}
          >
            <X />
          </Button>
        </div>
      ) : null}

      {mentionOpen ? (
        <div className="absolute bottom-full left-2 right-2 z-10 mb-1 max-h-40 overflow-y-auto rounded-md border bg-popover shadow-md">
          {filtered.map((p, idx) => {
            const fuori =
              participantIds != null && !participantIds.includes(p.employeeId)
            return (
              <button
                key={p.id}
                type="button"
                className={cn(
                  "flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent",
                  idx === mentionIndex && "bg-accent"
                )}
                onMouseDown={(e) => {
                  e.preventDefault()
                  insertMention(p)
                }}
              >
                <span className="truncate">{p.employeeName}</span>
                {fuori ? (
                  <span className="ml-auto shrink-0 text-[10px] text-muted-foreground">
                    lo aggiungi alla chat
                  </span>
                ) : null}
              </button>
            )
          })}
        </div>
      ) : null}

      <div className="flex items-end gap-2">
        <input
          ref={fileRef}
          type="file"
          className="hidden"
          onChange={handleFileChange}
        />
        <Button
          type="button"
          variant="outline"
          size="icon"
          disabled={disabled || busy}
          aria-label="Allega file"
          onClick={() => fileRef.current?.click()}
        >
          <Paperclip />
        </Button>

        <Textarea
          ref={textareaRef}
          value={draft}
          disabled={disabled || busy}
          rows={1}
          placeholder="Scrivi un messaggio… (@ per menzionare, Invio invia)"
          className="min-h-9 max-h-32 resize-none"
          onChange={(e) => {
            setDraft(e.target.value)
            setCaret(e.target.selectionStart ?? e.target.value.length)
            onTyping?.()
          }}
          onPaste={handlePaste}
          onClick={syncCaret}
          onKeyUp={syncCaret}
          onKeyDown={handleKeyDown}
        />

        <Button
          type="button"
          size="icon"
          disabled={disabled || busy || !draft.trim()}
          aria-label="Invia"
          onClick={submit}
        >
          <Send />
        </Button>
      </div>
    </div>
  )
}
