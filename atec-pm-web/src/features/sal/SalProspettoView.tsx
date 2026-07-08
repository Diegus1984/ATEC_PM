import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ArrowDown, ArrowUp, ChevronsUpDown, Download, ExternalLink, Printer, RefreshCw } from "lucide-react"
import { Link } from "react-router-dom"
import { getSession } from "@/lib/auth/session"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { formatDateWithWeekday } from "@/components/shared/date-field"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchSalProspetto } from "@/lib/api/sal"
import type { SalProspettoRow } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { salRowClass } from "@/features/commesse/sal-utils"
import { Button } from "@/components/ui/button"

type SortKey =
  | ""
  | "alert"
  | "code"
  | "cliente"
  | "ord"
  | "step"
  | "perc"
  | "condizione"
  | "importo"
  | "dataFatt"

const COLS: { key: SortKey; label: string; className?: string }[] = [
  { key: "alert", label: "Segnalazione", className: "w-36" },
  { key: "code", label: "Commessa", className: "w-32" },
  { key: "cliente", label: "Cliente", className: "min-w-[12rem]" },
  { key: "ord", label: "Scad.(ord)", className: "w-24 text-center" },
  { key: "step", label: "Step SAL", className: "min-w-[14rem]" },
  { key: "perc", label: "%", className: "w-16 text-center" },
  { key: "condizione", label: "Condizione", className: "w-44" },
  { key: "importo", label: "Importo", className: "w-32 text-right" },
  { key: "dataFatt", label: "Ipotesi Fatturazione", className: "w-48 text-center" },
]

/** Ordine di gravità della segnalazione: scaduto → pre-warning → in programma. */
const alertRank = (a: string): number => (a === "warn" ? 0 : a === "pre" ? 1 : 2)

function cmp(a: SalProspettoRow, b: SalProspettoRow, key: SortKey): number {
  switch (key) {
    case "alert":
      return alertRank(a.alert) - alertRank(b.alert)
    case "code":
      return String(a.code).localeCompare(String(b.code), "it", { numeric: true })
    case "cliente":
      return String(a.cliente || "").localeCompare(String(b.cliente || ""), "it")
    case "ord":
      return a.ord - b.ord
    case "step":
      return String(a.step || "").localeCompare(String(b.step || ""), "it")
    case "perc":
      return (a.perc ?? -1) - (b.perc ?? -1)
    case "condizione":
      return String(a.condizione || "").localeCompare(String(b.condizione || ""), "it")
    case "importo":
      return (a.importo ?? -1) - (b.importo ?? -1)
    case "dataFatt":
      return String(a.dataFatt || "").localeCompare(String(b.dataFatt || ""))
    default:
      return 0
  }
}

function alertLabel(a: string): string {
  return a === "warn" ? "Scaduto" : a === "pre" ? "Pre-warning" : "In programma"
}

