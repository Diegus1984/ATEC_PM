import * as React from "react"
import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { ChevronRight, Plus, RefreshCw, Search } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { fetchProject, fetchProjects, updateProject } from "@/lib/api/projects"
import { isCommessaCode } from "@/lib/project-code"
import { isSystemProjectCode } from "@/lib/system-projects"
import type { ProjectListItem } from "@/lib/api/types"
import {
  canAccessFeature,
  canWriteFeature,
} from "@/lib/auth/permissions"
import { notifyError, notifySuccess } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import { CommessaDocumentsTree } from "./documenti/CommessaDocumentsTree"
import {
  CommessaProjectContextMenu,
  CommessaSectionContextMenu,
  CommessaTreeBackgroundMenu,
} from "./CommessaTreeContextMenu"
import { FolderDocContextMenu, toFolderFileItem } from "./documenti/FolderDocContextMenu"
import { sezioniVisibili } from "./commessa-sections"
import {
  PROJECT_STATUS_META,
  projectStatusMeta,
  type ProjectStatusMeta,
} from "./project-status"
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"

const PAGE_SIZE = 50

/** Preferenza "mostra anche le commesse chiuse" (per utente/browser). */
const SHOW_CLOSED_KEY = "commesse:showClosed"

function loadShowClosed(): boolean {
  try {
    return localStorage.getItem(SHOW_CLOSED_KEY) === "1"
  } catch {
    // storage non disponibile: si riparte dal default (solo aperte)
    return false
  }
}

/**
 * Stato mostrato accanto al codice. Le commesse attive hanno la **sola icona**
 * (sono il caso normale: l'etichetta su ogni riga riempirebbe l'albero di rumore),
 * tutte le altre icona + etichetta.
 */
function statusBadgeMeta(status: string): ProjectStatusMeta | null {
  return PROJECT_STATUS_META.find((m) => m.value === status) ?? null
}

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
  onEditProject: (project: ProjectListItem) => void
  onDeleteProject: (project: ProjectListItem) => void
  isCollapsed?: boolean
}

