import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ChevronRight, File as FileIcon, Folder, Search, X } from "lucide-react"

import { Input } from "@/components/ui/input"
import { fetchProjectFiles } from "@/lib/api/project-documents"
import { fetchProjectFileTree } from "@/lib/api/projects"
import type { FileItem, FileTreeItem } from "@/lib/api/types"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import {
  FolderDocContextMenu,
  toFolderFileItem,
} from "./FolderDocContextMenu"
import {
  collectFolderPaths,
  filterFileTree,
  normalizeDocPath,
} from "./project-documents-filter"
import { sortItems } from "./documents-shared"

function sortTreeItems(items: FileTreeItem[]): FileTreeItem[] {
  return [...items].sort((a, b) => {
    if (a.isFolder !== b.isFolder) {
      return a.isFolder ? -1 : 1
    }
    return a.name.localeCompare(b.name, "it")
  })
}

/** Cartella padre di un percorso file/cartella (vuoto = root commessa). */
function parentOf(path: string): string {
  const slash = path.lastIndexOf("/")
  return slash >= 0 ? path.slice(0, slash) : ""
}

/**
 * Albero documenti di navigazione per il pannello sinistro (CommessaTree).
 * Navigazione lazy per-livello; con ricerca attiva carica l'albero completo
 * (`file-tree`) e lo filtra espandendo i rami con corrispondenze.
 */
export function CommessaDocumentsTree({
  projectId,
  actionsProjectId,
  currentPath,
  onOpenFolder,
  onOpenFile,
}: {
  projectId: number
  /** Commessa attiva in URL: il menu contestuale agisce solo se coincide con projectId. */
  actionsProjectId: number | null
  currentPath: string
  onOpenFolder: (path: string) => void
  onOpenFile: (parentPath: string, fileRelativePath: string) => void
}) {
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 300)
  const isSearching = searchTerm.length > 0

  const [expanded, setExpanded] = React.useState<Set<string>>(new Set())

  // Espandi automaticamente la cartella corrente e tutti i suoi antenati.
  React.useEffect(() => {
    if (!currentPath || isSearching) {
      return
    }
    const segments = currentPath.split("/")
    const ancestors = segments.map((_, index) =>
      segments.slice(0, index + 1).join("/")
    )
    setExpanded((prev) => {
      const next = new Set(prev)
      for (const ancestor of ancestors) {
        next.add(ancestor)
      }
      return next
    })
  }, [currentPath, isSearching])

  const rootQuery = useQuery({
    queryKey: ["project-folder", projectId, ""],
    queryFn: () => fetchProjectFiles(projectId, ""),
    enabled: projectId > 0 && !isSearching,
  })

  const treeQuery = useQuery({
    queryKey: ["project-file-tree", projectId],
    queryFn: () => fetchProjectFileTree(projectId),
    enabled: projectId > 0 && isSearching,
  })

  const filteredTree = React.useMemo(() => {
    if (!isSearching || !treeQuery.data) {
      return []
    }
    return sortTreeItems(filterFileTree(treeQuery.data, searchTerm))
  }, [isSearching, treeQuery.data, searchTerm])

  // All'avvio di una nuova ricerca espandi i rami con corrispondenze; poi l'utente
  // può comprimere/espandere liberamente (non sovrascrivere ad ogni re-render).
  React.useEffect(() => {
    if (!isSearching || !treeQuery.data) {
      return
    }
    const filtered = sortTreeItems(filterFileTree(treeQuery.data, searchTerm))
    const folderPaths = collectFolderPaths(filtered)
    setExpanded(new Set(folderPaths))
  }, [searchTerm, isSearching, treeQuery.data])

  function toggle(path: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(path)) {
        next.delete(path)
      } else {
        next.add(path)
      }
      return next
    })
  }

  const rootItems = sortItems(rootQuery.data ?? [])

  function renderTreeBody() {
    if (isSearching) {
      if (treeQuery.isLoading) {
        return (
          <p className="px-2 py-1 text-xs text-muted-foreground">Caricamento…</p>
        )
      }
      if (treeQuery.isError) {
        return (
          <p className="px-2 py-1 text-xs text-destructive">
            {(treeQuery.error as Error).message}
          </p>
        )
      }
      if (filteredTree.length === 0) {
        return (
          <p className="px-2 py-1 text-xs text-muted-foreground">
            Nessuna corrispondenza per «{searchTerm}».
          </p>
        )
      }
      return filteredTree.map((item) => (
        <FilteredDocNavRow
          key={normalizeDocPath(item.relativePath)}
          item={item}
          depth={0}
          expanded={expanded}
          currentPath={currentPath}
          searchTerm={searchTerm}
          contextMenuEnabled={projectId === actionsProjectId}
          onToggle={toggle}
          onOpenFolder={onOpenFolder}
          onOpenFile={onOpenFile}
        />
      ))
    }

    if (rootQuery.isLoading) {
      return (
        <p className="px-2 py-1 text-xs text-muted-foreground">Caricamento…</p>
      )
    }
    if (rootQuery.isError) {
      return (
        <p className="px-2 py-1 text-xs text-destructive">
          {(rootQuery.error as Error).message}
        </p>
      )
    }
    if (rootItems.length === 0) {
      return (
        <p className="px-2 py-1 text-xs text-muted-foreground">
          Nessun documento.
        </p>
      )
    }

    return rootItems.map((item) => (
      <DocNavRow
        key={item.relativePath}
        projectId={projectId}
        item={item}
        depth={0}
        expanded={expanded}
        currentPath={currentPath}
        contextMenuEnabled={projectId === actionsProjectId}
        onToggle={toggle}
        onOpenFolder={onOpenFolder}
        onOpenFile={onOpenFile}
      />
    ))
  }

  return (
    <div className="flex flex-col gap-1">
      <div className="px-1 pb-1">
        <div className="relative">
          <Search className="absolute left-2 top-2 size-3.5 text-muted-foreground" />
          <Input
            value={searchInput}
            placeholder="Cerca cartella o file…"
            className="h-8 pl-7 pr-7 text-xs"
            onChange={(event) => setSearchInput(event.target.value)}
          />
          {searchInput ? (
            <button
              type="button"
              aria-label="Cancella ricerca"
              className="absolute right-1.5 top-1.5 flex size-5 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground"
              onClick={() => setSearchInput("")}
            >
              <X className="size-3.5" />
            </button>
          ) : null}
        </div>
      </div>
      {renderTreeBody()}
    </div>
  )
}

