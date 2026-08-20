import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  useReactTable,
  type ColumnDef,
} from "@tanstack/react-table"
import { ArrowUpRight, ChevronRight } from "lucide-react"
import { Link, useNavigate } from "react-router-dom"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { GridScroller } from "@/components/shared/grid-scroller"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { renderColumnDef } from "@/components/shared/render-column-def"
import { TablePagination } from "@/components/shared/table-pagination"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { ProjectDialog } from "@/features/commesse/ProjectDialog"
import { ProjectStatusCell } from "@/features/commesse/ProjectStatusCell"
import { fetchProjects, patchProjectStatus } from "@/lib/api/projects"
import type { ProjectListItem } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { partitionCommesseAltreAttivita } from "@/lib/project-code"
import { useProjectsHub } from "@/lib/signalr/use-projects-hub"
import { notifyError, notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

/**
 * La pagina d'ingresso della #88, rivista con la #89: sezioni collassabili «Commesse» /
 * «Commesse Chiuse» / «Altre Attività» / «Attività chiuse», con le viste Tutte · Attive ·
 * Stand-by · Bozze sulle aperte. Colonne Codice · Titolo · Cliente · Stato, SENZA la colonna
 * Ore (eliminata su richiesta). Lo Stato si cambia dalla cella (ProjectStatusCell) in OGNI
 * vista — riaprire una chiusa compreso; nelle Altre Attività chi ha il permesso promuove una
 * riga a commessa passando dal dialog di configurazione (#89): codice nuovo generato dal
 * server, quello vecchio conservato nelle note.
 */

const COMMESSE_COLUMNS_KEY = "dashboard-commesse-columns-v1"
const ALTRE_COLUMNS_KEY = "dashboard-altre-columns-v1"

/** Apertura/chiusura di una sezione della Dashboard, ricordata per browser. */
function usePersistedOpen(key: string, defaultOpen: boolean) {
  const [open, setOpen] = React.useState<boolean>(() => {
    try {
      const raw = localStorage.getItem(key)
      return raw == null ? defaultOpen : raw === "1"
    } catch {
      return defaultOpen
    }
  })
  const toggle = React.useCallback(() => {
    setOpen((prev) => {
      const next = !prev
      try {
        localStorage.setItem(key, next ? "1" : "0")
      } catch {
        // storage non disponibile: nessuna persistenza
      }
      return next
    })
  }, [key])
  return [open, toggle] as const
}

/** Sezione collassabile della Dashboard (#89): titolo + contatore, contenuto animato. */
function DashboardSection({
  id,
  title,
  count,
  defaultOpen,
  children,
}: {
  id: string
  title: string
  count: number
  defaultOpen: boolean
  children: React.ReactNode
}) {
  const [open, toggle] = usePersistedOpen(`dashboard:section:${id}:v1`, defaultOpen)
  return (
    <section className="rounded-lg border">
      <button
        type="button"
        aria-expanded={open}
        className="flex w-full items-center gap-2 px-3 py-2 text-left"
        onClick={toggle}
      >
        <ChevronRight
          className={cn(
            "size-4 shrink-0 text-muted-foreground transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
            open && "rotate-90"
          )}
        />
        <span className="text-base font-semibold tracking-tight">{title}</span>
        <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs font-semibold tabular-nums text-muted-foreground">
          {count}
        </span>
      </button>
      <Collapsible open={open}>
        <div className="border-t p-3">{children}</div>
      </Collapsible>
    </section>
  )
}

function buildColumns(
  onChangeStatus: (row: ProjectListItem, next: string) => void,
  onPromote: ((row: ProjectListItem) => void) | null
): ColumnDef<ProjectListItem>[] {
  const columns: ColumnDef<ProjectListItem>[] = [
    {
      accessorKey: "code",
      header: "Codice",
      cell: ({ row }) => (
        <Link
          to={`/commesse/${row.original.id}`}
          className="font-medium hover:underline"
          onClick={(event) => event.stopPropagation()}
        >
          {row.original.code}
        </Link>
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
        <ProjectStatusCell
          status={row.original.status}
          onChangeStatus={(next) => onChangeStatus(row.original, next)}
        />
      ),
    },
  ]

  if (onPromote) {
    columns.push({
      id: "promote",
      header: "",
      enableHiding: false,
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button
            variant="outline"
            size="sm"
            className="h-7 gap-1 text-xs"
            title={`Rinomina «${row.original.code}» come commessa (codice nuovo generato oggi)`}
            onClick={(event) => {
              event.stopPropagation()
              onPromote(row.original)
            }}
          >
            <ArrowUpRight className="size-3.5" />
            Promuovi a commessa
          </Button>
        </div>
      ),
    })
  }

  return columns
}

