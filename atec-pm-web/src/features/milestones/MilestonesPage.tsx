import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import {
  CalendarDays,
  ExternalLink,
  Folder,
  FolderOpen,
  RefreshCw,
} from "lucide-react"
import { Link } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { formatDateWithWeekday } from "@/components/shared/date-field"
import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { fetchProjects } from "@/lib/api/projects"
import { fetchMilestones, fetchMilestonesSummary } from "@/lib/api/milestones"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { formatDateShort } from "@/lib/date-iso"
import { useMilestonesHub } from "@/lib/signalr/use-milestones-hub"
import type { MilestoneSummary, ProjectListItem } from "@/lib/api/types"
import { avgAvanz, periodo, summaryStatusDots } from "@/features/milestones/milestone-utils"
import { MilestoneTable } from "@/features/milestones/milestone-table"
import { MilestoneGantt } from "@/features/milestones/MilestoneGantt"
import { ALTRE_ATTIVITA_SECTION_LABEL, compareProjectCodes, isCommessaCode } from "@/lib/project-code"
import { cn } from "@/lib/utils"

const GLOBAL_VIEW_STORAGE_KEY = "milestones:global:view"
const SUMMARY_QUERY_KEY = ["milestones-summary"] as const

function Stat({
  label,
  children,
}: {
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="text-sm font-medium">{children}</span>
    </div>
  )
}

type ViewMode = "table" | "gantt"

function loadGlobalViewMode(): ViewMode {
  try {
    const stored = localStorage.getItem(GLOBAL_VIEW_STORAGE_KEY)
    if (stored === "gantt") return "gantt"
  } catch {
    // ignore
  }
  return "table"
}

// Vista di PAGINA (#90): elenco schede per commessa («table») oppure griglia di card
// compatte stile MoM («cards»). Preferenza per-utente come in MoM (mom.list.viewMode),
// separata dal toggle Tabella|Gantt che governa solo il contenuto delle schede espanse.
type PageViewMode = "table" | "cards"

function pageViewModeStorageKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `milestones.list.viewMode.${employeeId}`
}

function loadPageViewMode(): PageViewMode {
  try {
    if (localStorage.getItem(pageViewModeStorageKey()) === "cards") return "cards"
  } catch {
    // ignore
  }
  return "table"
}

/** Percentuale + barretta di avanzamento: stessa resa nella testata di dettaglio,
 *  nell'header delle schede chiuse e nella vista card. */
function AvanzamentoBar({ avg }: { avg: number | null }) {
  return (
    <span className="flex items-center gap-2">
      <span className={cn("tabular-nums text-sm font-semibold", avg === 100 && "text-teal-600")}>
        {avg == null ? "—" : `${avg}%`}
      </span>
      {avg != null ? (
        <span className="h-1.5 w-24 overflow-hidden rounded bg-muted">
          {/* Verde come la barra incasso SAL (richiesta 16/08); al 100% resta il
              teal delle milestone completate. */}
          <span
            className={cn(
              "block h-full",
              avg >= 100 ? "bg-teal-500" : "bg-emerald-500"
            )}
            style={{ width: `${avg}%` }}
          />
        </span>
      ) : null}
    </span>
  )
}

/** Periodo del riepilogo aggregato in formato compatto gg/mm/aa (— se nessuna data). */
function summaryPeriodoLabel(summary: MilestoneSummary | undefined): string {
  if (!summary?.periodStart) return "—"
  return `${formatDateShort(summary.periodStart)} → ${formatDateShort(summary.periodEnd)}`
}

