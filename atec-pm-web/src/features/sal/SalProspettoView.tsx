import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { type SortingState } from "@tanstack/react-table"
import {
  CheckCircle2,
  Download,
  Printer,
  RefreshCw,
  TriangleAlert,
} from "lucide-react"

import { DataTableCard } from "@/components/shared/data-table-card"
import { printHtml } from "@/lib/print-template"
import { getSession } from "@/lib/auth/session"
import {
  confirmSalProspettoCheck,
  fetchSalProspetto,
  fetchSalProspettoCheck,
} from "@/lib/api/sal"
import type { SalProspettoCheck, SalProspettoRow } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"
import { salRowClass } from "@/features/commesse/sal-utils"
import { Button } from "@/components/ui/button"

import {
  SAL_PROSPETTO_COLUMN_LABELS,
  buildSalProspettoColumns,
  salProspettoAlertLabel,
} from "./sal-prospetto-columns"

/** Soglia del controllo periodico del prospetto (giorni) — regola v10. */
const CHECK_PERIOD_DAYS = 15

const CHECK_QUERY_KEY = ["sal", "prospetto-check"] as const

const DEFAULT_SORTING: SortingState = [{ id: "dataFatt", desc: false }]

/** Contatori del sommario segnalazioni (calcolati dagli alert delle righe). */
interface AlertCounters {
  total: number
  warn: number
  pre: number
  incasso: number
  attesa: number
}

function countAlerts(rows: SalProspettoRow[]): AlertCounters {
  const c: AlertCounters = { total: rows.length, warn: 0, pre: 0, incasso: 0, attesa: 0 }
  for (const r of rows) {
    if (r.alert === "warn") c.warn++
    else if (r.alert === "pre") c.pre++
    else if (r.alert === "incasso") c.incasso++
    else if (r.alert === "attesa") c.attesa++
  }
  return c
}

