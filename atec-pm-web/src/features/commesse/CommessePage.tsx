import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, PanelLeft, Pencil, Trash2 } from "lucide-react"
import { useNavigate, useParams, useSearchParams, useLocation } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import { hardDeleteProject } from "@/lib/api/projects"
import type { ProjectListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { cn } from "@/lib/utils"

import { CommessaTree } from "./CommessaTree"
import { ProjectDocumentsActionsProvider } from "./project-documents-actions"
import { ProjectBudgetVsActual } from "./ProjectBudgetVsActual"
import { ProjectCashFlow } from "./ProjectCashFlow"
import { ProjectChat } from "./ProjectChat"
import { ProjectDdpOfficina } from "./ProjectDdpOfficina"
import { ProjectChecklist } from "./ProjectChecklist"
import { ProjectMilestones } from "./ProjectMilestones"
import { ProjectMoM } from "./ProjectMoM"
import { ProjectSal } from "./ProjectSal"
import { ProjectDdpCommercial } from "./ProjectDdpCommercial"
import { ProjectDetailsSection } from "./ProjectDetailsSection"
import { ProjectDialog } from "./ProjectDialog"
import { ProjectDocuments } from "./ProjectDocuments"
import { ProjectWorkRequests } from "./ProjectWorkRequests"

const TREE_WIDTH_KEY = "atec_pm_commesse_tree_width"
const TREE_MIN = 180
const TREE_MAX = 500
const TREE_DEFAULT = 280

/** Titoli sezione dell'header destro, fedeli a `ProjectsPage` (WPF). */
const SECTION_TITLES: Record<string, string> = {
  details: "Dashboard Commessa",
  cashflow: "Flusso di Cassa",
  budget_vs_actual: "Preventivo vs Consuntivo",
  chat: "Chat",
  mom: "Verbali (MoM)",
  checklist: "Check list",
  ddp_commercial: "DDP Commerciali",
  ddp_officina: "DDP Officina",
  work_requests: "Lavorazioni",
  documents: "Documenti",
}

function loadTreeWidth(): number {
  const saved = Number(localStorage.getItem(TREE_WIDTH_KEY))
  return Number.isFinite(saved) && saved >= TREE_MIN && saved <= TREE_MAX
    ? saved
    : TREE_DEFAULT
}

/**
 * Gestione Commessa — layout a 3 colonne fedele al WPF
 * `ATEC.PM.Client/Views/Commesse/ProjectsPage`: albero a sinistra (commesse →
 * 8 sezioni), splitter ridimensionabile, area contenuto a destra che inietta
 * la sezione selezionata. Nessuna struttura a Tabs.
 */
export function CommessePage() {
  const navigate = useNavigate()
  const params = useParams()
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const fromGlobal = location.state?.fromGlobal
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const isAdmin = getSession()?.user.userRole === "ADMIN"
  const role = getSession()?.user.userRole
  const canSeeEconomics = role === "ADMIN" || role === "PM"

  const projectId = params.projectId ? Number(params.projectId) : null
  const section = params.section ?? null
  // Cartella documenti corrente (condivisa tra albero a sinistra e card a destra).
  const documentPath = section === "documents" ? searchParams.get("path") ?? "" : ""

  const [selectedProject, setSelectedProject] =
    React.useState<ProjectListItem | null>(null)
  const [dialogProject, setDialogProject] = React.useState<
    number | "new" | null
  >(null)

  // Larghezza pannello albero (persistita), con splitter trascinabile.
  const [treeWidth, setTreeWidth] = React.useState<number>(loadTreeWidth)
  const [isTreeCollapsed, setIsTreeCollapsed] = React.useState(false)
  const treeWidthRef = React.useRef(treeWidth)
  treeWidthRef.current = treeWidth
  const draggingRef = React.useRef(false)
  const rootRef = React.useRef<HTMLDivElement | null>(null)

  React.useEffect(() => {
    function onMove(event: MouseEvent) {
      if (!draggingRef.current || !rootRef.current) {
        return
      }
      const left = rootRef.current.getBoundingClientRect().left
      const width = Math.min(TREE_MAX, Math.max(TREE_MIN, event.clientX - left))
      setTreeWidth(width)
    }
    function onUp() {
      if (!draggingRef.current) {
        return
      }
      draggingRef.current = false
      document.body.style.cursor = ""
      document.body.style.userSelect = ""
      try {
        localStorage.setItem(TREE_WIDTH_KEY, String(treeWidthRef.current))
      } catch {
        // storage non disponibile: nessuna persistenza
      }
    }
    window.addEventListener("mousemove", onMove)
    window.addEventListener("mouseup", onUp)
    return () => {
      window.removeEventListener("mousemove", onMove)
      window.removeEventListener("mouseup", onUp)
    }
  }, [])

  // Altezza disponibile: misurata dal bordo superiore del layout fino in fondo
  // al viewport (l'AppShell mette l'Outlet in un wrapper ad altezza auto).
  const [height, setHeight] = React.useState<number | undefined>(undefined)
  React.useLayoutEffect(() => {
    function measure() {
      if (!rootRef.current) {
        return
      }
      const top = rootRef.current.getBoundingClientRect().top
      setHeight(Math.max(360, window.innerHeight - top - 16))
    }
    measure()
    const timer = window.setTimeout(measure, 400)
    window.addEventListener("resize", measure)
    return () => {
      window.clearTimeout(timer)
      window.removeEventListener("resize", measure)
    }
  }, [])

  function selectSection(project: ProjectListItem, sectionKey: string) {
    setSelectedProject(project)
    navigate(`/commesse/${project.id}/${sectionKey}`)
  }

  function selectRoot(project: ProjectListItem) {
    setSelectedProject(project)
    navigate(`/commesse/${project.id}`)
  }

  function openDocumentFolder(project: ProjectListItem, path: string) {
    setSelectedProject(project)
    const query = path ? `?path=${encodeURIComponent(path)}` : ""
    navigate(`/commesse/${project.id}/documents${query}`)
  }

  function openDocumentFile(
    project: ProjectListItem,
    parentPath: string,
    fileRelativePath: string
  ) {
    setSelectedProject(project)
    const query = new URLSearchParams()
    if (parentPath) {
      query.set("path", parentPath)
    }
    query.set("preview", fileRelativePath)
    navigate(`/commesse/${project.id}/documents?${query.toString()}`)
  }

  async function handleHardDelete() {
    if (!projectId) {
      return
    }
    const code = selectedProject?.code ?? `#${projectId}`
    const first = await confirm({
      title: "Elimina definitivamente",
      description: `Eliminare DEFINITIVAMENTE la commessa "${code}"?\n\nVerranno cancellati tutti i dati (fasi, timesheet, DDP, costing, documenti) e le cartelle su disco. Se la commessa deriva da un'offerta, l'offerta tornerà "Accettata". L'operazione è irreversibile.`,
      confirmLabel: "Continua",
    })
    if (!first) {
      return
    }
    const second = await confirm({
      title: "Conferma definitiva",
      description: `Ultima conferma: cancellare "${code}"? Non sarà possibile annullare.`,
      confirmLabel: "Elimina definitivamente",
    })
    if (!second) {
      return
    }
    try {
      await hardDeleteProject(projectId)
      await queryClient.invalidateQueries({ queryKey: ["projects-tree"] })
      setSelectedProject(null)
      navigate("/commesse")
    } catch (error) {
      notifyError(error, "Errore nell'eliminazione.")
    }
  }

  const isProjectNode = section == null || section === "details"
  const sectionTitle =
    section && SECTION_TITLES[section] ? SECTION_TITLES[section] : null
  const showCommessaHeader =
    projectId != null && sectionTitle == null && selectedProject != null

  const page = (
    <div
      ref={rootRef}
      style={{ height }}
      className="flex overflow-hidden rounded-lg border bg-card"
    >
      {/* Pannello albero */}
      <div
        style={{ width: isTreeCollapsed ? 52 : treeWidth }}
        className="flex shrink-0 flex-col overflow-hidden transition-[width] duration-200 ease-in-out"
      >
        <CommessaTree
          selectedProjectId={projectId}
          selectedSection={section}
          selectedDocumentPath={documentPath}
          canSeeEconomics={canSeeEconomics}
          onSelect={selectSection}
          onSelectRoot={selectRoot}
          onOpenDocumentFolder={openDocumentFolder}
          onOpenDocumentFile={openDocumentFile}
          onNewProject={() => setDialogProject("new")}
          isCollapsed={isTreeCollapsed}
        />
      </div>

      {/* Splitter */}
      <div
        role="separator"
        aria-orientation="vertical"
        className={cn(
          "w-px shrink-0 bg-border",
          !isTreeCollapsed && "w-1 cursor-col-resize hover:bg-primary/40"
        )}
        onMouseDown={
          isTreeCollapsed
            ? undefined
            : () => {
                draggingRef.current = true
                document.body.style.cursor = "col-resize"
                document.body.style.userSelect = "none"
              }
        }
      />

      {/* Contenuto sezione */}
      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        <div className="flex items-center gap-3 border-b px-5 py-3">
          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8 shrink-0 text-muted-foreground hover:text-foreground"
            onClick={() => setIsTreeCollapsed(!isTreeCollapsed)}
            title={isTreeCollapsed ? "Mostra elenco commesse" : "Nascondi elenco commesse"}
          >
            <PanelLeft className="size-4" />
          </Button>
          {fromGlobal && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate(fromGlobal)}
              className="h-8 gap-1.5"
            >
              <ArrowLeft className="size-4" />
              Indietro
            </Button>
          )}
          {showCommessaHeader ? (
            <div className="min-w-0">
              <h2 className="truncate text-xl font-bold leading-tight">
                {selectedProject!.code}
              </h2>
              <p className="truncate pl-4 text-base font-medium text-muted-foreground leading-tight">
                {selectedProject!.customerName || "—"}
              </p>
            </div>
          ) : (
            <h2
              className={cn(
                "truncate text-lg font-semibold",
                projectId == null && "text-muted-foreground"
              )}
            >
              {projectId == null
                ? "Seleziona una commessa"
                : sectionTitle ?? "Commessa"}
            </h2>
          )}
          <div className="ml-auto flex items-center gap-2">
            {projectId != null && section === "details" ? (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setDialogProject(projectId)}
              >
                <Pencil />
                Modifica
              </Button>
            ) : null}
            {projectId != null && isProjectNode && isAdmin ? (
              <Button
                variant="outline"
                size="sm"
                className="text-destructive hover:text-destructive"
                onClick={() => {
                  void handleHardDelete()
                }}
              >
                <Trash2 />
                Elimina commessa
              </Button>
            ) : null}
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {projectId == null ? (
            <div className="flex h-full items-center justify-center p-8 text-center">
              <div className="max-w-md">
                <p className="text-base font-semibold">
                  Seleziona una commessa
                </p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Scegli una commessa dall'albero a sinistra, poi apri una delle
                  sue sezioni (Dettagli, Flusso di cassa, Preventivo vs
                  consuntivo, Chat, Verbali, DDP, Documenti).
                </p>
              </div>
            </div>
          ) : section == null ? (
            <div className="flex h-full items-center justify-center p-8 text-center">
              <div className="max-w-md">
                <p className="text-base font-semibold">Seleziona una sezione</p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Apri Dettagli, Flusso di cassa, Preventivo vs consuntivo,
                  Chat, Verbali, DDP o Documenti dall'albero a sinistra.
                </p>
              </div>
            </div>
          ) : (
            <div className="p-5">
              <SectionContent
                projectId={projectId}
                section={section}
                canSeeEconomics={canSeeEconomics}
                projectCode={selectedProject?.code}
                projectTitle={selectedProject?.title}
              />
            </div>
          )}
        </div>
      </div>

      <ProjectDialog
        open={dialogProject !== null}
        projectId={dialogProject}
        onClose={() => setDialogProject(null)}
        onSaved={async () => {
          setDialogProject(null)
          await queryClient.invalidateQueries({ queryKey: ["projects-tree"] })
          if (typeof dialogProject === "number") {
            await queryClient.invalidateQueries({
              queryKey: ["project-dashboard", dialogProject],
            })
          }
        }}
      />
    </div>
  )

  if (projectId == null) {
    return page
  }

  return (
    <ProjectDocumentsActionsProvider projectId={projectId}>
      {page}
    </ProjectDocumentsActionsProvider>
  )
}

