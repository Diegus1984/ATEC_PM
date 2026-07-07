import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { useSearchParams } from "react-router-dom"
import {
  Download,
  Eye,
  File as FileIcon,
  Folder,
  FolderOpen,
  FolderPlus,
  Home,
  Pencil,
  RefreshCw,
  Search,
  Trash2,
  Upload,
  X,
} from "lucide-react"

import { RowActionsMenu } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchProjectFiles } from "@/lib/api/project-documents"
import { downloadProjectFile } from "@/lib/api/projects"
import { getSession } from "@/lib/auth/session"
import { notifyError } from "@/lib/toast"
import type { FileItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { filterFileItems } from "./project-documents-filter"
import { FileDocContextMenu } from "./FileDocContextMenu"
import { FolderDocContextMenu } from "./FolderDocContextMenu"
import { useProjectDocumentsActions } from "./project-documents-actions"
import { ProjectFilePreviewDialog } from "./ProjectFilePreviewDialog"

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function formatDateTime(value: string | null): string {
  if (!value) return "—"
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return "—"
  return date.toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })
}

function sortItems(items: FileItem[]): FileItem[] {
  return [...items].sort((a, b) => {
    if (a.isFolder !== b.isFolder) {
      return a.isFolder ? -1 : 1
    }
    return a.name.localeCompare(b.name, "it")
  })
}

interface PathCrumb {
  name: string
  path: string
}

/**
 * Card "contenuto cartella" della commessa. La cartella corrente è pilotata dal
 * query param `?path=` dell'URL, così l'albero documenti nel pannello sinistro
 * (CommessaTree → CommessaDocumentsTree) e questa vista restano sincronizzati.
 * `?preview=<percorso file>` apre direttamente l'anteprima (click su un file
 * nell'albero a sinistra).
 */
