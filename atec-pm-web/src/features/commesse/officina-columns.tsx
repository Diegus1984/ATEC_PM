// ── Colonne della griglia DDP Officina ─────────────────────────────────────
// Funzione pura: nessun hook qui dentro (le colonne vanno costruite in un
// useMemo del chiamante, altrimenti le celle si rimontano a ogni render).

import type { ColumnDef } from "@tanstack/react-table"
import { Ban, ChevronDown, ChevronRight, Pencil, Trash2 } from "lucide-react"

import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import type {
  DdpDestinationItem,
  DdpStatusItem,
  DdpTreatmentItem,
  OfficinaItem,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

import {
  DdpDestinationCell,
  DdpDestinationSpecCell,
} from "./DdpDestinationCell"
import { DdpInlineDateCell } from "./DdpInlineDateCell"
import { DdpInlineTextCell } from "./DdpInlineTextCell"
import { DdpQuantityStepper } from "./DdpQuantityStepper"
import { DdpStatusMenu } from "./DdpStatusMenu"
import { DdpSupplierCell } from "./DdpSupplierCell"
import { DdpTreatmentCell } from "./DdpTreatmentCell"
import { DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { DDP_STATUS_TO_ORDER } from "./ddp-constants"
import { OfficinaProducedCell } from "./OfficinaProducedCell"
import type { OfficinaRowMutations } from "./use-officina-row-mutations"
import { formatDateOrDash, toDateOnly } from "@/lib/date-iso"

export function buildOfficinaColumns({
  statuses,
  statusMap,
  transitionMap,
  destinations,
  treatments,
  parentIdsWithChildren,
  collapsedParentIds,
  toggleParentCollapse,
  mutations,
  canHardDelete,
  onEdit,
  onAnnul,
  onDelete,
  onQuantityAdjust,
}: {
  statuses: DdpStatusItem[]
  statusMap: Map<string, DdpStatusItem>
  transitionMap: Record<string, string[]>
  destinations: DdpDestinationItem[]
  treatments: DdpTreatmentItem[]
  parentIdsWithChildren: Set<number>
  collapsedParentIds: Set<number>
  toggleParentCollapse: (parentId: number) => void
  mutations: OfficinaRowMutations
  canHardDelete: boolean
  onEdit: (item: OfficinaItem) => void
  onAnnul: (item: OfficinaItem) => void
  onDelete: (item: OfficinaItem) => void
  onQuantityAdjust: (item: OfficinaItem, delta: 1 | -1) => void
}): ColumnDef<OfficinaItem>[] {
  const { pending } = mutations

  return [
    {
      accessorKey: "rowNumber",
      header: "#",
      enableColumnFilter: false,
      cell: ({ row }) => {
        const isChild = row.original.parentOfficinaItemId != null
        return (
          <span
            className={cn(
              "font-medium",
              isChild ? "pl-3 italic font-normal" : "opacity-80 tabular-nums"
            )}
          >
            {(row.original as OfficinaItem & { rowNumber?: string }).rowNumber}
          </span>
        )
      },
    },
    {
      accessorKey: "createdAt",
      header: "Data",
      enableColumnFilter: false,
      cell: ({ row }) => (
        <span className="whitespace-nowrap">{formatDateOrDash(row.original.createdAt)}</span>
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
      cell: ({ row }) => {
        const item = row.original
        const isChild = item.parentOfficinaItemId != null
        const hasChildren = parentIdsWithChildren.has(item.id)
        const isCollapsed = collapsedParentIds.has(item.id)

        return (
          <span className="flex items-center gap-1 font-medium">
            {isChild ? (
              <span
                className="mr-1 select-none"
                title={`Componente di composizione (${item.compositionQty ?? 1} per padre): segue la quantità del padre`}
              >
                ↳
              </span>
            ) : hasChildren ? (
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation()
                  toggleParentCollapse(item.id)
                }}
                className="mr-1 inline-flex size-5 items-center justify-center rounded hover:bg-muted"
                title={isCollapsed ? "Espandi componenti" : "Collassa componenti"}
              >
                {isCollapsed ? (
                  <ChevronRight className="size-4" strokeWidth={2.5} />
                ) : (
                  <ChevronDown className="size-4" strokeWidth={2.5} />
                )}
              </button>
            ) : null}
            {item.partNumber || "—"}
          </span>
        )
      },
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
        const canEditQty = item.itemStatus === DDP_STATUS_TO_ORDER
        const isChild = item.parentOfficinaItemId != null
        const atMin =
          item.quantity <= 1 && item.itemStatus === DDP_STATUS_CANCELLED

        let tooltipText = undefined
        if (isChild) {
          tooltipText =
            "Componente di composizione: la quantità segue quella del padre"
        } else if (!canEditQty) {
          tooltipText = "La quantità è modificabile solo in stato Da Ordinare"
        }

        return (
          <span title={tooltipText}>
            <DdpQuantityStepper
              quantity={item.quantity}
              disabled={pending.quantity || !canEditQty || isChild}
              decrementDisabled={atMin}
              onIncrement={() => onQuantityAdjust(item, 1)}
              onDecrement={() => onQuantityAdjust(item, -1)}
            />
          </span>
        )
      },
    },
    {
      accessorKey: "quantityProduced",
      header: "Prodotti",
      enableColumnFilter: false,
      cell: ({ row }) => {
        const item = row.original
        const annulled = item.itemStatus === DDP_STATUS_CANCELLED
        return (
          <OfficinaProducedCell
            quantity={item.quantity}
            quantityProduced={item.quantityProduced ?? 0}
            disabled={pending.produced || annulled}
            onCommit={(quantityProduced) =>
              mutations.changeProduced(item, quantityProduced)
            }
          />
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
          {euro((row.original as OfficinaItem & { totalCost: number }).totalCost)}
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
      cell: ({ row }) => (
        <DdpTreatmentCell
          treatment={row.original.treatment}
          treatments={treatments}
          disabled={pending.treatment}
          onTreatmentChange={(treatment) =>
            mutations.changeTreatment(row.original, treatment)
          }
        />
      ),
    },
    {
      accessorKey: "supplierName",
      header: "Fornitore",
      cell: ({ row }) => (
        <DdpSupplierCell
          supplierId={row.original.supplierId ?? null}
          supplierName={row.original.supplierName || ""}
          disabled={pending.supplier}
          onSupplierChange={(supplier) =>
            mutations.changeSupplier(row.original, supplier)
          }
        />
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
              transitions={transitionMap}
              disabled={pending.status}
              onSelect={(statusKey) =>
                mutations.changeStatus(row.original, statusKey)
              }
            />
          </div>
        )
      },
    },
    {
      accessorKey: "daneaRef",
      header: "N° Ordine",
      cell: ({ row }) => (
        <DdpInlineTextCell
          value={row.original.daneaRef ?? ""}
          disabled={pending.daneaRef}
          placeholder="—"
          onCommit={(value) => mutations.commitDaneaRef(row.original, value)}
        />
      ),
    },
    {
      accessorKey: "dateNeeded",
      header: "Necessario",
      enableColumnFilter: false,
      cell: ({ row }) => (
        <DdpInlineDateCell
          value={toDateOnly(row.original.dateNeeded)}
          disabled={pending.dateNeeded}
          onChange={(value) => mutations.changeDateNeeded(row.original, value)}
        />
      ),
    },
    {
      accessorKey: "orderDate",
      header: "Data ordine",
      enableColumnFilter: false,
      cell: ({ row }) => (
        <DdpInlineDateCell
          value={toDateOnly(row.original.orderDate)}
          disabled={pending.orderDate}
          onChange={(value) => mutations.changeOrderDate(row.original, value)}
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
          disabled={pending.destination}
          onDestinationChange={(destination) =>
            mutations.changeDestination(row.original, destination)
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
          disabled={pending.destinationSpec}
          onSpecCommit={(destinationSpec) =>
            mutations.commitDestinationSpec(row.original, destinationSpec)
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
          disabled={pending.notes}
          placeholder="—"
          onCommit={(value) => mutations.commitNotes(row.original, value)}
        />
      ),
    },
    {
      id: "actions",
      header: "",
      enableHiding: false,
      enableColumnFilter: false,
      cell: ({ row }) => {
        const item = row.original
        const isChild = item.parentOfficinaItemId != null
        const actions: RowAction[] = [
          {
            label: "Modifica",
            icon: Pencil,
            onClick: () => onEdit(item),
          },
        ]
        if (item.itemStatus !== DDP_STATUS_CANCELLED) {
          actions.push({
            label: "Annulla riga",
            icon: Ban,
            destructive: true,
            separatorBefore: true,
            disabled: isChild,
            onClick: () => onAnnul(item),
          })
        }
        if (canHardDelete) {
          actions.push({
            label: "Elimina definitivamente",
            icon: Trash2,
            destructive: true,
            separatorBefore: item.itemStatus === DDP_STATUS_CANCELLED,
            disabled: isChild,
            onClick: () => onDelete(item),
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
  ]
}