function SectionContent({
  projectId,
  section,
  canSeeEconomics,
  projectCode,
  projectTitle,
}: {
  projectId: number
  section: string
  canSeeEconomics: boolean
  projectCode?: string
  projectTitle?: string
}) {
  switch (section) {
    case "details":
      return <ProjectDetailsSection projectId={projectId} />
    case "documents":
      return <ProjectDocuments projectId={projectId} />
    case "ddp_commercial":
      return <ProjectDdpCommercial projectId={projectId} />
    case "budget_vs_actual":
      if (!canSeeEconomics) {
        return (
          <p className="text-sm text-muted-foreground">
            Sezione riservata ai ruoli PM e ADMIN.
          </p>
        )
      }
      return <ProjectBudgetVsActual projectId={projectId} />
    case "cashflow":
      return <ProjectCashFlow projectId={projectId} />
    case "chat":
      return <ProjectChat projectId={projectId} />
    case "mom":
      return <ProjectMoM projectId={projectId} />
    case "checklist":
      return <ProjectChecklist projectId={projectId} />
    case "milestones":
      return (
        <ProjectMilestones
          projectId={projectId}
          projectCode={projectCode}
          projectTitle={projectTitle}
        />
      )
    case "sal":
      return (
        <ProjectSal
          projectId={projectId}
          projectCode={projectCode}
          projectTitle={projectTitle}
        />
      )
    case "ddp_officina":
      return <ProjectDdpOfficina projectId={projectId} />
    case "work_requests":
      return <ProjectWorkRequests projectId={projectId} />
    default:
      return (
        <p className="text-sm text-muted-foreground">Sezione sconosciuta.</p>
      )
  }
}
