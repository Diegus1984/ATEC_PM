import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { type ColumnDef, type SortingState } from "@tanstack/react-table"
import { ArrowUpDown, ExternalLink, Pencil, Plus, Trash2 } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import {
  DataTableColumnSelectFilter,
  exactColumnFilterFn,
} from "@/components/shared/data-table-column-select-filter"
import "@/components/shared/data-table-column-meta"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { deleteMoM, fetchMoMList, updateMoM } from "@/lib/api/mom"
import { formatDateShort } from "@/lib/date-iso"
import { getSession } from "@/lib/auth/session"
import { useMoMHub } from "@/lib/signalr/use-mom-hub"
import type { MoMListItem, MoMSaveRequest } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { MoMVerbaleDialog } from "./MoMVerbaleDialog"
import { MoMSidebar, type MoMView } from "./MoMSidebar"

const COLUMN_LABELS: Record<string, string> = {
  title: "Titolo",
  tipo: "Tipo",
  projectCode: "Commessa",
  meetingDate: "Data riunione",
  rev: "Rev.",
  itemsCount: "Azioni",
  openCount: "Aperte",
  p1Count: "P1",
  p2Count: "P2",
  p3Count: "P3",
  period: "Periodo",
  inDashboard: "Dashboard",
}

const DEFAULT_SORTING: SortingState = [{ id: "meetingDate", desc: true }]

function formatDate(value: string | null): string {
  if (!value) return "—"
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? "—" : formatDateShort(date)
}

function periodLabel(item: MoMListItem): string {
  if (!item.periodStart && !item.periodEnd) return "—"
  return `${formatDate(item.periodStart)} → ${formatDate(item.periodEnd)}`
}

function displayTitle(item: MoMListItem, isInsideProjectView: boolean): string {
  if (isInsideProjectView) return item.title
  return item.tipo === "COMMESSA" && item.projectCode
    ? `${item.projectCode} — ${item.title}`
    : item.title
}

function itemToSaveRequest(item: MoMListItem): MoMSaveRequest {
  return {
    tipo: item.tipo,
    projectId: item.projectId,
    title: item.title,
    meetingDate: item.meetingDate ? item.meetingDate.slice(0, 10) : null,
    inDashboard: item.inDashboard,
  }
}

type ViewMode = "table" | "cards"

function viewModeStorageKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `mom.list.viewMode.${employeeId}`
}

function loadViewMode(): ViewMode {
  const stored = localStorage.getItem(viewModeStorageKey())
  return stored === "cards" ? "cards" : "table"
}

