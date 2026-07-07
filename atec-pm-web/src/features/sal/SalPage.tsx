import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import {
  CalendarClock,
  ExternalLink,
  Folder,
  FolderOpen,
  ReceiptText,
  RefreshCw,
} from "lucide-react"
import { Link } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { fetchProjects } from "@/lib/api/projects"
import { fetchSalSummary } from "@/lib/api/sal"
import type { ProjectListItem } from "@/lib/api/types"
import { salSummaryDots } from "@/features/commesse/sal-utils"
import { ProjectSal } from "@/features/commesse/ProjectSal"
import { SalProspettoView } from "./SalProspettoView"
import { cn } from "@/lib/utils"

const SUMMARY_QUERY_KEY = ["sal-summary"] as const

export function SalPage() {
  const queryClient = useQueryClient()

  // Stato di vista: all = tutte le commesse attive; prospetto = prospetto SAL; perProject = commessa singola
  const [view, setView] = React.useState<"all" | "prospetto" | "perProject">("all")
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)
  const [expandedProjects, setExpandedProjects] = React.useState<Record<number, boolean>>({})

  // Carica i progetti per l'area principale e il summary per la sidebar
  const projectsQuery = useQuery({
    queryKey: ["pm-sal-projects"],
    queryFn: () => fetchProjects({ page: 1, pageSize: 250 }),
  })

  const summaryQuery = useQuery({
    queryKey: SUMMARY_QUERY_KEY,
    queryFn: fetchSalSummary,
  })

  const refetchAll = () => {
    void projectsQuery.refetch()
    void summaryQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: ["sal"] })
    void queryClient.invalidateQueries({ queryKey: ["sal-prospetto"] })
  }

  const allProjects = React.useMemo(() => {
    return projectsQuery.data?.items ?? []
  }, [projectsQuery.data])

  const activeProjects = React.useMemo(() => {
    return allProjects.filter((p) => p.status === "ACTIVE")
  }, [allProjects])

  // Somma dei pre e warn per il conteggio del Prospetto SAL
  const prospettoCount = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.reduce((acc, curr) => acc + (curr.warn ?? 0) + (curr.pre ?? 0), 0)
  }, [summaryQuery.data])

  const quickViews: PmQuickView[] = [
    {
      key: "all",
      selected: view === "all",
      onClick: () => {
        setView("all")
        setSelectedProjectId(null)
      },
      icon: <ReceiptText />,
      label: "Tutte le commesse",
      count: activeProjects.length,
    },
    {
      key: "prospetto",
      selected: view === "prospetto",
      onClick: () => {
        setView("prospetto")
        setSelectedProjectId(null)
      },
      icon: <CalendarClock />,
      label: "Prospetto SAL",
      count: prospettoCount,
    },
  ]

  const containers: PmContainer[] = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.map((s) => ({
      key: `p${s.projectId}`,
      selected: view === "perProject" && selectedProjectId === s.projectId,
      onClick: () => {
        setSelectedProjectId(s.projectId)
        setView("perProject")
      },
      label: s.title ? `${s.code} — ${s.title}` : s.code,
      count: s.open,
      dots: salSummaryDots(s),
    }))
  }, [summaryQuery.data, selectedProjectId, view])

  const visibleProjects = React.useMemo(() => {
    if (view === "perProject" && selectedProjectId !== null) {
      const found = allProjects.find((p) => p.id === selectedProjectId)
      return found ? [found] : []
    }
    if (view === "all") {
      return activeProjects
    }
    return []
  }, [view, selectedProjectId, allProjects, activeProjects])

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

  // Espandi automaticamente se viene selezionato un singolo progetto
  React.useEffect(() => {
    if (view === "perProject" && selectedProjectId !== null) {
      setExpandedProjects((prev) => ({ ...prev, [selectedProjectId]: true }))
    }
  }, [view, selectedProjectId])

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">SAL / Fatturazione Commesse</h1>
        <p className="text-sm text-muted-foreground">
          Piani di fatturazione a stati d'avanzamento di tutte le commesse.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="sal"
          quickViews={quickViews}
          containers={containers}
          containersLabel="Commesse"
          emptyLabel="Nessuna commessa con SAL"
        />

        <main className="flex min-w-0 flex-1 flex-col gap-4 p-4 min-h-0">
          <header className="flex shrink-0 flex-row flex-wrap items-center justify-end gap-2">
            {view !== "prospetto" && visibleProjects.length > 0 && (
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
            {view === "prospetto" ? (
              <SalProspettoView />
            ) : projectsQuery.isLoading ? (
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
                  <ProjectSalCard
                    key={p.id}
                    project={p}
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

interface ProjectSalCardProps {
  project: ProjectListItem
  expanded: boolean
  onToggleExpanded: () => void
}

function ProjectSalCard({ project, expanded, onToggleExpanded }: ProjectSalCardProps) {
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
            <Link to={`/commesse/${project.id}/sal`}>
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
        <CardContent className="p-4 bg-zinc-50/50">
          <ProjectSal projectId={project.id} />
        </CardContent>
      )}
    </Card>
  )
}
