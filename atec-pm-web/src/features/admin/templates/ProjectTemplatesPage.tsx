import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ClipboardPaste,
  Copy,
  File,
  Folder,
  FolderPlus,
  Pencil,
  RefreshCw,
  Scissors,
  Trash2,
  Upload,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuShortcut,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import {
  copyTemplateFile,
  copyTemplateFolder,
  createTemplateFolder,
  deleteTemplateFile,
  deleteTemplateFolder,
  fetchProjectTemplateTree,
  moveTemplateFile,
  renameTemplateFile,
  updateTemplateFolder,
  uploadTemplateFile,
} from "@/lib/api/project-templates"
import type { TemplateFolderNode } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { formatSize } from "@/lib/format"

const MAX_UPLOAD_BYTES = 500 * 1024 * 1024

// Stesse estensioni dell'OpenFileDialog WPF (documenti, CAD, immagini, archivi).
// Il server valida comunque l'allowlist; questo è solo un filtro lato UI.
const UPLOAD_ACCEPT =
  ".pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.rtf,.dwg,.dxf,.step,.stp,.stl,.iges,.igs,.obj,.sldprt,.sldasm,.slddrw,.easm,.eprt,.edrw,.png,.jpg,.jpeg,.bmp,.gif,.webp,.zip,.rar,.7z"

interface TemplateTreeNode {
  id: number
  name: string
  isFolder: boolean
  parentId: number | null
  sortOrder: number
  size: number
  uploadedAt?: string
  children: TemplateTreeNode[]
}

interface ClipboardState {
  node: TemplateTreeNode
  isCut: boolean
}

function convertApiTree(roots: TemplateFolderNode[]): TemplateTreeNode[] {
  function convertFolder(node: TemplateFolderNode): TemplateTreeNode {
    const children: TemplateTreeNode[] = []
    for (const child of node.children) {
      children.push(convertFolder(child))
    }
    for (const file of node.files) {
      children.push({
        id: file.id,
        name: file.fileName,
        isFolder: false,
        parentId: node.id,
        sortOrder: 0,
        size: file.fileSize,
        uploadedAt: file.uploadedAt,
        children: [],
      })
    }
    children.sort((a, b) => {
      if (a.isFolder !== b.isFolder) {
        return a.isFolder ? -1 : 1
      }
      return a.name.localeCompare(b.name, "it")
    })
    return {
      id: node.id,
      name: node.name,
      isFolder: true,
      parentId: node.parentId,
      sortOrder: node.sortOrder,
      size: 0,
      children,
    }
  }

  return roots.map(convertFolder)
}

function findNode(
  nodes: TemplateTreeNode[],
  id: number,
  isFolder: boolean
): TemplateTreeNode | null {
  for (const node of nodes) {
    if (node.id === id && node.isFolder === isFolder) {
      return node
    }
    const child = findNode(node.children, id, isFolder)
    if (child) {
      return child
    }
  }
  return null
}

function isDescendantFolder(
  root: TemplateTreeNode,
  targetFolderId: number
): boolean {
  for (const child of root.children) {
    if (!child.isFolder) {
      continue
    }
    if (child.id === targetFolderId) {
      return true
    }
    if (isDescendantFolder(child, targetFolderId)) {
      return true
    }
  }
  return false
}

function countDescendants(node: TemplateTreeNode): { folders: number; files: number } {
  let folders = 0
  let files = 0
  for (const child of node.children) {
    if (child.isFolder) {
      folders++
      const nested = countDescendants(child)
      folders += nested.folders
      files += nested.files
    } else {
      files++
    }
  }
  return { folders, files }
}

