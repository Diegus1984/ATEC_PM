import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Link2, Pencil, Plus, Trash2 } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { formatDateShort } from "@/lib/date-iso"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { ApiError } from "@/lib/api/client"
import {
  buildDdpTransitionMap,
  fetchActiveDdpDestinations,
  fetchDdpAggregations,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import { fetchDdpRows, updateDdpRow, deleteDdpRow } from "@/lib/api/project-ddp"
import type { DdpRowItem, SupplierListItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { getSession } from "@/lib/auth/session"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { AtecPickerDialog } from "./AtecPickerDialog"
import { CatalogPickerDialog } from "./CatalogPickerDialog"
import { DdpAtecAlternativesDialog } from "./DdpAtecAlternativesDialog"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { DaneaOrderDialog } from "@/components/shared/danea-order-dialog"
import { canRecodeCodex } from "@/features/codex/codex-roles"
import {
  DdpDestinationCell,
  DdpDestinationSpecCell,
} from "./DdpDestinationCell"
import { DdpInlineDateCell } from "./DdpInlineDateCell"
import { DdpInlineTextCell } from "./DdpInlineTextCell"
import { DdpQuantityStepper } from "./DdpQuantityStepper"
import { DdpSupplierCell } from "./DdpSupplierCell"
import { DdpRowDialog } from "./DdpRowDialog"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { DdpStatusMenu } from "./DdpStatusMenu"
import { ddpCommercialRowToSaveRequest } from "./ddp-commercial-row"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { isCommercialQtyEditable } from "./ddp-constants"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"
import { toDateOnly } from "@/lib/date-iso"

function formatDate(value: string | null): string {
  if (!value) return "—"
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? "—" : formatDateShort(d)
}

const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  createdAt: "Data",
  requestedBy: "Rich.",
  atecCode: "Cod. ATEC",
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
  const role = getSession()?.user.userRole ?? ""
  const canHardDelete = role === "ADMIN" || role === "PM"
  const canMapAtec = canRecodeCodex(role)
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")
  const [dialogTarget, setDialogTarget] = React.useState<DdpRowItem | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [atecPickerOpen, setAtecPickerOpen] = React.useState(false)
  const [altsTarget, setAltsTarget] = React.useState<DdpRowItem | null>(null)
  /** Riga senza ATEC: apre CatalogAtecAssignDialog (come Inbox Acquisti). */
  const [assignTarget, setAssignTarget] = React.useState<DdpRowItem | null>(null)
  /** IDDoc dell'ordine Danea da mostrare nel popup di rendering (link sul Rif. Danea). */
  const [daneaOrderIdDoc, setDaneaOrderIdDoc] = React.useState<number | null>(null)
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
  // Matrice avanzamenti (v7): restringe la finestra opzioni di menu ⋮ e dialog.
  const transitionsQuery = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })
  const transitionMap = React.useMemo(
    () => buildDdpTransitionMap(transitionsQuery.data ?? [], "COMMERCIAL"),
    [transitionsQuery.data]
  )
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

  // Errore comune agli update inline: 409 = conflitto di concorrenza, si ricarica per il token fresco.
  const onRowMutationError = React.useCallback(
    (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
    [invalidate]
  )

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
        ddpCommercialRowToSaveRequest(projectId, row, { itemStatus: statusKey })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
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
        ddpCommercialRowToSaveRequest(projectId, row, { itemStatus, quantity })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
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
        ddpCommercialRowToSaveRequest(projectId, row, {
          destination,
          destinationSpec,
        })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
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
        ddpCommercialRowToSaveRequest(projectId, row, { destinationSpec })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const supplierMutation = useMutation({
    mutationFn: ({
      row,
      supplier,
    }: {
      row: DdpRowItem
      supplier: SupplierListItem | null
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, {
          supplier: { id: supplier?.id ?? null },
        })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const daneaRefMutation = useMutation({
    mutationFn: ({ row, daneaRef }: { row: DdpRowItem; daneaRef: string }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { daneaRef })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const dateNeededMutation = useMutation({
    mutationFn: ({
      row,
      dateNeeded,
    }: {
      row: DdpRowItem
      dateNeeded: string | null
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { dateNeeded })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const notesMutation = useMutation({
    mutationFn: ({ row, notes }: { row: DdpRowItem; notes: string }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { notes })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
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

  const handleSupplierChange = React.useCallback(
    (row: DdpRowItem, supplier: SupplierListItem | null) => {
      if (
        (supplier?.id ?? null) === (row.supplierId ?? null) ||
        supplierMutation.isPending
      ) {
        return
      }
      supplierMutation.mutate({ row, supplier })
    },
    [supplierMutation]
  )

  const handleDaneaRefCommit = React.useCallback(
    (row: DdpRowItem, daneaRef: string) => {
      const next = daneaRef.trim()
      if (next === (row.daneaRef ?? "") || daneaRefMutation.isPending) {
        return
      }
      daneaRefMutation.mutate({ row, daneaRef: next })
    },
    [daneaRefMutation]
  )

  const handleDateNeededChange = React.useCallback(
    (row: DdpRowItem, dateNeeded: string | null) => {
      if (
        dateNeeded === toDateOnly(row.dateNeeded) ||
        dateNeededMutation.isPending
      ) {
        return
      }
      dateNeededMutation.mutate({ row, dateNeeded })
    },
    [dateNeededMutation]
  )

  const handleNotesCommit = React.useCallback(
    (row: DdpRowItem, notes: string) => {
      const next = notes.trim()
      if (next === (row.notes ?? "") || notesMutation.isPending) {
        return
      }
      notesMutation.mutate({ row, notes: next })
    },
    [notesMutation]
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

  const deleteMutation = useMutation({
    mutationFn: (row: DdpRowItem) => deleteDdpRow(projectId, row.id),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDeleteRow = React.useCallback(
    async (row: DdpRowItem) => {
      if (deleteMutation.isPending) {
        return
      }
      const rowLabel = row.partNumber || row.description || "questa riga"
      const ok = await confirm({
        title: "Eliminare definitivamente la riga?",
        description: `La riga "${rowLabel}" verrà eliminata definitivamente dalla distinta.\n\nL'operazione non è reversibile.`,
        confirmLabel: "Elimina definitivamente",
        destructive: true,
      })
      if (ok) {
        deleteMutation.mutate(row)
      }
    },
    [confirm, deleteMutation]
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
        accessorKey: "atecCode",
        header: "Cod. ATEC",
        cell: ({ row }) => {
          const item = row.original
          if (item.atecCode) {
            return (
              <span className="font-medium tabular-nums">
                {item.atecCode}
              </span>
            )
          }
          // Stesso pattern Inbox Acquisti: icona a destra se manca l'ATEC e si può mappare.
          const canAssign =
            (item.catalogItemId != null && item.catalogItemId > 0) ||
            (item.partNumber ?? "").trim().length > 0
          if (canMapAtec && canAssign) {
            return (
              <div className="flex justify-end">
                <button
                  type="button"
                  title="Assegna codice ATEC"
                  className="rounded p-1 opacity-60 hover:bg-black/10 hover:opacity-100"
                  onClick={(e) => {
                    e.stopPropagation()
                    setAssignTarget(item)
                  }}
                >
                  <Link2 className="size-4" />
                </button>
              </div>
            )
          }
          return <span className="opacity-60">—</span>
        },
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
          const canEditQty = isCommercialQtyEditable(item.itemStatus)
          const atMin =
            item.quantity <= 1 && item.itemStatus === DDP_STATUS_CANCELLED
          return (
            <span
              title={
                canEditQty
                  ? undefined
                  : "La quantità è modificabile solo in stato Da Ordinare"
              }
            >
              <DdpQuantityStepper
                quantity={item.quantity}
                disabled={quantityMutation.isPending || !canEditQty}
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
        // Filtro stretto su misura: l'input standard a larghezza intrinseca
        // allargherebbe la colonna ben oltre i 4-5 caratteri delle UM.
        meta: {
          filterInput: ({ value, onChange }) => (
            <Input
              value={(value as string) ?? ""}
              className="h-8 w-14 bg-background dark:bg-background"
              onChange={(event) => onChange(event.target.value)}
            />
          ),
        },
        cell: ({ row }) => row.original.unit || "—",
      },
      {
        accessorKey: "supplierName",
        header: "Fornitore",
        cell: ({ row }) => (
          <DdpSupplierCell
            supplierId={row.original.supplierId ?? null}
            supplierName={row.original.supplierName || ""}
            disabled={supplierMutation.isPending}
            onSupplierChange={(supplier) =>
              handleSupplierChange(row.original, supplier)
            }
          />
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
                transitions={transitionMap}
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
        // Riga con ordine generato da ATEC PM: il riferimento È il link al popup
        // dell'ordine (niente edit inline; si può sempre cambiare dal dialog Modifica,
        // e il server in quel caso stacca il link). Senza ordine: edit inline classico.
        cell: ({ row }) =>
          row.original.daneaOrderIdDoc != null ? (
            <button
              type="button"
              className="font-medium text-teal-700 underline underline-offset-2 hover:text-teal-800"
              title="Apri ordine Danea"
              onClick={(e) => {
                e.stopPropagation()
                setDaneaOrderIdDoc(row.original.daneaOrderIdDoc!)
              }}
            >
              {row.original.daneaRef || "—"}
            </button>
          ) : (
            <DdpInlineTextCell
              value={row.original.daneaRef ?? ""}
              disabled={daneaRefMutation.isPending}
              placeholder="—"
              onCommit={(value) => handleDaneaRefCommit(row.original, value)}
            />
          ),
      },
      {
        accessorKey: "dateNeeded",
        header: "Data Prev.",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <DdpInlineDateCell
            value={toDateOnly(row.original.dateNeeded)}
            disabled={dateNeededMutation.isPending}
            onChange={(value) => handleDateNeededChange(row.original, value)}
          />
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
          <DdpInlineTextCell
            value={row.original.notes ?? ""}
            disabled={notesMutation.isPending}
            placeholder="—"
            onCommit={(value) => handleNotesCommit(row.original, value)}
          />
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
          if (item.atecCode) {
            actions.push({
              label: "Alternative dal mapping…",
              icon: Link2,
              onClick: () => setAltsTarget(item),
            })
          }
          if (item.itemStatus !== DDP_STATUS_CANCELLED) {
            actions.push({
              label: "Elimina",
              icon: Trash2,
              destructive: true,
              separatorBefore: true,
              onClick: () => void handleAnnulRow(item),
            })
          }
          if (canHardDelete) {
            actions.push({
              label: "Elimina definitivamente",
              icon: Trash2,
              destructive: true,
              separatorBefore: item.itemStatus === DDP_STATUS_CANCELLED,
              onClick: () => void handleDeleteRow(item),
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
      handleDeleteRow,
      canHardDelete,
      handleStatusChange,
      handleDestinationChange,
      handleDestinationSpecCommit,
      handleSupplierChange,
      handleDaneaRefCommit,
      handleDateNeededChange,
      handleNotesCommit,
      statusMutation.isPending,
      destinationMutation.isPending,
      destinationSpecMutation.isPending,
      quantityMutation.isPending,
      supplierMutation.isPending,
      daneaRefMutation.isPending,
      dateNeededMutation.isPending,
      notesMutation.isPending,
      handleQuantityAdjust,
      excludedSet,
      transitionMap,
      canMapAtec,
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
        }}
        // v4: colonna Cod. ATEC + picker per codice ATEC.
        visibilityStorageKey="table-visibility-ddp-commerciale-v4"
        toolbarActions={
          <>
            <span className="self-center text-sm font-medium">
              Totale:{" "}
              <span className="text-lg font-bold tabular-nums ml-1 text-blue-600 dark:text-blue-400">
                {euro(totalValue)}
              </span>
              {excludedRows.length > 0 ? (
                <span className="ml-2 font-normal text-muted-foreground">
                  · escluse {excludedRows.length} ({euro(excludedValue)})
                </span>
              ) : null}
            </span>
            <Button size="sm" variant="outline" onClick={() => setAtecPickerOpen(true)}>
              <Link2 />
              Per codice ATEC
            </Button>
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
        transitions={transitionMap}
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

      <AtecPickerDialog
        open={atecPickerOpen}
        projectId={projectId}
        onClose={() => setAtecPickerOpen(false)}
        onAdded={() => void invalidate()}
      />

      <DdpAtecAlternativesDialog
        open={altsTarget !== null}
        projectId={projectId}
        row={altsTarget}
        onClose={() => setAltsTarget(null)}
        onApplied={() => void invalidate()}
      />

      <CatalogAtecAssignDialog
        item={null}
        bomTarget={
          assignTarget
            ? {
                bomItemId: assignTarget.id,
                partNumber: assignTarget.partNumber ?? "",
                description: assignTarget.description ?? "",
              }
            : null
        }
        onClose={() => setAssignTarget(null)}
        onSaved={() => {
          setAssignTarget(null)
          void invalidate()
        }}
      />

      <DaneaOrderDialog
        idDoc={daneaOrderIdDoc}
        onClose={() => setDaneaOrderIdDoc(null)}
      />
    </>
  )
}
