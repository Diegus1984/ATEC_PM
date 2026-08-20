import * as React from "react"
import { Download, FileText, Loader2, Trash2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { fetchBugAttachmentBlob } from "@/lib/api/bug-reports"
import type { BugAttachment } from "@/lib/api/types"
import { notifyError } from "@/lib/toast"

import { formatSize } from "./bug-report-utils"

/**
 * Allegato di una segnalazione. Gli screenshot passano dall'endpoint autenticato,
 * quindi non si può usare un `src` diretto: si scarica il blob e si crea una object URL,
 * revocata allo smontaggio per non tenere memoria occupata.
 */
export function BugAttachmentThumb({
  attachment,
  canDelete,
  onDelete,
}: {
  attachment: BugAttachment
  canDelete: boolean
  onDelete: () => void
}) {
  const [url, setUrl] = React.useState<string | null>(null)
  const [loading, setLoading] = React.useState(false)

  React.useEffect(() => {
    if (!attachment.isImage) return
    let revoked: string | null = null
    let cancelled = false

    setLoading(true)
    void (async () => {
      try {
        const blob = await fetchBugAttachmentBlob(attachment.id)
        if (cancelled) return
        revoked = URL.createObjectURL(blob)
        setUrl(revoked)
      } catch {
        // Anteprima non disponibile: resta il pulsante di download.
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
      if (revoked) URL.revokeObjectURL(revoked)
    }
  }, [attachment.id, attachment.isImage])

  async function download() {
    try {
      const blob = await fetchBugAttachmentBlob(attachment.id)
      const href = URL.createObjectURL(blob)
      const link = document.createElement("a")
      link.href = href
      link.download = attachment.fileName
      link.click()
      URL.revokeObjectURL(href)
    } catch (err) {
      notifyError(err)
    }
  }

  return (
    <div className="flex w-40 flex-col gap-1 rounded-md border bg-card p-1.5">
      <div className="flex h-24 items-center justify-center overflow-hidden rounded bg-muted/40">
        {loading ? (
          <Loader2 className="size-4 animate-spin text-muted-foreground" />
        ) : url ? (
          <a href={url} target="_blank" rel="noreferrer" title="Apri a dimensione intera">
            <img
              src={url}
              alt={attachment.fileName}
              className="max-h-24 w-auto object-contain"
            />
          </a>
        ) : (
          <FileText className="size-6 text-muted-foreground" />
        )}
      </div>
      <div className="truncate text-[11px] font-medium" title={attachment.fileName}>
        {attachment.fileName}
      </div>
      <div className="flex items-center justify-between">
        <span className="text-[10px] text-muted-foreground">
          {formatSize(attachment.sizeBytes)}
        </span>
        <div className="flex items-center gap-0.5">
          <Button
            variant="ghost"
            size="icon"
            className="size-6"
            title="Scarica"
            onClick={() => void download()}
          >
            <Download className="size-3.5" />
          </Button>
          {canDelete ? (
            <Button
              variant="ghost"
              size="icon"
              className="size-6 text-destructive"
              title="Elimina allegato"
              onClick={onDelete}
            >
              <Trash2 className="size-3.5" />
            </Button>
          ) : null}
        </div>
      </div>
    </div>
  )
}
