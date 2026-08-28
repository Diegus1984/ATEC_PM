import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Download, Printer, Clock, Briefcase, Building, TrendingUp } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchDepartmentsLookup } from "@/lib/api/departments"
import { fetchHrQuadratura } from "@/lib/api/hr"
import type { HrQuadraturaDepartment, HrQuadraturaMonth, HrQuadraturaRow } from "@/lib/api/types"
import { printHtml } from "@/lib/print-template"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

const COLUMNS: { id: string; label: string }[] = [
  { id: "department", label: "Reparto" },
  { id: "presenze", label: "Ore presenze" },
  { id: "direct", label: "Commesse dirette" },
  { id: "internal", label: "Ore interne / indirette" },
  { id: "absence", label: "Ore assenze" },
  { id: "timesheet", label: "Totale timesheet" },
  { id: "diff", label: "Delta (h)" },
  { id: "coverage", label: "% Copertura" },
]
const COLUMNS_DEFAULT = Object.fromEntries(COLUMNS.map((c) => [c.id, true]))
const COLUMNS_STORAGE_KEY = "hr-quadratura-columns-v1"

interface QuadraturaPresenzeViewProps {
  anno: number
  mese: number
}

export function QuadraturaPresenzeView({ anno, mese }: QuadraturaPresenzeViewProps) {
  const [departmentId, setDepartmentId] = React.useState<number | null>(null)

  const [visible, setVisible] = usePersistedColumnVisibility(
    COLUMNS_STORAGE_KEY,
    COLUMNS_DEFAULT
  )
  const columnToggles = COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: visible[id] ?? true,
    onToggle: (value: boolean) => setVisible((prev) => ({ ...prev, [id]: value })),
  }))
  const show = (id: string) => visible[id] ?? true

  const deptsQuery = useQuery({
    queryKey: ["departments-lookup"],
    queryFn: fetchDepartmentsLookup,
  })

  const quadraturaQuery = useQuery({
    queryKey: ["hr-quadratura", anno, mese, departmentId],
    queryFn: () => fetchHrQuadratura(anno, mese, departmentId),
  })

  const data: HrQuadraturaMonth | undefined = quadraturaQuery.data
  const rows: HrQuadraturaRow[] = data?.rows ?? []
  const depts: HrQuadraturaDepartment[] = data?.departments ?? []

  const meseLabel = new Date(anno, mese - 1, 1).toLocaleDateString("it-IT", {
    month: "long",
    year: "numeric",
  })

  // Export CSV
  const handleExportCsv = () => {
    if (!data) return
    const headers = [
      "Dipendente",
      "Reparto",
      "Ore Presenze",
      "Ore Commesse Dirette",
      "Ore Interne/Indirette",
      "Ore Assenze",
      "Totale Timesheet",
      "Delta (Ore)",
      "Copertura %",
    ]

    const csvRows = rows.map((r: HrQuadraturaRow) => [
      `"${r.employeeName.replace(/"/g, '""')}"`,
      `"${(r.departmentName ?? "Senza reparto").replace(/"/g, '""')}"`,
      r.presenzeHours.toFixed(1),
      r.directTimesheetHours.toFixed(1),
      r.internalTimesheetHours.toFixed(1),
      r.absenceHours.toFixed(1),
      r.totalTimesheetHours.toFixed(1),
      r.differenceHours.toFixed(1),
      `${r.coveragePercent.toFixed(1)}%`,
    ])

    const csvContent =
      "\uFEFF" + [headers.join(";"), ...csvRows.map((e: string[]) => e.join(";"))].join("\r\n")
    const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" })
    const url = URL.createObjectURL(blob)
    const link = document.createElement("a")
    link.setAttribute("href", url)
    link.setAttribute(
      "download",
      `quadratura_presenze_commesse_${anno}_${String(mese).padStart(2, "0")}.csv`
    )
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
  }

  // Stampa ufficiale
  const handlePrint = () => {
    if (!data) return

    const summaryHtml = `
      <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 18px;">
        <div style="padding: 10px; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc;">
          <div style="font-size: 10px; text-transform: uppercase; color: #64748b;">Ore Presenze Totali</div>
          <div style="font-size: 16px; font-weight: bold; color: #0f172a;">${data.totalPresenzeHours.toFixed(1)} h</div>
        </div>
        <div style="padding: 10px; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc;">
          <div style="font-size: 10px; text-transform: uppercase; color: #64748b;">Commesse Dirette</div>
          <div style="font-size: 16px; font-weight: bold; color: #16a34a;">${data.totalDirectHours.toFixed(1)} h</div>
        </div>
        <div style="padding: 10px; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc;">
          <div style="font-size: 10px; text-transform: uppercase; color: #64748b;">Ore Interne / Indirette</div>
          <div style="font-size: 16px; font-weight: bold; color: #64748b;">${data.totalInternalHours.toFixed(1)} h</div>
        </div>
        <div style="padding: 10px; border: 1px solid #e2e8f0; border-radius: 6px; background: #f8fafc;">
          <div style="font-size: 10px; text-transform: uppercase; color: #64748b;">Indice Copertura Totale</div>
          <div style="font-size: 16px; font-weight: bold; color: #2563eb;">${data.overallCoveragePercent.toFixed(1)} %</div>
        </div>
      </div>
    `

    const deptsTableHtml = `
      <h3 style="font-size: 13px; font-weight: 600; margin: 16px 0 8px 0;">Riepilogo per Reparto</h3>
      <table style="width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 20px;">
        <thead>
          <tr style="background: #f1f5f9; border-bottom: 2px solid #cbd5e1;">
            <th style="padding: 6px 8px; text-align: left;">Reparto</th>
            <th style="padding: 6px 8px; text-align: right;">Ore Presenze</th>
            <th style="padding: 6px 8px; text-align: right;">Commesse Dirette</th>
            <th style="padding: 6px 8px; text-align: right;">Ore Interne</th>
            <th style="padding: 6px 8px; text-align: right;">Totale Timesheet</th>
            <th style="padding: 6px 8px; text-align: right;">Delta (h)</th>
            <th style="padding: 6px 8px; text-align: right;">% Copertura</th>
          </tr>
        </thead>
        <tbody>
          ${depts
            .map(
              (d: HrQuadraturaDepartment) => `
            <tr style="border-bottom: 1px solid #e2e8f0;">
              <td style="padding: 6px 8px; font-weight: 600;">${d.departmentName}</td>
              <td style="padding: 6px 8px; text-align: right; font-family: monospace;">${d.totalPresenzeHours.toFixed(1)}</td>
              <td style="padding: 6px 8px; text-align: right; font-family: monospace;">${d.totalDirectHours.toFixed(1)}</td>
              <td style="padding: 6px 8px; text-align: right; font-family: monospace;">${d.totalInternalHours.toFixed(1)}</td>
              <td style="padding: 6px 8px; text-align: right; font-family: monospace; font-weight: 600;">${d.totalTimesheetHours.toFixed(1)}</td>
              <td style="padding: 6px 8px; text-align: right; font-family: monospace; color: ${d.differenceHours < -4 ? "#dc2626" : "#0f172a"};">${d.differenceHours > 0 ? "+" : ""}${d.differenceHours.toFixed(1)}</td>
              <td style="padding: 6px 8px; text-align: right; font-weight: 600; color: ${d.coveragePercent >= 90 ? "#16a34a" : "#d97706"};">${d.coveragePercent.toFixed(1)}%</td>
            </tr>
          `
            )
            .join("")}
        </tbody>
      </table>
    `

    const empTableHtml = `
      <h3 style="font-size: 13px; font-weight: 600; margin: 16px 0 8px 0;">Dettaglio Dipendenti</h3>
      <table style="width: 100%; border-collapse: collapse; font-size: 10px;">
        <thead>
          <tr style="background: #f1f5f9; border-bottom: 2px solid #cbd5e1;">
            <th style="padding: 4px 6px; text-align: left;">Dipendente</th>
            <th style="padding: 4px 6px; text-align: left;">Reparto</th>
            <th style="padding: 4px 6px; text-align: right;">Presenze (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Dirette (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Interne (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Assenze (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Tot. Timesheet (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Delta (h)</th>
            <th style="padding: 4px 6px; text-align: right;">Copertura %</th>
          </tr>
        </thead>
        <tbody>
          ${rows
            .map(
              (r: HrQuadraturaRow) => `
            <tr style="border-bottom: 1px solid #f1f5f9;">
              <td style="padding: 4px 6px; font-weight: 500;">${r.employeeName}</td>
              <td style="padding: 4px 6px; color: #64748b;">${r.departmentName ?? "—"}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace;">${r.presenzeHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace;">${r.directTimesheetHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace;">${r.internalTimesheetHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace;">${r.absenceHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace; font-weight: 600;">${r.totalTimesheetHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-family: monospace;">${r.differenceHours > 0 ? "+" : ""}${r.differenceHours.toFixed(1)}</td>
              <td style="padding: 4px 6px; text-align: right; font-weight: 600;">${r.coveragePercent.toFixed(1)}%</td>
            </tr>
          `
            )
            .join("")}
        </tbody>
      </table>
    `

    printHtml({
      title: `Quadratura Presenze vs Commesse — ${meseLabel}`,
      subtitle: `Controllo di gestione e allineamento ore rilevate / consuntivate`,
      contentHtml: summaryHtml + deptsTableHtml + empTableHtml,
      orientation: "landscape",
    })
  }

  return (
    <div className="space-y-4">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Select
            value={departmentId == null ? "all" : String(departmentId)}
            onValueChange={(val) => setDepartmentId(val === "all" ? null : Number(val))}
          >
            <SelectTrigger className="w-48 text-xs">
              <SelectValue placeholder="Tutti i reparti" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tutti i reparti</SelectItem>
              {(deptsQuery.data ?? []).map((d) => (
                <SelectItem key={d.id} value={String(d.id)}>
                  {d.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <ColumnsMenu columns={columnToggles} />
          <Button variant="outline" size="sm" onClick={handleExportCsv} disabled={!data}>
            <Download className="mr-1 size-3.5" />
            CSV
          </Button>
          <Button variant="outline" size="sm" onClick={handlePrint} disabled={!data}>
            <Printer className="mr-1 size-3.5" />
            Stampa
          </Button>
        </div>
      </div>

      {/* KPI Cards */}
      {data && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Card className="py-2.5">
            <CardHeader className="py-0 px-4 pb-1">
              <CardTitle className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Clock className="size-3.5 text-blue-500" />
                Ore Presenze
              </CardTitle>
            </CardHeader>
            <CardContent className="py-0 px-4">
              <div className="text-xl font-bold font-mono">
                {data.totalPresenzeHours.toFixed(1)} <span className="text-xs font-normal text-muted-foreground">h</span>
              </div>
              <p className="text-[11px] text-muted-foreground">Da timbrature e forfait</p>
            </CardContent>
          </Card>

          <Card className="py-2.5">
            <CardHeader className="py-0 px-4 pb-1">
              <CardTitle className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Briefcase className="size-3.5 text-emerald-500" />
                Commesse Dirette
              </CardTitle>
            </CardHeader>
            <CardContent className="py-0 px-4">
              <div className="text-xl font-bold font-mono text-emerald-600 dark:text-emerald-400">
                {data.totalDirectHours.toFixed(1)} <span className="text-xs font-normal text-muted-foreground">h</span>
              </div>
              <p className="text-[11px] text-muted-foreground">Consuntivate su clienti</p>
            </CardContent>
          </Card>

          <Card className="py-2.5">
            <CardHeader className="py-0 px-4 pb-1">
              <CardTitle className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Building className="size-3.5 text-slate-500" />
                Ore Interne / Indirette
              </CardTitle>
            </CardHeader>
            <CardContent className="py-0 px-4">
              <div className="text-xl font-bold font-mono">
                {data.totalInternalHours.toFixed(1)} <span className="text-xs font-normal text-muted-foreground">h</span>
              </div>
              <p className="text-[11px] text-muted-foreground">Formazione, riunioni, manutenzioni</p>
            </CardContent>
          </Card>

          <Card className="py-2.5">
            <CardHeader className="py-0 px-4 pb-1">
              <CardTitle className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <TrendingUp className="size-3.5 text-indigo-500" />
                Copertura Complessiva
              </CardTitle>
            </CardHeader>
            <CardContent className="py-0 px-4">
              <div className="flex items-center gap-2">
                <div className="text-xl font-bold font-mono">
                  {data.overallCoveragePercent.toFixed(1)}%
                </div>
                {data.overallCoveragePercent >= 90 ? (
                  <Badge variant="outline" className="bg-emerald-500/10 text-emerald-600 border-emerald-500/30 text-[10px] py-0">
                    Ottima
                  </Badge>
                ) : (
                  <Badge variant="outline" className="bg-amber-500/10 text-amber-600 border-amber-500/30 text-[10px] py-0">
                    Discrepanze
                  </Badge>
                )}
              </div>
              <p className="text-[11px] text-muted-foreground">Timesheet su Presenze</p>
            </CardContent>
          </Card>
        </div>
      )}

      {/* Tabella Riepilogo Reparti */}
      {depts.length > 0 && (
        <Card>
          <CardHeader className="py-3 px-4">
            <CardTitle className="text-sm font-semibold">Riepilogo per Reparto</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Reparto</TableHead>
                  <TableHead className="text-right">Ore Presenze</TableHead>
                  <TableHead className="text-right">Commesse Dirette</TableHead>
                  <TableHead className="text-right">Ore Interne</TableHead>
                  <TableHead className="text-right">Ore Assenze</TableHead>
                  <TableHead className="text-right">Totale Timesheet</TableHead>
                  <TableHead className="text-right">Delta</TableHead>
                  <TableHead className="text-right">% Copertura</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {depts.map((d: HrQuadraturaDepartment) => (
                  <TableRow key={d.departmentId}>
                    <TableCell className="font-medium">{d.departmentName}</TableCell>
                    <TableCell className="text-right font-mono text-xs">{d.totalPresenzeHours.toFixed(1)} h</TableCell>
                    <TableCell className="text-right font-mono text-xs text-emerald-600 dark:text-emerald-400 font-medium">
                      {d.totalDirectHours.toFixed(1)} h
                    </TableCell>
                    <TableCell className="text-right font-mono text-xs text-muted-foreground">
                      {d.totalInternalHours.toFixed(1)} h
                    </TableCell>
                    <TableCell className="text-right font-mono text-xs text-muted-foreground">
                      {d.totalAbsenceHours.toFixed(1)} h
                    </TableCell>
                    <TableCell className="text-right font-mono text-xs font-semibold">
                      {d.totalTimesheetHours.toFixed(1)} h
                    </TableCell>
                    <TableCell className={cn(
                      "text-right font-mono text-xs font-medium",
                      d.differenceHours < -4 ? "text-destructive" : d.differenceHours > 4 ? "text-amber-600" : "text-muted-foreground"
                    )}>
                      {d.differenceHours > 0 ? `+${d.differenceHours.toFixed(1)}` : d.differenceHours.toFixed(1)} h
                    </TableCell>
                    <TableCell className="text-right">
                      <span className={cn(
                        "inline-block rounded px-1.5 py-0.5 text-xs font-semibold",
                        d.coveragePercent >= 95 ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400" :
                        d.coveragePercent >= 80 ? "bg-amber-500/10 text-amber-600 dark:text-amber-400" :
                        "bg-destructive/10 text-destructive"
                      )}>
                        {d.coveragePercent.toFixed(1)}%
                      </span>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {/* Tabella Dettaglio Dipendenti */}
      {quadraturaQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento quadratura…</p>
      ) : quadraturaQuery.error ? (
        <p className="text-sm text-destructive">{(quadraturaQuery.error as Error).message}</p>
      ) : rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nessun dipendente trovato per il periodo selezionato.</p>
      ) : (
        <GridScroller className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Dipendente</TableHead>
                {show("department") && <TableHead>Reparto</TableHead>}
                {show("presenze") && <TableHead className="text-right">Presenze</TableHead>}
                {show("direct") && <TableHead className="text-right">Commesse Dirette</TableHead>}
                {show("internal") && <TableHead className="text-right">Ore Interne</TableHead>}
                {show("absence") && <TableHead className="text-right">Assenze</TableHead>}
                {show("timesheet") && <TableHead className="text-right font-semibold">Tot. Timesheet</TableHead>}
                {show("diff") && <TableHead className="text-right">Delta</TableHead>}
                {show("coverage") && <TableHead className="text-right">% Copertura</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r: HrQuadraturaRow) => (
                <TableRow key={r.employeeId}>
                  <TableCell className="font-medium text-xs whitespace-nowrap">
                    {r.employeeName}
                  </TableCell>
                  {show("department") && (
                    <TableCell className="text-xs text-muted-foreground whitespace-nowrap">
                      {r.departmentName ?? "—"}
                    </TableCell>
                  )}
                  {show("presenze") && (
                    <TableCell className="text-right font-mono text-xs">
                      {r.presenzeHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("direct") && (
                    <TableCell className="text-right font-mono text-xs text-emerald-600 dark:text-emerald-400">
                      {r.directTimesheetHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("internal") && (
                    <TableCell className="text-right font-mono text-xs text-muted-foreground">
                      {r.internalTimesheetHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("absence") && (
                    <TableCell className="text-right font-mono text-xs text-muted-foreground">
                      {r.absenceHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("timesheet") && (
                    <TableCell className="text-right font-mono text-xs font-semibold">
                      {r.totalTimesheetHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("diff") && (
                    <TableCell
                      className={cn(
                        "text-right font-mono text-xs font-medium",
                        r.differenceHours < -4
                          ? "text-destructive"
                          : r.differenceHours > 4
                          ? "text-amber-600"
                          : "text-muted-foreground"
                      )}
                    >
                      {r.differenceHours > 0 ? `+${r.differenceHours.toFixed(1)}` : r.differenceHours.toFixed(1)} h
                    </TableCell>
                  )}
                  {show("coverage") && (
                    <TableCell className="text-right">
                      <span
                        className={cn(
                          "inline-block rounded px-1.5 py-0.5 text-xs font-semibold",
                          r.coveragePercent >= 95
                            ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                            : r.coveragePercent >= 80
                            ? "bg-amber-500/10 text-amber-600 dark:text-amber-400"
                            : "bg-destructive/10 text-destructive"
                        )}
                      >
                        {r.coveragePercent.toFixed(1)}%
                      </span>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </GridScroller>
      )}
    </div>
  )
}