export function MilestonesPage() {
  const queryClient = useQueryClient()

  // Funzione concessa in sola lettura: la panoramica si consulta (sidebar, schede,
  // Gantt, stampa) ma non si scrive. L'API respinge comunque le scritture con un 403:
  // qui si tolgono i comandi, altrimenti ogni clic finisce in un errore rosso.
  const readOnly = !canWriteFeature("nav.milestones")

  // Vista globale (Tabella o Gantt)
  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadGlobalViewMode)

  const changeViewMode = React.useCallback((value: string) => {
    const mode = value as ViewMode
    setViewModeState(mode)
    try {
      localStorage.setItem(GLOBAL_VIEW_STORAGE_KEY, mode)
    } catch {
      // ignore
    }
  }, [])

  // Vista di pagina Tabella|Card (per-utente). Nel ramo Card il toggle Tabella|Gantt è nascosto.
  const [pageView, setPageViewState] = React.useState<PageViewMode>(loadPageViewMode)

  const changePageView = React.useCallback((value: string) => {
    const mode = value as PageViewMode
    setPageViewState(mode)
    try {
      localStorage.setItem(pageViewModeStorageKey(), mode)
    } catch {
      // ignore
    }
  }, [])

  // Caricamento elenco commesse (per l'area principale) e riepilogo milestone (per la sidebar).
  const projectsQuery = useQuery({
    queryKey: ["pm-milestones-projects"],
    queryFn: () => fetchProjects({ page: 1, pageSize: 250 }),
  })

  const summaryQuery = useQuery({
    queryKey: SUMMARY_QUERY_KEY,
    queryFn: fetchMilestonesSummary,
  })

  const refetchAll = () => {
    projectsQuery.refetch()
    summaryQuery.refetch()
    queryClient.invalidateQueries({ queryKey: ["milestones"] })
  }

  const allProjects = React.useMemo(() => {
    return projectsQuery.data?.items ?? []
  }, [projectsQuery.data])

  const activeProjects = React.useMemo(() => {
    return allProjects.filter((p) => p.status === "ACTIVE")
  }, [allProjects])

  // Lookup del riepilogo per commessa: nutre la vista Card e l'header delle schede
  // chiuse senza caricare le milestone (il contenuto delle schede resta lazy).
  const summaryByProject = React.useMemo(() => {
    const map = new Map<number, MilestoneSummary>()
    for (const s of summaryQuery.data ?? []) map.set(s.projectId, s)
    return map
  }, [summaryQuery.data])

  // Selezione + espansione schede
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)
  const [expandedProjects, setExpandedProjects] = React.useState<Record<number, boolean>>({})

  // Sidebar PM condivisa: unica vista rapida (tutte le attive) + un contenitore per ogni commessa
  // che ha almeno una milestone attiva. Pallini = stato milestone, conteggio = milestone attive.
  const quickViews: PmQuickView[] = [
    {
      key: "all",
      selected: selectedProjectId === null,
      onClick: () => setSelectedProjectId(null),
      icon: <CalendarDays />,
      label: "Tutte le commesse attive",
      count: activeProjects.length,
    },
  ]

  // Commesse con milestone + tutte le commesse aperte ancora senza (a conteggio 0):
  // una commessa appena creata si trova subito in barra laterale, pronta al precarico.
  // Separazione come Check list e MoM: in «Commesse» solo i codici con data, le
  // commesse di servizio a codice libero (INTERNA, SERVICE _ SANGRATO…) a parte.
  const sections = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    const withMilestones = new Set(rows.map((s) => s.projectId))
    const entries: { code: string; container: PmContainer }[] = [
      ...rows.map((s) => ({
        code: s.code,
        container: {
          key: `p${s.projectId}`,
          selected: selectedProjectId === s.projectId,
          onClick: () => setSelectedProjectId(s.projectId),
          label: s.title ? `${s.code} — ${s.title}` : s.code,
          count: s.active,
          dots: summaryStatusDots(s),
        } satisfies PmContainer,
      })),
      ...allProjects
        .filter((p) => !withMilestones.has(p.id))
        .map((p) => ({
          code: p.code,
          container: {
            key: `p${p.id}`,
            selected: selectedProjectId === p.id,
            onClick: () => setSelectedProjectId(p.id),
            label: p.title ? `${p.code} — ${p.title}` : p.code,
            count: 0,
            dots: [],
          } satisfies PmContainer,
        })),
    ]

    return [
      {
        key: "projects",
        label: "Commesse",
        containers: entries
          .filter((e) => isCommessaCode(e.code))
          .sort((a, b) => compareProjectCodes(a.code, b.code))
          .map((e) => e.container),
        emptyLabel: "Nessuna commessa",
      },
      {
        key: "altre",
        label: ALTRE_ATTIVITA_SECTION_LABEL,
        containers: entries
          .filter((e) => !isCommessaCode(e.code))
          .sort((a, b) => a.container.label.localeCompare(b.container.label, "it"))
          .map((e) => e.container),
        emptyLabel: "Nessuna altra attività",
      },
    ]
  }, [summaryQuery.data, allProjects, selectedProjectId])

  // Area principale: la commessa selezionata, oppure tutte le attive.
  const visibleProjects = React.useMemo(() => {
    if (selectedProjectId !== null) {
      const found = allProjects.find((p) => p.id === selectedProjectId)
      return found ? [found] : []
    }
    return activeProjects
  }, [selectedProjectId, allProjects, activeProjects])

  const expandAll = () => {
    const next: Record<number, boolean> = {}
    visibleProjects.forEach((p) => {
      next[p.id] = true
    })
    setExpandedProjects(next)
  }

  const collapseAll = () => {
    setExpandedProjects({})
  }

  // Se viene selezionato un singolo progetto, espandilo in automatico
  React.useEffect(() => {
    if (selectedProjectId !== null) {
      setExpandedProjects((prev) => ({ ...prev, [selectedProjectId]: true }))
    }
  }, [selectedProjectId])

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">Milestones Commesse</h1>
        <p className="text-sm text-muted-foreground">
          Panoramica e programmazione delle milestone raggruppate per commessa.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="milestones"
          quickViews={quickViews}
          sections={sections}
        />

        <main className="flex min-w-0 flex-1 flex-col gap-4 p-4 min-h-0">
          <header className="flex shrink-0 flex-row flex-wrap items-center justify-end gap-2">
            {pageView === "table" && selectedProjectId === null && visibleProjects.length > 0 && (
              <div className="flex items-center gap-1.5 border rounded-lg bg-card p-1">
                <Button variant="ghost" size="sm" className="h-7 text-xs px-2" onClick={expandAll}>
                  Espandi tutte
                </Button>
                <div className="h-3 w-px bg-muted" />
                <Button variant="ghost" size="sm" className="h-7 text-xs px-2" onClick={collapseAll}>
                  Comprimi tutte
                </Button>
              </div>
            )}

            {/* Vista di pagina (#90): elenco schede oppure griglia di card stile MoM */}
            <Select value={pageView} onValueChange={changePageView}>
              <SelectTrigger size="sm" className="w-[130px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="table">Tabella</SelectItem>
                <SelectItem value="cards">Card</SelectItem>
              </SelectContent>
            </Select>

            {pageView === "table" && (
              <Tabs value={viewMode} onValueChange={changeViewMode}>
                <TabsList className="h-9">
                  <TabsTrigger value="table" className="h-7 text-xs">Tabella</TabsTrigger>
                  <TabsTrigger value="gantt" className="h-7 text-xs">Gantt</TabsTrigger>
                </TabsList>
              </Tabs>
            )}

            <Button
              variant="outline"
              size="sm"
              className="h-9"
              onClick={refetchAll}
              disabled={projectsQuery.isFetching || summaryQuery.isFetching}
            >
              <RefreshCw
                className={cn(
                  "size-4 mr-1.5",
                  (projectsQuery.isFetching || summaryQuery.isFetching) && "animate-spin"
                )}
              />
              Aggiorna
            </Button>
          </header>

          {/* Contenitore a BLOCCO come in /sal e /bilancio: con `flex flex-col` le card
              (che hanno `overflow-hidden`) perdono il min-height automatico e da una certa
              lunghezza in poi vengono compresse e tagliate. Vedi BLOCKS-RULES. */}
          <section className="flex-1 space-y-4 overflow-y-auto pr-1 min-h-0 pb-4">
            {projectsQuery.isLoading ? (
              <p className="text-sm text-muted-foreground p-4">Caricamento commesse...</p>
            ) : projectsQuery.isError ? (
              <PageErrorAlert message={(projectsQuery.error as Error).message} />
            ) : summaryQuery.isError ? (
              // #90: la vista Card e gli header a scheda chiusa vivono del riepilogo —
              // senza questo ramo un errore mostrerebbe «0 / — / —» come dati veri.
              <PageErrorAlert message={(summaryQuery.error as Error).message} />
            ) : visibleProjects.length === 0 ? (
              <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
                <p className="text-sm font-medium text-muted-foreground">Nessuna commessa trovata</p>
              </div>
            ) : pageView === "cards" ? (
              // Vista Card (#90): griglia stile MoM alimentata dal solo riepilogo
              // aggregato, nessun caricamento delle milestone per commessa.
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                {visibleProjects.map((p) => (
                  <MilestoneProjectCard
                    key={p.id}
                    project={p}
                    summary={summaryByProject.get(p.id)}
                  />
                ))}
              </div>
            ) : (
              visibleProjects.map((p) => {
                const isExpanded = !!expandedProjects[p.id]
                return (
                  <ProjectMilestoneCard
                    key={p.id}
                    project={p}
                    summary={summaryByProject.get(p.id)}
                    viewMode={viewMode}
                    readOnly={readOnly}
                    expanded={isExpanded}
                    onToggleExpanded={() =>
                      setExpandedProjects((prev) => ({ ...prev, [p.id]: !prev[p.id] }))
                    }
                  />
                )
              })
            )}
          </section>
        </main>
      </div>
    </div>
  )
}