export function ProjectDocuments({ projectId }: { projectId: number }) {
  const docActions = useProjectDocumentsActions()
  const [searchParams, setSearchParams] = useSearchParams()

  const subPath = searchParams.get("path") ?? ""

  const [previewItem, setPreviewItem] = React.useState<FileItem | null>(null)
  const [dragOver, setDragOver] = React.useState(false)
  const [statusMessage, setStatusMessage] = React.useState<string | null>(null)
  const [folderSearch, setFolderSearch] = React.useState("")

  const crumbs: PathCrumb[] = React.useMemo(() => {
    if (!subPath) return []
    const segments = subPath.split("/")
    return segments.map((segment, index) => ({
      name: segment,
      path: segments.slice(0, index + 1).join("/"),
    }))
  }, [subPath])

  /** Aggiorna la cartella corrente nell'URL (azzera sempre l'eventuale preview). */
  const navigateTo = React.useCallback(
    (path: string) => {
      setStatusMessage(null)
      const next = new URLSearchParams(searchParams)
      if (path) {
        next.set("path", path)
      } else {
        next.delete("path")
      }
      next.delete("preview")
      setSearchParams(next)
    },
    [searchParams, setSearchParams]
  )

  // Apertura anteprima da deep-link (?preview=...): apre il dialogo e ripulisce
  // il parametro dall'URL senza aggiungere voci alla history.
  React.useEffect(() => {
    const preview = searchParams.get("preview")
    if (!preview) {
      return
    }
    setPreviewItem({
      name: preview.split("/").pop() ?? preview,
      isFolder: false,
      size: 0,
      relativePath: preview,
      modified: null,
    })
    const next = new URLSearchParams(searchParams)
    next.delete("preview")
    setSearchParams(next, { replace: true })
  }, [searchParams, setSearchParams])

  const filesQuery = useQuery({
    queryKey: ["project-folder", projectId, subPath],
    queryFn: () => fetchProjectFiles(projectId, subPath),
    enabled: projectId > 0,
  })

  function handleFiles(fileList: FileList | File[]) {
    setStatusMessage(null)
    docActions.uploadFiles(subPath, Array.from(fileList))
  }

  function openFolder(item: FileItem) {
    navigateTo(item.relativePath)
  }

  function handleDownload(item: FileItem) {
    void downloadProjectFile(
      projectId,
      item.relativePath,
      getSession()?.token ?? null
    ).catch((err: Error) => notifyError(err))
  }

  const allItems = sortItems(filesQuery.data ?? [])
  const items = filterFileItems(allItems, folderSearch)
  const isFiltering = folderSearch.trim().length > 0
  const isEmpty = !filesQuery.isLoading && items.length === 0
  const isRoot = subPath === ""

  // Cambio cartella: azzera il filtro locale del contenuto.
  React.useEffect(() => {
    setFolderSearch("")
  }, [subPath])

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle>Documenti commessa</CardTitle>
            <CardDescription>
              Contenuto della cartella selezionata nell'albero a sinistra.
              Trascina i file per caricarli.
            </CardDescription>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => docActions.openNewFolderDialog(subPath)}
            >
              <FolderPlus />
              Nuova cartella
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={docActions.isUploadPending}
              onClick={() => docActions.triggerUpload(subPath)}
            >
              <Upload
                className={docActions.isUploadPending ? "animate-pulse" : ""}
              />
              Carica file
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => filesQuery.refetch()}
              disabled={filesQuery.isFetching}
            >
              <RefreshCw
                className={filesQuery.isFetching ? "animate-spin" : ""}
              />
              Aggiorna
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {/* Breadcrumb (sincronizzato con l'albero a sinistra) */}
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem>
              {isRoot ? (
                <BreadcrumbPage className="inline-flex items-center gap-1">
                  <Home className="size-3.5" />
                  Root
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <button
                    type="button"
                    className="inline-flex items-center gap-1"
                    onClick={() => navigateTo("")}
                  >
                    <Home className="size-3.5" />
                    Root
                  </button>
                </BreadcrumbLink>
              )}
            </BreadcrumbItem>
            {crumbs.map((crumb, index) => (
              <React.Fragment key={crumb.path}>
                <BreadcrumbSeparator />
                <BreadcrumbItem>
                  {index === crumbs.length - 1 ? (
                    <BreadcrumbPage>{crumb.name}</BreadcrumbPage>
                  ) : (
                    <BreadcrumbLink asChild>
                      <button
                        type="button"
                        onClick={() => navigateTo(crumb.path)}
                      >
                        {crumb.name}
                      </button>
                    </BreadcrumbLink>
                  )}
                </BreadcrumbItem>
              </React.Fragment>
            ))}
          </BreadcrumbList>
        </Breadcrumb>

        <div className="relative max-w-sm">
          <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
          <Input
            value={folderSearch}
            placeholder="Filtra elementi in questa cartella…"
            className="pl-8 pr-8"
            onChange={(event) => setFolderSearch(event.target.value)}
          />
          {folderSearch ? (
            <button
              type="button"
              aria-label="Cancella filtro"
              className="absolute right-2 top-2 flex size-5 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground"
              onClick={() => setFolderSearch("")}
            >
              <X className="size-3.5" />
            </button>
          ) : null}
        </div>

        {statusMessage ? (
          <p className="text-sm text-muted-foreground">{statusMessage}</p>
        ) : null}

        {filesQuery.isError ? (
          <p className="text-sm text-destructive">
            {(filesQuery.error as Error).message}
          </p>
        ) : null}

        <div
          className={cn(
            "relative overflow-x-auto rounded-lg border transition-colors",
            dragOver && "border-primary bg-primary/5"
          )}
          onDragOver={(event) => {
            event.preventDefault()
            setDragOver(true)
          }}
          onDragLeave={(event) => {
            event.preventDefault()
            setDragOver(false)
          }}
          onDrop={(event) => {
            event.preventDefault()
            setDragOver(false)
            if (docActions.isUploadPending) return
            const dropped = event.dataTransfer?.files
            if (dropped && dropped.length > 0) {
              handleFiles(dropped)
            }
          }}
        >
          {dragOver ? (
            <div className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center bg-primary/5 text-sm font-medium text-primary">
              Rilascia i file qui per caricarli
            </div>
          ) : null}

          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow className="hover:bg-transparent">
                <TableHead>Nome</TableHead>
                <TableHead className="w-28 text-right">Dimensione</TableHead>
                <TableHead className="w-44">Modificato</TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {filesQuery.isLoading ? (
                Array.from({ length: 5 }).map((_, rowIndex) => (
                  <TableRow key={`skeleton-${rowIndex}`}>
                    {Array.from({ length: 4 }).map((__, cellIndex) => (
                      <TableCell key={cellIndex}>
                        <Skeleton className="h-5 w-full" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              ) : isEmpty ? (
                <TableRow>
                  <TableCell
                    colSpan={4}
                    className="h-24 text-center text-muted-foreground"
                  >
                    {isFiltering ? (
                      `Nessuna corrispondenza per «${folderSearch.trim()}».`
                    ) : isRoot ? (
                      <div className="flex flex-col items-center gap-2">
                        <span>Cartella commessa vuota o non ancora creata.</span>
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={docActions.isCreateBaseFolderPending}
                          onClick={() => docActions.createBaseFolder()}
                        >
                          <FolderPlus />
                          Crea cartella commessa
                        </Button>
                      </div>
                    ) : (
                      "Cartella vuota."
                    )}
                  </TableCell>
                </TableRow>
              ) : (
                items.map((item) => {
                  const row = (
                    <TableRow
                      key={item.relativePath}
                      className="cursor-pointer"
                      onDoubleClick={() =>
                        item.isFolder ? openFolder(item) : setPreviewItem(item)
                      }
                    >
                      <TableCell>
                        <div className="flex items-center gap-2">
                          {item.isFolder ? (
                            <Folder className="size-4 shrink-0 text-muted-foreground" />
                          ) : (
                            <FileIcon className="size-4 shrink-0 text-muted-foreground" />
                          )}
                          <span className="truncate" title={item.name}>
                            {item.name}
                          </span>
                        </div>
                      </TableCell>
                      <TableCell className="text-right tabular-nums text-muted-foreground">
                        {item.isFolder ? "" : formatSize(item.size)}
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {formatDateTime(item.modified)}
                      </TableCell>
                      <TableCell className="text-right">
                        <RowActionsMenu
                          label={item.name}
                          actions={
                            item.isFolder
                              ? [
                                  {
                                    label: "Apri",
                                    icon: FolderOpen,
                                    onClick: () => openFolder(item),
                                  },
                                  {
                                    label: "Rinomina",
                                    icon: Pencil,
                                    onClick: () =>
                                      docActions.openRenameDialog(item),
                                  },
                                  {
                                    label: "Sposta…",
                                    icon: FolderOpen,
                                    onClick: () => docActions.openMoveDialog(item),
                                  },
                                  {
                                    label: "Elimina",
                                    icon: Trash2,
                                    destructive: true,
                                    separatorBefore: true,
                                    onClick: () => {
                                      void docActions.deleteItem(item)
                                    },
                                  },
                                ]
                              : [
                                  {
                                    label: "Anteprima",
                                    icon: Eye,
                                    onClick: () => setPreviewItem(item),
                                  },
                                  {
                                    label: "Scarica",
                                    icon: Download,
                                    onClick: () => handleDownload(item),
                                  },
                                  {
                                    label: "Rinomina",
                                    icon: Pencil,
                                    onClick: () =>
                                      docActions.openRenameDialog(item),
                                  },
                                  {
                                    label: "Sposta…",
                                    icon: FolderOpen,
                                    onClick: () => docActions.openMoveDialog(item),
                                  },
                                  {
                                    label: "Elimina",
                                    icon: Trash2,
                                    destructive: true,
                                    separatorBefore: true,
                                    onClick: () => {
                                      void docActions.deleteItem(item)
                                    },
                                  },
                                ]
                          }
                        />
                      </TableCell>
                    </TableRow>
                  )

                  if (item.isFolder) {
                    return (
                      <FolderDocContextMenu
                        key={item.relativePath}
                        item={item}
                        onOpen={() => openFolder(item)}
                      >
                        {row}
                      </FolderDocContextMenu>
                    )
                  }

                  return (
                    <FileDocContextMenu
                      key={item.relativePath}
                      item={item}
                      onPreview={() => setPreviewItem(item)}
                      onDownload={() => handleDownload(item)}
                    >
                      {row}
                    </FileDocContextMenu>
                  )
                })
              )}
            </TableBody>
          </Table>
        </div>
      </CardContent>

      <ProjectFilePreviewDialog
        open={previewItem !== null}
        projectId={projectId}
        item={previewItem}
        onClose={() => setPreviewItem(null)}
      />
    </Card>
  )
}
