import * as React from "react"
import {
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  useReactTable,
  type ColumnDef,
} from "@tanstack/react-table"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { renderColumnDef } from "@/components/shared/render-column-def"
import { TablePagination } from "@/components/shared/table-pagination"
import { Badge } from "@/components/ui/badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import type { DashboardProjectRow } from "@/lib/api/types"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

const DASHBOARD_COLUMNS_KEY = "table-visibility-dashboard-projects"

function statusLabel(status: string): string {
  switch (status) {
    case "DRAFT":
      return "Bozza"
    case "ACTIVE":
      return "Attiva"
    case "ON_HOLD":
      return "In pausa"
    case "COMPLETED":
      return "Completata"
    case "CANCELLED":
      return "Annullata"
    default:
      return status || "—"
  }
}

const columns: ColumnDef<DashboardProjectRow>[] = [
  {
    accessorKey: "code",
    header: "Codice",
    cell: ({ row }) => (
      <span className="font-medium">{row.original.code}</span>
    ),
  },
  {
    accessorKey: "title",
    header: "Titolo",
  },
  {
    accessorKey: "customerName",
    header: "Cliente",
  },
  {
    accessorKey: "status",
    header: "Stato",
    cell: ({ row }) => (
      <Badge variant="outline">{statusLabel(row.original.status)}</Badge>
    ),
  },
  {
    id: "hours",
    header: "Ore",
    cell: ({ row }) => (
      <span className="tabular-nums">
        {row.original.hoursWorked.toFixed(1)} /{" "}
        {row.original.budgetHours.toFixed(1)}
      </span>
    ),
  },
]

function ProjectsTablePanel({
  rows,
  emptyMessage,
}: {
  rows: DashboardProjectRow[]
  emptyMessage: string
}) {
  const [columnVisibility, setColumnVisibility] = usePersistedColumnVisibility(
    DASHBOARD_COLUMNS_KEY
  )
  const [pagination, setPagination] = React.useState({
    pageIndex: 0,
    pageSize: 10,
  })

  const table = useReactTable({
    data: rows,
    columns,
    state: {
      columnVisibility,
      pagination,
    },
    onColumnVisibilityChange: setColumnVisibility,
    onPaginationChange: setPagination,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
  })

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-end">
        <ColumnsMenu
          columns={table
            .getAllColumns()
            .filter((column) => column.getCanHide())
            .map((column) => ({
              id: column.id,
              label:
                typeof column.columnDef.header === "string"
                  ? column.columnDef.header
                  : column.id,
              checked: column.getIsVisible(),
              onToggle: (value) => column.toggleVisibility(value),
            }))}
        />
      </div>

      <div className="overflow-hidden rounded-lg border">
        <Table>
          <TableHeader className="bg-muted">
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : renderColumnDef(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows.length ? (
              table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {renderColumnDef(
                        cell.column.columnDef.cell,
                        cell.getContext()
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell
                  colSpan={columns.length}
                  className="h-24 text-center text-muted-foreground"
                >
                  {emptyMessage}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <TablePagination table={table} rowNoun="commessa/e" />
    </div>
  )
}

export function DashboardProjectsTable({
  projects,
}: {
  projects: DashboardProjectRow[]
}) {
  const activeRows = projects.filter((row) => row.status === "ACTIVE")
  const draftRows = projects.filter((row) => row.status === "DRAFT")

  return (
    <Tabs defaultValue="all" className="w-full gap-4">
      <div className="flex items-center justify-between">
        <TabsList>
          <TabsTrigger value="all">Tutte ({projects.length})</TabsTrigger>
          <TabsTrigger value="active">Attive ({activeRows.length})</TabsTrigger>
          <TabsTrigger value="draft">Bozze ({draftRows.length})</TabsTrigger>
        </TabsList>
      </div>

      <TabsContent value="all">
        <ProjectsTablePanel
          rows={projects}
          emptyMessage="Nessuna commessa recente."
        />
      </TabsContent>
      <TabsContent value="active">
        <ProjectsTablePanel
          rows={activeRows}
          emptyMessage="Nessuna commessa attiva recente."
        />
      </TabsContent>
      <TabsContent value="draft">
        <ProjectsTablePanel
          rows={draftRows}
          emptyMessage="Nessuna bozza recente."
        />
      </TabsContent>
    </Tabs>
  )
}
