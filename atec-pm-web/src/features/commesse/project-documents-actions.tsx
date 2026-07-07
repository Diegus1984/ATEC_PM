import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  createProjectBaseFolder,
  createProjectSubfolder,
  deleteProjectItem,
  moveProjectItem,
  renameProjectItem,
  uploadProjectFiles,
} from "@/lib/api/project-documents"
import type { FileItem } from "@/lib/api/types"
import { useProjectDocumentsHub } from "@/lib/signalr/use-project-documents-hub"

import { normalizeDocPath } from "./project-documents-filter"
import {
  buildFileRenameName,
  splitFileName,
} from "./project-documents-rename"

const MAX_UPLOAD_BYTES = 500 * 1024 * 1024

const UPLOAD_ACCEPT =
  ".pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.rtf,.dwg,.dxf,.step,.stp,.stl,.iges,.igs,.obj,.sldprt,.sldasm,.slddrw,.easm,.eprt,.edrw,.png,.jpg,.jpeg,.bmp,.gif,.webp,.mp4,.webm,.mov,.avi,.mkv,.m4v,.ogv,.wmv,.zip,.rar,.7z"

function normalizeFolderPath(value: string): string {
  return value
    .replace(/\\/g, "/")
    .replace(/\/+/g, "/")
    .replace(/^\/+|\/+$/g, "")
    .trim()
}

/** Cartella padre di un percorso (vuoto = root commessa). */
export function parentFolderPath(path: string): string {
  const normalized = normalizeDocPath(path)
  const slash = normalized.lastIndexOf("/")
  return slash >= 0 ? normalized.slice(0, slash) : ""
}

export interface ProjectDocumentsActions {
  openNewFolderDialog: (parentPath: string) => void
  triggerUpload: (targetPath: string) => void
  uploadFiles: (targetPath: string, files: File[]) => void
  openRenameDialog: (item: FileItem) => void
  openMoveDialog: (item: FileItem) => void
  deleteItem: (item: FileItem) => Promise<void>
  createBaseFolder: () => void
  isUploadPending: boolean
  isCreateBaseFolderPending: boolean
}

const ProjectDocumentsActionsContext =
  React.createContext<ProjectDocumentsActions | null>(null)

export function useProjectDocumentsActions(): ProjectDocumentsActions {
  const ctx = React.useContext(ProjectDocumentsActionsContext)
  if (!ctx) {
    throw new Error(
      "useProjectDocumentsActions richiede ProjectDocumentsActionsProvider"
    )
  }
  return ctx
}

