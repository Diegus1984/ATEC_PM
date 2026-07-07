import * as React from "react"
import { Download, Loader2 } from "lucide-react"

import { isImageAttachment } from "@/features/commesse/chat/chat-mentions"
import { Button } from "@/components/ui/button"
import { notifyError } from "@/lib/toast"
import {
  downloadChatAttachment,
  fetchChatAttachmentBlob,
} from "@/lib/api/project-chat"
import { cn } from "@/lib/utils"

export function ChatMessageAttachment({
  messageId,
  attachmentName,
  isMine,
}: {
  messageId: number
  attachmentName: string
  isMine: boolean
}) {
  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null)
  const [loading, setLoading] = React.useState(false)
  const [error, setError] = React.useState(false)
  const isImage = isImageAttachment(attachmentName)

  React.useEffect(() => {
    if (!isImage) return

    let revoked = false
    let objectUrl: string | null = null
    setLoading(true)
    setError(false)
    setPreviewUrl(null)

    void fetchChatAttachmentBlob(messageId)
      .then((blob) => {
        if (revoked) return
        objectUrl = URL.createObjectURL(blob)
        setPreviewUrl(objectUrl)
      })
      .catch(() => {
        if (!revoked) setError(true)
      })
      .finally(() => {
        if (!revoked) setLoading(false)
      })

    return () => {
      revoked = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [messageId, isImage])

  async function handleDownload() {
    try {
      await downloadChatAttachment(messageId, attachmentName)
    } catch (err) {
      notifyError(err, "Download non riuscito")
    }
  }

  if (isImage) {
    if (loading) {
      return (
        <div className="mt-2 flex items-center gap-2 text-xs opacity-80">
          <Loader2 className="size-3.5 animate-spin" />
          Caricamento immagine…
        </div>
      )
    }

    if (error || !previewUrl) {
      return (
        <button
          type="button"
          className={cn(
            "mt-2 flex items-center gap-1 text-xs underline",
            isMine ? "text-primary-foreground" : "text-primary"
          )}
          onClick={() => void handleDownload()}
        >
          <Download className="size-3" />
          {attachmentName}
        </button>
      )
    }

    return (
      <button
        type="button"
        className="mt-2 block max-w-full"
        onClick={() => void handleDownload()}
        title="Clicca per scaricare"
      >
        <img
          src={previewUrl}
          alt={attachmentName}
          className="max-h-56 max-w-full rounded-md object-contain"
        />
      </button>
    )
  }

  return (
    <Button
      type="button"
      variant="link"
      size="sm"
      className={cn(
        "mt-1 h-auto px-0 text-xs",
        isMine ? "text-primary-foreground" : "text-primary"
      )}
      onClick={() => void handleDownload()}
    >
      <Download className="size-3" />
      {attachmentName}
    </Button>
  )
}