/** Riga albero in modalità ricerca (albero filtrato, espansione manuale). */
function FilteredDocNavRow({
  item,
  depth,
  expanded,
  currentPath,
  searchTerm,
  contextMenuEnabled,
  onToggle,
  onOpenFolder,
  onOpenFile,
}: {
  item: FileTreeItem
  depth: number
  expanded: Set<string>
  currentPath: string
  searchTerm: string
  contextMenuEnabled: boolean
  onToggle: (path: string) => void
  onOpenFolder: (path: string) => void
  onOpenFile: (parentPath: string, fileRelativePath: string) => void
}) {
  const path = normalizeDocPath(item.relativePath)
  const isOpen = item.isFolder && expanded.has(path)
  const isActive = currentPath === path
  const indent = depth * 12 + 4
  const childIndent = (depth + 1) * 12 + 4 + 18
  const children = sortTreeItems(item.children)
  const nameMatches = item.name
    .toLowerCase()
    .includes(searchTerm.toLowerCase())

  const row = (
    <div
      className={cn(
        "flex items-center rounded-md hover:bg-accent",
        isActive && "bg-primary/10"
      )}
      style={{ paddingLeft: indent }}
    >
      {item.isFolder ? (
        <button
          type="button"
          aria-label={isOpen ? "Comprimi" : "Espandi"}
          className="flex size-6 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
          onClick={(event) => {
            event.stopPropagation()
            onToggle(path)
          }}
        >
          <ChevronRight
            className={cn("size-3.5 transition-transform", isOpen && "rotate-90")}
          />
        </button>
      ) : (
        <span className="w-6 shrink-0" />
      )}
      <button
        type="button"
        className={cn(
          "flex min-w-0 flex-1 items-center gap-1.5 py-1 pr-2 text-left text-[13px]",
          isActive && "font-medium text-primary",
          nameMatches && "font-medium"
        )}
        title={item.name}
        onClick={() => {
          if (item.isFolder) {
            if (!isOpen) {
              onToggle(path)
            }
            onOpenFolder(path)
          } else {
            onOpenFile(parentOf(path), path)
          }
        }}
      >
        {item.isFolder ? (
          <Folder className="size-3.5 shrink-0 text-muted-foreground" />
        ) : (
          <FileIcon className="size-3.5 shrink-0 text-muted-foreground" />
        )}
        <span className="truncate">{item.name}</span>
      </button>
    </div>
  )

  return (
    <>
      {item.isFolder && contextMenuEnabled ? (
        <FolderDocContextMenu
          item={toFolderFileItem(path, item.name)}
          onOpen={() => {
            if (!isOpen) {
              onToggle(path)
            }
            onOpenFolder(path)
          }}
        >
          {row}
        </FolderDocContextMenu>
      ) : (
        row
      )}

      {item.isFolder && isOpen ? (
        children.length === 0 ? (
          <p
            className="py-1 text-xs text-muted-foreground"
            style={{ paddingLeft: childIndent }}
          >
            Vuota.
          </p>
        ) : (
          children.map((child) => (
            <FilteredDocNavRow
              key={normalizeDocPath(child.relativePath)}
              item={child}
              depth={depth + 1}
              expanded={expanded}
              currentPath={currentPath}
              searchTerm={searchTerm}
              contextMenuEnabled={contextMenuEnabled}
              onToggle={onToggle}
              onOpenFolder={onOpenFolder}
              onOpenFile={onOpenFile}
            />
          ))
        )
      ) : null}
    </>
  )
}

