import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ExternalLink, RefreshCw } from "lucide-react"
import { Link } from "react-router-dom"

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
import { cn } from "@/lib/utils"
import { salRowClass } from "@/features/commesse/sal-utils"
import { Button } from "@/components/ui/button"

export function SalProspettoView() {
  const query = useQuery({
    queryKey: ["sal-prospetto"],
    queryFn: fetchSalProspetto,
  })

  const rows = React.useMemo(() => query.data ?? [], [query.data])

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
    <div className="overflow-x-auto rounded-lg border bg-background">
      <Table className="border-separate border-spacing-y-1">
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead className="w-36">Segnalazione</TableHead>
            <TableHead className="w-32">Commessa</TableHead>
            <TableHead className="min-w-[12rem]">Cliente</TableHead>
            <TableHead className="w-24 text-center">Scad.(ord)</TableHead>
            <TableHead className="min-w-[14rem]">Step SAL</TableHead>
            <TableHead className="w-16 text-center">%</TableHead>
            <TableHead className="w-44">Condizione</TableHead>
            <TableHead className="w-32 text-right">Importo</TableHead>
            <TableHead className="w-48 text-center">Ipotesi Fatturazione</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row, idx) => {
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
                <TableCell className="py-1.5 align-middle text-right font-mono text-xs font-semibold tabular-nums pr-3">
                  {formattedImporto}
                </TableCell>
                <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                  {row.dataFatt ? formatDateWithWeekday(row.dataFatt) : "—"}
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