interface ProjectMilestoneCardProps {
  project: ProjectListItem
  /** Riepilogo aggregato (#90): sintesi in header a scheda chiusa, senza caricare le milestone. */
  summary: MilestoneSummary | undefined
  viewMode: ViewMode
  /** Sola lettura: la scheda si apre e si legge, i comandi di scrittura no. */
  readOnly: boolean
  expanded: boolean
  onToggleExpanded: () => void
}

function ProjectMilestoneCard({
  project,
  summary,
  viewMode,
  readOnly,
  expanded,
  onToggleExpanded,
}: ProjectMilestoneCardProps) {
  return (
    <Card className="overflow-hidden py-0">
      <CardHeader
        onClick={onToggleExpanded}
        className={cn(
          // Stessa card di /sal e /bilancio: bordo inferiore solo a scheda aperta (da chiusa
          // era una riga che non separava niente) e `pb-3!` per non prendersi i 24px della
          // variante di serie di CardHeader.
          "flex flex-row items-center justify-between py-3 px-4 bg-muted/20 cursor-pointer select-none hover:bg-muted/40 transition-colors [.border-b]:pb-3!",
          expanded && "border-b"
        )}
      >
        <div className="flex flex-col gap-0.5 min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
              {project.code}
            </span>
            <CardTitle className="text-sm font-semibold truncate">
              {project.title}
            </CardTitle>
          </div>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-0.5 text-xs text-muted-foreground mt-1">
            <span>Cliente: <strong className="text-foreground">{project.customerName}</strong></span>
            <span>PM: <strong className="text-foreground">{project.pmName}</strong></span>
            {project.status !== "ACTIVE" && (
              <span className="uppercase text-[10px] bg-amber-100 text-amber-800 px-1 rounded font-bold">
                {project.status}
              </span>
            )}
          </div>
        </div>

        {/* Stessi 3 valori della testata di sintesi, ma dal riepilogo aggregato (#90):
            visibili anche a scheda CHIUSA. A scheda aperta li mostra già la testata di
            dettaglio (dati live), quindi qui si spengono per non duplicarli. */}
        {!expanded && (
          <div className="hidden lg:flex items-center gap-6 shrink-0 ml-4">
            <Stat label="Milestones">{summary?.active ?? 0}</Stat>
            <Stat label="Avanzamento medio">
              <AvanzamentoBar avg={summary?.avgAvanz ?? null} />
            </Stat>
            <Stat label="Periodo">
              <span className="tabular-nums">{summaryPeriodoLabel(summary)}</span>
            </Stat>
          </div>
        )}

        <div className="flex items-center gap-3 shrink-0 ml-4" onClick={(e) => e.stopPropagation()}>
          <Button asChild variant="outline" size="sm" className="h-8 gap-1">
            <Link to={`/commesse/${project.id}/milestones`} state={{ fromGlobal: "/milestones-summary" }}>
              <ExternalLink className="size-3.5" />
              Apri
            </Link>
          </Button>

          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={onToggleExpanded}
          >
            {expanded ? (
              <FolderOpen className="size-4 text-muted-foreground" />
            ) : (
              <Folder className="size-4 text-muted-foreground" />
            )}
          </Button>
        </div>
      </CardHeader>

      {expanded && (
        <CardContent className="p-4">
          <ProjectMilestoneCardContent
            projectId={project.id}
            viewMode={viewMode}
            readOnly={readOnly}
            projectCode={project.code}
            projectTitle={project.title}
          />
        </CardContent>
      )}
    </Card>
  )
}

