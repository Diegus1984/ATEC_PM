import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Pencil, Plus, Trash2 } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { ApiError } from "@/lib/api/client"
import {
  fetchActiveDdpDestinations,
  fetchDdpAggregations,
  fetchDdpStatuses,
} from "@/lib/api/ddp-config"
import { fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import type { DdpRowItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { CatalogPickerDialog } from "./CatalogPickerDialog"
import {
  DdpDestinationCell,
  DdpDestinationSpecCell,
} from "./DdpDestinationCell"
import { DdpQuantityStepper } from "./DdpQuantityStepper"
import { DdpRowDialog } from "./DdpRowDialog"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { DdpStatusMenu } from "./DdpStatusMenu"
import { ddpCommercialRowToSaveRequest } from "./ddp-commercial-row"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"

function formatDate(value: string | null): string {
  if (!value) return "—"
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleDateString("it-IT")
}

const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  createdAt: "Data",
  requestedBy: "Rich.",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  unit: "UM",
  supplierName: "Fornitore",
  manufacturer: "Produttore",
  itemStatus: "Stato",
  daneaRef: "Rif. Danea",
  dateNeeded: "Data Prev.",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  notes: "Note",
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
}

export function ProjectDdpCommercial({ projectId }: { projectId: number }) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")
  const [dialogTarget, setDialogTarget] = React.useState<DdpRowItem | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<
    Set<string>
  >(() => new Set())

  React.useEffect(() => {
    if (highlightRowId) {
      setSelectedStatusKeys(new Set())
    }
  }, [highlightRowId])

  const rowsQuery = useQuery({
    queryKey: ["project-ddp", projectId, "COMMERCIAL"],
    queryFn: () => fetchDdpRows(projectId, "COMMERCIAL"),
    enabled: projectId > 0,
  })
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const destinationsQuery = useQuery({
    queryKey: ["ddp-destinations", "active"],
    queryFn: fetchActiveDdpDestinations,
  })
  const aggregationsQuery = useQuery({
    queryKey: ["ddp-aggregations"],
    queryFn: fetchDdpAggregations,
  })
  // Stati «esclusi da totale/conteggi» (aggregazione A9): fuori dal totale € e con quantità bloccata.
  const excludedSet = React.useMemo(
    () =>
      new Set(
        aggregationsQuery.data?.find((a) => a.code === "A9")?.statusKeys ?? []
      ),
    [aggregationsQuery.data]
  )

  const invalidate = React.useCallback(
    () =>
      queryClient.invalidateQueries({
        queryKey: ["project-ddp", projectId, "COMMERCIAL"],
      }),
    [queryClient, projectId]
  )

  const onDdpChange = React.useCallback(
    (change: { ddpType: string }) => {
      if (change.ddpType?.toUpperCase() !== "OFFICINA") void invalidate()
    },
    [invalidate]
  )
  useProjectHub(projectId > 0 ? projectId : null, onDdpChange)

  const statusMutation = useMutation({
    mutationFn: ({
      row,
      statusKey,
    }: {
      row: DdpRowItem
      statusKey: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, statusKey)
      ),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
  })

  const quantityMutation = useMutation({
    mutationFn: ({
      row,
      quantity,
      itemStatus,
    }: {
      row: DdpRowItem
      quantity?: number
      itemStatus?: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(
          projectId,
          row,
          itemStatus ?? row.itemStatus,
          quantity
        )
      ),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
  })

  const destinationMutation = useMutation({
    mutationFn: ({
      row,
      destination,
      destinationSpec,
    }: {
      row: DdpRowItem
      destination: string
      destinationSpec?: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(
          projectId,
          row,
          row.itemStatus,
          undefined,
          destination,
          destinationSpec
        )
      ),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
  })

  const destinationSpecMutation = useMutation({
    mutationFn: ({
      row,
      destinationSpec,
    }: {
      row: DdpRowItem
      destinationSpec: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(
          projectId,
          row,
          row.itemStatus,
          undefined,
          undefined,
          destinationSpec
        )
      ),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
  })

  const applyQuantityPatch = React.useCallback(
    (
      row: DdpRowItem,
      patch: { quantity?: number; itemStatus?: string }
    ) => {
      quantityMutation.mutate({ row, ...patch })
    },
    [quantityMutation]
  )

  const handleStatusChange = React.useCallback(
    (row: DdpRowItem, statusKey: string) => {
      if (statusKey === row.itemStatus || statusMutation.isPending) {
        return
      }
      statusMutation.mutate({ row, statusKey })
    },
    [statusMutation]
  )

  const handleDestinationChange = React.useCallback(
    (row: DdpRowItem, destination: string) => {
      const nextSpec = !destination.trim()
        ? ""
        : !row.destination?.trim()
          ? ""
          : (row.destinationSpec ?? "")
      if (
        destination === (row.destination ?? "") &&
        nextSpec === (row.destinationSpec ?? "")
      ) {
        return
      }
      if (destinationMutation.isPending) {
        return
      }
      destinationMutation.mutate({ row, destination, destinationSpec: nextSpec })
    },
    [destinationMutation]
  )

  const handleDestinationSpecCommit = React.useCallback(
    (row: DdpRowItem, destinationSpec: string) => {
      if (
        !row.destination?.trim() ||
        destinationSpec === (row.destinationSpec ?? "") ||
        destinationSpecMutation.isPending
      ) {
        return
      }
      destinationSpecMutation.mutate({ row, destinationSpec })
    },
    [destinationSpecMutation]
  )

  const statuses = statusesQuery.data ?? []
  const destinations = React.useMemo(
    () => destinationsQuery.data ?? [],
    [destinationsQuery.data]
  )

  const statusMap = React.useMemo(
    () => new Map(statuses.map((s) => [s.statusKey, s])),
    [statuses]
  )

  const handleAnnulRow = React.useCallback(
    async (row: DdpRowItem) => {
      if (row.itemStatus === DDP_STATUS_CANCELLED || statusMutation.isPending) {
        return
      }
      const rowLabel = row.partNumber || row.description || "questa riga"
      const ok = await confirmDdpRowAnnul(confirm, statusMap, rowLabel)
      if (ok) {
        statusMutation.mutate({ row, statusKey: DDP_STATUS_CANCELLED })
      }
    },
    [confirm, statusMap, statusMutation]
  )

  const handleQuantityAdjust = useDdpQuantityAdjust({
    confirm,
    statusMap,
    isPending: quantityMutation.isPending,
    excludedSet,
    onApply: applyQuantityPatch,
  })

  const rows = rowsQuery.data ?? []

  const statusFilterItems = React.useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of rows) {
      const key = row.itemStatus ?? ""
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return [...counts.entries()]
      .map(([value, count]) => {
        const def = statusMap.get(value)
        return {
          value,
          label: value ? (def?.label ?? value) : "Senza stato",
          count,
          colorBg: def?.colorBg,
          colorFg: def?.colorFg,
          sortOrder: def?.sortOrder ?? Number.MAX_SAFE_INTEGER,
        }
      })
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder ||
          a.label.localeCompare(b.label, "it")
      )
  }, [rows, statusMap])

  const statusFilteredRows = React.useMemo(() => {
    if (selectedStatusKeys.size === 0) {
      return rows
    }
    return rows.filter((row) =>
      selectedStatusKeys.has(row.itemStatus ?? "")
    )
  }, [rows, selectedStatusKeys])

  const columns = React.useMemo<ColumnDef<DdpRowItem>[]>(
    () => [
      {
        accessorKey: "rowNumber",
        header: "#",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="tabular-nums opacity-80">{row.original.rowNumber}</span>
        ),
      },
      {
        accessorKey: "createdAt",
        header: "Data",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="whitespace-nowrap">
            {formatDate(row.original.createdAt)}
          </span>
        ),
      },
      {
        accessorKey: "requestedBy",
        header: "Rich.",
        cell: ({ row }) => row.original.requestedBy || "—",
      },
      {
        accessorKey: "partNumber",
        header: "Codice",
        cell: ({ row }) => (
          <span className="font-medium">{row.original.partNumber || "—"}</span>
        ),
      },
      {
        accessorKey: "description",
        header: "Descrizione",
        cell: ({ row }) => (
          <span
            className="block max-w-[260px] truncate"
            title={row.original.description}
          >
            {row.original.description || "—"}
          </span>
        ),
      },
      {
        accessorKey: "quantity",
        header: "Qtà",
        enableColumnFilter: false,
        cell: ({ row }) => {
          const item = row.original
          const isExcluded = excludedSet.has(item.itemStatus)
          const atMin =
            item.quantity <= 1 && item.itemStatus === DDP_STATUS_CANCELLED
          return (
            <span
              title={
                isExcluded
                  ? "Ripristina uno stato attivo per modificare la quantità"
                  : undefined
              }
            >
              <DdpQuantityStepper
                quantity={item.quantity}
                disabled={quantityMutation.isPending || isExcluded}
                decrementDisabled={atMin}
                onIncrement={() => void handleQuantityAdjust(item, 1)}
                onDecrement={() => void handleQuantityAdjust(item, -1)}
              />
            </span>
          )
        },
      },
      {
        accessorKey: "unit",
        header: "UM",
        cell: ({ row }) => row.original.unit || "—",
      },
      {
        accessorKey: "supplierName",
        header: "Fornitore",
        cell: ({ row }) => (
          <span
            className="block max-w-[140px] truncate"
            title={row.original.supplierName}
          >
            {row.original.supplierName || "—"}
          </span>
        ),
      },
      {
        accessorKey: "manufacturer",
        header: "Produttore",
        cell: ({ row }) => row.original.manufacturer || "—",
      },
      {
        id: "itemStatus",
        accessorFn: (r) => statusMap.get(r.itemStatus)?.label ?? r.itemStatus,
        header: "Stato",
        cell: ({ row }) => {
          const s = statusMap.get(row.original.itemStatus)
          return (
            <div className="flex min-w-[120px] items-center gap-1">
              <span className="min-w-0 flex-1 truncate font-semibold whitespace-nowrap">
                {s ? s.label : row.original.itemStatus || "—"}
              </span>
              <DdpStatusMenu
                currentStatusKey={row.original.itemStatus}
                statuses={statuses}
                disabled={statusMutation.isPending}
                onSelect={(statusKey) =>
                  handleStatusChange(row.original, statusKey)
                }
              />
            </div>
          )
        },
      },
      {
        accessorKey: "daneaRef",
        header: "Rif. Danea",
        cell: ({ row }) => row.original.daneaRef || "—",
      },
      {
        accessorKey: "dateNeeded",
        header: "Data Prev.",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="whitespace-nowrap">
            {formatDate(row.original.dateNeeded)}
          </span>
        ),
      },
      {
        accessorKey: "destination",
        header: "Destinazione",
        cell: ({ row }) => (
          <DdpDestinationCell
            destination={row.original.destination ?? ""}
            destinations={destinations}
            disabled={destinationMutation.isPending}
            onDestinationChange={(destination) =>
              handleDestinationChange(row.original, destination)
            }
          />
        ),
      },
      {
        accessorKey: "destinationSpec",
        header: "Specifica",
        cell: ({ row }) => (
          <DdpDestinationSpecCell
            destination={row.original.destination ?? ""}
            destinationSpec={row.original.destinationSpec ?? ""}
            disabled={destinationSpecMutation.isPending}
            onSpecCommit={(destinationSpec) =>
              handleDestinationSpecCommit(row.original, destinationSpec)
            }
          />
        ),
      },
      {
        accessorKey: "notes",
        header: "Note",
        cell: ({ row }) => (
          <span
            className="block max-w-[200px] truncate"
            title={row.original.notes}
          >
            {row.original.notes || "—"}
          </span>
        ),
      },
      {
        accessorKey: "unitCost",
        header: "€ Unit.",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="tabular-nums">{euro(row.original.unitCost)}</span>
        ),
      },
      {
        accessorKey: "totalCost",
        header: "€ Totale",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="font-semibold tabular-nums">
            {euro(row.original.totalCost)}
          </span>
        ),
      },
      {
        id: "actions",
        header: "",
        enableHiding: false,
        enableColumnFilter: false,
        cell: ({ row }) => {
          const item = row.original
          const actions: RowAction[] = [
            {
              label: "Modifica",
              icon: Pencil,
              onClick: () => setDialogTarget(item),
            },
          ]
          if (item.itemStatus !== DDP_STATUS_CANCELLED) {
            actions.push({
              label: "Elimina",
              icon: Trash2,
              destructive: true,
              separatorBefore: true,
              onClick: () => void handleAnnulRow(item),
            })
          }
          return (
            <RowActionsMenu
              label={item.partNumber || item.description}
              actions={actions}
            />
          )
        },
      },
    ],
    [
      statusMap,
      statuses,
      destinations,
      handleAnnulRow,
      handleStatusChange,
      handleDestinationChange,
      handleDestinationSpecCommit,
      statusMutation.isPending,
      destinationMutation.isPending,
      destinationSpecMutation.isPending,
      quantityMutation.isPending,
      handleQuantityAdjust,
      excludedSet,
    ]
  )

  // Il totale include solo le righe non escluse (aggregazione A9). Le escluse sono contate a parte.
  const totalValue = rows.reduce(
    (s, r) => (excludedSet.has(r.itemStatus) ? s : s + (r.totalCost || 0)),
    0
  )
  const excludedRows = rows.filter((r) => excludedSet.has(r.itemStatus))
  const excludedValue = excludedRows.reduce((s, r) => s + (r.totalCost || 0), 0)

  const rowStyle = React.useCallback(
    (row: DdpRowItem) => {
      const s = statusMap.get(row.itemStatus)
      return s ? { backgroundColor: s.colorBg, color: s.colorFg } : undefined
    },
    [statusMap]
  )

  return (
    <>
      <DataTableCardFiltered
        title="DDP commerciale"
        description="Distinta materiali commerciali della commessa"
        columns={columns}
        data={statusFilteredRows}
        columnLabels={COLUMN_LABELS}
        isLoading={rowsQuery.isLoading}
        isFetching={rowsQuery.isFetching}
        error={rowsQuery.error as Error | null}
        onRefresh={() => rowsQuery.refetch()}
        searchPlaceholder="Cerca nella distinta…"
        rowNoun="righe"
        emptyMessage="Nessuna riga nella distinta commerciale."
        getRowId={(r) => String(r.id)}
        highlightRowId={highlightRowId}
        onRowDoubleClick={(r) => setDialogTarget(r)}
        rowStyle={rowStyle}
        externalFiltersActive={selectedStatusKeys.size > 0}
        onClearExternalFilters={() => setSelectedStatusKeys(new Set())}
        aboveTable={
          <DdpStatusFilterBar
            items={statusFilterItems}
            selected={selectedStatusKeys}
            onChange={setSelectedStatusKeys}
          />
        }
        initialColumnVisibility={{
          createdAt: false,
          requestedBy: false,
          manufacturer: false,
          daneaRef: false,
          notes: false,
        }}
        toolbarActions={
          <>
            <span className="self-center text-sm font-medium tabular-nums">
              Totale: {euro(totalValue)}
              {excludedRows.length > 0 ? (
                <span className="ml-2 font-normal text-muted-foreground">
                  · escluse {excludedRows.length} ({euro(excludedValue)})
                </span>
              ) : null}
            </span>
            <Button size="sm" onClick={() => setPickerOpen(true)}>
              <Plus />
              Aggiungi da Catalogo
            </Button>
          </>
        }
      />

      <DdpRowDialog
        open={dialogTarget !== null}
        projectId={projectId}
        target={dialogTarget}
        statuses={statuses}
        destinations={destinations}
        onClose={() => setDialogTarget(null)}
        onSaved={async () => {
          setDialogTarget(null)
          await invalidate()
        }}
        onConflict={() => void invalidate()}
      />

      <CatalogPickerDialog
        open={pickerOpen}
        projectId={projectId}
        onClose={() => setPickerOpen(false)}
        onAdded={() => void invalidate()}
      />
    </>
  )
}