/** Genera e scarica un CSV (separatore ;, BOM UTF-8 per Excel italiano). */
function downloadCsv(rows: SalProspettoRow[], canSeeEconomics: boolean): void {
  const esc = (v: string | number | null): string => {
    const s = v == null ? "" : String(v)
    return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  const header = ["Segnalazione", "Commessa", "Cliente", "Scad.", "Step SAL", "%", "Condizione"]
  if (canSeeEconomics) header.push("Importo")
  header.push("Ipotesi Fatturazione", "Data Prevista Saldo")

  const lines = rows.map((r) => {
    const arr = [
      salProspettoAlertLabel(r.alert),
      r.code,
      r.cliente || "",
      r.ord,
      r.step || "",
      r.perc ?? "",
      r.condizione || "",
    ]
    if (canSeeEconomics) arr.push(r.importo ?? "")
    arr.push(r.dataFatt ? r.dataFatt.slice(0, 10) : "")
    arr.push(r.dataSaldo ? r.dataSaldo.slice(0, 10) : "")
    return arr.map(esc).join(";")
  })
  const csv = "\uFEFF" + [header.join(";"), ...lines].join("\r\n")
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" })
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = "prospetto-sal.csv"
  a.click()
  URL.revokeObjectURL(url)
}

/** Apre una finestra con la SOLA tabella e ne lancia la stampa (evita di stampare tutta l'app). */
function printProspetto(rows: SalProspettoRow[], canSeeEconomics: boolean): void {
  const esc = (s: string): string =>
    s.replace(/[&<>]/g, (c) => (c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"))
  const cols = ["Segnalazione", "Commessa", "Cliente", "Scad.", "Step SAL", "%", "Condizione"]
  if (canSeeEconomics) cols.push("Importo")
  cols.push("Ipotesi Fatturazione", "Data Prevista Saldo")
  const counters = countAlerts(rows)
  const summary =
    `${counters.total} ipotesi monitorate · ${counters.warn} scadute di fatturazione · ` +
    `${counters.pre} pre-warning · ${counters.incasso} fatture non incassate · ` +
    `${counters.attesa} emesse in attesa di incasso`
  const body = rows
    .map((r) => {
      const cells = [
        salProspettoAlertLabel(r.alert),
        r.code,
        r.cliente || "—",
        `${r.ord}°`,
        r.step || "—",
        r.perc != null ? `${r.perc}%` : "—",
        r.condizione || "—",
      ]
      if (canSeeEconomics) cells.push(euro(r.importo))
      cells.push(r.dataFatt ? r.dataFatt.slice(0, 10) : "—")
      cells.push(r.dataSaldo ? r.dataSaldo.slice(0, 10) : "—")
      return `<tr>${cells.map((c) => `<td>${esc(String(c))}</td>`).join("")}</tr>`
    })
    .join("")

  const customStyles = `
    table{border-collapse:collapse;width:100%;font-size:11px}
    th,td{border:1px solid #ccc;padding:4px 6px;text-align:left}th{background:#f3f4f6}
  `

  const contentHtml = `
    <table>
      <thead>
        <tr>${cols.map((c) => `<th>${c}</th>`).join("")}</tr>
      </thead>
      <tbody>${body}</tbody>
    </table>
  `

  printHtml({
    title: "Prospetto SAL — controllo fatturazione e incassi",
    subtitle: summary,
    contentHtml,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}

/**
 * Banner del controllo periodico del prospetto (regola v10: soglia 15 giorni).
 * Rosso se il controllo è dovuto (mai fatto o soglia superata), verde tenue altrimenti.
 * «Conferma controllo» registra la conferma e aggiorna lo stato in cache.
 */
function ProspettoCheckBanner({ check }: { check: SalProspettoCheck }) {
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = React.useState(false)

  const confirm = async () => {
    setConfirming(true)
    try {
      const updated = await confirmSalProspettoCheck()
      queryClient.setQueryData(CHECK_QUERY_KEY, updated)
      notifySuccess("Controllo periodico confermato")
    } catch (error) {
      notifyError(error, "Conferma controllo non riuscita")
    } finally {
      setConfirming(false)
    }
  }

  if (check.due) {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2.5 print:hidden">
        <TriangleAlert className="size-4 shrink-0 text-destructive" />
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-destructive">
            Warning — Allinea Gestione Commesse.
          </p>
          <p className="text-xs text-destructive/90">
            {check.checkedAt
              ? `Sono trascorsi ${check.days ?? 0} giorni dall'ultimo controllo del ${formatDateShort(check.checkedAt)}, oltre la soglia di ${CHECK_PERIOD_DAYS} giorni.`
              : "Controllo periodico non ancora effettuato."}
          </p>
        </div>
        <Button size="sm" className="h-8 shrink-0" onClick={() => void confirm()} disabled={confirming}>
          {confirming && <RefreshCw className="size-3.5 mr-1.5 animate-spin" />}
          Conferma controllo
        </Button>
      </div>
    )
  }

  const remaining = Math.max(0, CHECK_PERIOD_DAYS - (check.days ?? 0))
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-lg border border-emerald-200 bg-emerald-50/60 px-3 py-2.5 print:hidden">
      <CheckCircle2 className="size-4 shrink-0 text-emerald-600" />
      <p className="min-w-0 flex-1 text-xs text-emerald-800">
        Ultimo controllo il{" "}
        <strong className="font-semibold">{formatDateShort(check.checkedAt)}</strong>
        {check.checkedByName ? (
          <>
            {" "}di <strong className="font-semibold">{check.checkedByName}</strong>
          </>
        ) : null}
        . Prossimo avviso il{" "}
        <strong className="font-semibold">{formatDateShort(check.nextDue)}</strong>{" "}
        ({remaining === 1 ? "tra 1 giorno" : `tra ${remaining} giorni`}).
      </p>
      <Button variant="outline" size="sm" className="h-8 shrink-0" onClick={() => void confirm()} disabled={confirming}>
        {confirming && <RefreshCw className="size-3.5 mr-1.5 animate-spin" />}
        Conferma controllo
      </Button>
    </div>
  )
}

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

  const checkQuery = useQuery({
    queryKey: CHECK_QUERY_KEY,
    queryFn: fetchSalProspettoCheck,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  const session = getSession()
  const role = session?.user.userRole
  const canSeeEconomics = role === "ADMIN" || role === "PM"

  const rows = React.useMemo(() => query.data ?? [], [query.data])
  const counters = React.useMemo(() => countAlerts(rows), [rows])

  const columns = React.useMemo(
    () => buildSalProspettoColumns(canSeeEconomics),
    [canSeeEconomics]
  )

  return (
    <div className="flex flex-col gap-4">
      {checkQuery.data && <ProspettoCheckBanner check={checkQuery.data} />}

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
              onClick={() => downloadCsv(visibleRows, canSeeEconomics)}
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
