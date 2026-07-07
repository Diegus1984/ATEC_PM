import * as React from "react"
import { Download, Eye, FolderOpen, Pencil, Trash2 } from "lucide-react"

import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import type { FileItem } from "@/lib/api/types"

import { useProjectDocumentsActions } from "./project-documents-actions"

/** Menu contestuale (tasto destro) per file nell'area documenti. */
export function FileDocContextMenu({
  item,
  onPreview,
  onDownload,
  children,
}: {
  item: FileItem
  onPreview: () => void
  onDownload: () => void
  children: React.ReactNode
}) {
  const actions = useProjectDocumentsActions()

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={onPreview}>
          <Eye />
          Anteprima
        </ContextMenuItem>
        <ContextMenuItem onClick={onDownload}>
          <Download />
          Scarica
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={() => actions.openRenameDialog(item)}>
          <Pencil />
          Rinomina
        </ContextMenuItem>
        <ContextMenuItem onClick={() => actions.openMoveDialog(item)}>
          <FolderOpen />
          Sposta…
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem
          variant="destructive"
          onClick={() => void actions.deleteItem(item)}
        >
          <Trash2 />
          Elimina
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  )
}
