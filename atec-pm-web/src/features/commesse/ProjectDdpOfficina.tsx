import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Pencil, Plus, Trash2 } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { DateField } from "@/components/shared/date-field"
import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { ApiError } from "@/lib/api/client"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  fetchActiveDdpDestinations,
  fetchDdpAggregations,
  fetchDdpStatuses,
} from "@/lib/api/ddp-config"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import type {
  DdpDestinationItem,
  DdpStatusItem,
  OfficinaItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { CodexPickerDialog } from "./CodexPickerDialog"
import {
  buildDestinationOptions,
  DDP_DESTINATION_NONE,
} from "./ddp-destination-options"
import {
  DdpDestinationCell,
  DdpDestinationSpecCell,
} from "./DdpDestinationCell"
import { DdpQuantityStepper } from "./DdpQuantityStepper"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { DdpStatusMenu } from "./DdpStatusMenu"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"

function fmtDate(value: string | null): string {
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
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
  material: "Materiale",
  treatment: "Trattamento",
  supplierName: "Fornitore",
  itemStatus: "Stato",
  daneaRef: "Rif. Danea",
  dateNeeded: "Necessario",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  notes: "Note",
}

function toForm(item: OfficinaItem): OfficinaItemSaveRequest {
  return {
    id: item.id,
    projectId: item.projectId,
    partNumber: item.partNumber,
    description: item.description,
    quantity: item.quantity,
    unitCost: item.unitCost,
    material: item.material,
    treatment: item.treatment,
    supplierName: item.supplierName,
    itemStatus: item.itemStatus,
    requestedBy: item.requestedBy,
    daneaRef: item.daneaRef,
    dateNeeded: item.dateNeeded,
    destination: item.destination,
    destinationSpec: item.destinationSpec ?? "",
    notes: item.notes,
    expectedUpdatedAt: item.updatedAt,
  }
}

export function ProjectDdpOfficina({ projectId }: { projectId: number }) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")
  const [dialog, setDialog] = React.useState<OfficinaItemSaveRequest | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<
    Set<string>
  >(() => new Set())

  React.useEffect(() => {
    if (highlightRowId) {
      setSelectedStatusKeys(new Set())
    }
  }, [highlightRowId])

  const query = useQuery({
    queryKey: ["project-ddp-officina", projectId],
    queryFn: () => fetchOfficinaItems(projectId),
    enabled: projectId > 0,
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const statuses = React.useMemo(
    () => statusesQuery.data ?? [],
    [statusesQuery.data]
  )
  const statusMap = React.useMemo(
    () => new Map(statuses.map((s) => [s.statusKey, s])),
    [statuses]
  )

  const destinationsQuery = useQuery({
    queryKey: ["ddp-destinations", "active"],
    queryFn: fetchActiveDdpDestinations,
  })
  const destinations = React.useMemo(
    () => destinationsQuery.data ?? [],
    [destinationsQuery.data]
  )
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
        queryKey: ["project-ddp-officina", projectId],
      }),
    [queryClient, projectId]
  )

  const onDdpChange = React.useCallback(
    (change: { ddpType: string }) => {
      if (change.ddpType?.toUpperCase() === "OFFICINA") void invalidate()
    },
    [invalidate]
  )
  useProjectHub(projectId > 0 ? projectId : null, onDdpChange)

  const saveMutation = useMutation({
    mutationFn: async (form: OfficinaItemSaveRequest) => {
      if (form.id > 0) await updateOfficinaItem(projectId, form.id, form)
      else await addOfficinaItem(projectId, form)
    },
    onSuccess: async () => {
      setDialog(null)
      await invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const statusMutation = useMutation({
    mutationFn: ({
      item,
      statusKey,
    }: {
      item: OfficinaItem
      statusKey: string
    }) =>
      updateOfficinaItem(projectId, item.id, {
        ...toForm(item),
        itemStatus: statusKey,
      }),
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
      item,
      quantity,
      itemStatus,
    }: {
      item: OfficinaItem
      quantity?: number
      itemStatus?: string
    }) =>
      updateOfficinaItem(projectId, item.id, {
        ...toForm(item),
        ...(quantity !== undefined ? { quantity } : {}),
        ...(itemStatus !== undefined ? { itemStatus } : {}),
      }),
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
      item: OfficinaItem,
      patch: { quantity?: number; itemStatus?: string }
    ) => {
      quantityMutation.mutate({ item, ...patch })
    },
    [quantityMutation]
  )

  const handleQuantityAdjust = useDdpQuantityAdjust({
    confirm,
    statusMap,
    isPending: quantityMutation.isPending,
    excludedSet,
    onApply: applyQuantityPatch,
  })

  const handleStatusChange = React.useCallback(
    (item: OfficinaItem, statusKey: string) => {
      if (statusKey === item.itemStatus || statusMutation.isPending) {
        return
      }
      statusMutation.mutate({ item, statusKey })
    },
    [statusMutation]
  )

  const destinationMutation = useMutation({
    mutationFn: ({
      item,
      destination,
      destinationSpec,
    }: {
      item: OfficinaItem
      destination: string
      destinationSpec?: string
    }) =>
      updateOfficinaItem(projectId, item.id, {
        ...toForm(item),
        destination,
        destinationSpec:
          destinationSpec ??
          (!destination.trim()
            ? ""
            : !item.destination?.trim()
              ? ""
              : (item.destinationSpec ?? "")),
      }),
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
      item,
      destinationSpec,
    }: {
      item: OfficinaItem
      destinationSpec: string
    }) =>
      updateOfficinaItem(projectId, item.id, {
        ...toForm(item),
        destinationSpec,
      }),
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

  const handleDestinationChange = React.useCallback(
    (item: OfficinaItem, destination: string) => {
      const nextSpec = !destination.trim()
        ? ""
        : !item.destination?.trim()
          ? ""
          : (item.destinationSpec ?? "")
      if (
        destination === (item.destination ?? "") &&
        nextSpec === (item.destinationSpec ?? "")
      ) {
        return
      }
      if (destinationMutation.isPending) {
        return
      }
      destinationMutation.mutate({ item, destination, destinationSpec: nextSpec })
    },
    [destinationMutation]
  )

  const handleDestinationSpecCommit = React.useCallback(
    (item: OfficinaItem, destinationSpec: string) => {
      if (
        !item.destination?.trim() ||
        destinationSpec === (item.destinationSpec ?? "") ||
        destinationSpecMutation.isPending
      ) {
        return
      }
      destinationSpecMutation.mutate({ item, destinationSpec })
    },
    [destinationSpecMutation]
  )

  const handleAnnulRow = React.useCallback(
    async (item: OfficinaItem) => {
      if (item.itemStatus === DDP_STATUS_CANCELLED || statusMutation.isPending) {
        return
      }
      const rowLabel = item.partNumber || item.description || "questa riga"
      const ok = await confirmDdpRowAnnul(confirm, statusMap, rowLabel)
      if (ok) {
        statusMutation.mutate({ item, statusKey: DDP_STATUS_CANCELLED })
      }
    },
    [confirm, statusMap, statusMutation]
  )

  const items = React.useMemo(
    () => (query.data ?? []).map((it, index) => ({ ...it, rowNumber: index + 1 })),
    [query.data]
  )

  const statusFilterItems = React.useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of items) {
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
  }, [items, statusMap])

  const statusFilteredItems = React.useMemo(() => {
    if (selectedStatusKeys.size === 0) {
      return items
    }
    return items.filter((row) => selectedStatusKeys.has(row.itemStatus ?? ""))
  }, [items, selectedStatusKeys])

  const columns = React.useMemo<ColumnDef<OfficinaItem>[]>(
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
            {fmtDate(row.original.createdAt)}
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
            className="block max-w-[240px] truncate"
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
        accessorKey: "material",
        header: "Materiale",
        cell: ({ row }) => row.original.material || "—",
      },
      {
        accessorKey: "treatment",
        header: "Trattamento",
        cell: ({ row }) => row.original.treatment || "—",
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
        header: "Necessario",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="whitespace-nowrap">
            {fmtDate(row.original.dateNeeded)}
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
              onClick: () => setDialog(toForm(item)),
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
              label={item.partNumber || String(item.id)}
              actions={actions}
            />
          )
        },
      },
    ],
    [statusMap, statuses, destinations, handleAnnulRow, handleStatusChange, handleDestinationChange, handleDestinationSpecCommit, statusMutation.isPending, destinationMutation.isPending, destinationSpecMutation.isPending, quantityMutation.isPending, handleQuantityAdjust, excludedSet]
  )

  // Il totale include solo le righe non escluse (aggregazione A9). Le escluse sono contate a parte.
  const totalCost = items.reduce(
    (s, i) => (excludedSet.has(i.itemStatus) ? s : s + i.totalCost),
    0
  )
  const excludedRows = items.filter((i) => excludedSet.has(i.itemStatus))
  const excludedValue = excludedRows.reduce((s, i) => s + i.totalCost, 0)

  const rowStyle = React.useCallback(
    (item: OfficinaItem) => {
      const s = statusMap.get(item.itemStatus)
      return s ? { backgroundColor: s.colorBg, color: s.colorFg } : undefined
    },
    [statusMap]
  )

  return (
    <>
      <DataTableCardFiltered
        title="DDP officina"
        description="Distinta particolari meccanici della commessa"
        columns={columns}
        data={statusFilteredItems}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => query.refetch()}
        searchPlaceholder="Cerca nei particolari…"
        rowNoun="righe"
        emptyMessage="Nessun particolare meccanico."
        getRowId={(r) => String(r.id)}
        highlightRowId={highlightRowId}
        onRowDoubleClick={(r) => setDialog(toForm(r))}
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
          daneaRef: false,
          notes: false,
        }}
        toolbarActions={
          <>
            <span className="self-center text-sm font-medium tabular-nums">
              Totale: {euro(totalCost)}
              {excludedRows.length > 0 ? (
                <span className="ml-2 font-normal text-muted-foreground">
                  · escluse {excludedRows.length} ({euro(excludedValue)})
                </span>
              ) : null}
            </span>
            <Button size="sm" onClick={() => setPickerOpen(true)}>
              <Plus />
              Aggiungi da Codex
            </Button>
          </>
        }
      />

      <OfficinaDialog
        form={dialog}
        statuses={statuses}
        destinations={destinations}
        saving={saveMutation.isPending}
        onClose={() => setDialog(null)}
        onChange={setDialog}
        onSave={() => dialog && saveMutation.mutate(dialog)}
      />

      <CodexPickerDialog
        open={pickerOpen}
        projectId={projectId}
        onClose={() => setPickerOpen(false)}
        onAdded={() => void invalidate()}
      />
    </>
  )
}

