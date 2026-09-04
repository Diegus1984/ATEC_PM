import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { useLocation, useNavigate, useSearchParams } from "react-router-dom"
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

import { ColumnsMenu } from "@/components/shared/columns-menu"
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
import { GridScroller } from "@/components/shared/grid-scroller"
import { fetchProjectFiles } from "@/lib/api/project-documents"
import { downloadProjectFile } from "@/lib/api/projects"
import { getSession } from "@/lib/auth/session"
import { notifyError } from "@/lib/toast"
import type { FileItem } from "@/lib/api/types"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import {
  hasLegacyDocumentsParams,
  readDocumentsNav,
  stripLegacyDocumentsParams,
  withDocumentsNav,
} from "./documents-nav-state"
import { filterFileItems } from "./project-documents-filter"
import { FileDocContextMenu } from "./FileDocContextMenu"
import { FolderDocContextMenu } from "./FolderDocContextMenu"
import { useProjectDocumentsActions } from "./project-documents-actions"
import { ProjectFilePreviewDialog } from "./ProjectFilePreviewDialog"
import { formatSize } from "@/lib/format"
import { sortItems } from "./documents-shared"

/** Colonne opzionali dell'elenco documenti («Nome» e le azioni restano sempre). */
const DOC_COLUMNS: { id: string; label: string }[] = [
  { id: "size", label: "Dimensione" },
  { id: "modified", label: "Modificato" },
]
const DOC_COLUMNS_DEFAULT = Object.fromEntries(
  DOC_COLUMNS.map((column) => [column.id, true])
)

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

interface PathCrumb {
  name: string
  path: string
}

/**
 * Card "contenuto cartella" della commessa. La cartella corrente è pilotata
 * dall'history state (`docPath`, vedi `documents-nav-state`), così l'albero
 * documenti nel pannello sinistro (CommessaTree → CommessaDocumentsTree) e
 * questa vista restano sincronizzati senza esporre il percorso nell'URL.
 * `docPreview` apre direttamente l'anteprima di un file (click su un file
 * nell'albero a sinistra).
 */
export function ProjectDocuments({ projectId }: { projectId: number }) {
  const docActions = useProjectDocumentsActions()
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()

  const { docPath: subPath, docPreview } = readDocumentsNav(
    location.state,
    searchParams
  )

  const [visibleCols, setVisibleCols] = usePersistedColumnVisibility(
    "project-documents-columns-v1",
    DOC_COLUMNS_DEFAULT
  )
  const showCol = (id: string) => visibleCols[id] ?? true
  const columnToggles = DOC_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: showCol(id),
    onToggle: (value: boolean) =>
      setVisibleCols((prev) => ({ ...prev, [id]: value })),
  }))
  const docColCount = 2 + DOC_COLUMNS.filter((column) => showCol(column.id)).length

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

  /**
   * Aggiorna la cartella corrente nell'history state (azzera sempre l'eventuale
   * preview). L'URL non cambia, ma avanti/indietro del browser tornano alla
   * cartella precedente.
   */
  const navigateTo = React.useCallback(
    (path: string) => {
      setStatusMessage(null)
      navigate(
        {
          pathname: location.pathname,
          search: stripLegacyDocumentsParams(searchParams),
        },
        { state: withDocumentsNav(location.state, { docPath: path }) }
      )
    },
    [location.pathname, location.state, navigate, searchParams]
  )

  // Anteprima richiesta dall'albero (o da un vecchio link `?preview=`): apre il
  // dialogo e consuma il riferimento con un replace, così back e refresh non la
  // riaprono. Lo stesso passaggio ripulisce l'URL dai parametri legacy.
  const legacyParams = hasLegacyDocumentsParams(searchParams)
  React.useEffect(() => {
    if (!docPreview && !legacyParams) {
      return
    }
    if (docPreview) {
      setPreviewItem({
        name: docPreview.split("/").pop() ?? docPreview,
        isFolder: false,
        size: 0,
        relativePath: docPreview,
        modified: null,
      })
    }
    navigate(
      {
        pathname: location.pathname,
        search: stripLegacyDocumentsParams(searchParams),
      },
      {
        replace: true,
        state: withDocumentsNav(location.state, { docPath: subPath }),
      }
    )
  }, [
    docPreview,
    legacyParams,
    location.pathname,
    location.state,
    navigate,
    searchParams,
    subPath,
  ])

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
            <ColumnsMenu columns={columnToggles} />
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
            "relative overflow-hidden rounded-lg border transition-colors",
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

          <GridScroller>
          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow className="hover:bg-transparent">
                <TableHead>Nome</TableHead>
                {showCol("size") && (
                  <TableHead className="w-28 text-right">Dimensione</TableHead>
                )}
                {showCol("modified") && (
                  <TableHead className="w-44">Modificato</TableHead>
                )}
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {filesQuery.isLoading ? (
                Array.from({ length: 5 }).map((_, rowIndex) => (
                  <TableRow key={`skeleton-${rowIndex}`}>
                    {Array.from({ length: docColCount }).map((__, cellIndex) => (
                      <TableCell key={cellIndex}>
                        <Skeleton className="h-5 w-full" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              ) : isEmpty ? (
                <TableRow>
                  <TableCell
                    colSpan={docColCount}
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
                      {showCol("size") && (
                        <TableCell className="text-right tabular-nums text-muted-foreground">
                          {item.isFolder ? "" : formatSize(item.size)}
                        </TableCell>
                      )}
                      {showCol("modified") && (
                        <TableCell className="text-muted-foreground">
                          {formatDateTime(item.modified)}
                        </TableCell>
                      )}
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
          </GridScroller>
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