/** Genera e scarica un CSV (separatore ;, BOM UTF-8 per Excel italiano). */
function downloadCsv(rows: SalProspettoRow[], canSeeEconomics: boolean): void {
  const esc = (v: string | number | null): string => {
    const s = v == null ? "" : String(v)
    return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  const header = ["Segnalazione", "Commessa", "Cliente", "Scad.", "Step SAL", "%", "Condizione"]
  if (canSeeEconomics) header.push("Importo")
  header.push("Ipotesi Fatturazione")

  const lines = rows.map((r) => {
    const arr = [
      alertLabel(r.alert),
      r.code,
      r.cliente || "",
      r.ord,
      r.step || "",
      r.perc ?? "",
      r.condizione || "",
    ]
    if (canSeeEconomics) arr.push(r.importo ?? "")
    arr.push(r.dataFatt ? r.dataFatt.slice(0, 10) : "")
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
  const w = window.open("", "_blank", "width=1000,height=700")
  if (!w) return
  const esc = (s: string): string =>
    s.replace(/[&<>]/g, (c) => (c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"))
  const fmtImp = (n: number | null): string =>
    n == null ? "—" : n.toLocaleString("it-IT", { style: "currency", currency: "EUR" })
  const cols = ["Segnalazione", "Commessa", "Cliente", "Scad.", "Step SAL", "%", "Condizione"]
  if (canSeeEconomics) cols.push("Importo")
  cols.push("Ipotesi Fatturazione")
  const body = rows
    .map((r) => {
      const cells = [
        alertLabel(r.alert),
        r.code,
        r.cliente || "—",
        `${r.ord}°`,
        r.step || "—",
        r.perc != null ? `${r.perc}%` : "—",
        r.condizione || "—",
      ]
      if (canSeeEconomics) cells.push(fmtImp(r.importo))
      cells.push(r.dataFatt ? r.dataFatt.slice(0, 10) : "—")
      return `<tr>${cells.map((c) => `<td>${esc(String(c))}</td>`).join("")}</tr>`
    })
    .join("")
  w.document.write(
    `<!doctype html><html><head><meta charset="utf-8"><title>Prospetto SAL</title>` +
      `<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;color:#111}` +
      `h1{font-size:15px;margin:0 0 12px}table{border-collapse:collapse;width:100%;font-size:11px}` +
      `th,td{border:1px solid #ccc;padding:4px 6px;text-align:left}th{background:#f3f4f6}</style></head>` +
      `<body><h1>Prospetto SAL — ipotesi di fatturazione aperte</h1>` +
      `<table><thead><tr>${cols.map((c) => `<th>${c}</th>`).join("")}</tr></thead><tbody>${body}</tbody></table>` +
      `<script>window.onload=function(){window.print()}</script></body></html>`
  )
  w.document.close()
}

export function SalProspettoView() {
  const query = useQuery({
    queryKey: ["sal-prospetto"],
    queryFn: fetchSalProspetto,
    // Vista aggregata cross-commessa: aggiornamento automatico (60s + al focus finestra),
    // così le modifiche SAL fatte su ALTRE commesse compaiono senza refresh manuale.
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  })

  const [sort, setSort] = React.useState<{ key: SortKey; dir: "asc" | "desc" }>({
    key: "",
    dir: "asc",
  })

  const session = getSession()
  const role = session?.user.userRole
  const canSeeEconomics = role === "ADMIN" || role === "PM"

  const visibleCols = React.useMemo(() => {
    return COLS.filter((c) => canSeeEconomics || c.key !== "importo")
  }, [canSeeEconomics])

  const rows = React.useMemo(() => query.data ?? [], [query.data])

  const sortedRows = React.useMemo(() => {
    if (sort.key === "") return rows // ordine del server (commessa → data)
    const factor = sort.dir === "asc" ? 1 : -1
    return [...rows].sort((a, b) => cmp(a, b, sort.key) * factor)
  }, [rows, sort])

  const toggleSort = (key: SortKey) => {
    setSort((prev) =>
      prev.key === key
        ? { key, dir: prev.dir === "asc" ? "desc" : "asc" }
        : { key, dir: "asc" }
    )
  }

  if (query.isLoading) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-muted-foreground justify-center py-12">
        <RefreshCw className="size-4 animate-spin" />
        Caricamento prospetto SAL...
      </div>
    )
  }

  if (query.isError) {
    return <PageErrorAlert message={(query.error as Error).message} />
  }

  if (rows.length === 0) {
    return (
      <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
        <p className="text-sm font-medium text-muted-foreground">Nessuna ipotesi di fatturazione aperta</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-end gap-2 print:hidden">
        <Button variant="outline" size="sm" className="h-8" onClick={() => downloadCsv(sortedRows, canSeeEconomics)}>
          <Download className="size-3.5 mr-1.5" />
          Esporta CSV
        </Button>
        <Button variant="outline" size="sm" className="h-8" onClick={() => printProspetto(sortedRows, canSeeEconomics)}>
          <Printer className="size-3.5 mr-1.5" />
          Stampa
        </Button>
      </div>

      <div className="overflow-x-auto rounded-lg border bg-background">
        <Table className="border-separate border-spacing-y-1">
          <TableHeader className="bg-muted/40">
            <TableRow>
              {visibleCols.map((col) => {
                const active = sort.key === col.key
                const SortIcon = !active ? ChevronsUpDown : sort.dir === "asc" ? ArrowUp : ArrowDown
                return (
                  <TableHead key={col.key} className={col.className}>
                    <button
                      type="button"
                      onClick={() => toggleSort(col.key)}
                      className={cn(
                        "inline-flex items-center gap-1 select-none hover:text-foreground transition-colors",
                        active ? "text-foreground font-semibold" : "text-muted-foreground"
                      )}
                      title="Ordina"
                    >
                      {col.label}
                      <SortIcon className={cn("size-3", active ? "opacity-90" : "opacity-40")} />
                    </button>
                  </TableHead>
                )
              })}
            </TableRow>
          </TableHeader>
          <TableBody>
            {sortedRows.map((row, idx) => {
              const rowBg = salRowClass(row.alert as "warn" | "pre" | "none")

              const formattedImporto = row.importo !== null
                ? row.importo.toLocaleString("it-IT", { style: "currency", currency: "EUR" })
                : "—"

              let alertBadge = (
                <span className="inline-flex items-center rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-semibold text-emerald-700 border border-emerald-200">
                  In programma
                </span>
              )

              if (row.alert === "warn") {
                alertBadge = (
                  <span className="inline-flex items-center rounded-full bg-red-50 px-2 py-0.5 text-xs font-semibold text-red-700 border border-red-200">
                    Scaduto
                  </span>
                )
              } else if (row.alert === "pre") {
                alertBadge = (
                  <span className="inline-flex items-center rounded-full bg-yellow-50 px-2 py-0.5 text-xs font-semibold text-yellow-700 border border-yellow-200">
                    Pre-warning
                  </span>
                )
              }

              return (
                <TableRow
                  key={`${row.projectId}-${idx}`}
                  className={cn("border-0 transition-colors", rowBg)}
                >
                  <TableCell className="py-1.5 align-middle">
                    {alertBadge}
                  </TableCell>
                  <TableCell className="py-1.5 align-middle">
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
                        {row.code}
                      </span>
                      <Button asChild variant="ghost" size="icon" className="size-6 print:hidden" title="Apri commessa">
                        <Link to={`/commesse/${row.projectId}/sal`}>
                          <ExternalLink className="size-3" />
                        </Link>
                      </Button>
                    </div>
                  </TableCell>
                  <TableCell className="py-1.5 align-middle font-medium text-xs truncate max-w-[12rem]" title={row.cliente}>
                    {row.cliente || "—"}
                  </TableCell>
                  <TableCell className="py-1.5 align-middle text-center font-mono text-xs text-muted-foreground">
                    {row.ord}°
                  </TableCell>
                  <TableCell className="py-1.5 align-middle text-xs font-medium truncate max-w-[14rem]" title={row.step}>
                    {row.step || "—"}
                  </TableCell>
                  <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                    {row.perc !== null ? `${row.perc}%` : "—"}
                  </TableCell>
                  <TableCell className="py-1.5 align-middle text-xs text-muted-foreground truncate max-w-[10rem]" title={row.condizione}>
                    {row.condizione || "—"}
                  </TableCell>
                  {canSeeEconomics && (
                    <TableCell className="py-1.5 align-middle text-right font-mono text-xs font-semibold tabular-nums pr-3">
                      {formattedImporto}
                    </TableCell>
                  )}
                  <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                    {row.dataFatt ? formatDateWithWeekday(row.dataFatt) : "—"}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
