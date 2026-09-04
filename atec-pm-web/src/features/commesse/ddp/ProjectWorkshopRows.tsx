import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { Button } from "@/components/ui/button"
import { ApiError } from "@/lib/api/client"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import { patchWorkRequestField } from "@/lib/api/workRequests"
import { fetchWorkshopRows, patchWorkshopField } from "@/lib/api/workshop"
import type { DdpStatusItem, WorkshopRow } from "@/lib/api/types"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { useWorkRequestsHub } from "@/lib/signalr/use-work-requests-hub"
import { notifyError } from "@/lib/toast"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"
import { cn } from "@/lib/utils"

import {
  buildWorkshopColumns,
  WORKSHOP_COLUMN_LABELS,
} from "@/features/work-requests/workshop-columns"
import {
  filtraPerVista,
  rowKey,
  TITOLI_VISTA,
  type WorkshopView,
} from "@/features/work-requests/workshop-shared"

const VISTE: WorkshopView[] = ["interne", "esterne", "trattamenti"]

/**
 * Tab «Lavorazioni» della commessa: la stessa vista della pagina «Lavorazioni Officine»
 * (#83), ristretta a questa commessa. Le righe sono quelle della sua DDP Officina — qui
 * non se ne creano di nuove: le righe nascono in distinta, e quelle manuali stanno nella
 * pagina globale, dove possono anche non avere una commessa.
 */
export function ProjectWorkshopRows({ projectId }: { projectId: number }) {
  const queryClient = useQueryClient()
  const [view, setView] = React.useState<WorkshopView>("interne")
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

  const righeQuery = useQuery({
    queryKey: ["workshop-rows", projectId],
    queryFn: () => fetchWorkshopRows(projectId),
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const statusMap = React.useMemo(() => {
    const m = new Map<string, DdpStatusItem>()
    for (const s of statusesQuery.data ?? []) m.set(s.statusKey, s)
    return m
  }, [statusesQuery.data])

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["workshop-rows"] })
  }, [queryClient])

  useWorkRequestsHub(true, invalidate)
  useProjectHub(projectId, (change) => {
    if (change.ddpType === "OFFICINA") invalidate()
  })

  const patchMutation = useMutation({
    mutationFn: ({
      row,
      field,
      value,
    }: {
      row: WorkshopRow
      field: "request_date" | "notes" | "is_ultra_critical"
      value: string | boolean | null
    }) =>
      row.source === "DDP"
        ? patchWorkshopField(row.id, field, value, row.updatedAt)
        : patchWorkRequestField(row.id, field, value),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError("La riga è stata modificata da un altro utente. Ricarica e riprova.")
        invalidate()
        return
      }
      notifyError(err)
    },
  })

  const righe = React.useMemo(() => righeQuery.data ?? [], [righeQuery.data])
  const filtrate = React.useMemo(() => filtraPerVista(righe, view), [righe, view])

  const tieniOrdineServer = React.useCallback((list: WorkshopRow[]) => list, [])
  const displayRows = useDeferredItemOrder(filtrate, tieniOrdineServer, layoutEpoch, view)

  const columns = React.useMemo(
    () =>
      buildWorkshopColumns(
        view,
        {
          onRequestDate: (row, value) =>
            patchMutation.mutate({ row, field: "request_date", value }),
          onNotes: (row, value) => patchMutation.mutate({ row, field: "notes", value }),
          onUltraCritical: (row, value) =>
            patchMutation.mutate({ row, field: "is_ultra_critical", value }),
          disabled: patchMutation.isPending,
        },
        statusMap
      ),
    [view, patchMutation, statusMap]
  )

  return (
    <DataTableCardFiltered
      title="Lavorazioni"
      description="Righe della DDP Officina di questa commessa. Data richiesta, note e urgenza si scrivono qui."
      columns={columns}
      data={displayRows}
      isLoading={righeQuery.isLoading}
      error={righeQuery.error as Error | null}
      onRefresh={() => {
        setLayoutEpoch((n) => n + 1)
        void righeQuery.refetch()
      }}
      searchPlaceholder="Cerca codice, descrizione, materiale…"
      columnLabels={WORKSHOP_COLUMN_LABELS}
      getRowId={rowKey}
      visibilityStorageKey={`commessa-lavorazioni-${view}-v1`}
      gridLines
      aboveTable={
        <div className="mb-3 flex flex-wrap gap-1.5">
          {VISTE.map((v) => (
            <Button
              key={v}
              size="sm"
              variant={view === v ? "default" : "outline"}
              className={cn("h-7 text-xs")}
              onClick={() => {
                setView(v)
                setLayoutEpoch((n) => n + 1)
              }}
            >
              {TITOLI_VISTA[v]}
              <span className="ml-1 tabular-nums opacity-70">
                {filtraPerVista(righe, v).length}
              </span>
            </Button>
          ))}
        </div>
      }
      rowStyle={(row) =>
        row.isUltraCritical
          ? { backgroundColor: "rgba(244, 63, 94, 0.10)" }
          : (row.daysLate ?? 0) > 0
            ? { backgroundColor: "rgba(244, 63, 94, 0.05)" }
            : undefined
      }
    />
  )
}
