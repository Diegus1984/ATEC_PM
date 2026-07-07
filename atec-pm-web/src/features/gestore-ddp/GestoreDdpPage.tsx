import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { type ColumnDef, type SortingState } from "@tanstack/react-table"
import { ArrowUpDown, ClipboardList, ExternalLink, Warehouse } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import {
  DataTableColumnSelectFilter,
  exactColumnFilterFn,
} from "@/components/shared/data-table-column-select-filter"
import "@/components/shared/data-table-column-meta"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { getSession } from "@/lib/auth/session"
import { fetchDdpSummary } from "@/lib/api/ddp-manager"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import type { DdpProjectSummary, DdpStatusItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { cn } from "@/lib/utils"

import { DdpPieWithLegend } from "./DdpPieWithLegend"
import { buildRipartizioneBars, type BarRow } from "./ddp-sintesi-logic"

const COLUMN_LABELS: Record<string, string> = {
  code: "Commessa",
  customerName: "Cliente",
  ddpType: "Tipo",
  totalValue: "Tot. acquisti",
  totalRows: "Inserimenti",
  datedCount: "Mat. consegna",
  overdueCount: "Mat. ritardo",
  delivery: "Periodo consegne",
  lastInsertedAt: "Ultimo inserimento",
  statoChart: "Stato distinta",
}

const DEFAULT_SORTING: SortingState = [
  { id: "overdueCount", desc: true },
  { id: "code", desc: false },
]

type KpiFilter = null | "overdue" | "commercial" | "officina"

function typeLabel(ddpType: string): string {
  return ddpType === "OFFICINA" ? "OFFICINA" : "COMMERCIALE"
}

function formatDateTime(value: string | null): string {
  if (!value) return "—"
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return "—"
  return `${date.toLocaleDateString("it-IT")} ${date.toLocaleTimeString("it-IT", {
    hour: "2-digit",
    minute: "2-digit",
  })}`
}

function deliveryLabel(start: string | null, end: string | null): string {
  if (!start || !end) return "—"
  const from = new Date(start)
  const to = new Date(end)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) return "—"
  return `${from.toLocaleDateString("it-IT")} → ${to.toLocaleDateString("it-IT")}`
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

function TypeBadge({ ddpType }: { ddpType: string }) {
  const officina = ddpType === "OFFICINA"
  return (
    <Badge
      variant="outline"
      className={cn(
        "text-[10px] font-bold",
        officina
          ? "border-zinc-300 bg-zinc-100 text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
          : "border-blue-200 bg-blue-50 text-blue-700 dark:border-blue-900 dark:bg-blue-950/40 dark:text-blue-300"
      )}
    >
      {typeLabel(ddpType)}
    </Badge>
  )
}

function DdpKpiCards({
  items,
  activeFilter,
  onFilter,
}: {
  items: DdpProjectSummary[]
  activeFilter: KpiFilter
  onFilter: (filter: KpiFilter) => void
}) {
  const projectIds = new Set(items.map((item) => item.projectId))
  const totalValue = items.reduce((sum, item) => sum + item.totalValue, 0)
  const totalOverdue = items.reduce((sum, item) => sum + item.overdueCount, 0)

  const cards = [
    {
      key: null as KpiFilter,
      label: "Distinte DDP",
      value: items.length,
      hint: `${projectIds.size} commesse`,
    },
    {
      key: "overdue" as KpiFilter,
      label: "Mat. in ritardo",
      value: totalOverdue,
      hint: "Somma su tutte le distinte",
      valueClassName:
        totalOverdue > 0 ? "text-amber-700 dark:text-amber-400" : undefined,
    },
    {
      key: "commercial" as KpiFilter,
      label: "Commerciali",
      value: items.filter((item) => item.ddpType !== "OFFICINA").length,
      hint: "Distinte commerciali attive",
    },
    {
      key: "officina" as KpiFilter,
      label: "Officina",
      value: items.filter((item) => item.ddpType === "OFFICINA").length,
      hint: "Distinte officina attive",
    },
  ]

  return (
    <div className="grid grid-cols-1 gap-4 *:data-[slot=card]:bg-gradient-to-t *:data-[slot=card]:from-primary/5 *:data-[slot=card]:to-card *:data-[slot=card]:shadow-xs @xl/main:grid-cols-2 @3xl/main:grid-cols-3 @5xl/main:grid-cols-5 dark:*:data-[slot=card]:bg-card">
      <Card
        className="@container/card cursor-default"
        onClick={() => onFilter(null)}
      >
        <CardHeader>
          <CardDescription>Tot. acquisti</CardDescription>
          <CardTitle
            className={cn(
              "text-2xl font-semibold tabular-nums @[250px]/card:text-3xl",
              "text-red-700 dark:text-red-400"
            )}
          >
            {euro(totalValue)}
          </CardTitle>
        </CardHeader>
        <p className="px-6 pb-4 text-sm text-muted-foreground">
          Valore aggregato su tutte le distinte
        </p>
      </Card>

      {cards.map((card) => (
        <Card
          key={card.label}
          className={cn(
            "@container/card cursor-pointer transition-colors hover:border-primary/40",
            activeFilter === card.key && "border-primary ring-1 ring-primary/20"
          )}
          onClick={() => onFilter(activeFilter === card.key ? null : card.key)}
        >
          <CardHeader>
            <CardDescription>{card.label}</CardDescription>
            <CardTitle
              className={cn(
                "text-2xl font-semibold tabular-nums @[250px]/card:text-3xl",
                card.valueClassName
              )}
            >
              {card.value}
            </CardTitle>
          </CardHeader>
          <p className="px-6 pb-4 text-sm text-muted-foreground">{card.hint}</p>
        </Card>
      ))}
    </div>
  )
}

type ViewMode = "table" | "cards"

function viewModeStorageKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `ddp.gestore.viewMode.${employeeId}`
}