function ProjectMilestoneCardContent({
  projectId,
  viewMode,
  readOnly,
  projectCode,
  projectTitle,
}: {
  projectId: number
  viewMode: ViewMode
  readOnly: boolean
  projectCode?: string
  projectTitle?: string
}) {
  const queryClient = useQueryClient()
  const queryKey = React.useMemo(
    () => ["milestones", "project", projectId],
    [projectId]
  )

  const query = useQuery({
    queryKey,
    queryFn: () => fetchMilestones(projectId),
    enabled: projectId > 0,
  })

  // Invalida sia la card sia il riepilogo della sidebar: modificando una milestone qui (o ricevendo
  // un evento realtime sulla commessa espansa) i pallini + conteggio in sidebar restano allineati.
  const invalidate = React.useCallback(() => {
    queryClient.invalidateQueries({ queryKey })
    queryClient.invalidateQueries({ queryKey: SUMMARY_QUERY_KEY })
  }, [queryClient, queryKey])

  useMilestonesHub(projectId > 0, invalidate, projectId)

  const items = React.useMemo(() => query.data ?? [], [query.data])
  const avg = avgAvanz(items)
  const per = periodo(items)
  const activeCount = items.filter((m) => !m.spento).length

  if (query.isLoading) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-muted-foreground">
        <RefreshCw className="size-4 animate-spin" />
        Caricamento milestone...
      </div>
    )
  }

  if (query.isError) {
    return <PageErrorAlert message={(query.error as Error).message} />
  }

  return (
    <div className="space-y-4">
      {/* Testata di sintesi */}
      <div className="flex flex-row flex-wrap items-center gap-6 bg-muted/30 p-3 rounded-lg border">
        <Stat label="Milestones">{activeCount}</Stat>
        <Stat label="Avanzamento medio">
          <AvanzamentoBar avg={avg} />
        </Stat>
        <Stat label="Periodo">
          {per.min ? (
            <span className="tabular-nums text-sm font-semibold">
              {formatDateWithWeekday(per.min)} → {formatDateWithWeekday(per.max)}
            </span>
          ) : (
            "—"
          )}
        </Stat>
      </div>

      {/* Rendering della tabella o del Gantt */}
      {viewMode === "table" ? (
        <div className="m-animate-slide-in-left">
          <MilestoneTable
            projectId={projectId}
            items={items}
            onMutated={invalidate}
            projectCode={projectCode}
            projectTitle={projectTitle}
            readOnly={readOnly}
          />
        </div>
      ) : (
        <div className="m-animate-slide-in-right">
          <MilestoneGantt
            projectId={projectId}
            items={items}
            projectCode={projectCode}
            projectTitle={projectTitle}
            readOnly={readOnly}
          />
        </div>
      )}
    </div>
  )
}

