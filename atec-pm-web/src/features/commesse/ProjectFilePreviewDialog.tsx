import * as React from "react"
import { Download } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Skeleton } from "@/components/ui/skeleton"
import {
  fetchProjectFileBlobUrl,
  fetchProjectPreviewHtml,
} from "@/lib/api/project-documents"
import { downloadProjectFile } from "@/lib/api/projects"
import { getSession } from "@/lib/auth/session"
import { notifyError } from "@/lib/toast"
import type { FileItem } from "@/lib/api/types"

type Kind = "pdf" | "image" | "video" | "office" | "other"

const IMAGE_EXT = ["png", "jpg", "jpeg", "bmp", "gif", "webp", "svg"]
const VIDEO_EXT = ["mp4", "webm", "mov", "avi", "mkv", "m4v", "ogv", "wmv"]
const OFFICE_EXT = ["docx", "xlsx", "xls", "csv"]

function extOf(name: string): string {
  const dot = name.lastIndexOf(".")
  return dot >= 0 ? name.slice(dot + 1).toLowerCase() : ""
}

function kindOf(name: string): Kind {
  const ext = extOf(name)
  if (ext === "pdf") return "pdf"
  if (IMAGE_EXT.includes(ext)) return "image"
  if (VIDEO_EXT.includes(ext)) return "video"
  if (OFFICE_EXT.includes(ext)) return "office"
  return "other"
}

type Status = "loading" | "ready" | "error" | "unsupported"

/**
 * Corpo dell'anteprima. Tiene lo stato (status/blobUrl/html) di UN solo file:
 * va montato con `key={item.relativePath}` così che, cambiando file, lo stato
 * riparta da zero e non si mostri mai per un frame il contenuto del file precedente.
 */
function PreviewBody({
  projectId,
  item,
}: {
  projectId: number
  item: FileItem
}) {
  const kind = kindOf(item.name)
  const [status, setStatus] = React.useState<Status>(
    kind === "other" ? "unsupported" : "loading"
  )
  const [blobUrl, setBlobUrl] = React.useState<string | null>(null)
  const [html, setHtml] = React.useState<string | null>(null)
  const [errorMsg, setErrorMsg] = React.useState("")

  React.useEffect(() => {
    if (kind === "other") {
      setStatus("unsupported")
      return
    }
    let cancelled = false
    let createdUrl: string | null = null

    if (kind === "office") {
      void fetchProjectPreviewHtml(projectId, item.relativePath)
        .then((content) => {
          if (cancelled) return
          setHtml(content)
          setStatus("ready")
        })
        .catch((err: Error) => {
          if (cancelled) return
          setErrorMsg(err.message)
          setStatus("error")
        })
    } else {
      void fetchProjectFileBlobUrl(projectId, item.relativePath)
        .then((url) => {
          if (cancelled) {
            URL.revokeObjectURL(url)
            return
          }
          createdUrl = url
          setBlobUrl(url)
          setStatus("ready")
        })
        .catch((err: Error) => {
          if (cancelled) return
          setErrorMsg(err.message)
          setStatus("error")
        })
    }

    return () => {
      cancelled = true
      if (createdUrl) {
        URL.revokeObjectURL(createdUrl)
      }
    }
  }, [projectId, item.relativePath, kind])

  function handleDownload() {
    void downloadProjectFile(
      projectId,
      item.relativePath,
      getSession()?.token ?? null
    ).catch((err: Error) => notifyError(err))
  }

  if (status === "loading") {
    return <Skeleton className="h-[60vh] w-full" />
  }
  if (status === "error" || status === "unsupported") {
    return (
      <div className="flex h-[60vh] flex-col items-center justify-center gap-3 text-center">
        <p
          className={
            status === "error"
              ? "text-sm text-destructive"
              : "text-sm text-muted-foreground"
          }
        >
          {status === "error"
            ? errorMsg || "Impossibile generare l'anteprima."
            : "Anteprima non disponibile per questo tipo di file."}
        </p>
        <Button variant="outline" onClick={handleDownload}>
          <Download />
          Scarica
        </Button>
      </div>
    )
  }
  if (kind === "image" && blobUrl) {
    return (
      <div className="flex h-[70vh] items-center justify-center overflow-auto">
        <img
          src={blobUrl}
          alt={item.name}
          className="max-h-full max-w-full object-contain"
        />
      </div>
    )
  }
  if (kind === "pdf" && blobUrl) {
    return (
      <iframe
        title={item.name}
        src={blobUrl}
        className="h-[70vh] w-full rounded-md border"
      />
    )
  }
  if (kind === "video" && blobUrl) {
    return (
      <div className="flex h-[70vh] items-center justify-center overflow-auto bg-black/5">
        <video
          src={blobUrl}
          controls
          playsInline
          className="max-h-full max-w-full rounded-md"
        >
          Il browser non supporta la riproduzione di questo video.
        </video>
      </div>
    )
  }
  if (kind === "office" && html != null) {
    return (
      <iframe
        title={item.name}
        sandbox=""
        srcDoc={html}
        className="h-[70vh] w-full rounded-md border bg-white"
      />
    )
  }
  return null
}

export function ProjectFilePreviewDialog({
  open,
  projectId,
  item,
  onClose,
}: {
  open: boolean
  projectId: number
  item: FileItem | null
  onClose: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[92vh] overflow-hidden sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle className="truncate">
            {item?.name ?? "Anteprima"}
          </DialogTitle>
          <DialogDescription>Anteprima documento di commessa.</DialogDescription>
        </DialogHeader>

        <div className="min-h-[60vh]">
          {item ? (
            <PreviewBody
              key={item.relativePath}
              projectId={projectId}
              item={item}
            />
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  )
}