/** Azioni documenti condivise tra albero (sinistra) e card contenuto (destra). */
export function ProjectDocumentsActionsProvider({
  projectId,
  children,
}: {
  projectId: number
  children: React.ReactNode
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const uploadInputRef = React.useRef<HTMLInputElement>(null)
  const uploadTargetPathRef = React.useRef("")

  const [newFolderOpen, setNewFolderOpen] = React.useState(false)
  const [newFolderName, setNewFolderName] = React.useState("")
  const [newFolderParentPath, setNewFolderParentPath] = React.useState("")
  const [renameTarget, setRenameTarget] = React.useState<FileItem | null>(null)
  const [renameValue, setRenameValue] = React.useState("")
  const [moveTarget, setMoveTarget] = React.useState<FileItem | null>(null)
  const [moveDest, setMoveDest] = React.useState("")

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["project-folder", projectId] })
    void queryClient.invalidateQueries({ queryKey: ["project-file-tree", projectId] })
  }, [queryClient, projectId])

  useProjectDocumentsHub(projectId, () => {
    void invalidate()
  })

  const uploadMutation = useMutation({
    mutationFn: ({ subPath, files }: { subPath: string; files: File[] }) =>
      uploadProjectFiles(projectId, subPath, files),
    onSuccess: () => void invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const baseFolderMutation = useMutation({
    mutationFn: () => createProjectBaseFolder(projectId),
    onSuccess: () => void invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const createFolderMutation = useMutation({
    mutationFn: () =>
      createProjectSubfolder(
        projectId,
        newFolderParentPath,
        newFolderName.trim()
      ),
    onSuccess: () => {
      setNewFolderOpen(false)
      setNewFolderName("")
      void invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const renameMutation = useMutation({
    mutationFn: (newName: string) =>
      renameProjectItem(projectId, renameTarget!.relativePath, newName),
    onSuccess: () => {
      setRenameTarget(null)
      void invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const moveMutation = useMutation({
    mutationFn: () => {
      const dest = normalizeFolderPath(moveDest)
      if (dest.split("/").some((segment) => segment === "..")) {
        return Promise.reject(new Error("Percorso destinazione non valido."))
      }
      return moveProjectItem(projectId, moveTarget!.relativePath, dest)
    },
    onSuccess: () => {
      setMoveTarget(null)
      void invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  function handleUploadFiles(fileList: FileList | File[]) {
    const subPath = uploadTargetPathRef.current
    const all = Array.from(fileList)
    const tooBig = all.filter((file) => file.size > MAX_UPLOAD_BYTES)
    const valid = all.filter((file) => file.size <= MAX_UPLOAD_BYTES)
    if (tooBig.length > 0) {
      notifyError(
        `File troppo grandi (max 500 MB), saltati:\n${tooBig
          .map((file) => `• ${file.name}`)
          .join("\n")}`
      )
    }
    if (valid.length > 0) {
      uploadMutation.mutate({ subPath, files: valid })
    }
  }

  const actions = React.useMemo<ProjectDocumentsActions>(
    () => ({
      openNewFolderDialog(parentPath: string) {
        setNewFolderParentPath(normalizeDocPath(parentPath))
        setNewFolderName("")
        setNewFolderOpen(true)
      },
      triggerUpload(targetPath: string) {
        uploadTargetPathRef.current = normalizeDocPath(targetPath)
        uploadInputRef.current?.click()
      },
      uploadFiles(targetPath: string, files: File[]) {
        uploadTargetPathRef.current = normalizeDocPath(targetPath)
        handleUploadFiles(files)
      },
      openRenameDialog(item: FileItem) {
        if (item.isFolder) {
          setRenameValue(item.name)
        } else {
          setRenameValue(splitFileName(item.name).stem)
        }
        setRenameTarget(item)
      },
      openMoveDialog(item: FileItem) {
        setMoveDest(parentFolderPath(item.relativePath))
        setMoveTarget(item)
      },
      async deleteItem(item: FileItem) {
        const ok = await confirm({
          title: item.isFolder ? "Elimina cartella" : "Elimina file",
          description: item.isFolder
            ? `Eliminare la cartella "${item.name}"? Tutto il contenuto verrà eliminato.`
            : `Eliminare il file "${item.name}"?`,
          confirmLabel: "Elimina",
        })
        if (!ok) return
        try {
          await deleteProjectItem(projectId, item.relativePath)
          void invalidate()
        } catch (err) {
          notifyError(err)
        }
      },
      createBaseFolder() {
        baseFolderMutation.mutate()
      },
      isUploadPending: uploadMutation.isPending,
      isCreateBaseFolderPending: baseFolderMutation.isPending,
    }),
    [
      confirm,
      projectId,
      invalidate,
      uploadMutation.isPending,
      baseFolderMutation,
    ]
  )

  function submitNewFolder() {
    if (!newFolderName.trim() || createFolderMutation.isPending) return
    createFolderMutation.mutate()
  }

  function resolveRenameNewName(): string | null {
    if (!renameTarget) {
      return null
    }
    if (renameTarget.isFolder) {
      const name = renameValue.trim()
      return name || null
    }
    return buildFileRenameName(renameTarget.name, renameValue)
  }

  function submitRename() {
    if (renameMutation.isPending || !renameTarget) {
      return
    }
    const newName = resolveRenameNewName()
    if (!newName) {
      notifyError(
        renameTarget.isFolder
          ? "Inserisci un nome valido."
          : "Inserisci un nome file valido (senza estensione)."
      )
      return
    }
    if (newName === renameTarget.name) {
      setRenameTarget(null)
      return
    }
    renameMutation.mutate(newName)
  }

  function submitMove() {
    if (moveMutation.isPending) return
    moveMutation.mutate()
  }

  const newFolderIsRoot = newFolderParentPath === ""
  const renameIsFile = renameTarget != null && !renameTarget.isFolder
  const renameFileExtension =
    renameTarget && renameIsFile ? splitFileName(renameTarget.name).extension : ""
  const resolvedRenameName = resolveRenameNewName()

  return (
    <ProjectDocumentsActionsContext.Provider value={actions}>
      {children}
      <input
        ref={uploadInputRef}
        type="file"
        multiple
        accept={UPLOAD_ACCEPT}
        className="hidden"
        onChange={(event) => {
          const files = event.target.files
          if (!uploadMutation.isPending && files && files.length > 0) {
            handleUploadFiles(files)
          }
          event.target.value = ""
        }}
      />

      <Dialog open={newFolderOpen} onOpenChange={setNewFolderOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Nuova cartella</DialogTitle>
            <DialogDescription>
              {newFolderIsRoot
                ? "Nella cartella radice."
                : `Dentro: ${newFolderParentPath}`}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label>Nome cartella</Label>
            <Input
              value={newFolderName}
              autoFocus
              onChange={(event) => setNewFolderName(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter") submitNewFolder()
              }}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setNewFolderOpen(false)}>
              Annulla
            </Button>
            <Button
              onClick={submitNewFolder}
              disabled={!newFolderName.trim() || createFolderMutation.isPending}
            >
              {createFolderMutation.isPending ? "Creazione…" : "Crea"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog
        open={renameTarget !== null}
        onOpenChange={(next) => !next && setRenameTarget(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Rinomina</DialogTitle>
            {renameIsFile ? (
              <DialogDescription>
                Modifica solo il nome del file. L&apos;estensione{" "}
                <span className="font-medium">{renameFileExtension || "—"}</span>{" "}
                resta invariata.
              </DialogDescription>
            ) : null}
          </DialogHeader>
          <div className="space-y-2">
            <Label>{renameIsFile ? "Nome file" : "Nuovo nome"}</Label>
            {renameIsFile ? (
              <div className="flex items-center gap-1">
                <Input
                  value={renameValue}
                  autoFocus
                  className="min-w-0 flex-1"
                  onChange={(event) => setRenameValue(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") submitRename()
                  }}
                />
                {renameFileExtension ? (
                  <span className="shrink-0 text-sm text-muted-foreground">
                    {renameFileExtension}
                  </span>
                ) : null}
              </div>
            ) : (
              <Input
                value={renameValue}
                autoFocus
                onChange={(event) => setRenameValue(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") submitRename()
                }}
              />
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRenameTarget(null)}>
              Annulla
            </Button>
            <Button
              onClick={submitRename}
              disabled={
                !resolvedRenameName ||
                resolvedRenameName === renameTarget?.name ||
                renameMutation.isPending
              }
            >
              {renameMutation.isPending ? "Salvataggio…" : "Salva"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog
        open={moveTarget !== null}
        onOpenChange={(next) => !next && setMoveTarget(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Sposta «{moveTarget?.name}»</DialogTitle>
            <DialogDescription>
              Percorso cartella di destinazione, relativo alla root commessa
              (vuoto = root).
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label>Cartella destinazione</Label>
            <Input
              value={moveDest}
              autoFocus
              placeholder="Es. 20_Ufficio_Tecnico"
              onChange={(event) => setMoveDest(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter") submitMove()
              }}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setMoveTarget(null)}>
              Annulla
            </Button>
            <Button onClick={submitMove} disabled={moveMutation.isPending}>
              {moveMutation.isPending ? "Spostamento…" : "Sposta"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </ProjectDocumentsActionsContext.Provider>
  )
}
