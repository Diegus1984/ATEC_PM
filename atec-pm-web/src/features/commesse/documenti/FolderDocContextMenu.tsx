import * as React from "react"
import {
  FolderOpen,
  FolderPlus,
  Pencil,
  Trash2,
  Upload,
} from "lucide-react"

import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import type { FileItem } from "@/lib/api/types"

import { useProjectDocumentsActions } from "./project-documents-actions"
import { normalizeDocPath } from "./project-documents-filter"

/** Menu contestuale (tasto destro) per cartelle nell'albero documenti. */
export function FolderDocContextMenu({
  item,
  onOpen,
  children,
}: {
  item: FileItem
  onOpen: () => void
  children: React.ReactNode
}) {
  const actions = useProjectDocumentsActions()
  const path = normalizeDocPath(item.relativePath)
  const isRoot = path === ""

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      <ContextMenuContent>
        {!isRoot ? (
          <ContextMenuItem onClick={onOpen}>
            <FolderOpen />
            Apri
          </ContextMenuItem>
        ) : null}
        <ContextMenuItem
          onClick={() => actions.openNewFolderDialog(path)}
        >
          <FolderPlus />
          Nuova cartella
        </ContextMenuItem>
        <ContextMenuItem onClick={() => actions.triggerUpload(path)}>
          <Upload />
          Carica file
        </ContextMenuItem>
        {!isRoot ? (
          <>
            <ContextMenuSeparator />
            <ContextMenuItem
              onClick={() => actions.openRenameDialog(item)}
            >
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
          </>
        ) : null}
      </ContextMenuContent>
    </ContextMenu>
  )
}

/** Converte un nodo cartella dell'albero in FileItem per le azioni API. */
export function toFolderFileItem(
  relativePath: string,
  name: string
): FileItem {
  return {
    name,
    isFolder: true,
    size: 0,
    relativePath: normalizeDocPath(relativePath),
    modified: null,
  }
}
