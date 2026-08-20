import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { type SortingState } from "@tanstack/react-table"
import { Download, Printer } from "lucide-react"

import { DataTableCard } from "@/components/shared/data-table-card"
import { canAccessFeature } from "@/lib/auth/permissions"
import { fetchSalProspetto } from "@/lib/api/sal"
import type { SalProspettoRow } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { salRowClass } from "@/features/commesse/sal-utils"
import { Button } from "@/components/ui/button"

import {
  SAL_PROSPETTO_COLUMN_LABELS,
  buildSalProspettoColumns,
} from "./sal-prospetto-columns"
import {
  countAlerts,
  downloadProspettoCsv,
  printProspetto,
} from "./sal-prospetto-report"

const DEFAULT_SORTING: SortingState = [{ id: "dataFatt", desc: false }]

/**
 * Le due liste di allarme del prototipo, come viste dedicate.
 *
 * La regola esisteva già (Prospetto, `/scadenze`, campanella) ma non c'era un posto
 * dove vedere SOLO le righe in allarme: il Prospetto non è filtrabile per segnalazione
 * (la colonna espone un rango numerico che la ricerca testuale non intercetta) e
 * `/scadenze` usa una soglia fissa a 7 giorni, diversa dal pre-warning «dal lunedì
 * della settimana precedente» (che arriva fino a ~13 giorni).
 */
export type SalWarningMode = "fatturazione" | "incasso"

const MODES: Record<
  SalWarningMode,
  {
    title: string
    /** Alert che entrano in questa vista. */
    alerts: string[]
    empty: string
    csvName: string
    /** Sommario in testata: costruito sui contatori delle righe filtrate. */
    summary: (c: ReturnType<typeof countAlerts>) => React.ReactNode
    printSubtitle: (c: ReturnType<typeof countAlerts>) => string
  }
> = {
  fatturazione: {
    title: "Warning Fatturazione SAL",
    alerts: ["warn", "pre"],
    empty: "Nessuna ipotesi di fatturazione da segnalare.",
    csvName: "warning-fatturazione-sal.csv",
    summary: (c) => (
      <>
        <Chip dot="bg-red-500" text={`${c.warn} scadute`} />
        <span aria-hidden>·</span>
        <Chip dot="bg-amber-500" text={`${c.pre} in pre-warning`} />
      </>
    ),
    printSubtitle: (c) =>
      `${c.warn} scadute di fatturazione · ${c.pre} in pre-warning (dal lunedì della settimana precedente)`,
  },
  incasso: {
    title: "Warning incasso fattura",
    alerts: ["incasso"],
    empty: "Nessuna fattura scaduta di incasso.",
    csvName: "warning-incasso-sal.csv",
    summary: (c) => <Chip dot="bg-rose-700" text={`${c.incasso} fatture non incassate`} />,
    printSubtitle: (c) =>
      `${c.incasso} fatture emesse e non incassate oltre la data prevista di saldo`,
  },
}

function Chip({ dot, text }: { dot: string; text: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <span className={cn("size-2 rounded-full", dot)} />
      {text}
    </span>
  )
}

export function SalWarningView({ mode }: { mode: SalWarningMode }) {
  const cfg = MODES[mode]

  const query = useQuery({
    queryKey: ["sal-prospetto"],
    queryFn: fetchSalProspetto,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  const canSeeEconomics = canAccessFeature("sal.economics")

  // Stessa sorgente del Prospetto, filtrata alle sole righe in allarme di questo tipo.
  const rows = React.useMemo<SalProspettoRow[]>(
    () => (query.data ?? []).filter((r) => cfg.alerts.includes(r.alert)),
    [query.data, cfg.alerts]
  )
  const counters = React.useMemo(() => countAlerts(rows), [rows])

  const columns = React.useMemo(
    () => buildSalProspettoColumns(canSeeEconomics),
    [canSeeEconomics]
  )

  return (
    <div className="flex flex-col gap-4">
      <DataTableCard
        embedded
        title={cfg.title}
        visibilityStorageKey={`table-visibility-sal-warning-${mode}-v1`}
        columns={columns}
        data={rows}
        columnLabels={SAL_PROSPETTO_COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => void query.refetch()}
        searchPlaceholder="Cerca commessa, cliente, step…"
        rowNoun="segnalazioni"
        emptyMessage={cfg.empty}
        defaultSorting={DEFAULT_SORTING}
        getRowId={(row) => `${row.projectId}-${row.ord}-${row.dataFatt ?? ""}`}
        rowClassName={(row) => salRowClass(row.alert)}
        aboveTable={
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground print:hidden">
            <span className="whitespace-nowrap font-semibold text-foreground">
              {counters.total} segnalazioni
            </span>
            <span aria-hidden>·</span>
            {cfg.summary(counters)}
          </div>
        }
        toolbarActions={(visibleRows) => (
          <>
            <Button
              variant="outline"
              size="sm"
              className="h-8 print:hidden"
              onClick={() => downloadProspettoCsv(visibleRows, canSeeEconomics, cfg.csvName)}
            >
              <Download className="mr-1.5 size-3.5" />
              Esporta CSV
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-8 print:hidden"
              onClick={() =>
                printProspetto(visibleRows, canSeeEconomics, {
                  title: cfg.title,
                  subtitle: cfg.printSubtitle(countAlerts(visibleRows)),
                })
              }
            >
              <Printer className="mr-1.5 size-3.5" />
              Stampa
            </Button>
          </>
        )}
      />
    </div>
  )
}
