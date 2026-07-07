import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw } from "lucide-react"

import { formatDateWithWeekday } from "@/components/shared/date-field"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader } from "@/components/ui/card"
import { fetchMilestones } from "@/lib/api/milestones"
import { useMilestonesHub } from "@/lib/signalr/use-milestones-hub"
import { avgAvanz, periodo } from "@/features/milestones/milestone-utils"
import { MilestoneTable } from "@/features/milestones/milestone-table"
import { MilestoneGantt } from "@/features/milestones/MilestoneGantt"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import "@/features/milestones/milestones-gantt.css"
import { cn } from "@/lib/utils"

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

function loadViewMode(): ViewMode {
  try {
    const stored = localStorage.getItem("milestones:view")
    if (stored === "gantt") return "gantt"
  } catch {
    // ignore
  }
  return "table"
}

/** Tab «Milestone» del dettaglio commessa: pianificazione a righe con testata di sintesi. */
export function ProjectMilestones({
  projectId,
  projectCode,
  projectTitle,
}: {
  projectId: number
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
  const invalidate = () => queryClient.invalidateQueries({ queryKey })
  useMilestonesHub(projectId > 0, invalidate, projectId)

  const items = React.useMemo(() => query.data ?? [], [query.data])
  const avg = avgAvanz(items)
  const per = periodo(items)
  const activeCount = items.filter((m) => !m.spento).length

  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadViewMode)

  const changeViewMode = React.useCallback((value: string) => {
    const mode = value as ViewMode
    setViewModeState(mode)
    try {
      localStorage.setItem("milestones:view", mode)
    } catch {
      // ignore
    }
  }, [])

  return (
    <Card className="overflow-hidden py-0">
      <CardHeader className="flex flex-row flex-wrap items-center gap-6 border-b bg-muted/30 py-3">
        <Stat label="Milestones">{activeCount}</Stat>
        <Stat label="Avanzamento medio">
          <span className="flex items-center gap-2">
            <span className={cn("tabular-nums", avg === 100 && "text-teal-600")}>
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
            <span className="tabular-nums">
              {formatDateWithWeekday(per.min)} → {formatDateWithWeekday(per.max)}
            </span>
          ) : (
            "—"
          )}
        </Stat>
        <div className="ml-auto flex items-center gap-2">
          <Tabs value={viewMode} onValueChange={changeViewMode}>
            <TabsList className="h-9">
              <TabsTrigger value="table" className="h-7 text-xs">Tabella</TabsTrigger>
              <TabsTrigger value="gantt" className="h-7 text-xs">Gantt</TabsTrigger>
            </TabsList>
          </Tabs>

          <Button
            variant="outline"
            size="sm"
            onClick={() => query.refetch()}
            disabled={query.isFetching}
          >
            <RefreshCw className={cn("size-4 mr-1.5", query.isFetching && "animate-spin")} />
            Aggiorna
          </Button>
        </div>
      </CardHeader>
      <CardContent className="p-3">
        {query.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : query.isError ? (
          <PageErrorAlert message={(query.error as Error).message} />
        ) : (
          <>
            {items.length === 0 ? (
              <p className="mb-3 text-sm text-muted-foreground">
                Nessuna milestone per questa commessa. Aggiungine una nella riga
                in fondo alla tabella (le attività standard si precaricano alla
                creazione della commessa).
              </p>
            ) : null}
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
          </>
        )}
      </CardContent>
    </Card>
  )
}