function TemplateTreeRow({
  node,
  depth,
  selectedKey,
  expandedIds,
  onToggleExpand,
  onSelect,
  onContextAction,
}: {
  node: TemplateTreeNode
  depth: number
  selectedKey: string | null
  expandedIds: Set<number>
  onToggleExpand: (folderId: number) => void
  onSelect: (node: TemplateTreeNode) => void
  onContextAction: (action: string, node: TemplateTreeNode) => void
}) {
  const key = `${node.isFolder ? "f" : "file"}-${node.id}`
  const isSelected = selectedKey === key
  const isExpanded = node.isFolder && expandedIds.has(node.id)

  return (
    <>
      <ContextMenu>
        <ContextMenuTrigger asChild>
          <button
            type="button"
            className={cn(
              "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted",
              isSelected && "bg-muted font-medium"
            )}
            style={{ paddingLeft: `${depth * 14 + 8}px` }}
            onClick={() => {
              onSelect(node)
              if (node.isFolder) {
                onToggleExpand(node.id)
              }
            }}
            onContextMenu={() => onSelect(node)}
          >
            {node.isFolder ? (
              <>
                <span className="w-3 text-xs text-muted-foreground">
                  {isExpanded ? "▾" : "▸"}
                </span>
                <Folder className="size-4 shrink-0 text-muted-foreground" />
              </>
            ) : (
              <>
                <span className="w-3" />
                <File className="size-4 shrink-0 text-muted-foreground" />
              </>
            )}
            <span className="flex-1 truncate">{node.name}</span>
            {!node.isFolder ? (
              <span className="text-xs text-muted-foreground">
                {formatSize(node.size)}
              </span>
            ) : null}
          </button>
        </ContextMenuTrigger>
        <ContextMenuContent>
          {node.isFolder ? (
            <>
              <ContextMenuItem onClick={() => onContextAction("newfolder", node)}>
                Nuova sotto-cartella
              </ContextMenuItem>
              <ContextMenuItem onClick={() => onContextAction("upload", node)}>
                Carica file
              </ContextMenuItem>
              <ContextMenuSeparator />
            </>
          ) : null}
          <ContextMenuItem onClick={() => onContextAction("rename", node)}>
            Rinomina
            <ContextMenuShortcut>F2</ContextMenuShortcut>
          </ContextMenuItem>
          <ContextMenuItem onClick={() => onContextAction("cut", node)}>
            Taglia
            <ContextMenuShortcut>Ctrl+X</ContextMenuShortcut>
          </ContextMenuItem>
          <ContextMenuItem onClick={() => onContextAction("copy", node)}>
            Copia
            <ContextMenuShortcut>Ctrl+C</ContextMenuShortcut>
          </ContextMenuItem>
          {node.isFolder ? (
            <ContextMenuItem onClick={() => onContextAction("paste", node)}>
              Incolla
              <ContextMenuShortcut>Ctrl+V</ContextMenuShortcut>
            </ContextMenuItem>
          ) : null}
          <ContextMenuSeparator />
          <ContextMenuItem
            variant="destructive"
            onClick={() => onContextAction("delete", node)}
          >
            Elimina
            <ContextMenuShortcut>Canc</ContextMenuShortcut>
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>

      {node.isFolder && isExpanded
        ? node.children.map((child) => (
            <TemplateTreeRow
              key={`${child.isFolder ? "f" : "file"}-${child.id}`}
              node={child}
              depth={depth + 1}
              selectedKey={selectedKey}
              expandedIds={expandedIds}
              onToggleExpand={onToggleExpand}
              onSelect={onSelect}
              onContextAction={onContextAction}
            />
          ))
        : null}
    </>
  )
}

