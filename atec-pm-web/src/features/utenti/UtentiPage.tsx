import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { type ColumnDef } from "@tanstack/react-table"
import { KeyRound, Pencil, Plus, RotateCcw, UserX } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { StatusDot } from "@/components/shared/status-dot"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { resetUserPassword, fetchUsers, setUserStatus } from "@/lib/api/users"
import { getSession } from "@/lib/auth/session"
import type { UserListItem } from "@/lib/api/types"

import { EmployeeDialog } from "./EmployeeDialog"
import { ResetCredentialsDialog } from "./ResetCredentialsDialog"

const COLUMN_LABELS: Record<string, string> = {
  fullName: "Dipendente",
  userRole: "Ruolo",
  username: "Username",
  status: "Stato",
  departments: "Reparti",
}

const STATUS_META: Record<string, { label: string; dot: string }> = {
  ACTIVE: { label: "Attivo", dot: "bg-emerald-500" },
  ON_LEAVE: { label: "In ferie", dot: "bg-amber-500" },
  SICK: { label: "Malattia", dot: "bg-red-500" },
  TERMINATED: { label: "Cessato", dot: "bg-zinc-400" },
}

const ROLE_LABEL: Record<string, string> = {
  TECH: "TECH",
  RESP_REPARTO: "RESP",
  PM: "PM",
  ADMIN: "ADMIN",
}

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return "?"
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
}

export function UtentiPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const currentEmployeeId = getSession()?.user.employeeId ?? -1

  const [includeTerminated, setIncludeTerminated] = React.useState(false)
  const [dialogUser, setDialogUser] = React.useState<number | "new" | null>(null)
  const [resetResult, setResetResult] = React.useState<string | null>(null)

  const query = useQuery({
    queryKey: ["users", includeTerminated],
    queryFn: () => fetchUsers(includeTerminated),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["users"] })

  const resetMutation = useMutation({
    mutationFn: resetUserPassword,
    onSuccess: (login) => {
      setResetResult(login)
      void invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const statusMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) =>
      setUserStatus(id, isActive),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleReset = React.useCallback(
    async (row: UserListItem) => {
      const ok = await confirm({
        title: "Reset credenziali",
        description: `Reimpostare le credenziali di ${row.fullName}? Username e password torneranno alla forma iniziale (iniziale.cognome).`,
        confirmLabel: "Reimposta",
        destructive: false,
      })
      if (ok) {
        resetMutation.mutate(row.id)
      }
    },
    [confirm, resetMutation]
  )

  const handleTerminate = React.useCallback(
    async (row: UserListItem) => {
      const ok = await confirm({
        title: "Cessa utente",
        description: `Cessare ${row.fullName}? L'utente verrà disattivato (reversibile con «Mostra cessati» → «Riattiva»).`,
        confirmLabel: "Cessa",
      })
      if (ok) {
        statusMutation.mutate({ id: row.id, isActive: false })
      }
    },
    [confirm, statusMutation]
  )

  const handleReactivate = React.useCallback(
    async (row: UserListItem) => {
      const ok = await confirm({
        title: "Riattiva utente",
        description: `Riattivare ${row.fullName}? Tornerà attivo e potrà accedere di nuovo.`,
        confirmLabel: "Riattiva",
        destructive: false,
      })
      if (ok) {
        statusMutation.mutate({ id: row.id, isActive: true })
      }
    },
    [confirm, statusMutation]
  )

  const columns = React.useMemo<ColumnDef<UserListItem>[]>(
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
        accessorKey: "fullName",
        enableHiding: false,
        header: COLUMN_LABELS.fullName,
        cell: ({ row }) => (
          <div className="flex items-center gap-2">
            <Avatar className="size-7">
              <AvatarFallback className="text-xs">
                {initials(row.original.fullName)}
              </AvatarFallback>
            </Avatar>
            <span className="font-medium">{row.original.fullName}</span>
          </div>
        ),
      },
      {
        accessorKey: "userRole",
        header: COLUMN_LABELS.userRole,
        cell: ({ row }) => (
          <Badge variant="secondary">
            {ROLE_LABEL[row.original.userRole] ?? row.original.userRole}
          </Badge>
        ),
      },
      {
        accessorKey: "username",
        header: COLUMN_LABELS.username,
        cell: ({ row }) =>
          row.original.hasCredentials ? (
            row.original.username
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        accessorKey: "status",
        header: COLUMN_LABELS.status,
        enableColumnFilter: false,
        cell: ({ row }) => {
          const meta = STATUS_META[row.original.status] ?? {
            label: row.original.status,
            dot: "bg-zinc-400",
          }
          return <StatusDot color={meta.dot}>{meta.label}</StatusDot>
        },
      },
      {
        id: "departments",
        accessorFn: (row) => row.departmentCodes.join(", "),
        header: COLUMN_LABELS.departments,
        cell: ({ row }) =>
          row.original.departmentCodes.length > 0 ? (
            <span className="text-muted-foreground">
              {row.original.departmentCodes.join(", ")}
            </span>
          ) : (
            "—"
          ),
      },
      {
        id: "actions",
        enableHiding: false,
        enableSorting: false,
        cell: ({ row }) => {
          const terminated = row.original.status === "TERMINATED"
          return (
            <div className="flex justify-end">
              <RowActionsMenu
                label={row.original.fullName}
                actions={[
                  {
                    label: "Modifica",
                    icon: Pencil,
                    onClick: () => setDialogUser(row.original.id),
                  },
                  {
                    label: "Reset password",
                    icon: KeyRound,
                    onClick: () => {
                      void handleReset(row.original)
                    },
                  },
                  terminated
                    ? {
                        label: "Riattiva",
                        icon: RotateCcw,
                        separatorBefore: true,
                        onClick: () => {
                          void handleReactivate(row.original)
                        },
                      }
                    : {
                        label: "Cessa",
                        icon: UserX,
                        destructive: true,
                        separatorBefore: true,
                        disabled: row.original.id === currentEmployeeId,
                        onClick: () => {
                          void handleTerminate(row.original)
                        },
                      },
                ]}
              />
            </div>
          )
        },
      },
    ],
    [handleReset, handleTerminate, handleReactivate, currentEmployeeId]
  )

  return (
    <>
      <DataTableCardFiltered
        title="Utenti"
        description="Dipendenti, credenziali, reparti e competenze"
        columns={columns}
        data={query.data}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => query.refetch()}
        searchPlaceholder="Cerca per nome, username, ruolo, reparto…"
        rowNoun="utenti"
        emptyMessage="Nessun utente trovato."
        getRowId={(row) => String(row.id)}
        onRowDoubleClick={(row) => setDialogUser(row.id)}
        toolbarActions={
          <>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIncludeTerminated((value) => !value)}
            >
              {includeTerminated ? "Nascondi cessati" : "Mostra cessati"}
            </Button>
            <Button size="sm" onClick={() => setDialogUser("new")}>
              <Plus />
              Nuovo utente
            </Button>
          </>
        }
      />

      <EmployeeDialog
        open={dialogUser !== null}
        userId={dialogUser}
        onClose={() => setDialogUser(null)}
        onSaved={async () => {
          setDialogUser(null)
          await invalidate()
        }}
      />

      <ResetCredentialsDialog
        login={resetResult}
        onClose={() => setResetResult(null)}
      />
    </>
  )
}
