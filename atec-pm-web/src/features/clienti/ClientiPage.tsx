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
import { deleteCustomer, fetchCustomers } from "@/lib/api/customers"
import type { CustomerListItem } from "@/lib/api/types"

import { CustomerDialog } from "./CustomerDialog"

const COLUMN_LABELS: Record<string, string> = {
  companyName: "Ragione sociale",
  contactName: "Referente",
  email: "Email",
  phone: "Telefono",
  cell: "Cellulare",
  vatNumber: "P. IVA",
  fiscalCode: "Cod. fiscale",
  sdiCode: "Cod. SDI",
  address: "Indirizzo",
  paymentTerms: "Pagamento",
  isActive: "Stato",
}

const DEFAULT_SORTING: SortingState = [{ id: "companyName", desc: false }]
const HIDDEN_COLUMNS = {
  cell: false,
  fiscalCode: false,
  sdiCode: false,
  address: false,
  paymentTerms: false,
}

function dash(value: string) {
  return value && value.trim() ? value : "—"
}

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

export function ClientiPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [dialogCustomer, setDialogCustomer] = React.useState<
    number | "new" | null
  >(null)

  const query = useQuery({
    queryKey: ["customers"],
    queryFn: fetchCustomers,
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["customers"] })

  const deleteMutation = useMutation({
    mutationFn: deleteCustomer,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (row: CustomerListItem) => {
      const ok = await confirm({
        title: "Disattiva cliente",
        description: `Disattivare "${row.companyName}"? Resterà nello storico ma non sarà più attivo.`,
        confirmLabel: "Disattiva",
      })
      if (ok) {
        deleteMutation.mutate(row.id)
      }
    },
    [confirm, deleteMutation]
  )

  const columns = React.useMemo<ColumnDef<CustomerListItem>[]>(
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
        accessorKey: "cell",
        header: COLUMN_LABELS.cell,
        cell: ({ row }) => dash(row.original.cell),
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
        accessorKey: "sdiCode",
        header: COLUMN_LABELS.sdiCode,
        cell: ({ row }) => dash(row.original.sdiCode),
      },
      {
        accessorKey: "address",
        header: COLUMN_LABELS.address,
        cell: ({ row }) => dash(row.original.address),
      },
      {
        accessorKey: "paymentTerms",
        header: COLUMN_LABELS.paymentTerms,
        cell: ({ row }) => dash(row.original.paymentTerms),
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
                  onClick: () => setDialogCustomer(row.original.id),
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
        title="Clienti"
        description="Anagrafica clienti"
        columns={columns}
        data={query.data}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => query.refetch()}
        searchPlaceholder="Cerca cliente…"
        rowNoun="clienti"
        emptyMessage="Nessun cliente trovato."
        defaultSorting={DEFAULT_SORTING}
        initialColumnVisibility={HIDDEN_COLUMNS}
        getRowId={(row) => String(row.id)}
        onRowDoubleClick={(row) => setDialogCustomer(row.id)}
        toolbarActions={
          <Button size="sm" onClick={() => setDialogCustomer("new")}>
            <Plus />
            Aggiungi cliente
          </Button>
        }
      />

      <CustomerDialog
        open={dialogCustomer !== null}
        customerId={dialogCustomer}
        onClose={() => setDialogCustomer(null)}
        onSaved={async () => {
          setDialogCustomer(null)
          await invalidate()
        }}
      />
    </>
  )
}
