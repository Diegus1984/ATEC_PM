import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Calendar, ChevronLeft, ChevronRight, Printer, RefreshCw } from "lucide-react"
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  LabelList,
  Line,
  XAxis,
  YAxis,
} from "recharts"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { printHtml } from "@/lib/print-template"
import { Button } from "@/components/ui/button"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import { ApiError } from "@/lib/api/client"
import { fetchSalEconomics } from "@/lib/api/sal"
import { dateToIso, formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"
import {
  drillPill,
  drillRows,
  monthlySeries,
  salYm,
  salYmTitle,
  type SalBucket,
  type SalDrillKind,
  type SalMonthPoint,
  type SalPillTone,
} from "./sal-economics"

// Analisi Cash Flow SAL (Fase 5 v10): barre mensili impilate per mese di
// Ipotesi Fatturazione (Incassate / Emesse / da Fatturare) + linea blu
// «Incasso previsto» per mese di Data Prevista Saldo. Ogni segmento, etichetta
// totale e punto della linea è cliccabile → dialog drill-down con le righe.

const COLORS: Record<"inc" | "em" | "daf" | "prev", string> = {
  inc: "#3FA45E",
  em: "#E0A93B",
  daf: "#9AA7B4",
  prev: "#2F6DB0",
}

const chartConfig = {
  inc: { label: "Incassate", color: COLORS.inc },
  em: { label: "Emesse", color: COLORS.em },
  daf: { label: "da Fatturare", color: COLORS.daf },
  prev: { label: "Incasso previsto", color: COLORS.prev },
} satisfies ChartConfig

const KIND_LABEL: Record<SalDrillKind, string> = {
  inc: "Fatture Incassate",
  em: "Fatture Emesse",
  daf: "da Fatturare",
  bar: "Totale del mese",
  prev: "Incasso previsto",
}

const PILL_CLASSES: Record<SalPillTone, string> = {
  green: "bg-emerald-50 text-emerald-700 border-emerald-200",
  red: "bg-red-50 text-red-700 border-red-200",
  amber: "bg-yellow-50 text-yellow-700 border-yellow-200",
  sky: "bg-sky-50 text-sky-700 border-sky-200",
  muted: "bg-muted text-muted-foreground border-border",
}

/** Larghezza minima per mese (px) per far respirare barre + etichette. */
const MONTH_MIN_WIDTH = 74

/** Formato compatto per etichette totale e asse Y: intero it-IT. */
const n0 = (v: number): string => Math.round(v).toLocaleString("it-IT")

/** Estrae `ym` dal payload di un evento recharts (shape difensivo). */
function pickYm(arg: unknown): string | null {
  if (!arg || typeof arg !== "object") return null
  const obj = arg as { ym?: unknown; payload?: { ym?: unknown } }
  if (typeof obj.ym === "string") return obj.ym
  if (obj.payload && typeof obj.payload.ym === "string") return obj.payload.ym
  return null
}

/**
 * Markup del grafico per il popup di stampa: clona il nodo ChartContainer
 * (`[data-chart]`, che include lo <style> con i token `--color-*`), garantisce
 * il viewBox dell'SVG e lo porta a larghezza 100%. Ritorna null se l'SVG non è
 * (ancora) renderizzato: in quel caso si stampa la sola tabella.
 */
function chartHtmlForPrint(wrap: HTMLDivElement | null): string | null {
  const container = wrap?.querySelector<HTMLElement>("[data-chart]")
  const svg = container?.querySelector("svg")
  if (!container || !svg) return null
  const clone = container.cloneNode(true) as HTMLElement
  const svgClone = clone.querySelector("svg")
  if (!svgClone) return null
  if (!svgClone.getAttribute("viewBox")) {
    const width = Number(svg.getAttribute("width")) || svg.clientWidth
    const height = Number(svg.getAttribute("height")) || svg.clientHeight
    if (width > 0 && height > 0) {
      svgClone.setAttribute("viewBox", `0 0 ${width} ${height}`)
    }
  }
  svgClone.setAttribute("width", "100%")
  svgClone.removeAttribute("height")
  return clone.outerHTML
}

/**
 * Stampa dell'Analisi (parità v10): grafico vero (SVG renderizzato, se
 * disponibile) + tabella mese × categoria con i totali come complemento.
 */
function printAnalisi(
  series: SalMonthPoint[],
  truncated: boolean,
  chartHtml: string | null
): void {
  const tot = series.reduce(
    (acc, p) => ({
      inc: acc.inc + p.inc,
      em: acc.em + p.em,
      daf: acc.daf + p.daf,
      tot: acc.tot + p.tot,
      prev: acc.prev + p.prev,
    }),
    { inc: 0, em: 0, daf: 0, tot: 0, prev: 0 }
  )
  const right = (v: number) => `<td style="text-align:right">${euro(v)}</td>`
  const body = series
    .map(
      (p) =>
        `<tr><td>${p.label}</td>${right(p.inc)}${right(p.em)}${right(p.daf)}` +
        `${right(p.tot)}${right(p.prev)}</tr>`
    )
    .join("")
  const totRow =
    `<tr style="font-weight:600;background:#f3f4f6"><td>Totale</td>` +
    `${right(tot.inc)}${right(tot.em)}${right(tot.daf)}${right(tot.tot)}${right(tot.prev)}</tr>`
  const truncNote = truncated
    ? ` Serie limitata agli ultimi 600 mesi: i mesi più vecchi non sono rappresentati.`
    : ""
  const chartBlock = chartHtml ? `<div class="chart">${chartHtml}</div>` : ""

  const customStyles = `
    .chart{margin:0 0 14px}.chart svg{width:100%;height:auto}.fill-foreground{fill:#111}
    table{border-collapse:collapse;width:100%;font-size:11px}
    th,td{border:1px solid #ccc;padding:4px 8px;text-align:left}th{background:#f3f4f6}
  `

  const contentHtml = `
    ${chartBlock}
    <table>
      <thead>
        <tr>
          <th>Mese</th>
          <th style="text-align:right">Incassate</th>
          <th style="text-align:right">Emesse</th>
          <th style="text-align:right">da Fatturare</th>
          <th style="text-align:right">Totale mese</th>
          <th style="text-align:right">Incasso previsto</th>
        </tr>
      </thead>
      <tbody>${body}${totRow}</tbody>
    </table>
  `

  printHtml({
    title: "Analisi Cash Flow SAL — serie mensile",
    subtitle: `Situazione al ${formatDateShort(new Date())} — barre per mese di Ipotesi Fatturazione; Incasso previsto per mese di Data Prevista Saldo Fattura.${truncNote}`,
    contentHtml,
    orientation: "landscape",
    paperSize: "A4",
    customStyles,
  })
}

export function SalAnalisiView() {
  const query = useQuery({
    queryKey: ["sal", "economics"],
    queryFn: fetchSalEconomics,
    refetchOnWindowFocus: true,
  })

  const rows = query.data?.rows
  const { series, truncated } = React.useMemo(() => monthlySeries(rows ?? []), [rows])
  const todayIso = React.useMemo(() => dateToIso(new Date()), [])

  // Ref sul contenitore scrollabile e sul wrapper del ChartContainer
  const scrollContainerRef = React.useRef<HTMLDivElement>(null)
  const chartRef = React.useRef<HTMLDivElement>(null)

  const scrollToMonth = React.useCallback(
    (targetYm?: string, smooth = true) => {
      const container = scrollContainerRef.current
      if (!container || series.length === 0) return
      const ym = targetYm ?? todayIso.slice(0, 7)
      let idx = series.findIndex((p) => p.ym === ym)
      if (idx === -1) {
        idx = series.findIndex((p) => p.ym > ym)
        if (idx === -1) idx = series.length - 1
      }
      const monthOffset = idx * MONTH_MIN_WIDTH + 72
      const targetScroll = Math.max(
        0,
        monthOffset - container.clientWidth / 2 + MONTH_MIN_WIDTH / 2
      )
      container.scrollTo({ left: targetScroll, behavior: smooth ? "smooth" : "auto" })
    },
    [series, todayIso]
  )

  const hasAutoScrolledRef = React.useRef(false)
  React.useEffect(() => {
    if (series.length > 0 && !hasAutoScrolledRef.current) {
      hasAutoScrolledRef.current = true
      const t = setTimeout(() => scrollToMonth(undefined, false), 60)
      return () => clearTimeout(t)
    }
  }, [series, scrollToMonth])

  // Righe con importo ma senza Ipotesi di Fatturazione: non compaiono tra le
  // barre del grafico → nota informativa sotto la legenda.
  const senzaDataFatt = React.useMemo(
    () => (rows ?? []).filter((r) => (r.importo ?? 0) > 0 && !salYm(r.dataFatt)).length,
    [rows]
  )

  const [drill, setDrill] = React.useState<{ kind: SalDrillKind; ym: string } | null>(null)

  const drillList = React.useMemo(
    () => (drill ? drillRows(rows ?? [], drill.kind, drill.ym) : []),
    [rows, drill]
  )
  const drillTotal = React.useMemo(
    () => drillList.reduce((acc, r) => acc + (r.importo ?? 0), 0),
    [drillList]
  )

  const legendTotals = React.useMemo(
    () =>
      series.reduce(
        (acc, p) => ({
          inc: acc.inc + p.inc,
          em: acc.em + p.em,
          daf: acc.daf + p.daf,
          prev: acc.prev + p.prev,
        }),
        { inc: 0, em: 0, daf: 0, prev: 0 }
      ),
    [series]
  )

  const openDrill = React.useCallback((kind: SalDrillKind, ym: string) => {
    setDrill({ kind, ym })
  }, [])

  /** onClick di un segmento barra: recharts passa l'item con il payload del mese. */
  const barClick = React.useCallback(
    (kind: SalBucket) => (data: unknown) => {
      const ym = pickYm(data)
      if (ym) openDrill(kind, ym)
    },
    [openDrill]
  )

  /**
   * Etichetta totale sopra la barra: cliccabile → drill 'bar'.
   * Usa sempre `payload.ym` (non `series[index]`): con LabelList sul segmento
   * in cima allo stack Recharts può passare un index relativo alle sole celle
   * disegnate (#76: click su ago 26 apriva dicembre 2024).
   * `segment` = quale Bar monta la LabelList; disegna solo se è il pezzo in cima
   * (altrimenti i mesi con daf=0 non avrebbero etichetta, o ci sarebbero doppioni).
   */
  const renderTotalLabelFor = React.useCallback(
    (segment: SalBucket) =>
      (props: unknown): React.ReactElement | null => {
        const { x, y, width, payload } = props as {
          x?: number | string
          y?: number | string
          width?: number | string
          payload?: SalMonthPoint
        }
        if (!payload || x == null || y == null) return null
        const tot = Number(payload.tot ?? 0)
        if (!tot || Number.isNaN(tot)) return null
        // Ordine stack: inc (basso) → em → daf (alto)
        const top: SalBucket | null =
          payload.daf > 0 ? "daf" : payload.em > 0 ? "em" : payload.inc > 0 ? "inc" : null
        if (top !== segment) return null
        const ym = payload.ym
        return (
          <text
            x={Number(x) + Number(width ?? 0) / 2}
            y={Number(y) - 6}
            textAnchor="middle"
            fontSize={10}
            fontWeight={600}
            className={cn("fill-foreground", ym && "cursor-pointer")}
            onClick={() => ym && openDrill("bar", ym)}
          >
            {n0(tot)}
          </text>
        )
      },
    [openDrill]
  )

  /** Punto della linea «Incasso previsto»: cliccabile → drill 'prev'. */
  const prevDot = React.useCallback(
    (props: unknown): React.ReactElement => {
      const { key, cx, cy, payload, index } = props as {
        key?: string
        cx?: number
        cy?: number
        payload?: SalMonthPoint
        index?: number
      }
      const k = key ?? `prev-dot-${index ?? ""}`
      if (cx == null || cy == null || Number.isNaN(cx) || Number.isNaN(cy)) {
        return <g key={k} />
      }
      return (
        <g
          key={k}
          className="cursor-pointer"
          onClick={() => payload?.ym && openDrill("prev", payload.ym)}
        >
          {/* Area di click allargata e invisibile attorno al punto */}
          <circle cx={cx} cy={cy} r={9} fill="transparent" />
          <circle
            cx={cx}
            cy={cy}
            r={3.5}
            fill={COLORS.prev}
            stroke="var(--background)"
            strokeWidth={1}
          />
        </g>
      )
    },
    [openDrill]
  )

  if (query.isLoading) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-muted-foreground justify-center py-12">
        <RefreshCw className="size-4 animate-spin" />
        Caricamento analisi SAL...
      </div>
    )
  }

  if (query.isError) {
    const message =
      query.error instanceof ApiError && query.error.status === 403
        ? "Dati economici riservati ai ruoli PM/ADMIN."
        : (query.error as Error).message
    return <PageErrorAlert message={message} />
  }

  if (series.length === 0) {
    return (
      <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
        <p className="text-sm font-medium text-muted-foreground">
          Nessuna riga SAL con importo e Ipotesi di Fatturazione valorizzata
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-1.5 text-xs">
          <span className="text-muted-foreground mr-1">Centra grafico:</span>
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs px-2.5"
            onClick={() => scrollToMonth(todayIso.slice(0, 7))}
            title="Centra sul mese corrente"
          >
            <Calendar className="size-3.5 mr-1" />
            Mese Corrente
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="h-7 text-xs px-2"
            onClick={() => scrollContainerRef.current?.scrollTo({ left: 0, behavior: "smooth" })}
            title="Vai al primo mese della serie"
          >
            <ChevronLeft className="size-3.5 mr-0.5" />
            Inizio
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="h-7 text-xs px-2"
            onClick={() => {
              const c = scrollContainerRef.current
              if (c) c.scrollTo({ left: c.scrollWidth, behavior: "smooth" })
            }}
            title="Vai all'ultimo mese della serie"
          >
            Fine
            <ChevronRight className="size-3.5 ml-0.5" />
          </Button>
        </div>

        <Button
          variant="outline"
          size="sm"
          className="h-8"
          onClick={() => printAnalisi(series, truncated, chartHtmlForPrint(chartRef.current))}
        >
          <Printer className="size-3.5 mr-1.5" />
          Stampa PDF
        </Button>
      </div>

      <div className="rounded-lg border bg-background p-3">
        <p className="px-1 pb-2 text-xs text-muted-foreground">
          Barre per mese di <strong>Ipotesi Fatturazione</strong> · linea blu per mese di{" "}
          <strong>Data Prevista Saldo</strong>. Clic su un segmento, sull'etichetta del
          totale o su un punto della linea per il dettaglio.
        </p>
        {truncated && (
          <p className="px-1 pb-2 text-xs font-medium text-amber-700">
            Serie limitata agli ultimi 600 mesi: i mesi più vecchi non sono mostrati.
          </p>
        )}
        <div ref={scrollContainerRef} className="overflow-x-auto">
          <div
            ref={chartRef}
            style={{
              minWidth: `${Math.max(series.length * MONTH_MIN_WIDTH, 560)}px`,
            }}
          >
            <ChartContainer
              config={chartConfig}
              className="aspect-auto h-[340px] w-full"
            >
              <ComposedChart
                data={series}
                margin={{ top: 26, right: 16, left: 8, bottom: 0 }}
              >
                <CartesianGrid strokeDasharray="3 3" vertical={false} />
                <XAxis dataKey="label" fontSize={10} interval={0} tickMargin={6} />
                <YAxis fontSize={10} width={72} tickFormatter={(v: number) => n0(v)} />
                <ChartTooltip
                  content={
                    <ChartTooltipContent
                      formatter={(value, name, item) => {
                        const cfg = chartConfig[name as keyof typeof chartConfig]
                        const color =
                          (item as { color?: string })?.color ??
                          cfg?.color ??
                          COLORS.daf
                        return (
                          <>
                            <div
                              className="h-2.5 w-2.5 shrink-0 rounded-[2px]"
                              style={{ backgroundColor: color }}
                            />
                            <div className="flex flex-1 items-center justify-between gap-4 leading-none">
                              <span className="text-muted-foreground">
                                {cfg?.label ?? String(name)}
                              </span>
                              <span className="font-mono font-medium tabular-nums text-foreground">
                                {euro(Number(value))}
                              </span>
                            </div>
                          </>
                        )
                      }}
                    />
                  }
                />
                <Bar
                  dataKey="inc"
                  stackId="a"
                  fill="var(--color-inc)"
                  maxBarSize={46}
                  className="cursor-pointer"
                  onClick={barClick("inc")}
                >
                  <LabelList dataKey="tot" content={renderTotalLabelFor("inc")} />
                </Bar>
                <Bar
                  dataKey="em"
                  stackId="a"
                  fill="var(--color-em)"
                  maxBarSize={46}
                  className="cursor-pointer"
                  onClick={barClick("em")}
                >
                  <LabelList dataKey="tot" content={renderTotalLabelFor("em")} />
                </Bar>
                <Bar
                  dataKey="daf"
                  stackId="a"
                  fill="var(--color-daf)"
                  maxBarSize={46}
                  className="cursor-pointer"
                  onClick={barClick("daf")}
                >
                  <LabelList dataKey="tot" content={renderTotalLabelFor("daf")} />
                </Bar>
                <Line
                  type="monotone"
                  dataKey="prev"
                  stroke="var(--color-prev)"
                  strokeWidth={2}
                  dot={prevDot}
                  activeDot={prevDot}
                />
              </ComposedChart>
            </ChartContainer>
          </div>
        </div>

        {/* Legenda con i totali complessivi per serie */}
        <div className="flex flex-wrap items-center justify-center gap-x-5 gap-y-1 pt-2 text-xs">
          {(
            [
              { key: "inc", label: "Incassate" },
              { key: "em", label: "Emesse" },
              { key: "daf", label: "da Fatturare" },
              { key: "prev", label: "Incasso previsto" },
            ] as const
          ).map((s) => (
            <span key={s.key} className="inline-flex items-center gap-1.5">
              <span
                className="h-2.5 w-2.5 shrink-0 rounded-[2px]"
                style={{ backgroundColor: COLORS[s.key] }}
              />
              <span className="text-muted-foreground">{s.label}</span>
              <span className="font-mono font-semibold tabular-nums">
                {euro(legendTotals[s.key])}
              </span>
            </span>
          ))}
        </div>

        {senzaDataFatt > 0 && (
          <p className="pt-1.5 text-center text-[11px] text-muted-foreground">
            {senzaDataFatt === 1
              ? "1 riga con importo ma senza Ipotesi di Fatturazione non è rappresentata nel grafico"
              : `${senzaDataFatt} righe con importo ma senza Ipotesi di Fatturazione non sono rappresentate nel grafico`}
          </p>
        )}
      </div>

      {/* Dialog drill-down */}
      <Dialog
        open={drill !== null}
        onOpenChange={(open) => {
          if (!open) setDrill(null)
        }}
      >
        <DialogContent className="flex max-h-[85vh] flex-col gap-4 overflow-hidden sm:max-w-5xl">
          {drill && (
            <>
              <DialogHeader className="pr-8">
                <DialogTitle>
                  Dettaglio — {KIND_LABEL[drill.kind]} · {salYmTitle(drill.ym)}
                </DialogTitle>
                <DialogDescription>
                  {drillList.length === 1 ? "1 riga" : `${drillList.length} righe`} ·
                  Totale imponibile{" "}
                  <span className="font-mono font-semibold tabular-nums text-foreground">
                    {euro(drillTotal)}
                  </span>{" "}
                  —{" "}
                  {drill.kind === "prev"
                    ? "Collocazione per Data Prevista Saldo Fattura."
                    : "Collocazione per Ipotesi Fatturazione."}
                </DialogDescription>
              </DialogHeader>

              {drillList.length === 0 ? (
                <p className="py-6 text-center text-sm text-muted-foreground">
                  Nessuna riga per il mese selezionato.
                </p>
              ) : (
                <GridScroller fill className="rounded-lg border">
                  <Table>
                    <TableHeader className="bg-muted/40">
                      <TableRow>
                        <TableHead className="w-40">Stato</TableHead>
                        <TableHead className="w-28">Commessa</TableHead>
                        <TableHead className="min-w-[10rem]">Cliente</TableHead>
                        <TableHead className="min-w-[12rem]">Step</TableHead>
                        <TableHead className="w-14 text-center">%</TableHead>
                        <TableHead className="w-40">Condizioni</TableHead>
                        <TableHead className="w-28 text-right">Importo</TableHead>
                        <TableHead className="w-24 text-center">Ipotesi Fatt.</TableHead>
                        <TableHead className="w-28 text-center">Data Prev. Saldo</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {drillList.map((row, idx) => {
                        const pill = drillPill(row, todayIso)
                        return (
                          <TableRow key={`${row.projectId}-${idx}`}>
                            <TableCell className="py-1.5 align-middle">
                              <span
                                className={cn(
                                  "inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-semibold",
                                  PILL_CLASSES[pill.tone]
                                )}
                              >
                                {pill.label}
                              </span>
                            </TableCell>
                            <TableCell className="py-1.5 align-middle">
                              <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
                                {row.code}
                              </span>
                            </TableCell>
                            <TableCell
                              className="py-1.5 align-middle text-xs font-medium truncate max-w-[10rem]"
                              title={row.cliente}
                            >
                              {row.cliente || "—"}
                            </TableCell>
                            <TableCell
                              className="py-1.5 align-middle text-xs truncate max-w-[12rem]"
                              title={row.step}
                            >
                              {row.step || "—"}
                            </TableCell>
                            <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                              {row.perc != null ? `${row.perc}%` : "—"}
                            </TableCell>
                            <TableCell
                              className="py-1.5 align-middle text-xs text-muted-foreground truncate max-w-[10rem]"
                              title={row.condizione}
                            >
                              {row.condizione || "—"}
                            </TableCell>
                            <TableCell className="py-1.5 align-middle text-right font-mono text-xs font-semibold tabular-nums">
                              {euro(row.importo)}
                            </TableCell>
                            <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                              {row.dataFatt ? formatDateShort(row.dataFatt) : "—"}
                            </TableCell>
                            <TableCell className="py-1.5 align-middle text-center font-mono text-xs tabular-nums">
                              {row.dataSaldo ? formatDateShort(row.dataSaldo) : "—"}
                            </TableCell>
                          </TableRow>
                        )
                      })}
                    </TableBody>
                  </Table>
                </GridScroller>
              )}
            </>
          )}
        </DialogContent>
      </Dialog>
    </div>
  )
}
