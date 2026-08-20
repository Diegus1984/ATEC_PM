import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import {
  Archive,
  ArchiveRestore,
  Copy,
  ListFilter,
  Paperclip,
  Plus,
  Trash2,
} from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { useCopyText } from "@/components/shared/copy-text"
import { DataTableCard } from "@/components/shared/data-table-card"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  archiveBugReport,
  deleteBugReport,
  fetchBugReports,
  unarchiveBugReport,
} from "@/lib/api/bug-reports"
import type { BugReport, BugStatus } from "@/lib/api/types"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { formatDateTimeShort } from "@/lib/date-iso"
import { useBugReportsHub } from "@/lib/signalr/use-bug-reports-hub"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { BugReportDialog } from "./BugReportDialog"
import {
  KIND_META,
  SEVERITY_META,
  STATUS_META,
  STATUS_ORDER,
  buildBugMarkdown,
} from "./bug-report-utils"

const ALL = "__all__"

/** Chiave localStorage del filtro stati (come le colonne): versione perché il default
 *  nasconde Risolte/Non accolte — senza v1 chi aveva già «Tutti» resterebbe intasato. */
const STATUS_FILTER_KEY = "bug-reports-status-filter-v1"

/** Di partenza solo le aperte e in lavorazione: le risolte intasano l'elenco. */
const DEFAULT_STATUS_VISIBILITY: Record<BugStatus, boolean> = {
  OPEN: true,
  IN_PROGRESS: true,
  RESOLVED: false,
  REJECTED: false,
}

function loadStatusVisibility(): Record<BugStatus, boolean> {
  try {
    const raw = localStorage.getItem(STATUS_FILTER_KEY)
    if (!raw) return { ...DEFAULT_STATUS_VISIBILITY }
    const parsed = JSON.parse(raw) as Partial<Record<BugStatus, boolean>>
    return {
      OPEN: parsed.OPEN ?? DEFAULT_STATUS_VISIBILITY.OPEN,
      IN_PROGRESS: parsed.IN_PROGRESS ?? DEFAULT_STATUS_VISIBILITY.IN_PROGRESS,
      RESOLVED: parsed.RESOLVED ?? DEFAULT_STATUS_VISIBILITY.RESOLVED,
      REJECTED: parsed.REJECTED ?? DEFAULT_STATUS_VISIBILITY.REJECTED,
    }
  } catch {
    return { ...DEFAULT_STATUS_VISIBILITY }
  }
}

const COLUMN_LABELS: Record<string, string> = {
  id: "ID",
  kind: "Tipo",
  title: "Titolo",
  description: "Descrizione",
  area: "Dove",
  severity: "Gravità",
  status: "Stato",
  fixedInBuild: "Risolto in",
  attachments: "Allegati",
  createdByName: "Segnalata da",
  createdAt: "Aperta il",
  actions: "Azioni",
}

/**
 * Segnalazioni su ATEC PM: ognuno vede e modifica le proprie; l'elenco completo (con relativo
 * filtro) resta a chi gestisce o ha `data.bug_reports_all` (#93). Realtime sul gruppo `bugs-all`.
 */
