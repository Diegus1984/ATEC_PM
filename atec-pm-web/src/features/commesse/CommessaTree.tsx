import * as React from "react"
import { useInfiniteQuery } from "@tanstack/react-query"
import { ChevronRight, Plus, RefreshCw, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { fetchProjects } from "@/lib/api/projects"
import { isSystemProjectCode } from "@/lib/system-projects"
import type { ProjectListItem } from "@/lib/api/types"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import { CommessaDocumentsTree } from "./CommessaDocumentsTree"
import { FolderDocContextMenu, toFolderFileItem } from "./FolderDocContextMenu"
import { COMMESSA_SECTIONS } from "./commessa-sections"
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"

const PAGE_SIZE = 50

interface CommessaTreeProps {
  selectedProjectId: number | null
  selectedSection: string | null
  /** Cartella documenti aperta (query `?path=`), per evidenziare/espandere l'albero. */
  selectedDocumentPath: string
  canSeeEconomics: boolean
  onSelect: (project: ProjectListItem, section: string) => void
  onSelectRoot: (project: ProjectListItem) => void
  /** Apre la sezione Documenti posizionata su una cartella (vuoto = root). */
  onOpenDocumentFolder: (project: ProjectListItem, path: string) => void
  /** Apre la sezione Documenti su una cartella e mostra l'anteprima di un file. */
  onOpenDocumentFile: (
    project: ProjectListItem,
    parentPath: string,
    fileRelativePath: string
  ) => void
  onNewProject: () => void
  isCollapsed?: boolean
}

/**
 * Pannello albero a sinistra: lista piatta di commesse (ricerca server-side
 * con debounce 350ms, paginazione 50 + infinite scroll). Ogni commessa è un
 * nodo radice "{code} - {customerName}" con le 8 sotto-sezioni fisse.
 * Fedele a `ATEC.PM.Client/Views/Commesse/ProjectsPage`.
 */
export function CommessaTree({
  selectedProjectId,
  selectedSection,
  selectedDocumentPath,
  canSeeEconomics,
  onSelect,
  onSelectRoot,
  onOpenDocumentFolder,
  onOpenDocumentFile,
  onNewProject,
  isCollapsed = false,
}: CommessaTreeProps) {
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 350)
  const [expanded, setExpanded] = React.useState<Set<number>>(new Set())
  // Nodi "Documenti" espansi (per commessa): mostrano l'albero cartelle inline.
  const [docOpen, setDocOpen] = React.useState<Set<number>>(new Set())

  const query = useInfiniteQuery({
    queryKey: ["projects-tree", searchTerm],
    queryFn: ({ pageParam }) =>
      fetchProjects({ page: pageParam, pageSize: PAGE_SIZE, search: searchTerm }),
    initialPageParam: 1,
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? lastPage.page + 1 : undefined,
  })

  // Esclude le commesse di sistema (es. INTERNA): non sono operative in Commesse.
  const projects = React.useMemo(
    () =>
      (query.data?.pages.flatMap((page) => page.items) ?? []).filter(
        (p) => !isSystemProjectCode(p.code)
      ),
    [query.data]
  )
  const totalCount = query.data?.pages[0]?.totalCount ?? 0

  // Auto-espandi la commessa selezionata (utile su deep-link /commesse/:id/:sezione).
  React.useEffect(() => {
    if (selectedProjectId == null) {
      return
    }
    setExpanded((prev) => {
      if (prev.has(selectedProjectId)) {
        return prev
      }
      const next = new Set(prev)
      next.add(selectedProjectId)
      return next
    })
  }, [selectedProjectId])

  // Quando si è nella sezione Documenti, apri il relativo nodo dell'albero.
  React.useEffect(() => {
    if (selectedProjectId == null || selectedSection !== "documents") {
      return
    }
    setDocOpen((prev) => {
      if (prev.has(selectedProjectId)) {
        return prev
      }
      const next = new Set(prev)
      next.add(selectedProjectId)
      return next
    })
  }, [selectedProjectId, selectedSection])

  // Infinite scroll: sentinella in fondo alla lista.
  const sentinelRef = React.useRef<HTMLDivElement | null>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query
  React.useEffect(() => {
    const el = sentinelRef.current
    if (!el) {
      return
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting && hasNextPage && !isFetchingNextPage) {
        void fetchNextPage()
      }
    })
    observer.observe(el)
    return () => observer.disconnect()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  function toggle(projectId: number) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(projectId)) {
        next.delete(projectId)
      } else {
        next.add(projectId)
      }
      return next
    })
  }

  function open(projectId: number) {
    setExpanded((prev) => {
      if (prev.has(projectId)) {
        return prev
      }
      const next = new Set(prev)
      next.add(projectId)
      return next
    })
  }

  function toggleDoc(projectId: number) {
    setDocOpen((prev) => {
      const next = new Set(prev)
      if (next.has(projectId)) {
        next.delete(projectId)
      } else {
        next.add(projectId)
      }
      return next
    })
  }

  const sections = canSeeEconomics
    ? COMMESSA_SECTIONS
    : COMMESSA_SECTIONS.filter((section) => !section.economicsOnly)

  const isRefreshing = query.isFetching && !query.isFetchingNextPage

  const statusText =
    totalCount <= 0
      ? query.isLoading
        ? "Caricamento…"
        : "Nessuna commessa"
      : projects.length >= totalCount
        ? `${totalCount} commesse`
        : `${projects.length} di ${totalCount} commesse`

  if (isCollapsed) {
    const activeProject = projects.find((p) => p.id === selectedProjectId)
    const projectSuffix = activeProject ? activeProject.code.slice(-3) : ""

    return (
      <TooltipProvider delayDuration={100}>
        <div className="flex h-full flex-col items-center gap-2 py-2 bg-card">
          {/* Active project code suffix */}
          {activeProject ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="outline"
                  size="icon"
                  className="size-9 rounded-lg font-mono text-[10px] font-bold bg-muted/60"
                  onClick={() => onSelectRoot(activeProject)}
                >
                  {projectSuffix}
                </Button>
              </TooltipTrigger>
              <TooltipContent side="right">
                {activeProject.code} — {activeProject.customerName || "Senza cliente"}
              </TooltipContent>
            </Tooltip>
          ) : (
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-9 rounded-lg text-muted-foreground"
                  disabled
                >
                  💼
                </Button>
              </TooltipTrigger>
              <TooltipContent side="right">Seleziona una commessa</TooltipContent>
            </Tooltip>
          )}

          <div className="h-px w-8 bg-border my-1" />

          {/* Section icons or project selection */}
          <div className="flex-1 w-full overflow-y-auto px-1 flex flex-col items-center gap-1.5 min-h-0">
            {activeProject ? (
              sections.map((sec) => {
                const isSelected = selectedSection === sec.key
                return (
                  <Tooltip key={sec.key}>
                    <TooltipTrigger asChild>
                      <Button
                        variant={isSelected ? "default" : "ghost"}
                        size="icon"
                        className="size-9 rounded-lg shrink-0"
                        onClick={() => onSelect(activeProject, sec.key)}
                      >
                        <span className="text-base leading-none">{sec.icon}</span>
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent side="right">{sec.label}</TooltipContent>
                  </Tooltip>
                )
              })
            ) : (
              projects.map((project) => {
                const isSelected = selectedProjectId === project.id
                const suffix = project.code.slice(-3)
                return (
                  <Tooltip key={project.id}>
                    <TooltipTrigger asChild>
                      <Button
                        variant={isSelected ? "default" : "outline"}
                        size="icon"
                        className="size-9 rounded-lg font-mono text-[10px] font-bold shrink-0"
                        onClick={() => onSelectRoot(project)}
                      >
                        {suffix}
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent side="right">
                      {project.code} — {project.customerName || "Senza cliente"}
                    </TooltipContent>
                  </Tooltip>
                )
              })
            )}
          </div>
        </div>
      </TooltipProvider>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Toolbar */}
      <div className="flex items-center gap-2 border-b p-2">
        <Button size="sm" className="flex-1" onClick={onNewProject}>
          <Plus />
          Nuova
        </Button>
        <Button
          size="sm"
          variant="outline"
          onClick={() => query.refetch()}
          disabled={isRefreshing}
        >
          <RefreshCw className={isRefreshing ? "animate-spin" : undefined} />
          Aggiorna
        </Button>
      </div>

      {/* Ricerca */}
      <div className="border-b p-2">
        <div className="relative">
          <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
          <Input
            value={searchInput}
            placeholder="Cerca commessa o cliente…"
            className="pl-8"
            onChange={(event) => setSearchInput(event.target.value)}
          />
        </div>
      </div>

      {/* Albero */}
      <div className="min-h-0 flex-1 overflow-y-auto p-1">
        {query.isError ? (
          <p className="p-3 text-sm text-destructive">
            {(query.error as Error).message || "Errore di caricamento."}
          </p>
        ) : projects.length === 0 && !query.isLoading ? (
          <p className="p-3 text-sm text-muted-foreground">
            {searchTerm ? "Nessuna corrispondenza." : "Nessuna commessa."}
          </p>
        ) : (
          projects.map((project) => {
            const isOpen = expanded.has(project.id)
            const isRootSelected =
              selectedProjectId === project.id && selectedSection == null
            return (
              <div key={project.id}>
                <div
                  className={cn(
                    "flex items-start rounded-md hover:bg-accent",
                    isRootSelected && "bg-accent"
                  )}
                >
                  <button
                    type="button"
                    aria-label={isOpen ? "Comprimi" : "Espandi"}
                    className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
                    onClick={(event) => {
                      event.stopPropagation()
                      toggle(project.id)
                    }}
                  >
                    <ChevronRight
                      className={cn(
                        "size-4 transition-transform",
                        isOpen && "rotate-90"
                      )}
                    />
                  </button>
                  <button
                    type="button"
                    className="min-w-0 flex-1 py-1.5 pr-2 text-left"
                    title={`${project.code} — ${project.customerName || "—"}`}
                    onClick={() => {
                      open(project.id)
                      onSelectRoot(project)
                    }}
                  >
                    <div className="truncate text-base font-bold leading-tight">
                      {project.code}
                    </div>
                    <div className="truncate pl-4 text-sm font-medium text-muted-foreground leading-tight">
                      {project.customerName || "—"}
                    </div>
                  </button>
                </div>

                {isOpen ? (
                  <div className="ml-3.5 border-l pl-2">
                    {sections.map((section) => {
                      const active =
                        selectedProjectId === project.id &&
                        selectedSection === section.key

                      // Documenti: nodo espandibile che mostra l'albero cartelle
                      // inline (esplorazione); la card a destra mostra contenuto+azioni.
                      if (section.key === "documents") {
                        const docExpanded = docOpen.has(project.id)
                        const docActive = active && !selectedDocumentPath
                        const documentsSectionRow = (
                          <div
                            className={cn(
                              "flex items-center rounded-md hover:bg-accent",
                              docActive && "bg-primary/10"
                            )}
                          >
                            <button
                              type="button"
                              aria-label={docExpanded ? "Comprimi" : "Espandi"}
                              className="flex size-6 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
                              onClick={(event) => {
                                event.stopPropagation()
                                toggleDoc(project.id)
                              }}
                            >
                              <ChevronRight
                                className={cn(
                                  "size-3.5 transition-transform",
                                  docExpanded && "rotate-90"
                                )}
                              />
                            </button>
                            <button
                              type="button"
                              className={cn(
                                "flex min-w-0 flex-1 items-center gap-1.5 py-1 pr-2 text-left text-[13px]",
                                docActive && "font-medium text-primary"
                              )}
                              onClick={() => {
                                setDocOpen((prev) =>
                                  new Set(prev).add(project.id)
                                )
                                onOpenDocumentFolder(project, "")
                              }}
                            >
                              {section.icon ? (
                                <span className="shrink-0">{section.icon}</span>
                              ) : null}
                              <span className="truncate">{section.label}</span>
                            </button>
                          </div>
                        )
                        return (
                          <div key={section.key}>
                            {project.id === selectedProjectId ? (
                              <FolderDocContextMenu
                                item={toFolderFileItem("", "Root")}
                                onOpen={() => {
                                  setDocOpen((prev) =>
                                    new Set(prev).add(project.id)
                                  )
                                  onOpenDocumentFolder(project, "")
                                }}
                              >
                                {documentsSectionRow}
                              </FolderDocContextMenu>
                            ) : (
                              documentsSectionRow
                            )}
                            {docExpanded ? (
                              <div className="ml-3 border-l pl-1">
                                <CommessaDocumentsTree
                                  projectId={project.id}
                                  actionsProjectId={selectedProjectId}
                                  currentPath={
                                    active ? selectedDocumentPath : ""
                                  }
                                  onOpenFolder={(path) =>
                                    onOpenDocumentFolder(project, path)
                                  }
                                  onOpenFile={(parent, file) =>
                                    onOpenDocumentFile(project, parent, file)
                                  }
                                />
                              </div>
                            ) : null}
                          </div>
                        )
                      }

                      return (
                        <button
                          key={section.key}
                          type="button"
                          className={cn(
                            "flex w-full items-center gap-1.5 rounded-md px-2 py-1 text-left text-[13px] hover:bg-accent",
                            active && "bg-primary/10 font-medium text-primary"
                          )}
                          onClick={() => onSelect(project, section.key)}
                        >
                          {section.icon ? (
                            <span className="shrink-0">{section.icon}</span>
                          ) : null}
                          <span className="truncate">{section.label}</span>
                        </button>
                      )
                    })}
                  </div>
                ) : null}
              </div>
            )
          })
        )}
        <div ref={sentinelRef} />
        {query.isFetchingNextPage ? (
          <p className="p-2 text-center text-xs text-muted-foreground">
            Caricamento…
          </p>
        ) : null}
      </div>

      {/* Stato */}
      <div className="border-t px-3 py-1.5 text-[11px] text-muted-foreground">
        {statusText}
      </div>
    </div>
  )
}
