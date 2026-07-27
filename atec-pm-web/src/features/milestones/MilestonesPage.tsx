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
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { fetchProjects } from "@/lib/api/projects"
import { fetchMilestones, fetchMilestonesSummary } from "@/lib/api/milestones"
import { useMilestonesHub } from "@/lib/signalr/use-milestones-hub"
import type { ProjectListItem } from "@/lib/api/types"
import { avgAvanz, periodo, summaryStatusDots } from "@/features/milestones/milestone-utils"
import { MilestoneTable } from "@/features/milestones/milestone-table"
import { MilestoneGantt } from "@/features/milestones/MilestoneGantt"
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

export function MilestonesPage() {
  const queryClient = useQueryClient()

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

  const containers: PmContainer[] = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.map((s) => ({
      key: `p${s.projectId}`,
      selected: selectedProjectId === s.projectId,
      onClick: () => setSelectedProjectId(s.projectId),
      label: s.title ? `${s.code} — ${s.title}` : s.code,
      count: s.active,
      dots: summaryStatusDots(s),
    }))
  }, [summaryQuery.data, selectedProjectId])

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
          containers={containers}
          containersLabel="Commesse"
          emptyLabel="Nessuna commessa con milestone"
        />

        <main className="flex min-w-0 flex-1 flex-col gap-4 p-4 min-h-0">
          <header className="flex shrink-0 flex-row flex-wrap items-center justify-end gap-2">
            {selectedProjectId === null && visibleProjects.length > 0 && (
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

            <Tabs value={viewMode} onValueChange={changeViewMode}>
              <TabsList className="h-9">
                <TabsTrigger value="table" className="h-7 text-xs">Tabella</TabsTrigger>
                <TabsTrigger value="gantt" className="h-7 text-xs">Gantt</TabsTrigger>
              </TabsList>
            </Tabs>

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

          <section className="flex-1 overflow-y-auto pr-1 flex flex-col gap-4 min-h-0 pb-4">
            {projectsQuery.isLoading ? (
              <p className="text-sm text-muted-foreground p-4">Caricamento commesse...</p>
            ) : projectsQuery.isError ? (
              <PageErrorAlert message={(projectsQuery.error as Error).message} />
            ) : visibleProjects.length === 0 ? (
              <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
                <p className="text-sm font-medium text-muted-foreground">Nessuna commessa trovata</p>
              </div>
            ) : (
              visibleProjects.map((p) => {
                const isExpanded = !!expandedProjects[p.id]
                return (
                  <ProjectMilestoneCard
                    key={p.id}
                    project={p}
                    viewMode={viewMode}
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
  viewMode: ViewMode
  expanded: boolean
  onToggleExpanded: () => void
}

function ProjectMilestoneCard({
  project,
  viewMode,
  expanded,
  onToggleExpanded,
}: ProjectMilestoneCardProps) {
  return (
    <Card className="overflow-hidden">
      <CardHeader
        onClick={onToggleExpanded}
        className="flex flex-row items-center justify-between py-3 px-4 bg-muted/20 border-b cursor-pointer select-none hover:bg-muted/40 transition-colors"
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
  projectCode,
  projectTitle,
}: {
  projectId: number
  viewMode: ViewMode
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
          <span className="flex items-center gap-2">
            <span className={cn("tabular-nums text-sm font-semibold", avg === 100 && "text-teal-600")}>
              {avg == null ? "—" : `${avg}%`}
            </span>
            {avg != null ? (
              <span className="h-1.5 w-24 overflow-hidden rounded bg-muted">
                <span
                  className={cn(
                    "block h-full",
                    avg >= 100 ? "bg-teal-500" : "bg-primary"
                  )}
                  style={{ width: `${avg}%` }}
                />
              </span>
            ) : null}
          </span>
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
          />
        </div>
      ) : (
        <div className="m-animate-slide-in-right">
          <MilestoneGantt
            projectId={projectId}
            items={items}
            projectCode={projectCode}
            projectTitle={projectTitle}
          />
        </div>
      )}
    </div>
  )
}