/** Card compatta della vista Card (#90, stile MoM): dati dal solo riepilogo aggregato,
 *  nessun caricamento delle milestone. Commesse senza riepilogo: 0 / — / —.
 *  Pura lettura: l'unica azione è «Apri» verso il tab Milestone della commessa. */
function MilestoneProjectCard({
  project,
  summary,
}: {
  project: ProjectListItem
  summary: MilestoneSummary | undefined
}) {
  return (
    <div className="flex flex-col rounded-xl border bg-card shadow-xs transition-colors hover:border-primary/40">
      <div className="flex-1 space-y-3 p-4">
        <div className="flex flex-col gap-0.5 min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
              {project.code}
            </span>
            <span className="text-sm font-semibold leading-snug truncate">
              {project.title}
            </span>
          </div>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-0.5 text-xs text-muted-foreground mt-1">
            <span>Cliente: <strong className="text-foreground">{project.customerName}</strong></span>
            <span>PM: <strong className="text-foreground">{project.pmName}</strong></span>
          </div>
        </div>
        <div className="space-y-1.5 text-sm">
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Milestones</span>
            <span className="font-medium tabular-nums">{summary?.active ?? 0}</span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Avanzamento medio</span>
            <AvanzamentoBar avg={summary?.avgAvanz ?? null} />
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Periodo</span>
            <span className="text-xs font-medium tabular-nums">
              {summaryPeriodoLabel(summary)}
            </span>
          </div>
        </div>
      </div>
      <div className="flex items-center justify-end border-t px-4 py-2">
        <Button asChild variant="outline" size="sm" className="h-8 gap-1">
          <Link to={`/commesse/${project.id}/milestones`} state={{ fromGlobal: "/milestones-summary" }}>
            <ExternalLink className="size-3.5" />
            Apri
          </Link>
        </Button>
      </div>
    </div>
  )
}
