import * as React from "react"
import { type ColumnDef } from "@tanstack/react-table"
import { ChevronDown, ChevronRight, ExternalLink, Printer } from "lucide-react"
import { Link } from "react-router-dom"

import { DataTableCard } from "@/components/shared/data-table-card"
import { Button } from "@/components/ui/button"
import { ProjectSal } from "@/features/commesse/ProjectSal"
import { printSalSheet } from "@/features/commesse/sal-sheet-print"
import { SalIncassoProgress } from "./SalIncassoProgress"
import { fetchSal } from "@/lib/api/sal"
import type { ProjectListItem, SalSummary } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

export interface SalTableViewProps {
  projects: ProjectListItem[]
  summaryData?: SalSummary[]
  expandedProjects: Record<number, boolean>
  onToggleExpanded: (projectId: number) => void
  canSeeEconomics: boolean
  isLoading?: boolean
  isFetching?: boolean
  error?: Error | null
  onRefresh?: () => void
}

const SAL_COMMESSE_COLUMN_LABELS: Record<string, string> = {
  expand: "Espandi",
  code: "N° Commessa",
  title: "Descrizione",
  customerName: "Cliente",
  po: "PO - Ordine",
  rifOfferta: "Rif. Offerta",
  valore: "Importo Ordine",
  pmName: "PM",
  status: "Stato",
  aperti: "Step aperti",
  incasso: "Avanzamento Incasso SAL",
  actions: "Azioni",
}

