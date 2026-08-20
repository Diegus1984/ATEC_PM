import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { type SortingState } from "@tanstack/react-table"
import { Download, Printer } from "lucide-react"

import { DataTableCard } from "@/components/shared/data-table-card"
import { canAccessFeature } from "@/lib/auth/permissions"
import { fetchSalProspetto } from "@/lib/api/sal"
import { cn } from "@/lib/utils"
import { salRowClass } from "@/features/commesse/sal-utils"
import { Button } from "@/components/ui/button"

import {
  SAL_PROSPETTO_COLUMN_LABELS,
  buildSalProspettoColumns,
} from "./sal-prospetto-columns"
import {
  type AlertCounters,
  countAlerts,
  downloadProspettoCsv,
  printProspetto,
} from "./sal-prospetto-report"

const DEFAULT_SORTING: SortingState = [{ id: "dataFatt", desc: false }]

/** Sommario contatori delle segnalazioni (stile riepilogo warning del prototipo v10). */
function ProspettoSummary({ counters }: { counters: AlertCounters }) {
  const chip = (dotClass: string, text: string) => (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <span className={cn("size-2 rounded-full", dotClass)} />
      {text}
    </span>
  )
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground print:hidden">
      <span className="font-semibold text-foreground whitespace-nowrap">
        {counters.total} ipotesi monitorate
      </span>
      <span aria-hidden>·</span>
      {chip("bg-red-500", `${counters.warn} scadute di fatturazione`)}
      <span aria-hidden>·</span>
      {chip("bg-amber-500", `${counters.pre} pre-warning`)}
      <span aria-hidden>·</span>
      {chip("bg-rose-700", `${counters.incasso} fatture non incassate`)}
      <span aria-hidden>·</span>
      {chip("bg-sky-500", `${counters.attesa} emesse in attesa di incasso`)}
    </div>
  )
}

export function SalProspettoView() {
  const query = useQuery({
    queryKey: ["sal-prospetto"],
    queryFn: fetchSalProspetto,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  // Importi visibili con la funzione `sal.economics` (livello 2: PM/ADMIN come prima).
  const canSeeEconomics = canAccessFeature("sal.economics")

  const rows = React.useMemo(() => query.data ?? [], [query.data])
  const counters = React.useMemo(() => countAlerts(rows), [rows])

  const columns = React.useMemo(
    () => buildSalProspettoColumns(canSeeEconomics),
    [canSeeEconomics]
  )

  return (
    <div className="flex flex-col gap-4">
      <DataTableCard
        embedded
        title="Prospetto SAL"
        visibilityStorageKey="table-visibility-sal-prospetto-v1"
        columns={columns}
        data={rows}
        columnLabels={SAL_PROSPETTO_COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => void query.refetch()}
        searchPlaceholder="Cerca commessa, cliente, step…"
        rowNoun="ipotesi"
        emptyMessage="Nessuna ipotesi di fatturazione da monitorare."
        defaultSorting={DEFAULT_SORTING}
        getRowId={(row) => `${row.projectId}-${row.ord}-${row.dataFatt ?? ""}`}
        rowClassName={(row) => salRowClass(row.alert)}
        aboveTable={<ProspettoSummary counters={counters} />}
        toolbarActions={(visibleRows) => (
          <>
            <Button
              variant="outline"
              size="sm"
              className="h-8 print:hidden"
              onClick={() => downloadProspettoCsv(visibleRows, canSeeEconomics)}
            >
              <Download className="size-3.5 mr-1.5" />
              Esporta CSV
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-8 print:hidden"
              onClick={() => printProspetto(visibleRows, canSeeEconomics)}
            >
              <Printer className="size-3.5 mr-1.5" />
              Stampa
            </Button>
          </>
        )}
      />
    </div>
  )
}