function ProjectsPanel({
  rows,
  columns,
  emptyMessage,
  storageKey,
}: {
  rows: ProjectListItem[]
  columns: ColumnDef<ProjectListItem>[]
  emptyMessage: string
  storageKey: string
}) {
  const navigate = useNavigate()
  const [columnVisibility, setColumnVisibility] =
    usePersistedColumnVisibility(storageKey)
  const [pagination, setPagination] = React.useState({ pageIndex: 0, pageSize: 10 })

  const table = useReactTable({
    data: rows,
    columns,
    state: { columnVisibility, pagination },
    onColumnVisibilityChange: setColumnVisibility,
    onPaginationChange: setPagination,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
  })

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-end">
        <ColumnsMenu
          columns={table
            .getAllColumns()
            .filter((column) => column.getCanHide())
            .map((column) => ({
              id: column.id,
              label:
                typeof column.columnDef.header === "string" &&
                column.columnDef.header
                  ? column.columnDef.header
                  : column.id,
              checked: column.getIsVisible(),
              onToggle: (value) => column.toggleVisibility(value),
            }))}
        />
      </div>

      <GridScroller className="rounded-lg border">
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
                <TableRow
                  key={row.id}
                  className="cursor-pointer"
                  title={`Apri la commessa ${row.original.code}`}
                  onClick={() => navigate(`/commesse/${row.original.id}`)}
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {renderColumnDef(cell.column.columnDef.cell, cell.getContext())}
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
      </GridScroller>

      <TablePagination table={table} rowNoun="commessa/e" />
    </div>
  )
}

/**
 * Viste ad anta unica sulle commesse aperte (#89): Tutte · Attive · Stand-by · Bozze,
 * con i contatori nel titolo. Le chiuse stanno nella loro sezione, non qui.
 */
function StatusTabs({
  rows,
  columns,
  storageKey,
  noun,
}: {
  rows: ProjectListItem[]
  columns: ColumnDef<ProjectListItem>[]
  storageKey: string
  noun: string
}) {
  const attive = rows.filter((p) => p.status === "ACTIVE")
  const standBy = rows.filter((p) => p.status === "ON_HOLD")
  const bozze = rows.filter((p) => p.status === "DRAFT")

  const views = [
    { key: "all", label: "Tutte", rows, empty: `Nessuna ${noun}.` },
    { key: "active", label: "Attive", rows: attive, empty: `Nessuna ${noun} attiva.` },
    { key: "onhold", label: "Stand-by", rows: standBy, empty: `Nessuna ${noun} in stand-by.` },
    { key: "draft", label: "Bozze", rows: bozze, empty: "Nessuna bozza." },
  ]

  return (
    <Tabs defaultValue="all" className="w-full gap-3">
      <TabsList>
        {views.map((view) => (
          <TabsTrigger key={view.key} value={view.key}>
            {view.label} ({view.rows.length})
          </TabsTrigger>
        ))}
      </TabsList>
      {views.map((view) => (
        <TabsContent key={view.key} value={view.key}>
          <ProjectsPanel
            rows={view.rows}
            columns={columns}
            emptyMessage={view.empty}
            storageKey={storageKey}
          />
        </TabsContent>
      ))}
    </Tabs>
  )
}

