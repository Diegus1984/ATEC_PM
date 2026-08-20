import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw } from "lucide-react"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { Button } from "@/components/ui/button"
import { ChecklistSummaryCharts } from "@/features/checklist/ChecklistSummaryCharts"
import {
  ChecklistColumnsMenu,
  ChecklistColumnsProvider,
  ChecklistContainerCard,
  ChecklistTable,
} from "@/features/checklist/checklist-shared"
import {
  computeChecklistStats,
  filterChecklistItems,
  type ChecklistDueFilter,
  type ChecklistPriorityFilter,
} from "@/features/checklist/checklist-utils"
import { fetchProjectChecklist } from "@/lib/api/checklist"
import { useChecklistHub } from "@/lib/signalr/use-checklist-hub"
import { cn } from "@/lib/utils"

const pill = "rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums"

/** Attività (Check list) filtrate sulla commessa — variante per-commessa del modulo. */
export function ProjectChecklist({ projectId }: { projectId: number }) {
  const queryClient = useQueryClient()
  const queryKey = ["checklist", "project", projectId]
  const [priorityFilter, setPriorityFilter] =
    React.useState<ChecklistPriorityFilter>("all")
  const [dueFilter, setDueFilter] = React.useState<ChecklistDueFilter>("all")
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

  const query = useQuery({
    queryKey,
    queryFn: () => fetchProjectChecklist(projectId),
    enabled: projectId > 0,
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey })

  useChecklistHub(projectId > 0, () => invalidate(), projectId)

  const items = React.useMemo(() => query.data ?? [], [query.data])
  const stats = React.useMemo(() => computeChecklistStats(items), [items])
  const filteredItems = React.useMemo(
    () => filterChecklistItems(items, priorityFilter, dueFilter),
    [items, priorityFilter, dueFilter]
  )

  const handleRefresh = React.useCallback(() => {
    setLayoutEpoch((n) => n + 1)
    void query.refetch()
  }, [query])

  const statusPills =
    items.length > 0 ? (
      <div className="flex flex-wrap items-center gap-1">
        {stats.overdue > 0 ? (
          <span className={cn(pill, "bg-red-100 text-red-700")}>
            {stats.overdue} scadute
          </span>
        ) : null}
        {stats.today > 0 ? (
          <span className={cn(pill, "bg-orange-100 text-orange-700")}>
            {stats.today} oggi
          </span>
        ) : null}
        {stats.critical > 0 ? (
          <span className={cn(pill, "bg-red-100 text-red-700")}>
            {stats.critical} critiche
          </span>
        ) : null}
      </div>
    ) : null

  return (
    <ChecklistColumnsProvider>
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={handleRefresh}
          disabled={query.isFetching}
          title="Riordina le righe secondo priorità e scadenze aggiornate"
        >
          <RefreshCw className={query.isFetching ? "animate-spin" : ""} />
          Aggiorna
        </Button>
        <div className="ml-auto">
          <ChecklistColumnsMenu />
        </div>
      </div>

      {query.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : query.isError ? (
        <PageErrorAlert message={(query.error as Error).message} />
      ) : (
        <>
          {items.length > 0 ? (
            <ChecklistSummaryCharts
              stats={stats}
              priorityFilter={priorityFilter}
              dueFilter={dueFilter}
              onPriorityFilter={setPriorityFilter}
              onDueFilter={setDueFilter}
            />
          ) : null}

          <ChecklistContainerCard
            title="Attività"
            count={filteredItems.length}
            accent="bg-primary"
            headerExtra={statusPills}
          >
            {items.length === 0 ? (
              <p className="mb-3 text-sm text-muted-foreground">
                Nessuna attività per questa commessa. Aggiungine una nella riga in
                fondo alla tabella.
              </p>
            ) : filteredItems.length === 0 ? (
              <p className="mb-3 text-sm text-muted-foreground">
                Nessuna attività con i filtri selezionati.{" "}
                <button
                  type="button"
                  className="font-medium text-primary hover:underline"
                  onClick={() => {
                    setPriorityFilter("all")
                    setDueFilter("all")
                  }}
                >
                  Mostra tutte
                </button>
              </p>
            ) : null}

            <ChecklistTable
              items={filteredItems}
              container={{ projectId }}
              onMutated={invalidate}
              layoutEpoch={layoutEpoch}
            />
          </ChecklistContainerCard>
        </>
      )}
    </div>
    </ChecklistColumnsProvider>
  )
}