/** Badge tipo colorato come il prototipo v9: commessa=verde, riunione=arancio. */
function TipoBadge({ tipo }: { tipo: string }) {
  const commessa = tipo === "COMMESSA"
  return (
    <Badge
      variant="outline"
      className={cn(
        "text-[10px] font-bold",
        commessa
          ? "border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-300"
          : "border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-900 dark:bg-orange-950/40 dark:text-orange-300"
      )}
    >
      {tipo}
    </Badge>
  )
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

function PriorityPill({ value, variant }: { value: number; variant: "p1" | "p2" | "p3" }) {
  const styles = {
    p1: "bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300",
    p2: "bg-amber-100 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300",
    p3: "bg-muted text-muted-foreground",
  }
  return (
    <span
      className={cn(
        "inline-flex min-w-6 justify-center rounded-full px-1.5 py-0.5 text-xs font-semibold tabular-nums",
        styles[variant]
      )}
    >
      {value}
    </span>
  )
}

export function MoMPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [dialogVerbale, setDialogVerbale] = React.useState<number | "new" | null>(
    null
  )
  const [view, setView] = React.useState<MoMView>({ kind: "all" })
  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadViewMode)

  const setViewMode = React.useCallback((mode: ViewMode) => {
    setViewModeState(mode)
    localStorage.setItem(viewModeStorageKey(), mode)
  }, [])

  const query = useQuery({
    queryKey: ["mom-list"],
    queryFn: () => fetchMoMList(),
  })

  // Realtime: la lista si aggiorna quando un altro utente modifica i verbali.
  useMoMHub(
    true,
    React.useCallback(() => {
      void queryClient.invalidateQueries({ queryKey: ["mom-list"] })
    }, [queryClient])
  )

  const filteredData = React.useMemo(() => {
    const items = query.data ?? []
    if (view.kind === "all") return items
    if (view.kind === "open") return items.filter((item) => item.openCount > 0)
    if (view.kind === "p1") return items.filter((item) => item.p1Count > 0)
    if (view.kind === "dashboard") return items.filter((item) => item.inDashboard)
    if (view.kind === "project") {
      return items.filter((item) => item.projectCode === view.projectCode && item.tipo === "COMMESSA")
    }
    if (view.kind === "riunioni") return items.filter((item) => item.tipo !== "COMMESSA")
    return items
  }, [query.data, view])

  const tipoFilterOptions = React.useMemo(() => {
    const values = new Set<string>()
    for (const item of filteredData) {
      if (item.tipo) {
        values.add(item.tipo)
      }
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b, "it"))
  }, [filteredData])

  const commessaFilterOptions = React.useMemo(() => {
    const values = new Set<string>()
    for (const item of filteredData) {
      if (item.projectCode) {
        values.add(item.projectCode)
      }
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b, "it"))
  }, [filteredData])

  // Vista Card: verbali di commessa raggruppati per codice + gruppo «Riunioni».
  const groupedForCards = React.useMemo(() => {
    const byDate = (a: MoMListItem, b: MoMListItem) =>
      (b.meetingDate ?? "").localeCompare(a.meetingDate ?? "") || b.id - a.id
    const commesse = new Map<string, { label: string; items: MoMListItem[] }>()
    const riunioni: MoMListItem[] = []
    for (const item of filteredData) {
      if (item.tipo === "COMMESSA") {
        const key = item.projectCode || "Senza commessa"
        const display = item.projectTitle ? `${key} — ${item.projectTitle}` : key
        const existing = commesse.get(key)
        if (existing) {
          existing.items.push(item)
        } else {
          commesse.set(key, { label: display, items: [item] })
        }
      } else {
        riunioni.push(item)
      }
    }
    const groups = Array.from(commesse.values())
      .sort((a, b) => a.label.localeCompare(b.label, "it"))
      .map(g => ({ ...g, items: g.items.sort(byDate) }))

    if (riunioni.length > 0) {
      groups.push({ label: "Riunioni", items: riunioni.sort(byDate) })
    }
    return groups
  }, [filteredData])

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["mom-list"] })

  const deleteMutation = useMutation({
    mutationFn: deleteMoM,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const toggleDashboardMutation = useMutation({
    mutationFn: ({ item, checked }: { item: MoMListItem; checked: boolean }) =>
      updateMoM(item.id, { ...itemToSaveRequest(item), inDashboard: checked }),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDelete = React.useCallback(
    async (row: MoMListItem) => {
      const ok = await confirm({
        title: "Elimina verbale",
        description: `Eliminare il verbale "${row.title}" e tutte le sue azioni?`,
        confirmLabel: "Elimina",
      })
      if (ok) deleteMutation.mutate(row.id)
    },
    [confirm, deleteMutation]
  )

  const columns = React.useMemo<ColumnDef<MoMListItem>[]>(
    () => [
      {
        accessorKey: "title",
        enableHiding: false,
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.title}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => (
          <button
            type="button"
            className="max-w-xs truncate text-left font-medium hover:underline"
            onClick={() => navigate(`/mom/${row.original.id}`)}
          >
            {displayTitle(row.original, view.kind === "project")}
          </button>
        ),
        sortingFn: (a, b) =>
          displayTitle(a.original, view.kind === "project").localeCompare(
            displayTitle(b.original, view.kind === "project"),
            "it"
          ),
      },
      {
        accessorKey: "tipo",
        header: COLUMN_LABELS.tipo,
        filterFn: exactColumnFilterFn,
        meta: {
          filterInput: ({ value, onChange }) => (
            <DataTableColumnSelectFilter
              value={value}
              onChange={onChange}
              options={tipoFilterOptions}
            />
          ),
        },
        cell: ({ row }) => <TipoBadge tipo={row.original.tipo} />,
      },
      {
        accessorKey: "projectCode",
        header: COLUMN_LABELS.projectCode,
        filterFn: exactColumnFilterFn,
        meta: {
          filterInput: ({ value, onChange }) => (
            <DataTableColumnSelectFilter
              value={value}
              onChange={onChange}
              options={commessaFilterOptions}
            />
          ),
        },
        cell: ({ row }) =>
          row.original.projectTitle
            ? `${row.original.projectCode} — ${row.original.projectTitle}`
            : row.original.projectCode ?? "—",
      },
      {
        accessorKey: "meetingDate",
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.meetingDate}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => formatDate(row.original.meetingDate),
      },
      {
        accessorKey: "rev",
        header: COLUMN_LABELS.rev,
        enableColumnFilter: false,
        cell: ({ row }) => (
          <Badge
            variant={row.original.rev > 0 ? "default" : "outline"}
            className="text-[10px] font-bold tabular-nums"
            title="Revisione del verbale (cambi della data riunione)"
          >
            Rev. {row.original.rev}
          </Badge>
        ),
      },
      {
        accessorKey: "itemsCount",
        header: COLUMN_LABELS.itemsCount,
        cell: ({ row }) => (
          <span className="tabular-nums">{row.original.itemsCount}</span>
        ),
      },
      {
        accessorKey: "openCount",
        header: COLUMN_LABELS.openCount,
        cell: ({ row }) => (
          <span
            className={cn(
              "tabular-nums",
              row.original.openCount > 0 && "font-medium text-amber-700 dark:text-amber-400"
            )}
          >
            {row.original.openCount}
          </span>
        ),
      },
      {
        accessorKey: "p1Count",
        header: COLUMN_LABELS.p1Count,
        enableColumnFilter: false,
        cell: ({ row }) => (
          <PriorityPill value={row.original.p1Count} variant="p1" />
        ),
      },
      {
        accessorKey: "p2Count",
        header: COLUMN_LABELS.p2Count,
        enableColumnFilter: false,
        cell: ({ row }) => (
          <PriorityPill value={row.original.p2Count} variant="p2" />
        ),
      },
      {
        accessorKey: "p3Count",
        header: COLUMN_LABELS.p3Count,
        enableColumnFilter: false,
        cell: ({ row }) => (
          <PriorityPill value={row.original.p3Count} variant="p3" />
        ),
      },
      {
        id: "period",
        accessorFn: (row) => periodLabel(row),
        header: COLUMN_LABELS.period,
        cell: ({ row }) => (
          <span className="whitespace-nowrap text-sm">{periodLabel(row.original)}</span>
        ),
      },
      {
        accessorKey: "inDashboard",
        header: COLUMN_LABELS.inDashboard,
        enableColumnFilter: false,
        cell: ({ row }) => (
          <Checkbox
            checked={row.original.inDashboard}
            onCheckedChange={(checked) =>
              toggleDashboardMutation.mutate({
                item: row.original,
                checked: checked === true,
              })
            }
            aria-label="Dashboard commesse"
          />
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <RowActionsMenu
              label={row.original.title}
              actions={[
                {
                  label: "Apri dettaglio",
                  icon: ExternalLink,
                  onClick: () => navigate(`/mom/${row.original.id}`),
                },
                {
                  label: "Modifica",
                  icon: Pencil,
                  onClick: () => setDialogVerbale(row.original.id),
                },
                {
                  label: "Elimina",
                  icon: Trash2,
                  destructive: true,
                  separatorBefore: true,
                  onClick: () => void handleDelete(row.original),
                },
              ]}
            />
          </div>
        ),
      },
    ],
    [view.kind, commessaFilterOptions, handleDelete, navigate, tipoFilterOptions, toggleDashboardMutation]
  )

  const viewToggle = (
    <Select
      value={viewMode}
      onValueChange={(value) => setViewMode(value as ViewMode)}
    >
      <SelectTrigger size="sm" className="w-[130px]">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="table">Tabella</SelectItem>
        <SelectItem value="cards">Card</SelectItem>
      </SelectContent>
    </Select>
  )

  const newVerbaleButton = (
    <Button size="sm" onClick={() => setDialogVerbale("new")}>
      <Plus />
      Nuovo verbale
    </Button>
  )

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">Verbali (MoM)</h1>
        <p className="text-sm text-muted-foreground">
          Verbali di riunione e commessa con action item e scadenze.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <MoMSidebar
          view={view}
          onViewChange={setView}
          items={query.data ?? []}
        />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          {query.isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : query.isError ? (
            <PageErrorAlert message={(query.error as Error).message} />
          ) : (
            <>
              {viewMode === "table" ? (
                <DataTableCardFiltered
                  title="Verbali (MoM)"
                  description="Verbali di riunione e commessa con action item e scadenze."
                  columns={columns}
                  data={filteredData}
                  columnLabels={COLUMN_LABELS}
                  isLoading={query.isLoading}
                  isFetching={query.isFetching}
                  error={query.error as Error | null}
                  onRefresh={() => query.refetch()}
                  searchPlaceholder="Cerca verbali…"
                  rowNoun="verbali"
                  emptyMessage="Nessun verbale trovato."
                  defaultSorting={DEFAULT_SORTING}
                  getRowId={(row) => String(row.id)}
                  onRowDoubleClick={(row) => navigate(`/mom/${row.id}`)}
                  externalFiltersActive={view.kind !== "all"}
                  onClearExternalFilters={() => setView({ kind: "all" })}
                  toolbarActions={
                    <>
                      {viewToggle}
                      {newVerbaleButton}
                    </>
                  }
                />
              ) : (
                <Card>
                  <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
                    <div>
                      <CardTitle>Verbali (MoM)</CardTitle>
                      <CardDescription>
                        Verbali raggruppati per commessa (cartelle) + riunioni.
                      </CardDescription>
                    </div>
                    <div className="flex items-center gap-2">
                      {viewToggle}
                      {newVerbaleButton}
                    </div>
                  </CardHeader>
                  <div className="space-y-6 px-6 pb-6">
                    {groupedForCards.length === 0 ? (
                      <p className="text-sm text-muted-foreground">
                        Nessun verbale trovato.
                      </p>
                    ) : (
                      groupedForCards.map((group) => (
                        <div key={group.label} className="space-y-2">
                          <div className="flex items-baseline gap-2">
                            <span className="font-semibold">{group.label}</span>
                            <span className="text-sm text-muted-foreground">
                              {group.items.length}{" "}
                              {group.items.length === 1 ? "verbale" : "verbali"}
                            </span>
                          </div>
                          <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                            {group.items.map((item) => (
                              <MoMVerbaleCard
                                key={item.id}
                                item={item}
                                isInsideProjectView={view.kind === "project"}
                                onOpen={() => navigate(`/mom/${item.id}`)}
                                onEdit={() => setDialogVerbale(item.id)}
                                onDelete={() => void handleDelete(item)}
                                onToggleDashboard={(checked) =>
                                  toggleDashboardMutation.mutate({ item, checked })
                                }
                              />
                            ))}
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </Card>
              )}
            </>
          )}
        </main>
      </div>

      <MoMVerbaleDialog
        open={dialogVerbale !== null}
        verbaleId={dialogVerbale}
        onClose={() => setDialogVerbale(null)}
        defaultProjectCode={view.kind === "project" ? view.projectCode : null}
        defaultTipo={view.kind === "riunioni" ? "RIUNIONE" : null}
        onSaved={async (id) => {
          const wasNew = dialogVerbale === "new"
          setDialogVerbale(null)
          await invalidate()
          if (typeof id === "number" && wasNew) {
            navigate(`/mom/${id}`)
          }
        }}
      />
    </div>
  )
}

/** Card-verbale della vista Card (cartella v9: statistiche, priorità, revisione). */
function MoMVerbaleCard({
  item,
  isInsideProjectView,
  onOpen,
  onEdit,
  onDelete,
  onToggleDashboard,
}: {
  item: MoMListItem
  isInsideProjectView: boolean
  onOpen: () => void
  onEdit: () => void
  onDelete: () => void
  onToggleDashboard: (checked: boolean) => void
}) {
  return (
    <div className="flex flex-col rounded-xl border bg-card shadow-xs transition-colors hover:border-primary/40">
      <button
        type="button"
        className="flex-1 space-y-3 p-4 text-left"
        onClick={onOpen}
      >
        <div className="flex items-start justify-between gap-2">
          <span className="font-semibold leading-snug">
            {displayTitle(item, isInsideProjectView)}
          </span>
          <TipoBadge tipo={item.tipo} />
        </div>
        <div className="space-y-1.5 text-sm">
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Azioni</span>
            <span className="font-medium tabular-nums">
              {item.itemsCount}
              {item.openCount > 0 ? ` (${item.openCount} aperte)` : ""}
            </span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Ripartizione priorità</span>
            <span className="flex gap-1">
              <PriorityPill value={item.p1Count} variant="p1" />
              <PriorityPill value={item.p2Count} variant="p2" />
              <PriorityPill value={item.p3Count} variant="p3" />
            </span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Periodo attività</span>
            <span className="text-xs font-medium">{periodLabel(item)}</span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Data riunione</span>
            <span className="text-xs font-medium">
              {formatDate(item.meetingDate)}
            </span>
          </div>
          <div className="flex items-center justify-between gap-2">
            <span className="text-muted-foreground">Revisione</span>
            <Badge
              variant={item.rev > 0 ? "default" : "outline"}
              className="text-[10px] font-bold tabular-nums"
            >
              Rev. {item.rev}
            </Badge>
          </div>
        </div>
      </button>
      <div className="flex items-center justify-between gap-2 border-t px-4 py-2">
        <Label className="flex items-center gap-2 text-xs font-normal text-muted-foreground">
          <Checkbox
            checked={item.inDashboard}
            onCheckedChange={(checked) => onToggleDashboard(checked === true)}
            aria-label="Dashboard commesse"
          />
          dashboard commesse
        </Label>
        <RowActionsMenu
          label={item.title}
          actions={[
            { label: "Apri dettaglio", icon: ExternalLink, onClick: onOpen },
            { label: "Modifica", icon: Pencil, onClick: onEdit },
            {
              label: "Elimina",
              icon: Trash2,
              destructive: true,
              separatorBefore: true,
              onClick: onDelete,
            },
          ]}
        />
      </div>
    </div>
  )
}
