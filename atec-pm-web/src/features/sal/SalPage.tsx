import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Banknote,
  CalendarClock,
  ExternalLink,
  Folder,
  FolderOpen,
  ReceiptText,
  RefreshCw,
} from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { fetchProjects } from "@/lib/api/projects"
import { fetchSalSummary } from "@/lib/api/sal"
import type { ProjectListItem, SalSummary } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { salSummaryDots } from "@/features/commesse/sal-utils"
import { ProjectSal } from "@/features/commesse/ProjectSal"
import { SalIncassoProgress } from "./SalIncassoProgress"
import { SalAnalisiView } from "./SalAnalisiView"
import { SalCashFlowView } from "./SalCashFlowView"
import { SalProspettoView } from "./SalProspettoView"
import { useGlobalSalHub } from "@/lib/signalr/use-sal-hub"
import { cn } from "@/lib/utils"

const SUMMARY_QUERY_KEY = ["sal-summary"] as const

export function SalPage() {
  const queryClient = useQueryClient()

  // Le viste economiche (Cash Flow / Analisi) sono riservate ai ruoli PM/ADMIN
  // (stesso pattern canSeeEconomics di ProjectSal; l'endpoint economics fa 403).
  const role = getSession()?.user.userRole
  const canSeeEconomics = role === "ADMIN" || role === "PM"

  // Deep-link delle viste via query param `?view=` (sostituisce la rotta figlia
  // /sal/cashflow prevista da D8): valori validi prospetto|cashflow;
  // cashflow solo per PM/ADMIN, altrimenti il param viene ignorato.
  const [searchParams, setSearchParams] = useSearchParams()

  // Stato di vista: all = tutte le commesse attive; prospetto = prospetto SAL;
  // cashflow = vista economica globale con card + grafico Analisi (solo PM/ADMIN);
  // perProject = commessa singola
  const [view, setView] = React.useState<
    "all" | "prospetto" | "cashflow" | "perProject"
  >(() => {
    const v = searchParams.get("view")
    if (v === "prospetto") return "prospetto"
    // "analisi" è un alias storico: l'Analisi vive sotto le card del Cash Flow
    if ((v === "cashflow" || v === "analisi") && canSeeEconomics) return "cashflow"
    return "all"
  })
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)
  const [expandedProjects, setExpandedProjects] = React.useState<Record<number, boolean>>({})

  // Mantiene l'URL allineato alla vista corrente (replace: niente voci di history);
  // le viste senza deep-link (all/perProject) rimuovono il param.
  React.useEffect(() => {
    const next = view === "prospetto" || view === "cashflow" ? view : null
    if ((searchParams.get("view") ?? null) === next) return
    const params = new URLSearchParams(searchParams)
    if (next) params.set("view", next)
    else params.delete("view")
    setSearchParams(params, { replace: true })
  }, [view, searchParams, setSearchParams])

  // Carica i progetti per l'area principale e il summary per la sidebar
  const projectsQuery = useQuery({
    queryKey: ["pm-sal-projects"],
    queryFn: () => fetchProjects({ page: 1, pageSize: 250 }),
  })

  const summaryQuery = useQuery({
    queryKey: SUMMARY_QUERY_KEY,
    queryFn: fetchSalSummary,
  })

  const refetchAll = React.useCallback(() => {
    void projectsQuery.refetch()
    void summaryQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: ["sal"] })
    void queryClient.invalidateQueries({ queryKey: ["sal-prospetto"] })
  }, [projectsQuery, summaryQuery, queryClient])

  useGlobalSalHub(true, refetchAll)

  const allProjects = React.useMemo(() => {
    return projectsQuery.data?.items ?? []
  }, [projectsQuery.data])

  const activeProjects = React.useMemo(() => {
    return allProjects.filter((p) => p.status === "ACTIVE")
  }, [allProjects])

  // Somma di warn + pre + incassi scaduti per il conteggio del Prospetto SAL
  const prospettoCount = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.reduce(
      (acc, curr) => acc + (curr.warn ?? 0) + (curr.pre ?? 0) + (curr.incasso ?? 0),
      0
    )
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

  if (canSeeEconomics) {
    const monitoredCount = summaryQuery.data?.length ?? 0
    // Un'unica voce: la vista Cash Flow contiene le card e, sotto, il grafico Analisi
    quickViews.push({
      key: "cashflow",
      selected: view === "cashflow",
      onClick: () => {
        setView("cashflow")
        setSelectedProjectId(null)
      },
      icon: <Banknote />,
      label: "Cash Flow",
      count: monitoredCount,
    })
  }

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
            ) : view === "cashflow" ? (
              <>
                {/* Card di sintesi + grafico Analisi sulla stessa pagina (richiesta 09/07) */}
                <SalCashFlowView />
                <SalAnalisiView />
              </>
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
                const salSummary = summaryQuery.data?.find((s) => s.projectId === p.id)
                return (
                  <ProjectSalCard
                    key={p.id}
                    project={p}
                    salSummary={salSummary}
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
  salSummary?: SalSummary
  expanded: boolean
  onToggleExpanded: () => void
}

function ProjectSalCard({ project, salSummary, expanded, onToggleExpanded }: ProjectSalCardProps) {
  const showIncasso = salSummary != null && salSummary.percTotal > 0

  return (
    <Card className="overflow-hidden">
      <CardHeader
        onClick={onToggleExpanded}
        className="flex flex-row items-center justify-between py-3 px-4 bg-muted/20 border-b cursor-pointer select-none hover:bg-muted/40 transition-colors"
      >
        <div className="flex flex-col gap-0.5 min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
              {project.code}
            </span>
            <CardTitle className="text-sm font-semibold truncate">
              {project.title}
            </CardTitle>
            {showIncasso && (
              <SalIncassoProgress
                compact
                percTotal={salSummary.percTotal}
                percPaid={salSummary.percPaid}
              />
            )}
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
            <Link to={`/commesse/${project.id}/sal`} state={{ fromGlobal: "/sal" }}>
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