function loadViewMode(): ViewMode {
  const stored = localStorage.getItem(viewModeStorageKey())
  return stored === "cards" ? "cards" : "table"
}

export function GestoreDdpPage() {
  const navigate = useNavigate()
  const [kpiFilter, setKpiFilter] = React.useState<KpiFilter>(null)
  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadViewMode)

  const setViewMode = React.useCallback((mode: ViewMode) => {
    setViewModeState(mode)
    localStorage.setItem(viewModeStorageKey(), mode)
  }, [])

  const query = useQuery({
    queryKey: ["ddp-summary"],
    queryFn: fetchDdpSummary,
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  const statusDefs = React.useMemo(() => {
    const map = new Map<string, DdpStatusItem>()
    for (const status of statusesQuery.data ?? []) {
      map.set(status.statusKey, status)
    }
    return map
  }, [statusesQuery.data])

  useProjectHub("all", () => {
    void query.refetch()
  })

  const filteredData = React.useMemo(() => {
    const items = query.data ?? []
    if (!kpiFilter) return items
    switch (kpiFilter) {
      case "overdue":
        return items.filter((item) => item.overdueCount > 0)
      case "commercial":
        return items.filter((item) => item.ddpType !== "OFFICINA")
      case "officina":
        return items.filter((item) => item.ddpType === "OFFICINA")
      default:
        return items
    }
  }, [query.data, kpiFilter])

  const codeFilterOptions = React.useMemo(() => {
    const values = new Set<string>()
    for (const item of filteredData) {
      if (item.code) values.add(item.code)
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b, "it"))
  }, [filteredData])

  const customerFilterOptions = React.useMemo(() => {
    const values = new Set<string>()
    for (const item of filteredData) {
      if (item.customerName) values.add(item.customerName)
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b, "it"))
  }, [filteredData])

  const tipoFilterOptions = React.useMemo(() => {
    const values = new Set<string>()
    for (const item of filteredData) {
      values.add(typeLabel(item.ddpType))
    }
    return Array.from(values).sort((a, b) => a.localeCompare(b, "it"))
  }, [filteredData])

  const ripartizioneByRow = React.useMemo(() => {
    const map = new Map<string, BarRow[]>()
    for (const item of filteredData) {
      const key = `${item.projectId}-${item.ddpType}`
      map.set(
        key,
        buildRipartizioneBars(item.statusCounts ?? [], statusDefs)
      )
    }
    return map
  }, [filteredData, statusDefs])

  const groupedByCode = React.useMemo(() => {
    const groups = new Map<string, DdpProjectSummary[]>()
    for (const item of filteredData) {
      const key = item.code || "—"
      const list = groups.get(key)
      if (list) list.push(item)
      else groups.set(key, [item])
    }
    return Array.from(groups.entries())
      .sort((a, b) => a[0].localeCompare(b[0], "it"))
      .map(([code, items]) => ({
        code,
        customerName: items[0]?.customerName || "",
        items: items
          .slice()
          .sort((a, b) => (a.ddpType === "OFFICINA" ? 1 : 0) - (b.ddpType === "OFFICINA" ? 1 : 0)),
      }))
  }, [filteredData])

  const openSintesi = React.useCallback(
    (row: DdpProjectSummary) => {
      navigate(`/gestore-ddp/${row.projectId}?type=${row.ddpType}`)
    },
    [navigate]
  )

  const columns = React.useMemo<ColumnDef<DdpProjectSummary>[]>(
    () => [
      {
        id: "statoChart",
        header: COLUMN_LABELS.statoChart,
        enableSorting: false,
        enableColumnFilter: false,
        enableHiding: false,
        cell: ({ row }) => (
          <DdpPieWithLegend
            bars={
              ripartizioneByRow.get(
                `${row.original.projectId}-${row.original.ddpType}`
              ) ?? []
            }
          />
        ),
      },
      {
        accessorKey: "code",
        enableHiding: false,
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.code}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        filterFn: exactColumnFilterFn,
        meta: {
          filterInput: ({ value, onChange }) => (
            <DataTableColumnSelectFilter
              value={value}
              onChange={onChange}
              options={codeFilterOptions}
              allLabel="Tutte"
            />
          ),
        },
        cell: ({ row }) => (
          <button
            type="button"
            className="font-medium hover:underline"
            onClick={() => openSintesi(row.original)}
          >
            {row.original.code}
          </button>
        ),
      },
      {
        accessorKey: "customerName",
        header: COLUMN_LABELS.customerName,
        filterFn: exactColumnFilterFn,
        meta: {
          filterInput: ({ value, onChange }) => (
            <DataTableColumnSelectFilter
              value={value}
              onChange={onChange}
              options={customerFilterOptions}
              allLabel="Tutti"
            />
          ),
        },
        cell: ({ row }) => row.original.customerName || "—",
      },
      {
        accessorKey: "ddpType",
        header: COLUMN_LABELS.ddpType,
        filterFn: (row, columnId, filterValue) => {
          if (filterValue === undefined || filterValue === null || filterValue === "") {
            return true
          }
          return typeLabel(String(row.getValue(columnId))) === String(filterValue)
        },
        meta: {
          filterInput: ({ value, onChange }) => (
            <DataTableColumnSelectFilter
              value={value}
              onChange={onChange}
              options={tipoFilterOptions}
            />
          ),
        },
        cell: ({ row }) => <TypeBadge ddpType={row.original.ddpType} />,
        sortingFn: (a, b) =>
          typeLabel(a.original.ddpType).localeCompare(
            typeLabel(b.original.ddpType),
            "it"
          ),
      },
      {
        accessorKey: "totalValue",
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.totalValue}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => (
          <span className="font-medium tabular-nums text-red-700 dark:text-red-400">
            {euro(row.original.totalValue)}
          </span>
        ),
      },
      {
        accessorKey: "totalRows",
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.totalRows}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => (
          <span className="tabular-nums">{row.original.totalRows}</span>
        ),
      },
      {
        accessorKey: "datedCount",
        header: COLUMN_LABELS.datedCount,
        cell: ({ row }) => (
          <span className="tabular-nums">{row.original.datedCount}</span>
        ),
      },
      {
        accessorKey: "overdueCount",
        header: ({ column }) => (
          <SortHeader
            label={COLUMN_LABELS.overdueCount}
            onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
          />
        ),
        cell: ({ row }) => (
          <span
            className={cn(
              "tabular-nums",
              row.original.overdueCount > 0 &&
                "font-semibold text-amber-700 dark:text-amber-400"
            )}
          >
            {row.original.overdueCount}
          </span>
        ),
      },
      {
        id: "delivery",
        accessorFn: (row) =>
          deliveryLabel(row.deliveryStart, row.deliveryEnd),
        header: COLUMN_LABELS.delivery,
        cell: ({ row }) => (
          <span className="whitespace-nowrap text-sm">
            {deliveryLabel(row.original.deliveryStart, row.original.deliveryEnd)}
          </span>
        ),
      },
      {
        accessorKey: "lastInsertedAt",
        header: COLUMN_LABELS.lastInsertedAt,
        cell: ({ row }) => (
          <span className="whitespace-nowrap text-sm tabular-nums">
            {formatDateTime(row.original.lastInsertedAt)}
          </span>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <RowActionsMenu
              label={`${row.original.code} ${typeLabel(row.original.ddpType)}`}
              actions={[
                {
                  label: "Apri sintesi",
                  icon: ExternalLink,
                  onClick: () => openSintesi(row.original),
                },
              ]}
            />
          </div>
        ),
      },
    ],
    [codeFilterOptions, customerFilterOptions, openSintesi, ripartizioneByRow, tipoFilterOptions]
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

  return (
    <div className="space-y-4">
      <div className="flex justify-end gap-2">
        <Button variant="outline" size="sm" onClick={() => navigate("/gestore-ddp/feedback/acquisti")}>
          <ClipboardList className="mr-1.5 size-4" />
          Feedback Acquisti
        </Button>
        <Button variant="outline" size="sm" onClick={() => navigate("/gestore-ddp/feedback/magazzino")}>
          <Warehouse className="mr-1.5 size-4" />
          Feedback Magazzino
        </Button>
      </div>

      <DdpKpiCards
        items={query.data ?? []}
        activeFilter={kpiFilter}
        onFilter={setKpiFilter}
      />

      {viewMode === "table" ? (
        <DataTableCardFiltered
          title="Gestore DDP"
          description="Riepilogo distinte commerciali e officina per commessa."
          columns={columns}
          data={filteredData}
          columnLabels={COLUMN_LABELS}
          isLoading={query.isLoading}
          isFetching={query.isFetching}
          error={query.error as Error | null}
          onRefresh={() => query.refetch()}
          searchPlaceholder="Cerca commessa o cliente…"
          rowNoun="distinte"
          emptyMessage={
            kpiFilter
              ? "Nessuna distinta per il filtro KPI selezionato."
              : "Nessuna commessa con righe DDP."
          }
          defaultSorting={DEFAULT_SORTING}
          getRowId={(row) => `${row.projectId}-${row.ddpType}`}
          onRowDoubleClick={openSintesi}
          externalFiltersActive={kpiFilter != null}
          onClearExternalFilters={() => setKpiFilter(null)}
          toolbarActions={viewToggle}
        />
      ) : (
        <Card>
          <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
            <div>
              <CardTitle>Gestore DDP</CardTitle>
              <CardDescription>
                Distinte raggruppate per commessa.
              </CardDescription>
            </div>
            {viewToggle}
          </CardHeader>
          <div className="space-y-6 px-6 pb-6">
            {query.isLoading ? (
              <p className="text-sm text-muted-foreground">Caricamento…</p>
            ) : groupedByCode.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {kpiFilter
                  ? "Nessuna distinta per il filtro KPI selezionato."
                  : "Nessuna commessa con righe DDP."}
              </p>
            ) : (
              groupedByCode.map((group) => (
                <div key={group.code} className="space-y-2">
                  <div className="flex items-baseline gap-2">
                    <span className="font-semibold">{group.code}</span>
                    <span className="text-sm text-muted-foreground">
                      {group.customerName}
                    </span>
                  </div>
                  <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                    {group.items.map((item) => (
                      <DdpProjectCard
                        key={`${item.projectId}-${item.ddpType}`}
                        item={item}
                        bars={
                          ripartizioneByRow.get(
                            `${item.projectId}-${item.ddpType}`
                          ) ?? []
                        }
                        onOpen={() => openSintesi(item)}
                      />
                    ))}
                  </div>
                </div>
              ))
            )}
          </div>
        </Card>
      )}
    </div>
  )
}