export function ProjectTemplatesPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const fileInputRef = React.useRef<HTMLInputElement>(null)
  const containerRef = React.useRef<HTMLDivElement>(null)
  const uploadTargetRef = React.useRef<number | null>(null)

  const [selected, setSelected] = React.useState<TemplateTreeNode | null>(null)
  const [expandedIds, setExpandedIds] = React.useState<Set<number>>(new Set())
  const [clipboard, setClipboard] = React.useState<ClipboardState | null>(null)
  const [renameOpen, setRenameOpen] = React.useState(false)
  const [renameValue, setRenameValue] = React.useState("")
  const [newFolderOpen, setNewFolderOpen] = React.useState(false)
  const [newFolderName, setNewFolderName] = React.useState("")
  const [statusMessage, setStatusMessage] = React.useState<string | null>(null)

  const treeQuery = useQuery({
    queryKey: ["project-template-tree"],
    queryFn: fetchProjectTemplateTree,
  })

  const tree = React.useMemo(
    () => convertApiTree(treeQuery.data ?? []),
    [treeQuery.data]
  )

  React.useEffect(() => {
    if (tree.length > 0 && expandedIds.size === 0) {
      setExpandedIds(new Set(tree.map((node) => node.id)))
    }
  }, [tree, expandedIds.size])

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["project-template-tree"] })
  }

  const refreshMutation = useMutation({
    mutationFn: invalidate,
  })

  function nodeKey(node: TemplateTreeNode): string {
    return `${node.isFolder ? "f" : "file"}-${node.id}`
  }

  function toggleExpand(folderId: number) {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(folderId)) {
        next.delete(folderId)
      } else {
        next.add(folderId)
      }
      return next
    })
  }

  async function handleCreateFolder(parent: TemplateTreeNode | null, name: string) {
    const parentId = parent?.isFolder ? parent.id : null
    await createTemplateFolder(name, parentId)
    if (parent?.isFolder) {
      setExpandedIds((prev) => new Set(prev).add(parent.id))
    }
    await invalidate()
  }

  async function handleUploadFiles(folderId: number, files: File[]) {
    const tooBig = files.filter((file) => file.size > MAX_UPLOAD_BYTES)
    const valid = files.filter((file) => file.size <= MAX_UPLOAD_BYTES)

    if (tooBig.length > 0) {
      notifyError(
        `File troppo grandi (max 500 MB), saltati:\n${tooBig
          .map(
            (file) =>
              `• ${file.name} (${Math.round(file.size / (1024 * 1024))} MB)`
          )
          .join("\n")}`
      )
    }
    if (valid.length === 0) {
      return
    }

    let uploaded = 0
    for (const file of valid) {
      try {
        await uploadTemplateFile(folderId, file)
        uploaded++
      } catch (error) {
        notifyError(`Errore caricando "${file.name}": ${(error as Error).message}`)
      }
    }

    if (uploaded > 0) {
      setExpandedIds((prev) => new Set(prev).add(folderId))
      setStatusMessage(
        uploaded === 1 ? "1 file caricato." : `${uploaded} file caricati.`
      )
      await invalidate()
    }
  }

  async function handleRename(node: TemplateTreeNode, name: string) {
    if (node.isFolder) {
      await updateTemplateFolder(node.id, name, node.parentId, node.sortOrder)
    } else {
      await renameTemplateFile(node.id, name)
    }
    await invalidate()
  }

  async function handleDelete(node: TemplateTreeNode) {
    let title = ""
    let description = ""
    if (node.isFolder) {
      const counts = countDescendants(node)
      const detail =
        counts.folders === 0 && counts.files === 0
          ? "Cartella vuota."
          : `Contiene ${counts.folders} cartelle e ${counts.files} file: verranno eliminati anch'essi.`
      title = "Elimina cartella"
      description = `Eliminare la cartella "${node.name}"?\n${detail}`
    } else {
      title = "Elimina file"
      description = `Eliminare il file "${node.name}"?`
    }
    const ok = await confirm({ title, description, confirmLabel: "Elimina" })
    if (!ok) {
      return
    }

    if (node.isFolder) {
      await deleteTemplateFolder(node.id)
    } else {
      await deleteTemplateFile(node.id)
    }

    if (clipboard?.node.id === node.id && clipboard.node.isFolder === node.isFolder) {
      setClipboard(null)
    }
    if (selected && nodeKey(selected) === nodeKey(node)) {
      setSelected(null)
    }
    await invalidate()
  }

  async function handlePaste(targetFolder: TemplateTreeNode) {
    if (!clipboard || !targetFolder.isFolder) {
      return
    }

    const source = clipboard.node
    if (source.isFolder && source.id === targetFolder.id) {
      notifyError("Impossibile incollare una cartella dentro se stessa.")
      return
    }
    if (source.isFolder && isDescendantFolder(source, targetFolder.id)) {
      notifyError("Impossibile spostare o copiare una cartella dentro un suo discendente.")
      return
    }

    if (source.isFolder) {
      if (clipboard.isCut) {
        await updateTemplateFolder(
          source.id,
          source.name,
          targetFolder.id,
          source.sortOrder
        )
      } else {
        await copyTemplateFolder(source.id, targetFolder.id)
      }
    } else if (clipboard.isCut) {
      await moveTemplateFile(source.id, targetFolder.id)
    } else {
      await copyTemplateFile(source.id, targetFolder.id)
    }

    if (clipboard.isCut) {
      setClipboard(null)
    }
    setExpandedIds((prev) => new Set(prev).add(targetFolder.id))
    await invalidate()
  }

  async function handleContextAction(action: string, node: TemplateTreeNode) {
    setSelected(node)
    setStatusMessage(null)

    switch (action) {
      case "newfolder":
        setNewFolderName("")
        setNewFolderOpen(true)
        break
      case "upload":
        uploadTargetRef.current = node.id
        fileInputRef.current?.click()
        break
      case "rename":
        setRenameValue(node.name)
        setRenameOpen(true)
        break
      case "cut":
        setClipboard({ node, isCut: true })
        setStatusMessage(`Tagliato: ${node.name}`)
        break
      case "copy":
        setClipboard({ node, isCut: false })
        setStatusMessage(`Copiato: ${node.name}`)
        break
      case "paste":
        if (node.isFolder) {
          await handlePaste(node)
        }
        break
      case "delete":
        await handleDelete(node)
        break
      default:
        break
    }
  }

  React.useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!selected || !containerRef.current?.contains(document.activeElement)) {
        return
      }
      const ctrl = event.ctrlKey || event.metaKey

      if (event.key === "F2") {
        event.preventDefault()
        setRenameValue(selected.name)
        setRenameOpen(true)
      } else if (event.key === "Delete") {
        event.preventDefault()
        void handleDelete(selected)
      } else if (ctrl && event.key.toLowerCase() === "x") {
        event.preventDefault()
        setClipboard({ node: selected, isCut: true })
      } else if (ctrl && event.key.toLowerCase() === "c") {
        event.preventDefault()
        setClipboard({ node: selected, isCut: false })
      } else if (ctrl && event.key.toLowerCase() === "v") {
        event.preventDefault()
        if (selected.isFolder && clipboard) {
          void handlePaste(selected)
        }
      }
    }

    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  })

  const selectedKey = selected ? nodeKey(selected) : null
  const pasteTarget =
    selected?.isFolder ? selected : selected?.parentId
      ? findNode(tree, selected.parentId, true)
      : null

  return (
    <div className="space-y-4" ref={containerRef} tabIndex={0}>
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Template commesse</CardTitle>
              <CardDescription>
                Struttura cartelle e file copiati alla creazione commessa. Taglia/copia/incolla,
                rinomina (F2), elimina (Canc).
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => refreshMutation.mutate()}
                disabled={refreshMutation.isPending}
              >
                <RefreshCw className={refreshMutation.isPending ? "animate-spin" : ""} />
                Aggiorna
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setSelected(null)
                  setNewFolderOpen(true)
                }}
              >
                <FolderPlus />
                Nuova cartella
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!selected?.isFolder}
                onClick={() => {
                  if (selected?.isFolder) {
                    uploadTargetRef.current = selected.id
                    fileInputRef.current?.click()
                  }
                }}
              >
                <Upload />
                Carica file
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!selected}
                onClick={() => {
                  if (selected) {
                    setRenameValue(selected.name)
                    setRenameOpen(true)
                  }
                }}
              >
                <Pencil />
                Rinomina
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!clipboard || !pasteTarget}
                onClick={() => {
                  if (pasteTarget) {
                    void handlePaste(pasteTarget)
                  }
                }}
              >
                <ClipboardPaste />
                Incolla
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!selected}
                onClick={() => selected && setClipboard({ node: selected, isCut: true })}
              >
                <Scissors />
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!selected}
                onClick={() => selected && setClipboard({ node: selected, isCut: false })}
              >
                <Copy />
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!selected}
                onClick={() => selected && void handleDelete(selected)}
              >
                <Trash2 />
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept={UPLOAD_ACCEPT}
            className="hidden"
            onChange={(event) => {
              const files = event.target.files
              const folderId = uploadTargetRef.current
              if (files && files.length > 0 && folderId) {
                void handleUploadFiles(folderId, Array.from(files))
              }
              event.target.value = ""
            }}
          />

          {statusMessage ? (
            <p className="mb-3 text-sm text-muted-foreground">{statusMessage}</p>
          ) : null}

          {treeQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : tree.length === 0 ? (
            <Empty>
              <EmptyHeader>
                <EmptyTitle>Nessun template configurato</EmptyTitle>
                <EmptyDescription>
                  Usa «Nuova cartella» per iniziare.
                </EmptyDescription>
              </EmptyHeader>
            </Empty>
          ) : (
            <div className="max-h-[70vh] overflow-y-auto rounded-lg border p-2">
              {tree.map((node) => (
                <TemplateTreeRow
                  key={nodeKey(node)}
                  node={node}
                  depth={0}
                  selectedKey={selectedKey}
                  expandedIds={expandedIds}
                  onToggleExpand={toggleExpand}
                  onSelect={setSelected}
                  onContextAction={handleContextAction}
                />
              ))}
            </div>
          )}

          {treeQuery.error ? (
            <PageErrorAlert message={(treeQuery.error as Error).message} />
          ) : null}
        </CardContent>
      </Card>

      <Dialog open={newFolderOpen} onOpenChange={setNewFolderOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Nuova cartella</DialogTitle>
          </DialogHeader>
          <div className="space-y-2">
            <Label>Nome</Label>
            <Input
              value={newFolderName}
              onChange={(event) => setNewFolderName(event.target.value)}
              placeholder="Es. 01_Documenti"
            />
            <p className="text-xs text-muted-foreground">
              {selected?.isFolder
                ? `Sotto-cartella di: ${selected.name}`
                : "Cartella radice"}
            </p>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setNewFolderOpen(false)}>
              Annulla
            </Button>
            <Button
              onClick={async () => {
                if (!newFolderName.trim()) return
                await handleCreateFolder(selected?.isFolder ? selected : null, newFolderName.trim())
                setNewFolderOpen(false)
                setNewFolderName("")
              }}
            >
              Crea
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={renameOpen} onOpenChange={setRenameOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Rinomina</DialogTitle>
          </DialogHeader>
          <div className="space-y-2">
            <Label>Nuovo nome</Label>
            <Input
              value={renameValue}
              onChange={(event) => setRenameValue(event.target.value)}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRenameOpen(false)}>
              Annulla
            </Button>
            <Button
              onClick={async () => {
                if (!selected || !renameValue.trim() || renameValue === selected.name) {
                  setRenameOpen(false)
                  return
                }
                await handleRename(selected, renameValue.trim())
                setRenameOpen(false)
              }}
            >
              Salva
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