export function BugReportsPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const copiaTesto = useCopyText()
  const canManage = canWriteFeature("action.manage_bug_reports")
  const canWrite = canWriteFeature("nav.bug_reports")
  const canSeeAll = canAccessFeature("data.bug_reports_all") || canManage

  const [viewArchived, setViewArchived] = React.useState(false)

  const bugsQuery = useQuery({
    queryKey: ["bug-reports", { archived: viewArchived }],
    queryFn: () => fetchBugReports({ archived: viewArchived }),
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["bug-reports"] })
  }, [queryClient])

  useBugReportsHub(true, invalidate)

  const [statusVisibility, setStatusVisibility] = React.useState<
    Record<BugStatus, boolean>
  >(loadStatusVisibility)
  const [ownerFilter, setOwnerFilter] = React.useState<string>(ALL)
  const [dialogOpen, setDialogOpen] = React.useState(false)
  const [selected, setSelected] = React.useState<BugReport | null>(null)

  React.useEffect(() => {
    try {
      localStorage.setItem(STATUS_FILTER_KEY, JSON.stringify(statusVisibility))
    } catch {
      /* localStorage pieno o bloccato */
    }
  }, [statusVisibility])

  const all = React.useMemo(() => bugsQuery.data ?? [], [bugsQuery.data])

  const openBug = React.useMemo(
    () => (selected ? (all.find((b) => b.id === selected.id) ?? selected) : null),
    [all, selected]
  )

  const rows = React.useMemo(() => {
    return all.filter((bug) => {
      // In vista archivio mostriamo tutti gli stati (sono già tutte chiuse/archiviate)
      if (!viewArchived && !statusVisibility[bug.status]) return false
      if (ownerFilter === "mine" && !bug.isMine) return false
      return true
    })
  }, [all, viewArchived, statusVisibility, ownerFilter])

  const statusMenuColumns = React.useMemo(
    () =>
      STATUS_ORDER.map((status) => ({
        id: status,
        label: STATUS_META[status].label,
        checked: statusVisibility[status],
        onToggle: (checked: boolean) =>
          setStatusVisibility((prev) => ({ ...prev, [status]: checked })),
      })),
    [statusVisibility]
  )

  const statusTriggerLabel = React.useMemo(() => {
    const active = STATUS_ORDER.filter((s) => statusVisibility[s])
    if (active.length === STATUS_ORDER.length) return "Tutti gli stati"
    if (active.length === 0) return "Nessuno stato"
    if (active.length === 1) return STATUS_META[active[0]].label
    return `Stati (${active.length})`
  }, [statusVisibility])

  const remove = React.useCallback(
    async (bug: BugReport) => {
      const ok = await confirm({
        title: "Eliminare la segnalazione?",
        description: `«${bug.title}» e i suoi allegati verranno eliminati.`,
        confirmLabel: "Elimina",
      })
      if (!ok) return
      try {
        await deleteBugReport(bug.id)
        notifySuccess("Segnalazione eliminata")
        invalidate()
      } catch (err) {
        notifyError(err)
      }
    },
    [confirm, invalidate]
  )

  const handleArchive = React.useCallback(
    async (bug: BugReport) => {
      const ok = await confirm({
        title: "Archiviare la segnalazione?",
        description: `«${bug.title}» verrà spostata nell'archivio.`,
        confirmLabel: "Archivia",
      })
      if (!ok) return
      try {
        await archiveBugReport(bug.id)
        notifySuccess("Segnalazione archiviata")
        invalidate()
      } catch (err) {
        notifyError(err)
      }
    },
    [confirm, invalidate]
  )

  const handleUnarchive = React.useCallback(
    async (bug: BugReport) => {
      try {
        await unarchiveBugReport(bug.id)
        notifySuccess("Segnalazione ripristinata")
        invalidate()
      } catch (err) {
        notifyError(err)
      }
    },
    [invalidate]
  )

  const handleCopyAnalysis = React.useCallback(
    async (bug: BugReport) => {
      await copiaTesto(buildBugMarkdown(bug), `Blocco BUG-${String(bug.id).padStart(3, "0")}`)
    },
    [copiaTesto]
  )

  const columns = React.useMemo<ColumnDef<BugReport>[]>(
    () => [
      {
        accessorKey: "id",
        header: "ID",
        cell: ({ row }) => (
          <span className="tabular-nums text-muted-foreground">#{row.original.id}</span>
        ),
      },
      {
        accessorKey: "kind",
        header: "Tipo",
        cell: ({ row }) => {
          const meta = KIND_META[row.original.kind]
          return (
            <Badge variant="outline" className={cn("font-medium", meta.className)}>
              {meta.label}
            </Badge>
          )
        },
      },
      {
        accessorKey: "title",
        header: "Titolo",
        cell: ({ row }) => (
          <div className="max-w-[420px] whitespace-normal break-words font-medium">
            {row.original.title}
          </div>
        ),
      },
      {
        accessorKey: "description",
        header: "Descrizione",
        cell: ({ row }) => (
          <div className="w-[36ch] whitespace-pre-wrap break-words text-sm text-muted-foreground">
            {row.original.description || "—"}
          </div>
        ),
      },
      {
        accessorKey: "area",
        header: "Dove",
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.area || "—"}
          </span>
        ),
      },
      {
        accessorKey: "severity",
        header: "Gravità",
        cell: ({ row }) => {
          const meta = SEVERITY_META[row.original.severity]
          return (
            <Badge variant="outline" className={cn(meta.className)}>
              {meta.label}
            </Badge>
          )
        },
      },
      {
        accessorKey: "status",
        header: "Stato",
        cell: ({ row }) => {
          const meta = STATUS_META[row.original.status]
          return (
            <Badge variant="outline" className={cn("font-medium", meta.className)}>
              {meta.label}
            </Badge>
          )
        },
      },
      {
        accessorKey: "fixedInBuild",
        header: "Risolto in",
        cell: ({ row }) =>
          row.original.fixedInBuild ? (
            <span className="font-mono text-xs text-muted-foreground">
              {row.original.fixedInBuild}
            </span>
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        id: "attachments",
        header: "Allegati",
        enableSorting: false,
        cell: ({ row }) =>
          row.original.attachments.length > 0 ? (
            <span className="inline-flex items-center gap-1 text-sm text-muted-foreground">
              <Paperclip className="size-3.5" />
              {row.original.attachments.length}
            </span>
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        accessorKey: "createdByName",
        header: "Segnalata da",
        cell: ({ row }) => (
          <span className="text-sm">
            {row.original.createdByName}
            {row.original.isMine ? (
              <span className="ml-1 text-xs text-primary">(tu)</span>
            ) : null}
          </span>
        ),
      },
      {
        accessorKey: "createdAt",
        header: "Aperta il",
        cell: ({ row }) => (
          <span className="whitespace-nowrap text-sm tabular-nums">
            {formatDateTimeShort(row.original.createdAt)}
          </span>
        ),
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        cell: ({ row }) => {
          const bug = row.original
          const canDelete = canWrite && (bug.isMine || canManage)
          const canArchive = canManage && !bug.archivedAt && (bug.status === "RESOLVED" || bug.status === "REJECTED")
          const canUnarchive = canManage && Boolean(bug.archivedAt)

          return (
            <div className="flex justify-end">
              <RowActionsMenu
                size="icon-sm"
                triggerClassName="size-8"
                actions={[
                  {
                    label: canWrite && (bug.isMine || canManage) ? "Apri / modifica" : "Apri",
                    onClick: () => {
                      setSelected(bug)
                      setDialogOpen(true)
                    },
                  },
                  {
                    label: "Copia per analisi (Markdown)",
                    icon: Copy,
                    onClick: () => void handleCopyAnalysis(bug),
                  },
                  ...(canArchive
                    ? [
                        {
                          label: "Archivia",
                          icon: Archive,
                          onClick: () => void handleArchive(bug),
                        },
                      ]
                    : []),
                  ...(canUnarchive
                    ? [
                        {
                          label: "Ripristina dall'archivio",
                          icon: ArchiveRestore,
                          onClick: () => void handleUnarchive(bug),
                        },
                      ]
                    : []),
                  ...(canDelete
                    ? [
                        {
                          label: "Elimina",
                          icon: Trash2,
                          destructive: true,
                          onClick: () => void remove(bug),
                        },
                      ]
                    : []),
                ]}
              />
            </div>
          )
        },
      },
    ],
    [canManage, canWrite, handleArchive, handleCopyAnalysis, handleUnarchive, remove]
  )

  return (
    <div className="space-y-4">
      <DataTableCard
        title={viewArchived ? "Segnalazioni (Archivio)" : "Segnalazioni"}
        description={
          viewArchived
            ? "Segnalazioni risolte o archiviate."
            : canSeeAll
            ? "Bug e richieste di miglioramento su ATEC PM. L'elenco è condiviso: prima di aprirne una controlla che non ci sia già."
            : "Le tue segnalazioni di bug e richieste di miglioramento su ATEC PM."
        }
        columns={columns}
        columnLabels={COLUMN_LABELS}
        data={rows}
        isLoading={bugsQuery.isLoading}
        isFetching={bugsQuery.isFetching}
        error={bugsQuery.error as Error | null}
        onRefresh={() => void bugsQuery.refetch()}
        searchPlaceholder="Cerca per titolo, descrizione, sezione, autore…"
        rowNoun="segnalazioni"
        emptyMessage={viewArchived ? "Nessuna segnalazione archiviata." : "Nessuna segnalazione."}
        visibilityStorageKey="bug-reports-columns-v4"
        getRowId={(row) => String(row.id)}
        onRowDoubleClick={(row) => {
          setSelected(row)
          setDialogOpen(true)
        }}
        rowClassName={(row) =>
          row.status === "RESOLVED" || row.status === "REJECTED"
            ? "opacity-60"
            : undefined
        }
        toolbarActions={
          <>
            {!viewArchived && (
              <ColumnsMenu
                triggerLabel={statusTriggerLabel}
                menuLabel="Mostra stati"
                icon={ListFilter}
                align="start"
                columns={statusMenuColumns}
              />
            )}
            {canSeeAll && (
              <Select value={ownerFilter} onValueChange={setOwnerFilter}>
                <SelectTrigger size="sm" className="w-[140px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>Di tutti</SelectItem>
                  <SelectItem value="mine">Solo le mie</SelectItem>
                </SelectContent>
              </Select>
            )}
            {canManage && (
              <Button
                variant={viewArchived ? "secondary" : "outline"}
                size="sm"
                className="gap-1.5"
                onClick={() => setViewArchived((prev) => !prev)}
              >
                <Archive className="size-3.5" />
                {viewArchived ? "Vedi attive" : "Archivio"}
              </Button>
            )}
            {canWrite && !viewArchived && (
              <Button
                size="sm"
                onClick={() => {
                  setSelected(null)
                  setDialogOpen(true)
                }}
              >
                <Plus />
                Nuova segnalazione
              </Button>
            )}
          </>
        }
      />

      <BugReportDialog
        open={dialogOpen}
        bug={openBug}
        isAdmin={canManage}
        canWrite={canWrite}
        onClose={() => {
          setDialogOpen(false)
          setSelected(null)
        }}
        onSaved={invalidate}
      />
    </div>
  )
}
