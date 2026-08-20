import { splitMessageMentions } from "@/features/commesse/chat/chat-mentions"
import { cn } from "@/lib/utils"

const URL_RE = /(https?:\/\/[^\s<]+[^<.,:;"')\]\s])/gi

function TextWithLinks({
  text,
  isMine,
}: {
  text: string
  isMine: boolean
}) {
  const parts = text.split(URL_RE)
  return (
    <>
      {parts.map((part, idx) =>
        /^https?:\/\//i.test(part) ? (
          <a
            key={idx}
            href={part}
            target="_blank"
            rel="noreferrer"
            className={cn(
              "underline underline-offset-2",
              isMine ? "text-primary-foreground" : "text-primary"
            )}
            onClick={(e) => e.stopPropagation()}
          >
            {part}
          </a>
        ) : (
          <span key={idx}>{part}</span>
        )
      )}
    </>
  )
}

export function ChatMessageBody({
  message,
  isMine,
}: {
  message: string
  isMine: boolean
}) {
  const segments = splitMessageMentions(message)

  return (
    <p className="whitespace-pre-wrap break-words">
      {segments.map((seg, idx) =>
        seg.type === "mention" ? (
          <span
            key={idx}
            className={cn(
              "font-semibold",
              isMine ? "text-primary-foreground" : "text-primary"
            )}
          >
            {seg.value}
          </span>
        ) : (
          <TextWithLinks key={idx} text={seg.value} isMine={isMine} />
        )
      )}
    </p>
  )
}