function OfficinaDialog({
  form,
  statuses,
  destinations,
  saving,
  onClose,
  onChange,
  onSave,
}: {
  form: OfficinaItemSaveRequest | null
  statuses: DdpStatusItem[]
  destinations: DdpDestinationItem[]
  saving: boolean
  onClose: () => void
  onChange: (form: OfficinaItemSaveRequest) => void
  onSave: () => void
}) {
  if (!form) return null
  // In modifica il Codice (101 Codex), la Descrizione e il Richiedente provengono
  // dal Codex e sono read-only, come nella griglia officina del WPF.
  const isEdit = form.id > 0
  const set = (patch: Partial<OfficinaItemSaveRequest>) =>
    onChange({ ...form, ...patch })
  const num = (s: string) => {
    const v = Number(s.replace(",", "."))
    return Number.isFinite(v) ? v : 0
  }
  const field = (
    label: string,
    value: string,
    key: keyof OfficinaItemSaveRequest,
    disabled = false
  ) => (
    <div className="grid gap-1.5">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Input
        value={value}
        disabled={disabled}
        onChange={(e) => set({ [key]: e.target.value })}
      />
    </div>
  )

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            {form.id > 0 ? "Modifica particolare" : "Nuovo particolare"}
          </DialogTitle>
        </DialogHeader>
        <div className="grid grid-cols-2 gap-3">
          {field("Codice (101 Codex)", form.partNumber, "partNumber", isEdit)}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Stato</Label>
            <Select
              value={form.itemStatus}
              onValueChange={(v) => set({ itemStatus: v })}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {statuses.map((s) => (
                  <SelectItem key={s.statusKey} value={s.statusKey}>
                    {s.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="col-span-2">
            {field("Descrizione", form.description, "description", isEdit)}
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Quantità</Label>
            <Input
              inputMode="decimal"
              value={String(form.quantity)}
              onChange={(e) => set({ quantity: num(e.target.value) })}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Costo unitario (€)</Label>
            <Input
              inputMode="decimal"
              value={String(form.unitCost)}
              onChange={(e) => set({ unitCost: num(e.target.value) })}
            />
          </div>
          {field("Materiale", form.material, "material")}
          {field("Trattamento", form.treatment, "treatment")}
          {field("Fornitore (officina)", form.supplierName, "supplierName")}
          {field("Richiesto da", form.requestedBy, "requestedBy", isEdit)}
          {field("Rif. Danea", form.daneaRef, "daneaRef")}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Necessario per</Label>
            <DateField
              value={form.dateNeeded}
              onChange={(value) => set({ dateNeeded: value })}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Descr. destinazione</Label>
            <Select
              value={form.destination || DDP_DESTINATION_NONE}
              onValueChange={(v) => {
                const destination = v === DDP_DESTINATION_NONE ? "" : v
                set({
                  destination,
                  destinationSpec: destination
                    ? form.destination.trim()
                      ? form.destinationSpec
                      : ""
                    : "",
                })
              }}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={DDP_DESTINATION_NONE}>(nessuna)</SelectItem>
                {buildDestinationOptions(destinations, form.destination).map(
                  (name) => (
                    <SelectItem key={name} value={name}>
                      {name}
                    </SelectItem>
                  )
                )}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Specifica destinazione</Label>
            <Input
              value={form.destinationSpec}
              disabled={!form.destination.trim()}
              placeholder={
                form.destination.trim()
                  ? "Es. R1, QE1…"
                  : "Selezionare prima la descrizione"
              }
              onChange={(e) => set({ destinationSpec: e.target.value })}
            />
          </div>
          <div className="col-span-2">{field("Note", form.notes, "notes")}</div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Annulla
          </Button>
          <Button onClick={onSave} disabled={saving}>
            {saving ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