export function SalTableView({
  projects,
  summaryData = [],
  expandedProjects,
  onToggleExpanded,
  canSeeEconomics,
  isLoading,
  isFetching,
  error,
  onRefresh,
}: SalTableViewProps) {
  const [printingProjectId, setPrintingProjectId] = React.useState<number | null>(null)

  const summaryMap = React.useMemo(() => {
    const map = new Map<number, SalSummary>()
    summaryData.forEach((s) => map.set(s.projectId, s))
    return map
  }, [summaryData])

  const handlePrintPdf = async (project: ProjectListItem, e: React.MouseEvent) => {
    e.stopPropagation()
    if (printingProjectId !== null) return
    setPrintingProjectId(project.id)
    try {
      const bundle = await fetchSal(project.id)
      printSalSheet(bundle, {
        projectCode: project.code,
        projectTitle: project.title,
        canSeeEconomics,
      })
    } catch (err) {
      notifyError(err as Error)
    } finally {
      setPrintingProjectId(null)
    }
  }

  const columns = React.useMemo<ColumnDef<ProjectListItem>[]>(
    () => [
      {
        id: "expand",
        header: "",
        enableHiding: false,
        cell: ({ row }) => {
          const isExpanded = !!expandedProjects[row.original.id]
          return (
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              onClick={(e) => {
                e.stopPropagation()
                onToggleExpanded(row.original.id)
              }}
            >
              {isExpanded ? (
                <ChevronDown className="size-4 text-primary" />
              ) : (
                <ChevronRight className="size-4 text-muted-foreground" />
              )}
            </Button>
          )
        },
      },
      {
        accessorKey: "code",
        header: "N° Commessa",
        cell: ({ row }) => (
          <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
            {row.original.code}
          </span>
        ),
      },
      {
        accessorKey: "title",
        header: "Descrizione",
        cell: ({ row }) => (
          <span className="font-semibold text-sm truncate max-w-xs block" title={row.original.title}>
            {row.original.title}
          </span>
        ),
      },
      {
        accessorKey: "customerName",
        header: "Cliente",
        cell: ({ row }) => (
          <span className="text-xs font-medium text-foreground">{row.original.customerName || "—"}</span>
        ),
      },
      // Dati d'intestazione del foglio SAL (#91): stessi campi mostrati nelle card.
      {
        id: "po",
        header: "PO - Ordine",
        cell: ({ row }) => (
          <span className="text-xs">{summaryMap.get(row.original.id)?.po || "—"}</span>
        ),
      },
      {
        id: "rifOfferta",
        header: "Rif. Offerta",
        cell: ({ row }) => (
          <span className="text-xs">{summaryMap.get(row.original.id)?.rifOfferta || "—"}</span>
        ),
      },
      // L'Importo Ordine esiste solo per chi ha `sal.economics` (il server manda
      // comunque valore null agli altri: la colonna assente è doppia cintura).
      ...(canSeeEconomics
        ? [
            {
              id: "valore",
              header: "Importo Ordine",
              cell: ({ row }) => (
                <span className="text-xs tabular-nums">
                  {euro(summaryMap.get(row.original.id)?.valore)}
                </span>
              ),
            } satisfies ColumnDef<ProjectListItem>,
          ]
        : []),
      {
        accessorKey: "pmName",
        header: "PM",
        cell: ({ row }) => (
          <span className="text-xs text-muted-foreground">{row.original.pmName || "—"}</span>
        ),
      },
      {
        accessorKey: "status",
        header: "Stato",
        cell: ({ row }) => {
          const st = row.original.status
          if (st === "ACTIVE") return <span className="text-xs font-semibold text-emerald-600">Attiva</span>
          return (
            <span className="uppercase text-[10px] bg-amber-100 text-amber-800 px-1 rounded font-bold">
              {st}
            </span>
          )
        },
      },
      {
        id: "aperti",
        header: "Step aperti",
        cell: ({ row }) => {
          const sum = summaryMap.get(row.original.id)
          if (!sum) return <span className="text-xs text-muted-foreground">—</span>
          return (
            <span className="flex items-center gap-1.5 text-xs">
              <span className="tabular-nums font-medium">
                {sum.open} / {sum.total}
              </span>
              {sum.warn > 0 && (
                <span className="rounded bg-red-100 px-1 text-[10px] font-bold text-red-700">
                  {sum.warn} scadute
                </span>
              )}
              {sum.pre > 0 && (
                <span className="rounded bg-yellow-100 px-1 text-[10px] font-bold text-yellow-700">
                  {sum.pre} imminenti
                </span>
              )}
              {sum.incasso > 0 && (
                <span className="rounded bg-rose-100 px-1 text-[10px] font-bold text-rose-700">
                  {sum.incasso} incassi scaduti
                </span>
              )}
            </span>
          )
        },
      },
      {
        id: "incasso",
        header: "Avanzamento Incasso SAL",
        cell: ({ row }) => {
          const sum = summaryMap.get(row.original.id)
          if (!sum || sum.percTotal === 0) {
            return <span className="text-xs text-muted-foreground">—</span>
          }
          return (
            <SalIncassoProgress
              compact
              percTotal={sum.percTotal}
              percPaid={sum.percPaid}
            />
          )
        },
      },
      {
        id: "actions",
        header: "Azioni",
        enableHiding: false,
        cell: ({ row }) => {
          const isPrinting = printingProjectId === row.original.id
          return (
            <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
              <Button
                variant="outline"
                size="sm"
                className="h-7 px-2 text-xs gap-1"
                disabled={isPrinting}
                onClick={(e) => void handlePrintPdf(row.original, e)}
                title="Stampa PDF SAL"
              >
                <Printer className={cn("size-3.5", isPrinting && "animate-pulse")} />
                PDF
              </Button>
              {canAccessFeature("nav.commesse") && (
                <Button asChild variant="outline" size="sm" className="h-7 px-2 text-xs gap-1">
                  <Link to={`/commesse/${row.original.id}/sal`} state={{ fromGlobal: "/sal" }}>
                    <ExternalLink className="size-3.5" />
                    Apri
                  </Link>
                </Button>
              )}
            </div>
          )
        },
      },
    ],
    [expandedProjects, onToggleExpanded, summaryMap, printingProjectId, canSeeEconomics]
  )

  return (
    <DataTableCard
      embedded
      title="Tutte le commesse - Tabella SAL"
      // v2: colonne #91 (po, rifOfferta, valore, aperti) — chiave versionata
      visibilityStorageKey="table-visibility-sal-commesse-v2"
      columns={columns}
      data={projects}
      columnLabels={SAL_COMMESSE_COLUMN_LABELS}
      isLoading={isLoading}
      isFetching={isFetching}
      error={error}
      onRefresh={onRefresh}
      searchPlaceholder="Cerca commessa, codice, cliente, PM…"
      rowNoun="commesse"
      emptyMessage="Nessuna commessa trovata."
      getRowId={(row) => String(row.id)}
      rowClassName={(row) => (expandedProjects[row.id] ? "bg-muted/30 font-medium" : undefined)}
      onRowDoubleClick={(row) => onToggleExpanded(row.id)}
      renderExpandedRow={(project) =>
        expandedProjects[project.id] ? (
          <div className="py-2 px-1">
            <ProjectSal
              projectId={project.id}
              projectCode={project.code}
              projectTitle={project.title}
            />
          </div>
        ) : null
      }
    />
  )
}