/**
 * Pannello albero a sinistra: commesse in tre gruppi collassabili — «Commesse
 * Attive», «Altre Attività», «Commesse Chiuse» (#95) — con ricerca server-side
 * (debounce 350ms) e paginazione 50 + infinite scroll. Ogni commessa è un
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
  onEditProject,
  onDeleteProject,
  isCollapsed = false,
}: CommessaTreeProps) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const canEditProject = canWriteFeature("action.edit_project")
  const canDeleteProject = canWriteFeature("action.delete_project")
  const [searchInput, setSearchInput] = React.useState("")
  const searchTerm = useDebounced(searchInput.trim(), 350)
  const [expanded, setExpanded] = React.useState<Set<number>>(new Set())
  // Nodi "Documenti" espansi (per commessa): mostrano l'albero cartelle inline.
  const [docOpen, setDocOpen] = React.useState<Set<number>>(new Set())
  // Filtro stato: di default l'albero mostra solo le commesse aperte (Bozza/Attiva/
  // In pausa). La scelta dell'utente è persistita; `autoShowClosed` è l'accensione
  // temporanea quando si arriva su una commessa chiusa (vedi effetto sotto).
  const [showClosed, setShowClosed] = React.useState(loadShowClosed)
  const [autoShowClosed, setAutoShowClosed] = React.useState(false)
  const includeClosed = showClosed || autoShowClosed

  // Commessa già selezionata al montaggio (deep-link /commesse/:id/:sezione): va
  // inclusa anche se chiusa, altrimenti l'albero non ha il nodo di ciò che si vede
  // a destra. Congelata in una ref per non cambiare la queryKey a ogni selezione.
  const initialProjectIdRef = React.useRef(selectedProjectId ?? 0)
  const initialProjectId = initialProjectIdRef.current

  const query = useInfiniteQuery({
    queryKey: ["projects-tree", searchTerm, includeClosed, initialProjectId],
    queryFn: ({ pageParam }) =>
      fetchProjects({
        page: pageParam,
        pageSize: PAGE_SIZE,
        search: searchTerm,
        includeClosed,
        includeId: initialProjectId,
      }),
    initialPageParam: 1,
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? lastPage.page + 1 : undefined,
  })

  const loadedProjects = React.useMemo(
    () => query.data?.pages.flatMap((page) => page.items) ?? [],
    [query.data]
  )
  // Esclude le commesse di sistema (es. INTERNA): non sono operative in Commesse.
  // Le Annullate restano fuori come in Dashboard (#89), tranne quella del deep-link
  // iniziale: senza il suo nodo l'albero non rispecchierebbe ciò che si vede a destra.
  const projects = React.useMemo(
    () =>
      loadedProjects.filter(
        (p) =>
          !isSystemProjectCode(p.code) &&
          // Annullate fuori (soft delete), TRANNE quella del deep-link e quella
          // ancora selezionata (es. appena annullata dal menu): finché è aperta a
          // destra deve avere il suo nodo sotto «Commesse Chiuse».
          (p.status !== "CANCELLED" ||
            p.id === initialProjectId ||
            p.id === selectedProjectId)
      ),
    [loadedProjects, initialProjectId, selectedProjectId]
  )
  // Segnalazione #95: tre gruppi decisi RIGA PER RIGA (stato + tipo di codice).
  // Il vecchio confine d'indice si fidava dell'ordinamento del server, ma con
  // `includeClosed` le chiuse arrivano DOPO le altre attività aperte e finivano
  // sotto il titolo sbagliato. Le chiuse (Completate, più l'eventuale Annullata
  // da deep-link) stanno insieme, commesse e attività: le distingue il badge.
  const groups = React.useMemo(() => {
    const attive: ProjectListItem[] = []
    const altre: ProjectListItem[] = []
    const chiuse: ProjectListItem[] = []
    for (const p of projects) {
      if (p.status === "COMPLETED" || p.status === "CANCELLED") chiuse.push(p)
      else if (isCommessaCode(p.code)) attive.push(p)
      else altre.push(p)
    }
    return { attive, altre, chiuse }
  }, [projects])
  // Etichette LOCALI all'albero: le costanti condivise di `project-code.ts`
  // restano a due sezioni per le sidebar PM, qui serve la terza voce. La chiave
  // di collasso riusa 'commesse'/'altre' per non perdere le preferenze salvate.
  const treeGroups = [
    { key: "commesse", label: "Commesse Attive", items: groups.attive },
    { key: "altre", label: "Altre Attività", items: groups.altre },
    { key: "chiuse", label: "Commesse Chiuse", items: groups.chiuse },
  ]
  // Rail collassata e conteggi lavorano sulla lista piatta nell'ordine dei
  // gruppi: attive → altre → chiuse.
  const orderedProjects = React.useMemo(
    () => [...groups.attive, ...groups.altre, ...groups.chiuse],
    [groups]
  )
  // Con un solo gruppo popolato i titoli sarebbero solo rumore (com'era prima).
  const showGroupHeaders =
    treeGroups.filter((group) => group.items.length > 0).length >= 2
  const [collapsedGroups, setCollapsedGroups] = React.useState<Set<string>>(
    () => {
      try {
        const raw = localStorage.getItem("commesse-tree:groups")
        if (!raw) return new Set()
        const parsed = JSON.parse(raw) as unknown
        return Array.isArray(parsed)
          ? new Set(parsed.filter((k): k is string => typeof k === "string"))
          : new Set()
      } catch {
        return new Set()
      }
    }
  )
  const toggleGroup = React.useCallback((key: string) => {
    setCollapsedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      try {
        localStorage.setItem("commesse-tree:groups", JSON.stringify([...next]))
      } catch {
        /* ignore */
      }
      return next
    })
  }, [])
  const totalCount = query.data?.pages[0]?.totalCount ?? 0

  // Se la commessa selezionata non è tra quelle caricate (tipico: link a una commessa
  // chiusa arrivato da un'altra pagina, quando `includeId` non era ancora noto),
  // accende il filtro per non lasciare l'albero senza il nodo corrispondente.
  const { hasNextPage: morePages, isFetching } = query
  React.useEffect(() => {
    if (includeClosed || selectedProjectId == null || searchTerm) {
      return
    }
    if (isFetching || morePages) {
      return
    }
    if (loadedProjects.some((p) => p.id === selectedProjectId)) {
      return
    }
    setAutoShowClosed(true)
  }, [includeClosed, selectedProjectId, searchTerm, isFetching, morePages, loadedProjects])

  function toggleShowClosed(next: boolean) {
    setShowClosed(next)
    setAutoShowClosed(false)
    try {
      localStorage.setItem(SHOW_CLOSED_KEY, next ? "1" : "0")
    } catch {
      // storage non disponibile: la scelta vale solo per questa sessione
    }
  }

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

  function expandAll() {
    setExpanded(new Set(projects.map((project) => project.id)))
  }

  function collapseAll() {
    setExpanded(new Set())
    setDocOpen(new Set())
  }

  function openSection(project: ProjectListItem, sectionKey: string) {
    const key = sections.some((section) => section.key === sectionKey)
      ? sectionKey
      : sections[0]?.key
    if (!key) {
      return
    }
    open(project.id)
    if (key === "documents") {
      setDocOpen((prev) => new Set(prev).add(project.id))
      onOpenDocumentFolder(project, "")
      return
    }
    onSelect(project, key)
  }

  const statusMutation = useMutation({
    mutationFn: async ({
      project,
      status,
    }: {
      project: ProjectListItem
      status: string
    }) => {
      const current = await fetchProject(project.id)
      await updateProject(project.id, { ...current, status })
      return { id: project.id, code: project.code, status }
    },
    onSuccess: async ({ code, status, id }) => {
      // Chiusa/annullata la commessa SELEZIONATA a switch spento: il nodo uscirebbe
      // dal filtro e — con pagine ancora da caricare — il fallback in fondo non
      // scatterebbe. Qui si sa già cosa è successo: si accende subito la vista chiuse.
      if (
        (status === "COMPLETED" || status === "CANCELLED") &&
        id === selectedProjectId &&
        !showClosed
      ) {
        setAutoShowClosed(true)
      }
      await queryClient.invalidateQueries({ queryKey: ["projects-tree"] })
      await queryClient.invalidateQueries({ queryKey: ["project-dashboard", id] })
      await queryClient.invalidateQueries({ queryKey: ["project-header", id] })
      notifySuccess(`${code} → ${projectStatusMeta(status).label}`)
    },
    onError: (error) => {
      notifyError(error, "Impossibile cambiare lo stato.")
    },
  })

  async function changeStatus(project: ProjectListItem, status: string) {
    const next = projectStatusMeta(status)
    if (status === "COMPLETED" || status === "CANCELLED") {
      const ok = await confirm({
        title: `Segnare come ${next.label.toLowerCase()}?`,
        description:
          status === "COMPLETED"
            ? `«${project.code}» verrà archiviata tra le commesse chiuse.`
            : `«${project.code}» verrà annullata e sparirà dall'elenco delle aperte.`,
        confirmLabel: next.label,
        destructive: status === "CANCELLED",
      })
      if (!ok) {
        return
      }
    }
    statusMutation.mutate({ project, status })
  }

  const sections = sezioniVisibili(canSeeEconomics, canAccessFeature)

  const isRefreshing = query.isFetching && !query.isFetchingNextPage

  // Conteggio a piè pagina: annullate e commesse di sistema sono filtrate qui
  // nel client, quindi il confronto col totale del server non torna più; a
  // caricamento completo si conta ciò che si vede, con pagine ancora da
  // caricare si mostra «viste di totale» (il totale server è approssimato).
  const noun = includeClosed ? "commesse" : "commesse aperte"
  const statusText =
    totalCount <= 0
      ? query.isLoading
        ? "Caricamento…"
        : includeClosed
          ? "Nessuna commessa"
          : "Nessuna commessa aperta"
      : query.hasNextPage
        ? `${projects.length} di ${totalCount} ${noun}`
        : `${projects.length} ${noun}`

  if (isCollapsed) {
    const activeProject = projects.find((p) => p.id === selectedProjectId)
    const projectSuffix = activeProject ? activeProject.code.slice(-3) : ""

    return (
      <TooltipProvider delayDuration={100}>
        <div className="flex h-full flex-col items-center gap-2 py-2 bg-card">
          {/* Active project code suffix */}
          {activeProject ? (
            <CommessaProjectContextMenu
              project={activeProject}
              isExpanded
              sections={sections}
              canEdit={canEditProject}
              canDelete={canDeleteProject}
              onOpenDashboard={() => openSection(activeProject, "details")}
              onOpenSection={(key) => openSection(activeProject, key)}
              onToggleExpand={() => toggle(activeProject.id)}
              onEdit={() => onEditProject(activeProject)}
              onChangeStatus={(status) => {
                void changeStatus(activeProject, status)
              }}
              onDelete={() => onDeleteProject(activeProject)}
            >
              <span className="inline-flex">
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
                    {statusBadgeMeta(activeProject.status)
                      ? ` · ${statusBadgeMeta(activeProject.status)?.label}`
                      : ""}
                  </TooltipContent>
                </Tooltip>
              </span>
            </CommessaProjectContextMenu>
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
                  <CommessaSectionContextMenu
                    key={sec.key}
                    projectId={activeProject.id}
                    section={sec}
                    onOpen={() => openSection(activeProject, sec.key)}
                  >
                    <span className="inline-flex">
                      <Tooltip>
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
                    </span>
                  </CommessaSectionContextMenu>
                )
              })
            ) : (
              // Lista piatta (niente gruppi), ma nello stesso ordine dell'albero.
              orderedProjects.map((project) => {
                const isSelected = selectedProjectId === project.id
                const suffix = project.code.slice(-3)
                return (
                  <CommessaProjectContextMenu
                    key={project.id}
                    project={project}
                    isExpanded={false}
                    sections={sections}
                    canEdit={canEditProject}
                    canDelete={canDeleteProject}
                    onOpenDashboard={() => openSection(project, "details")}
                    onOpenSection={(key) => openSection(project, key)}
                    onToggleExpand={() => toggle(project.id)}
                    onEdit={() => onEditProject(project)}
                    onChangeStatus={(status) => {
                      void changeStatus(project, status)
                    }}
                    onDelete={() => onDeleteProject(project)}
                  >
                    <span className="inline-flex">
                      <Tooltip>
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
                          {statusBadgeMeta(project.status)
                            ? ` · ${statusBadgeMeta(project.status)?.label}`
                            : ""}
                        </TooltipContent>
                      </Tooltip>
                    </span>
                  </CommessaProjectContextMenu>
                )
              })
            )}
          </div>
        </div>
      </TooltipProvider>
    )
  }

  // #95: nodo commessa (riga radice + sotto-sezioni), riusato dalle tre sezioni
  // dell'albero per non triplicare il JSX.
  function renderProjectNode(project: ProjectListItem) {
    const isOpen = expanded.has(project.id)
    const isRootSelected =
      selectedProjectId === project.id && selectedSection == null
    const badge = statusBadgeMeta(project.status)
    return (
      <div key={project.id}>
        <CommessaProjectContextMenu
          project={project}
          isExpanded={isOpen}
          sections={sections}
          canEdit={canEditProject}
          canDelete={canDeleteProject}
          onOpenDashboard={() => openSection(project, "details")}
          onOpenSection={(key) => openSection(project, key)}
          onToggleExpand={() => toggle(project.id)}
          onEdit={() => onEditProject(project)}
          onChangeStatus={(status) => {
            void changeStatus(project, status)
          }}
          onDelete={() => onDeleteProject(project)}
        >
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
              title={[
                `${project.code} — ${project.customerName || "—"}`,
                project.title?.trim(),
              ]
                .filter(Boolean)
                .join("\n")}
              onClick={() => {
                open(project.id)
                onSelectRoot(project)
              }}
            >
              <div className="flex items-center gap-1.5">
                <span className="truncate text-base font-bold leading-tight">
                  {project.code}
                </span>
                {badge ? (
                  <Badge
                    variant="outline"
                    title={badge.label}
                    className={cn(
                      "h-4 shrink-0 gap-1 px-1 text-[10px] font-medium",
                      badge.className,
                      badge.borderClassName
                    )}
                  >
                    <badge.icon className="size-3" />
                    {badge.value === "ACTIVE" ? null : badge.label}
                  </Badge>
                ) : null}
              </div>
              <div className="truncate pl-4 text-sm font-medium text-muted-foreground leading-tight">
                {project.customerName || "—"}
              </div>
              {/* Titolo commessa: terza riga del nodo. In corsivo per non confonderlo
                  con il cliente, che sta sulla riga sopra con lo stesso colore. */}
              {project.title?.trim() ? (
                <div className="truncate pl-4 text-xs italic text-muted-foreground/70 leading-tight">
                  {project.title.trim()}
                </div>
              ) : null}
            </button>
          </div>
        </CommessaProjectContextMenu>

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
                      <CommessaSectionContextMenu
                        projectId={project.id}
                        section={section}
                        onOpen={() => openSection(project, "documents")}
                      >
                        {documentsSectionRow}
                      </CommessaSectionContextMenu>
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
                <CommessaSectionContextMenu
                  key={section.key}
                  projectId={project.id}
                  section={section}
                  onOpen={() => openSection(project, section.key)}
                >
                  <button
                    type="button"
                    className={cn(
                      "flex w-full items-center gap-1.5 rounded-md px-2 py-1 text-left text-[13px] hover:bg-accent",
                      active && "bg-primary/10 font-medium text-primary"
                    )}
                    title={section.hint}
                    onClick={() => onSelect(project, section.key)}
                  >
                    {section.icon ? (
                      <span className="shrink-0">{section.icon}</span>
                    ) : null}
                    <span className="truncate">{section.label}</span>
                  </button>
                </CommessaSectionContextMenu>
              )
            })}
          </div>
        ) : null}
      </div>
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

      {/* Filtro stato: di default restano fuori Completate e Annullate */}
      <div className="flex items-center justify-between gap-2 border-b px-3 py-1.5">
        <Label
          htmlFor="commesse-show-closed"
          className="text-xs font-normal text-muted-foreground"
        >
          Mostra anche chiuse
        </Label>
        <Switch
          id="commesse-show-closed"
          checked={includeClosed}
          onCheckedChange={toggleShowClosed}
        />
      </div>

      {/* Albero */}
      <CommessaTreeBackgroundMenu
        onNewProject={onNewProject}
        onRefresh={() => {
          void query.refetch()
        }}
        onExpandAll={expandAll}
        onCollapseAll={collapseAll}
      >
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
          // #95: le tre sezioni si rendono solo se hanno righe (le Chiuse
          // compaiono con lo switch acceso o con una chiusa iniettata da
          // `includeId` a switch spento — lo switch resta il cancello del fetch).
          treeGroups.map((group) => {
            if (group.items.length === 0) return null
            const groupCollapsed =
              showGroupHeaders && collapsedGroups.has(group.key)
            return (
              <div key={group.key}>
                {showGroupHeaders ? (
                  <button
                    type="button"
                    onClick={() => toggleGroup(group.key)}
                    className="flex w-full items-center gap-1 px-2 pb-1 pt-2 text-left text-[11px] font-semibold uppercase tracking-wide text-muted-foreground hover:text-foreground"
                    aria-expanded={!groupCollapsed}
                  >
                    <ChevronRight
                      className={cn(
                        "size-3.5 shrink-0 transition-transform",
                        !groupCollapsed && "rotate-90"
                      )}
                    />
                    <span className="min-w-0 flex-1 truncate">
                      {group.label}
                    </span>
                  </button>
                ) : null}
                {groupCollapsed ? null : group.items.map(renderProjectNode)}
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
      </CommessaTreeBackgroundMenu>

      {/* Stato */}
      <div className="border-t px-3 py-1.5 text-[11px] text-muted-foreground">
        {statusText}
      </div>
    </div>
  )
}