function DocNavRow({
  projectId,
  item,
  depth,
  expanded,
  currentPath,
  contextMenuEnabled,
  onToggle,
  onOpenFolder,
  onOpenFile,
}: {
  projectId: number
  item: FileItem
  depth: number
  expanded: Set<string>
  currentPath: string
  contextMenuEnabled: boolean
  onToggle: (path: string) => void
  onOpenFolder: (path: string) => void
  onOpenFile: (parentPath: string, fileRelativePath: string) => void
}) {
  const isOpen = item.isFolder && expanded.has(item.relativePath)
  const isActive = currentPath === item.relativePath
  const indent = depth * 12 + 4
  const childIndent = (depth + 1) * 12 + 4 + 18

  const childrenQuery = useQuery({
    queryKey: ["project-folder", projectId, item.relativePath],
    queryFn: () => fetchProjectFiles(projectId, item.relativePath),
    enabled: item.isFolder && isOpen,
  })
  const children = sortItems(childrenQuery.data ?? [])

  const row = (
    <div
      className={cn(
        "flex items-center rounded-md hover:bg-accent",
        isActive && "bg-primary/10"
      )}
      style={{ paddingLeft: indent }}
    >
      {item.isFolder ? (
        <button
          type="button"
          aria-label={isOpen ? "Comprimi" : "Espandi"}
          className="flex size-6 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
          onClick={(event) => {
            event.stopPropagation()
            onToggle(item.relativePath)
          }}
        >
          <ChevronRight
            className={cn("size-3.5 transition-transform", isOpen && "rotate-90")}
          />
        </button>
      ) : (
        <span className="w-6 shrink-0" />
      )}
      <button
        type="button"
        className={cn(
          "flex min-w-0 flex-1 items-center gap-1.5 py-1 pr-2 text-left text-[13px]",
          isActive && "font-medium text-primary"
        )}
        title={item.name}
        onClick={() => {
          if (item.isFolder) {
            if (!isOpen) {
              onToggle(item.relativePath)
            }
            onOpenFolder(item.relativePath)
          } else {
            onOpenFile(parentOf(item.relativePath), item.relativePath)
          }
        }}
      >
        {item.isFolder ? (
          <Folder className="size-3.5 shrink-0 text-muted-foreground" />
        ) : (
          <FileIcon className="size-3.5 shrink-0 text-muted-foreground" />
        )}
        <span className="truncate">{item.name}</span>
      </button>
    </div>
  )

  return (
    <>
      {item.isFolder && contextMenuEnabled ? (
        <FolderDocContextMenu
          item={item}
          onOpen={() => {
            if (!isOpen) {
              onToggle(item.relativePath)
            }
            onOpenFolder(item.relativePath)
          }}
        >
          {row}
        </FolderDocContextMenu>
      ) : (
        row
      )}

      {isOpen ? (
        childrenQuery.isLoading ? (
          <p
            className="py-1 text-xs text-muted-foreground"
            style={{ paddingLeft: childIndent }}
          >
            Caricamento…
          </p>
        ) : childrenQuery.isError ? (
          <p
            className="py-1 text-xs text-destructive"
            style={{ paddingLeft: childIndent }}
          >
            {(childrenQuery.error as Error).message}
          </p>
        ) : children.length === 0 ? (
          <p
            className="py-1 text-xs text-muted-foreground"
            style={{ paddingLeft: childIndent }}
          >
            Vuota.
          </p>
        ) : (
          children.map((child) => (
            <DocNavRow
              key={child.relativePath}
              projectId={projectId}
              item={child}
              depth={depth + 1}
              expanded={expanded}
              currentPath={currentPath}
              contextMenuEnabled={contextMenuEnabled}
              onToggle={onToggle}
              onOpenFolder={onOpenFolder}
              onOpenFile={onOpenFile}
            />
          ))
        )
      ) : null}
    </>
  )
}
