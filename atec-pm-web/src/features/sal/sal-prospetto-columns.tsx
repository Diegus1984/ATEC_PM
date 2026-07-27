import { type ColumnDef } from "@tanstack/react-table"
import { ArrowUpDown, ExternalLink } from "lucide-react"
import { Link } from "react-router-dom"

import { ReadonlyDateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import type { SalProspettoRow } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * Cella data del prospetto: stesso chip readonly del foglio SAL (icona
 * calendario + giorno della settimana impilato sopra la data gg/mm/aa,
 * festivi in rosso), su fondo semi-bianco per risaltare sulle righe tinte
 * (pattern input bianchi del foglio). Data assente → «—» muted.
 */
function ProspettoDateCell({ value }: { value: string | null }) {
  if (!value) {
    return (
      <span className="block text-center text-sm text-muted-foreground">—</span>
    )
  }
  return (
    <ReadonlyDateField
      value={value}
      size="sm"
      stackedWeekday
      className="min-w-[8.5rem] border-zinc-200 bg-white/70 shadow-none dark:bg-zinc-950/40"
    />
  )
}

/** Ordine di gravità della segnalazione (per ordinamento colonna). */
export const salProspettoAlertRank = (a: string): number =>
  a === "incasso" ? 0 : a === "warn" ? 1 : a === "pre" ? 2 : a === "attesa" ? 3 : 4

export function salProspettoAlertLabel(a: string): string {
  switch (a) {
    case "incasso":
      return "Fattura no incasso"
    case "warn":
      return "Scaduto"
    case "pre":
      return "Pre-warning"
    case "attesa":
      return "Emessa – attesa incasso"
    default:
      return "In programma"
  }
}

export const SAL_PROSPETTO_COLUMN_LABELS: Record<string, string> = {
  alert: "Segnalazione",
  code: "Commessa",
  cliente: "Cliente",
  ord: "Scad.(ord)",
  step: "Step SAL",
  perc: "%",
  condizione: "Condizione",
  importo: "Importo",
  dataFatt: "Ipotesi Fatturazione",
  dataSaldo: "Data Prevista Saldo",
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

export function SalProspettoAlertBadge({ alert }: { alert: SalProspettoRow["alert"] }) {
  const label = salProspettoAlertLabel(alert)
  const className = cn(
    "inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-semibold",
    alert === "incasso" || alert === "warn"
      ? "bg-red-50 text-red-700 border-red-200"
      : alert === "pre"
        ? "bg-yellow-50 text-yellow-700 border-yellow-200"
        : alert === "attesa"
          ? "bg-sky-50 text-sky-700 border-sky-200"
          : "bg-emerald-50 text-emerald-700 border-emerald-200"
  )
  return <span className={className}>{label}</span>
}

export function buildSalProspettoColumns(
  canSeeEconomics: boolean
): ColumnDef<SalProspettoRow>[] {
  const cols: ColumnDef<SalProspettoRow>[] = [
    {
      id: "alert",
      accessorFn: (row) => salProspettoAlertRank(row.alert),
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.alert}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => <SalProspettoAlertBadge alert={row.original.alert} />,
    },
    {
      accessorKey: "code",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.code}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        <div className="flex items-center gap-1.5">
          <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
            {row.original.code}
          </span>
          <Button asChild variant="ghost" size="icon" className="size-6 print:hidden" title="Apri commessa">
            <Link
              to={`/commesse/${row.original.projectId}/sal`}
              state={{ fromGlobal: "/sal-prospetto" }}
            >
              <ExternalLink className="size-3" />
            </Link>
          </Button>
        </div>
      ),
    },
    {
      accessorKey: "cliente",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.cliente}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        <span
          className="font-medium text-sm truncate max-w-[12rem] block"
          title={row.original.cliente}
        >
          {row.original.cliente || "—"}
        </span>
      ),
    },
    {
      accessorKey: "ord",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.ord}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        <span className="text-center font-mono text-xs text-muted-foreground block">
          {row.original.ord}°
        </span>
      ),
    },
    {
      accessorKey: "step",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.step}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        <span className="text-sm truncate max-w-[14rem] block" title={row.original.step}>
          {row.original.step || "—"}
        </span>
      ),
    },
    {
      accessorKey: "perc",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.perc}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        <span className="text-center text-sm tabular-nums block">
          {row.original.perc !== null ? (
            <>
              {row.original.perc}
              <span className="ml-1 text-muted-foreground font-semibold">%</span>
            </>
          ) : (
            <span className="text-muted-foreground">—</span>
          )}
        </span>
      ),
    },
    {
      accessorKey: "condizione",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.condizione}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) =>
        row.original.condizione ? (
          // Chip a forma di combo: stesso look del trigger AnagraficaSelect del
          // foglio SAL (bordo zinc, rounded, px-2, testo piccolo, fondo chiaro
          // che risalta sulle righe tinte), ma senza chevron e non cliccabile.
          <span
            className="inline-flex h-8 max-w-[12rem] items-center rounded-md border border-zinc-200 bg-white/70 px-2 text-xs font-normal text-foreground dark:bg-zinc-950/40"
            title={row.original.condizione}
          >
            <span className="truncate">{row.original.condizione}</span>
          </span>
        ) : (
          <span className="block text-xs text-muted-foreground">—</span>
        ),
    },
  ]

  if (canSeeEconomics) {
    cols.push({
      accessorKey: "importo",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.importo}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => (
        // Stessa cella Importo del foglio SAL: valore monetario in grassetto
        <div className="flex h-10 items-center justify-end text-sm font-semibold text-foreground tabular-nums">
          {euro(row.original.importo)}
        </div>
      ),
    })
  }

  cols.push(
    {
      accessorKey: "dataFatt",
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.dataFatt}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => <ProspettoDateCell value={row.original.dataFatt} />,
    },
    {
      id: "dataSaldo",
      accessorKey: "dataSaldo",
      sortingFn: (rowA, rowB) => {
        const av = rowA.original.dataSaldo
        const bv = rowB.original.dataSaldo
        if (av == null && bv == null) return 0
        if (av == null) return 1
        if (bv == null) return -1
        return av.localeCompare(bv)
      },
      header: ({ column }) => (
        <SortHeader
          label={SAL_PROSPETTO_COLUMN_LABELS.dataSaldo}
          onClick={() => column.toggleSorting(column.getIsSorted() === "asc")}
        />
      ),
      cell: ({ row }) => <ProspettoDateCell value={row.original.dataSaldo} />,
    }
  )

  return cols
}
