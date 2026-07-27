import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { type ColumnDef, type SortingState } from "@tanstack/react-table"
import { ArrowUpDown, Pencil, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { ActiveStatus } from "@/components/shared/status-dot"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { deleteSupplier, fetchSuppliers } from "@/lib/api/suppliers"
import type { SupplierListItem } from "@/lib/api/types"

import { SupplierDialog } from "./SupplierDialog"
import { dash } from "@/lib/format"

const COLUMN_LABELS: Record<string, string> = {
  companyName: "Ragione sociale",
  contactName: "Referente",
  email: "Email",
  phone: "Telefono",
  vatNumber: "P. IVA",
  fiscalCode: "Cod. fiscale",
  isActive: "Stato",
}

const DEFAULT_SORTING: SortingState = [{ id: "companyName", desc: false }]
const HIDDEN_COLUMNS = { fiscalCode: false }

function SortHeader({
  label,
  onClick,
}: {
  label: string
  onClick: () => void
}) {
  return (
    <Button variant="ghost" size="sm" className="-ml-2 h-8" onClick={onClick}>
      {label}
      <ArrowUpDown className="ml-1 size-3.5" />
    </Button>
  )
}

export function FornitoriPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [dialogSupplier, setDialogSupplier] = React.useState<
    number | "new" | null
  >(null)

  const query = useQuery({
    queryKey: ["suppliers"],
    queryFn: fetchSuppliers,
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["suppliers"] })

  const deleteMutation = useMutation({
    mutationFn: deleteSupplier,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (row: SupplierListItem) => {
      const ok = await confirm({
        title: "Disattiva fornitore",
        description: `Disattivare "${row.companyName}"? Resterà nello storico ma non sarà più attivo.`,
        confirmLabel: "Disattiva",
      })
      if (ok) {
        deleteMutation.mutate(row.id)
      }
    },
    [confirm, deleteMutation]
  )

  const columns = React.useMemo<ColumnDef<SupplierListItem>[]>(
    () => [
      {
        id: "select",
        enableHiding: false,
        enableSorting: false,
        header: ({ table }) => (
          <Checkbox
            checked={
              table.getIsAllPageRowsSelected() ||
              (table.getIsSomePageRowsSelected() && "indeterminate")
            }
            onCheckedChange={(value) =>
              table.toggleAllPageRowsSelected(!!value)
            }
            aria-label="Seleziona tutto"
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            checked={row.getIsSelected()}
            onCheckedChange={(value) => row.toggleSelected(!!value)}
            aria-label="Seleziona riga"
          />
        ),
      },
      {
        accessorKey: "companyName",
        enableHiding: false,
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.companyName}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => (
          <span className="font-medium">{row.original.companyName}</span>
        ),
      },
      {
        accessorKey: "contactName",
        header: COLUMN_LABELS.contactName,
        cell: ({ row }) => dash(row.original.contactName),
      },
      {
        accessorKey: "email",
        header: COLUMN_LABELS.email,
        cell: ({ row }) => dash(row.original.email),
      },
      {
        accessorKey: "phone",
        header: COLUMN_LABELS.phone,
        cell: ({ row }) => dash(row.original.phone),
      },
      {
        accessorKey: "vatNumber",
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.vatNumber}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => dash(row.original.vatNumber),
      },
      {
        accessorKey: "fiscalCode",
        header: COLUMN_LABELS.fiscalCode,
        cell: ({ row }) => dash(row.original.fiscalCode),
      },
      {
        accessorKey: "isActive",
        header: COLUMN_LABELS.isActive,
        enableColumnFilter: false,
        cell: ({ row }) => <ActiveStatus active={row.original.isActive} />,
      },
      {
        id: "actions",
        enableHiding: false,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <RowActionsMenu
              label={row.original.companyName}
              actions={[
                {
                  label: "Modifica",
                  icon: Pencil,
                  onClick: () => setDialogSupplier(row.original.id),
                },
                {
                  label: "Disattiva",
                  icon: Trash2,
                  destructive: true,
                  separatorBefore: true,
                  onClick: () => {
                    void handleDelete(row.original)
                  },
                },
              ]}
            />
          </div>
        ),
      },
    ],
    [handleDelete]
  )

  return (
    <>
      <DataTableCardFiltered
        title="Fornitori"
        description="Anagrafica fornitori"
        columns={columns}
        data={query.data}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => query.refetch()}
        searchPlaceholder="Cerca fornitore…"
        rowNoun="fornitori"
        emptyMessage="Nessun fornitore trovato."
        defaultSorting={DEFAULT_SORTING}
        initialColumnVisibility={HIDDEN_COLUMNS}
        getRowId={(row) => String(row.id)}
        onRowDoubleClick={(row) => setDialogSupplier(row.id)}
        toolbarActions={
          <Button size="sm" onClick={() => setDialogSupplier("new")}>
            <Plus />
            Aggiungi fornitore
          </Button>
        }
      />

      <SupplierDialog
        open={dialogSupplier !== null}
        supplierId={dialogSupplier}
        onClose={() => setDialogSupplier(null)}
        onSaved={async () => {
          setDialogSupplier(null)
          await invalidate()
        }}
      />
    </>
  )
}