function DdpProjectCard({
  item,
  bars,
  onOpen,
}: {
  item: DdpProjectSummary
  bars: BarRow[]
  onOpen: () => void
}) {
  return (
    <div className="rounded-xl border bg-card p-4 shadow-xs">
      <div className="flex items-start justify-between gap-2">
        <TypeBadge ddpType={item.ddpType} />
        <span className="whitespace-nowrap text-xs text-muted-foreground">
          {formatDateTime(item.lastInsertedAt)}
        </span>
      </div>

      <div className="mt-3">
        <DdpPieWithLegend bars={bars} />
      </div>

      <div className="mt-3 grid grid-cols-4 gap-2 text-center">
        <div>
          <div className="font-semibold tabular-nums text-red-700 dark:text-red-400">
            {euro(item.totalValue)}
          </div>
          <div className="text-[10px] text-muted-foreground">
            Tot. acquisti
          </div>
        </div>
        <div>
          <div className="font-semibold tabular-nums">{item.totalRows}</div>
          <div className="text-[10px] text-muted-foreground">Inserim.</div>
        </div>
        <div>
          <div className="font-semibold tabular-nums">{item.datedCount}</div>
          <div className="text-[10px] text-muted-foreground">
            Mat. consegna
          </div>
        </div>
        <div>
          <div
            className={cn(
              "font-semibold tabular-nums",
              item.overdueCount > 0 &&
                "text-amber-700 dark:text-amber-400"
            )}
          >
            {item.overdueCount}
          </div>
          <div className="text-[10px] text-muted-foreground">
            Mat. ritardo
          </div>
        </div>
      </div>

      <div className="mt-3 flex items-center justify-between gap-2 border-t pt-3">
        <span className="text-xs text-muted-foreground">
          {deliveryLabel(item.deliveryStart, item.deliveryEnd)}
        </span>
        <Button variant="outline" size="xs" onClick={onOpen}>
          Apri sintesi
        </Button>
      </div>
    </div>
  )
}
