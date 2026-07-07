import * as React from "react"

import {
  RichTextEditor,
  type RichTextEditorHandle,
} from "@/components/shared/RichTextEditor"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"

/**
 * Editor descrizione (RTF/HTML) di un articolo o contenuto del preventivo.
 * Fedele a MaterialRtfDialog del WPF: stesso editor TinyMCE, ritorna l'HTML al salvataggio.
 */
export function RtfEditDialog({
  open,
  title,
  initialHtml,
  onClose,
  onSave,
}: {
  open: boolean
  title: string
  initialHtml: string
  onClose: () => void
  onSave: (html: string) => void
}) {
  const editorRef = React.useRef<RichTextEditorHandle | null>(null)

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-4 overflow-hidden sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle className="truncate">{title}</DialogTitle>
        </DialogHeader>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {open ? (
            <RichTextEditor ref={editorRef} initialValue={initialHtml} height={420} />
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button onClick={() => onSave(editorRef.current?.getContent() ?? initialHtml)}>
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
