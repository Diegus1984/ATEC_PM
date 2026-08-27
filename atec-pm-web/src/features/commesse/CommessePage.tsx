import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, PanelLeft, Pencil, Trash2 } from "lucide-react"
import { useNavigate, useParams, useSearchParams, useLocation } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import { fetchProject, hardDeleteProject } from "@/lib/api/projects"
import type { ProjectListItem } from "@/lib/api/types"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { cn } from "@/lib/utils"

import { CommessaTree } from "./CommessaTree"
import { readDocumentsNav, withDocumentsNav } from "./documents-nav-state"
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
import { ProjectWorkshopRows } from "./ProjectWorkshopRows"
import { sezioniVisibili } from "./commessa-sections"

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
  milestones: "Milestone",
  sal: "SAL / Fatturazione",
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
  // Sezioni economiche dell'albero: è una VISIBILITÀ di dato, non una modifica, quindi
  // `canAccessFeature` — chi ha `data.budget` in sola lettura deve comunque vederle.
  const canSeeEconomics = canAccessFeature("data.budget")

  const projectId = params.projectId ? Number(params.projectId) : null
  const section = params.section ?? null
  const chatFromUrl = Number(searchParams.get("chat") || 0)
  // Cartella documenti corrente (condivisa tra albero a sinistra e card a destra):
  // sta nell'history state, non nell'URL (vedi `documents-nav-state`).
  const documentPath =
    section === "documents"
      ? readDocumentsNav(location.state, searchParams).docPath
      : ""

  const [selectedProject, setSelectedProject] =
    React.useState<ProjectListItem | null>(null)
  const [dialogProject, setDialogProject] = React.useState<
    number | "new" | null
  >(null)

  React.useEffect(() => {
    if (location.state?.newProject || searchParams.get("action") === "new") {
      setDialogProject("new")
      if (location.state?.newProject) {
        window.history.replaceState({}, document.title)
      }
    }
  }, [location.state, searchParams])

  // #94 — Intestazione commessa: `selectedProject` si valorizza solo dai click
  // sull'albero, quindi su deep-link (es. card di /milestones) resterebbe null e
  // l'header direbbe solo «Commessa». Codice e titolo arrivano da questa query
  // leggera; finché carica — o se fallisce (bozza 404 per chi non ha la chiave) —
  // si ripiega su `selectedProject` e la pagina non si rompe.
  const headerQuery = useQuery({
    queryKey: ["project-header", projectId],
    queryFn: () => fetchProject(projectId!),
    enabled: projectId != null,
    retry: false, // un 404 su bozza è definitivo: inutile insistere
  })
  // Il ripiego vale solo se è la STESSA commessa dell'URL: con Back/Forward o un
  // link interno verso un'altra commessa il componente non si rimonta e
  // `selectedProject` resterebbe quello vecchio — l'header (e la stampa SAL)
  // direbbero codice e titolo di un'altra riga.
  const fallbackProject =
    selectedProject && selectedProject.id === projectId ? selectedProject : null
  const headerProject = headerQuery.data ?? fallbackProject
  const headerLabel = headerProject
    ? [headerProject.code, headerProject.title].filter(Boolean).join(" — ")
    : ""

  // Titolo della scheda del browser: «{code} — {title} · ATEC PM» quando la
  // commessa è nota; uscendo dalla pagina si ripristina il default.
  React.useEffect(() => {
    if (!headerLabel) {
      return
    }
    document.title = `${headerLabel} · ATEC PM`
    return () => {
      document.title = "ATEC PM"
    }
  }, [headerLabel])

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
    navigate(`/commesse/${project.id}/documents`, {
      state: withDocumentsNav(null, { docPath: path }),
    })
  }

  function openDocumentFile(
    project: ProjectListItem,
    parentPath: string,
    fileRelativePath: string
  ) {
    setSelectedProject(project)
    navigate(`/commesse/${project.id}/documents`, {
      state: withDocumentsNav(null, {
        docPath: parentPath,
        docPreview: fileRelativePath,
      }),
    })
  }

  async function handleHardDelete(project?: ProjectListItem) {
    const id = project?.id ?? projectId
    if (!id) {
      return
    }
    const code = project?.code ?? selectedProject?.code ?? `#${id}`
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
      await hardDeleteProject(id)
      await queryClient.invalidateQueries({ queryKey: ["projects-tree"] })
      if (projectId === id) {
        setSelectedProject(null)
        navigate("/commesse")
      }
    } catch (error) {
      notifyError(error, "Errore nell'eliminazione.")
    }
  }

  const sezioneConsentita =
    section == null ||
    sezioniVisibili(canSeeEconomics, canAccessFeature).some((s) => s.key === section)

  // Sezione senza permesso aperta scrivendo l'indirizzo a mano: si torna alla commessa
  // senza dire nulla — per chi non ha il permesso quella sezione non esiste.
  React.useEffect(() => {
    if (!sezioneConsentita) {
      navigate(projectId != null ? `/commesse/${projectId}` : "/commesse", {
        replace: true,
      })
    }
  }, [sezioneConsentita, projectId, navigate])

  const isProjectNode = section == null || section === "details"
  const sectionTitle =
    section && SECTION_TITLES[section] ? SECTION_TITLES[section] : null
  // #94 — Riga principale dell'header: sempre «{code} — {title}» quando c'è una
  // commessa (anche su deep-link, appena la query risponde); «Commessa» è solo il
  // placeholder transitorio mentre non si sa ancora nulla.
  const headerText =
    projectId == null ? "Seleziona una commessa" : headerLabel || "Commessa"

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
          onEditProject={(project) => setDialogProject(project.id)}
          onDeleteProject={(project) => {
            void handleHardDelete(project)
          }}
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
          <div className="min-w-0">
            <h2
              className={cn(
                "truncate text-lg font-semibold leading-tight",
                projectId == null && "text-muted-foreground"
              )}
              title={headerText}
            >
              {headerText}
            </h2>
            {projectId != null && sectionTitle ? (
              <p
                className="truncate text-sm text-muted-foreground leading-tight"
                title={sectionTitle}
              >
                {sectionTitle}
              </p>
            ) : null}
          </div>
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
            {projectId != null &&
            isProjectNode &&
            canWriteFeature("action.delete_project") ? (
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

        <div
          className={cn(
            "min-h-0 flex-1",
            section === "chat" ? "overflow-hidden" : "overflow-y-auto"
          )}
        >
          {projectId == null ? (
            <div className="flex h-full items-center justify-center p-8 text-center">
              <div className="max-w-md">
                <p className="text-base font-semibold">
                  Seleziona una commessa
                </p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Scegli una commessa dall'albero a sinistra, poi apri una delle
                  sue sezioni (Dashboard Commessa, Flusso di cassa, Preventivo vs
                  consuntivo, Chat, Verbali, DDP, Documenti).
                </p>
              </div>
            </div>
          ) : section == null ? (
            <div className="flex h-full items-center justify-center p-8 text-center">
              <div className="max-w-md">
                <p className="text-base font-semibold">Seleziona una sezione</p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Apri Dashboard Commessa, Flusso di cassa, Preventivo vs consuntivo,
                  Chat, Verbali, DDP o Documenti dall'albero a sinistra.
                </p>
              </div>
            </div>
          ) : !sezioneConsentita ? null : (
            <div
              className={
                section === "chat" ? "flex h-full min-h-0 flex-col" : "p-5"
              }
            >
              <SectionContent
                projectId={projectId}
                section={section}
                canSeeEconomics={canSeeEconomics}
                projectCode={headerProject?.code}
                projectTitle={headerProject?.title}
                initialChatId={chatFromUrl > 0 ? chatFromUrl : null}
              />
            </div>
          )}
        </div>
      </div>

      <ProjectDialog
        open={dialogProject !== null}
        projectId={dialogProject}
        onClose={() => setDialogProject(null)}
        onSaved={async (newProjectId) => {
          setDialogProject(null)
          await queryClient.invalidateQueries({ queryKey: ["projects-tree"] })
          if (typeof dialogProject === "number") {
            await queryClient.invalidateQueries({
              queryKey: ["project-dashboard", dialogProject],
            })
            await queryClient.invalidateQueries({
              queryKey: ["project-header", dialogProject],
            })
          }
          // Commessa appena creata: si atterra sul suo piano di fatturazione, che è
          // la prima cosa da compilare (il SAL vuoto viene creato al volo). Chi il SAL
          // non lo vede resta sulla scheda della commessa.
          if (newProjectId) {
            navigate(
              canAccessFeature("project.sal")
                ? `/commesse/${newProjectId}/sal`
                : `/commesse/${newProjectId}`
            )
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
  initialChatId,
}: {
  projectId: number
  section: string
  canSeeEconomics: boolean
  projectCode?: string
  projectTitle?: string
  initialChatId?: number | null
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
      return (
        <ProjectChat
          projectId={projectId}
          projectCode={projectCode}
          projectTitle={projectTitle}
          initialChatId={initialChatId}
        />
      )
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
      return <ProjectWorkshopRows projectId={projectId} />
    default:
      return (
        <p className="text-sm text-muted-foreground">Sezione sconosciuta.</p>
      )
  }
}
