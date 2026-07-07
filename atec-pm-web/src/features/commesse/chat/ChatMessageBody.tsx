import { splitMessageMentions } from "@/features/commesse/chat/chat-mentions"
import { cn } from "@/lib/utils"

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
          <span key={idx}>{seg.value}</span>
        )
      )}
    </p>
  )
}
