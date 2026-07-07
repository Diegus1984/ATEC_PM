import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import {
  ArrowLeft,
  ArrowLeftRight,
  ChevronRight,
  FileSpreadsheet,
  Printer,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Collapsible } from "@/components/ui/collapsible"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchDdpSummary } from "@/lib/api/ddp-manager"
import {
  fetchDdpAggregations,
  fetchDdpStatuses,
} from "@/lib/api/ddp-config"
import { fetchDdpRows } from "@/lib/api/project-ddp"
import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { cn } from "@/lib/utils"

import {
  barWidthPercent,
  buildSintesiModel,
  type BarRow,
  type DdpSintesiModel,
} from "./ddp-sintesi-logic"
import { exportDdpExcel, printDdpTables, type ExportTable } from "./ddp-export"
import { DdpOverviewPie } from "./DdpOverviewPie"
import { DdpStatusBreakdown } from "./DdpStatusBreakdown"
import {
  ddpRowToSintesiCells,
  sintesiTableHeaders,
} from "./ddp-sintesi-table"

const MULTI_OPEN_KEY = "ddp.sintesi.multiOpen"

// ── Sotto-componenti ────────────────────────────────────────────

function KpiCard({
  label,
  value,
  valueClassName,
  small,
}: {
  label: string
  value: React.ReactNode
  valueClassName?: string
  small?: boolean
}) {
  return (
    <div className="w-[170px] rounded-xl border bg-card px-3.5 py-2.5 shadow-xs">
      <div
        className={cn(
          "font-bold tabular-nums",
          small ? "text-sm" : "text-xl",
          valueClassName
        )}
      >
        {value}
      </div>
      <div className="mt-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
    </div>
  )
}

function BarList({ bars }: { bars: BarRow[] }) {
  if (bars.length === 0) {
    return <p className="text-sm text-muted-foreground">Nessuna riga.</p>
  }
  return (
    <div className="space-y-2">
      {bars.map((bar, index) => (
        <div key={`${bar.key}-${bar.label}-${index}`} className="flex items-center gap-2">
          {bar.key ? (
            <span
              className="inline-flex w-16 shrink-0 justify-center rounded-full px-2 py-0.5 text-xs font-bold"
              style={{ backgroundColor: bar.bg, color: bar.fg }}
            >
              {bar.key}
            </span>
          ) : null}
          <span className="min-w-0 flex-1 truncate text-sm">{bar.label}</span>
          <div className="h-2 w-52 shrink-0 overflow-hidden rounded-full bg-muted">
            <div
              className="h-full rounded-full"
              style={{
                width: `${barWidthPercent(bar.fraction, bar.count)}%`,
                backgroundColor: bar.bg,
              }}
            />
          </div>
          <span className="w-10 shrink-0 text-right text-sm font-semibold tabular-nums">
            {bar.count}
          </span>
          <span className="w-12 shrink-0 text-right text-xs text-muted-foreground">
            {bar.pct}
          </span>
        </div>
      ))}
    </div>
  )
}