export function DashboardCommesseTables() {
  const queryClient = useQueryClient()

  // #89: le chiuse non spariscono più — arrivano anche loro e finiscono nelle sezioni
  // «Chiuse». Le CANCELLED restano fuori: sono il soft delete dell'eliminazione.
  // Il server taglia pageSize a 200 e ordina le chiuse IN FONDO: con una chiamata sola,
  // oltre le 200 righe sarebbero proprio le chiuse a sparire in silenzio — quindi si
  // cicla finché `hasMore` (tetto di sicurezza a 25 pagine).
  const projectsQuery = useQuery({
    queryKey: ["projects", "dashboard-tables"],
    queryFn: async () => {
      const items: ProjectListItem[] = []
      let page = 1
      for (;;) {
        const result = await fetchProjects({ page, pageSize: 200, includeClosed: true })
        items.push(...result.items)
        if (!result.hasMore || page >= 25) break
        page += 1
      }
      return items
    },
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["projects"] })
    void queryClient.invalidateQueries({ queryKey: ["dashboard-folders"] })
  }, [queryClient])

  // Un collega crea/promuove/cambia stato: la pagina d'ingresso si aggiorna da sola.
  useProjectsHub(true, invalidate)

  const statusMutation = useMutation({
    mutationFn: ({ row, next }: { row: ProjectListItem; next: string }) =>
      patchProjectStatus(row.id, next),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })

  const puoPromuovere = canWriteFeature("action.project_locked_write")

  // #89: la promozione passa dal dialog di configurazione commessa, precompilato
  // dall'attività. Qui resta solo la riga scelta; il resto lo fa ProjectDialog.
  const [promoteRow, setPromoteRow] = React.useState<ProjectListItem | null>(null)

  const { commesse, commesseChiuse, altreAttivita, altreChiuse } = React.useMemo(() => {
    const items = (projectsQuery.data ?? []).filter(
      (p) => p.status !== "CANCELLED"
    )
    const partition = partitionCommesseAltreAttivita(
      items,
      (p) => p.code,
      (p) => p.title
    )
    return {
      commesse: partition.commesse.filter((p) => p.status !== "COMPLETED"),
      commesseChiuse: partition.commesse.filter((p) => p.status === "COMPLETED"),
      altreAttivita: partition.altreAttivita.filter((p) => p.status !== "COMPLETED"),
      altreChiuse: partition.altreAttivita.filter((p) => p.status === "COMPLETED"),
    }
  }, [projectsQuery.data])

  const commesseColumns = React.useMemo(
    () => buildColumns((row, next) => statusMutation.mutate({ row, next }), null),
    [statusMutation]
  )
  const altreColumns = React.useMemo(
    () =>
      buildColumns(
        (row, next) => statusMutation.mutate({ row, next }),
        puoPromuovere ? (row) => setPromoteRow(row) : null
      ),
    [statusMutation, puoPromuovere]
  )

  if (projectsQuery.isError) {
    return <PageErrorAlert message={(projectsQuery.error as Error).message} />
  }

  return (
    <div className="flex flex-col gap-4">
      <DashboardSection
        id="commesse"
        title="Commesse"
        count={commesse.length}
        defaultOpen
      >
        <StatusTabs
          rows={commesse}
          columns={commesseColumns}
          storageKey={COMMESSE_COLUMNS_KEY}
          noun="commessa"
        />
      </DashboardSection>

      {/* #89: una chiusa non sparisce — scende qui, e dalla colonna Stato si riapre. */}
      <DashboardSection
        id="commesse-chiuse"
        title="Commesse Chiuse"
        count={commesseChiuse.length}
        defaultOpen={false}
      >
        <ProjectsPanel
          rows={commesseChiuse}
          columns={commesseColumns}
          emptyMessage="Nessuna commessa chiusa."
          storageKey={COMMESSE_COLUMNS_KEY}
        />
      </DashboardSection>

      <DashboardSection
        id="altre-attivita"
        title="Altre Attività"
        count={altreAttivita.length}
        defaultOpen
      >
        <StatusTabs
          rows={altreAttivita}
          columns={altreColumns}
          storageKey={ALTRE_COLUMNS_KEY}
          noun="attività"
        />
      </DashboardSection>

      <DashboardSection
        id="altre-chiuse"
        title="Attività chiuse"
        count={altreChiuse.length}
        defaultOpen={false}
      >
        <ProjectsPanel
          rows={altreChiuse}
          columns={altreColumns}
          emptyMessage="Nessuna attività chiusa."
          storageKey={ALTRE_COLUMNS_KEY}
        />
      </DashboardSection>

      <ProjectDialog
        open={promoteRow != null}
        projectId={null}
        promoteFromId={promoteRow?.id ?? null}
        onClose={() => setPromoteRow(null)}
        onSaved={async () => {
          setPromoteRow(null)
          invalidate()
        }}
        onPromoted={(nuovoCodice) =>
          notifySuccess(`Promossa: ora è la commessa ${nuovoCodice}`)
        }
      />
    </div>
  )
}