function RowsTable({
  rows,
  statusDefs,
  statoLabel,
  officina,
  markOverdue,
}: {
  rows: DdpRowItem[]
  statusDefs: Map<string, DdpStatusItem>
  statoLabel: (key: string) => string
  officina: boolean
  markOverdue?: boolean
}) {
  const headers = sintesiTableHeaders(officina)
  if (rows.length === 0) {
    return <p className="text-sm text-muted-foreground">Nessuna riga.</p>
  }
  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table className="min-w-[1400px] text-xs">
        <TableHeader className="bg-muted/50">
          <TableRow>
            {headers.map((header) => (
              <TableHead key={header} className="h-8 whitespace-nowrap">
                {header}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const def = statusDefs.get(row.itemStatus)
            const style = def
              ? { backgroundColor: def.colorBg, color: def.colorFg }
              : undefined
            const cells = ddpRowToSintesiCells(row, officina, statoLabel, {
              markOverdue,
            })
            return (
              <TableRow key={row.id} style={style}>
                {cells.map((cell, index) => (
                  <TableCell
                    key={index}
                    className={cn(
                      index === 4 || index === headers.length - 2
                        ? "min-w-[160px]"
                        : "whitespace-nowrap",
                      index === 0 || index === 5 ? "tabular-nums" : undefined,
                      index >= headers.length - 2 ? "tabular-nums" : undefined
                    )}
                  >
                    {cell}
                  </TableCell>
                ))}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}

function Section({
  id,
  title,
  open,
  onToggle,
  onPrint,
  children,
}: {
  id: string
  title: string
  open: boolean
  onToggle: (id: string) => void
  onPrint: () => void
  children: React.ReactNode
}) {
  return (
    <div className="rounded-lg border bg-card">
      <div className="flex items-center justify-between gap-2 px-3 py-2">
        <button
          type="button"
          aria-expanded={open}
          className="flex flex-1 items-center gap-2 text-left text-sm font-semibold"
          onClick={() => onToggle(id)}
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 text-muted-foreground transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
              open && "rotate-90"
            )}
          />
          {title}
        </button>
        <Button variant="outline" size="xs" onClick={onPrint}>
          <Printer className="size-3" />
          PDF
        </Button>
      </div>
      <Collapsible open={open}>
        <div className="border-t p-3">{children}</div>
      </Collapsible>
    </div>
  )
}

// ── Pagina ──────────────────────────────────────────────────────

export function DdpSintesiPage() {
  const params = useParams<{ projectId: string }>()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const projectId = Number(params.projectId)
  const type = (searchParams.get("type") || "COMMERCIAL").toUpperCase()
  const officina = type === "OFFICINA"

  const rowsQuery = useQuery({
    queryKey: ["ddp-rows", projectId, type],
    queryFn: () => fetchDdpRows(projectId, type),
    enabled: Number.isFinite(projectId),
  })
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const aggregationsQuery = useQuery({
    queryKey: ["ddp-aggregations"],
    queryFn: fetchDdpAggregations,
  })
  const summaryQuery = useQuery({
    queryKey: ["ddp-summary"],
    queryFn: fetchDdpSummary,
  })

  useProjectHub(Number.isFinite(projectId) ? projectId : null, (change) => {
    void summaryQuery.refetch()
    if (change.ddpType && change.ddpType.toUpperCase() !== type) return
    void rowsQuery.refetch()
  })

  const statusDefs = React.useMemo(() => {
    const map = new Map<string, DdpStatusItem>()
    for (const status of statusesQuery.data ?? []) map.set(status.statusKey, status)
    return map
  }, [statusesQuery.data])

  const aggSets = React.useMemo(() => {
    const map = new Map<string, Set<string>>()
    for (const agg of aggregationsQuery.data ?? [])
      map.set(agg.code, new Set(agg.statusKeys))
    return map
  }, [aggregationsQuery.data])

  const model: DdpSintesiModel = React.useMemo(
    () =>
      buildSintesiModel({
        rows: rowsQuery.data ?? [],
        statusDefs,
        aggSets,
      }),
    [rowsQuery.data, statusDefs, aggSets]
  )

  const statoLabel = React.useCallback(
    (key: string) => statusDefs.get(key)?.label ?? key,
    [statusDefs]
  )

  const meta =
    summaryQuery.data?.find(
      (item) => item.projectId === projectId && item.ddpType.toUpperCase() === type
    ) ?? summaryQuery.data?.find((item) => item.projectId === projectId)
  const code = meta?.code ?? `#${projectId}`
  const customer = meta?.customerName ?? ""
  const reportHeader = customer ? `${code} — ${customer}` : code

  const otherDdpType = officina ? "COMMERCIAL" : "OFFICINA"
  const otherSummary = React.useMemo(
    () =>
      summaryQuery.data?.find(
        (item) =>
          item.projectId === projectId &&
          item.ddpType.toUpperCase() === otherDdpType &&
          item.totalRows > 0
      ),
    [summaryQuery.data, projectId, otherDdpType]
  )
  const switchSintesiLabel = officina
    ? "Sintesi commerciale"
    : "Sintesi meccanica"

  // ── Accordion ──
  const [multiOpen, setMultiOpen] = React.useState(
    () => localStorage.getItem(MULTI_OPEN_KEY) === "1"
  )
  const [openSections, setOpenSections] = React.useState<Record<string, boolean>>({
    rip: true,
  })

  function toggleSection(id: string) {
    setOpenSections((prev) => {
      const wasOpen = prev[id] ?? false
      if (multiOpen) return { ...prev, [id]: !wasOpen }
      return wasOpen ? {} : { [id]: true }
    })
  }

  function toggleMultiOpen(value: boolean) {
    setMultiOpen(value)
    localStorage.setItem(MULTI_OPEN_KEY, value ? "1" : "0")
  }

  // ── Export ──
  const fullRow = (row: DdpRowItem): string[] =>
    ddpRowToSintesiCells(row, officina, statoLabel)
  const rowColors = (rows: DdpRowItem[]) =>
    rows.map((row) => statusDefs.get(row.itemStatus)?.colorBg ?? null)

  const barTable = (title: string, bars: BarRow[]): ExportTable => ({
    title,
    headers: ["Stato", "Descrizione", "N", "%"],
    rows: bars.map((bar) => [bar.key, bar.label, String(bar.count), bar.pct]),
  })
  const fullTable = (title: string, rows: DdpRowItem[]): ExportTable => ({
    title,
    headers: [...sintesiTableHeaders(officina)],
    rows: rows.map(fullRow),
    rowColors: rowColors(rows),
  })

  const sectionTable: Record<string, () => ExportTable> = {
    avanz: () => ({
      title: "Stati Avanzamento",
      headers: ["Stato", "N", "%"],
      rows: model.avanzamento.map((card) => [
        card.label,
        String(card.count),
        card.pctLabel,
      ]),
    }),
    rip: () => barTable("Ripartizione per stato", model.ripartizione),
    consegne: () => fullTable("Materiale in Consegna", model.consegne),
    consegnato: () => fullTable("Materiale Consegnato", model.consegnato),
    top10: () => ({
      title: "Top 10 Costi",
      headers: ["Pos.", ...sintesiTableHeaders(officina), "% tot."],
      rows: model.top10.map((row) => [
        String(row.rank),
        ...fullRow(row.item),
        row.pctLabel,
      ]),
    }),
    dest: () => ({
      title: "Destinazioni",
      headers: ["Destinazione", "N", "%"],
      rows: model.destinazioni.map((bar) => [bar.label, String(bar.count), bar.pct]),
    }),
    mancanti: () => ({
      // Stampa di sezione: layout per-campo come BuildSectionDoc('mancanti') del WPF.
      title: "Dati Mancanti",
      headers: [
        "Riga",
        "Stato",
        "Descrizione",
        "Stato",
        "Rif. Danea",
        "Data prev.",
        "Destinazione",
        "Costo",
      ],
      rows: model.mancanti.map((row) => [
        String(row.rowNo),
        statoLabel(row.statoKey),
        row.desc,
        row.stato.text,
        row.rif.text,
        row.data.text,
        row.dest.text,
        row.costo.text,
      ]),
    }),
    distinta: () => fullTable("Dati Distinta", model.distinta),
    acq: () => barTable("Feedback Acquisti", model.feedbackAcquisti),
    mag: () => barTable("Feedback Magazzino", model.feedbackMagazzino),
  }

  function printSection(key: string) {
    printDdpTables(`DDP ${code} — ${key}`, reportHeader, [sectionTable[key]()])
  }

  function printReport() {
    // Nel report completo la sezione mancanti è condensata (come BuildReport del WPF).
    const mancantiReport: ExportTable = {
      title: "Dati mancanti",
      headers: ["Riga", "Stato", "Descrizione", "Campi mancanti"],
      rows: model.mancanti.map((row) => [
        String(row.rowNo),
        statoLabel(row.statoKey),
        row.desc,
        row.missingLabel,
      ]),
    }
    printDdpTables(`Sintesi DDP — ${reportHeader}`, reportHeader, [
      sectionTable.rip(),
      sectionTable.top10(),
      sectionTable.consegne(),
      sectionTable.consegnato(),
      sectionTable.dest(),
      mancantiReport,
    ])
  }

  function exportExcel() {
    exportDdpExcel(code, fullTable("Dati Distinta", model.distinta))
  }

  const isOpen = (id: string) => openSections[id] ?? false

  return (
    <div className="flex flex-col gap-4">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-3">
          <Button variant="outline" size="sm" onClick={() => navigate("/gestore-ddp")}>
            <ArrowLeft />
            Indietro
          </Button>
          <span className="text-base font-semibold">
            {officina ? "Sintesi DDP Meccanica" : "Sintesi DDP"} · {reportHeader}
          </span>
        </div>
        <div className="flex flex-wrap gap-2">
          {otherSummary ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() =>
                navigate(
                  `/gestore-ddp/${projectId}?type=${otherDdpType}`
                )
              }
            >
              <ArrowLeftRight />
              {switchSintesiLabel}
            </Button>
          ) : null}
          <Button variant="outline" size="sm" onClick={exportExcel}>
            <FileSpreadsheet />
            Esporta Excel
          </Button>
          <Button variant="outline" size="sm" onClick={printReport}>
            <Printer />
            Stampa
          </Button>
        </div>
      </div>

      {rowsQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : rowsQuery.isError ? (
        <p className="text-sm text-destructive">
          {(rowsQuery.error as Error).message}
        </p>
      ) : (
        <>
          {/* KPI */}
          <div className="flex flex-wrap gap-3">
            <KpiCard
              label="Tot. acquisti"
              value={euro(model.kpi.totValue)}
              valueClassName="text-red-700 dark:text-red-400"
            />
            <KpiCard label="Inserimenti" value={model.kpi.count} />
            <KpiCard label="Finestra consegne" value={model.kpi.finestra} small />
            <KpiCard label="Mat. in consegna" value={model.kpi.datedCount} />
            <KpiCard
              label="Mat. in ritardo"
              value={model.kpi.overdue}
              valueClassName={model.kpi.overdue > 0 ? "text-amber-600" : ""}
            />
            <KpiCard label="Mat. consegnato" value={model.kpi.consegnato} />
            <KpiCard label="Mat. parziali" value={model.kpi.parziali} />
          </div>

          <DdpOverviewPie
            bars={model.ripartizione}
            variant={officina ? "officina" : "commercial"}
          />

          {/* Stati Avanzamento */}
          <div>
            <div className="mb-2 flex items-center justify-between">
              <div>
                <h3 className="text-sm font-semibold">Stati Avanzamento</h3>
                <p className="text-xs text-muted-foreground">{model.avanzSub}</p>
              </div>
              <Button
                variant="outline"
                size="xs"
                onClick={() => printSection("avanz")}
              >
                <Printer className="size-3" />
                PDF
              </Button>
            </div>
            <div className="flex flex-wrap gap-2.5">
              {model.avanzamento.map((card) => (
                <div
                  key={card.label}
                  className="w-[132px] rounded-xl border px-3 py-2.5"
                  style={{ backgroundColor: card.bg, borderColor: card.border }}
                >
                  <div className="truncate text-[10px] font-semibold text-slate-600">
                    {card.label}
                  </div>
                  <div className="mt-1 text-2xl font-bold text-slate-900 tabular-nums">
                    {card.count}
                  </div>
                  <div className="mt-0.5 text-[10px] text-slate-500">
                    {card.pctLabel}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Opzione locale */}
          <Label className="ml-auto flex items-center gap-2 text-sm font-normal">
            <Checkbox
              checked={multiOpen}
              onCheckedChange={(checked) => toggleMultiOpen(checked === true)}
            />
            Più sezioni aperte
          </Label>

          {/* Accordion */}
          <div className="space-y-2">
            <Section
              id="rip"
              title="Ripartizione per stato"
              open={isOpen("rip")}
              onToggle={toggleSection}
              onPrint={() => printSection("rip")}
            >
              <DdpStatusBreakdown
                bars={model.ripartizione}
                subtitle={model.ripSub}
                sectionLabel="Ripartizione per stato"
              />
            </Section>

            <Section
              id="consegne"
              title="Materiale in Consegna"
              open={isOpen("consegne")}
              onToggle={toggleSection}
              onPrint={() => printSection("consegne")}
            >
              <RowsTable
                rows={model.consegne}
                statusDefs={statusDefs}
                statoLabel={statoLabel}
                officina={officina}
                markOverdue
              />
              <p className="mt-2 text-xs text-muted-foreground">{model.consegneSub}</p>
            </Section>

            <Section
              id="consegnato"
              title="Materiale Consegnato"
              open={isOpen("consegnato")}
              onToggle={toggleSection}
              onPrint={() => printSection("consegnato")}
            >
              <RowsTable
                rows={model.consegnato}
                statusDefs={statusDefs}
                statoLabel={statoLabel}
                officina={officina}
              />
              <p className="mt-2 text-xs text-muted-foreground">{model.consegnatoSub}</p>
            </Section>

            <Section
              id="top10"
              title="Top 10 Costi"
              open={isOpen("top10")}
              onToggle={toggleSection}
              onPrint={() => printSection("top10")}
            >
              <div className="overflow-x-auto rounded-lg border">
                <Table className="min-w-[1500px] text-xs">
                  <TableHeader className="bg-muted/50">
                    <TableRow>
                      <TableHead className="h-8">Pos.</TableHead>
                      {sintesiTableHeaders(officina).map((header) => (
                        <TableHead key={header} className="h-8 whitespace-nowrap">
                          {header}
                        </TableHead>
                      ))}
                      <TableHead className="h-8">% tot.</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {model.top10.map((row) => {
                      const def = statusDefs.get(row.item.itemStatus)
                      const style = def
                        ? { backgroundColor: def.colorBg, color: def.colorFg }
                        : undefined
                      return (
                        <TableRow key={row.item.id} style={style}>
                          <TableCell className="tabular-nums">{row.rank}</TableCell>
                          {fullRow(row.item).map((cell, index) => (
                            <TableCell
                              key={index}
                              className={index === 4 || index === 14 ? "min-w-[160px]" : "whitespace-nowrap"}
                            >
                              {cell}
                            </TableCell>
                          ))}
                          <TableCell className="whitespace-nowrap tabular-nums">
                            {row.pctLabel}
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </div>
            </Section>

            <Section
              id="dest"
              title="Destinazioni"
              open={isOpen("dest")}
              onToggle={toggleSection}
              onPrint={() => printSection("dest")}
            >
              <p className="mb-3 text-xs text-muted-foreground">{model.destSub}</p>
              <BarList bars={model.destinazioni} />
            </Section>

            <Section
              id="mancanti"
              title="Dati Mancanti"
              open={isOpen("mancanti")}
              onToggle={toggleSection}
              onPrint={() => printSection("mancanti")}
            >
              <p className="mb-2 text-xs text-muted-foreground">{model.mancantiSub}</p>
              {model.mancanti.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  Nessuna riga con dati mancanti.
                </p>
              ) : (
                <div className="overflow-x-auto rounded-lg border">
                  <Table className="min-w-[760px] text-xs">
                    <TableHeader className="bg-muted/50">
                      <TableRow>
                        <TableHead className="h-8 w-14">Riga</TableHead>
                        <TableHead className="h-8">Stato</TableHead>
                        <TableHead className="h-8 min-w-[180px]">Descrizione</TableHead>
                        <TableHead className="h-8 text-center">Stato</TableHead>
                        <TableHead className="h-8 text-center">Rif. Danea</TableHead>
                        <TableHead className="h-8 text-center">Data Prev.</TableHead>
                        <TableHead className="h-8 text-center">Destinazione</TableHead>
                        <TableHead className="h-8 text-center">Costo</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {model.mancanti.map((row) => {
                        const def = statusDefs.get(row.statoKey)
                        const style = def
                          ? { backgroundColor: def.colorBg, color: def.colorFg }
                          : undefined
                        const flag = (cell: { text: string; missing: boolean }) => (
                          <span
                            className="font-semibold"
                            style={{ color: cell.missing ? row.flagColor : "#94A3B8" }}
                          >
                            {cell.text}
                          </span>
                        )
                        return (
                          <TableRow key={row.rowNo} style={style}>
                            <TableCell className="tabular-nums">{row.rowNo}</TableCell>
                            <TableCell className="whitespace-nowrap">
                              {statoLabel(row.statoKey)}
                            </TableCell>
                            <TableCell className="min-w-[180px]">{row.desc}</TableCell>
                            <TableCell className="text-center">{flag(row.stato)}</TableCell>
                            <TableCell className="text-center">{flag(row.rif)}</TableCell>
                            <TableCell className="text-center">{flag(row.data)}</TableCell>
                            <TableCell className="text-center">{flag(row.dest)}</TableCell>
                            <TableCell className="text-center">{flag(row.costo)}</TableCell>
                          </TableRow>
                        )
                      })}
                    </TableBody>
                  </Table>
                </div>
              )}
            </Section>

            <Section
              id="distinta"
              title="Dati Distinta"
              open={isOpen("distinta")}
              onToggle={toggleSection}
              onPrint={() => printSection("distinta")}
            >
              <RowsTable
                rows={model.distinta}
                statusDefs={statusDefs}
                statoLabel={statoLabel}
                officina={officina}
              />
            </Section>

            <Section
              id="acq"
              title="Feedback Acquisti"
              open={isOpen("acq")}
              onToggle={toggleSection}
              onPrint={() => printSection("acq")}
            >
              <DdpStatusBreakdown
                bars={model.feedbackAcquisti}
                subtitle={model.acqSub}
                sectionLabel="Feedback acquisti"
              />
            </Section>

            <Section
              id="mag"
              title="Feedback Magazzino"
              open={isOpen("mag")}
              onToggle={toggleSection}
              onPrint={() => printSection("mag")}
            >
              <DdpStatusBreakdown
                bars={model.feedbackMagazzino}
                subtitle={model.magSub}
                sectionLabel="Feedback magazzino"
              />
            </Section>
          </div>
        </>
      )}
    </div>
  )
}
