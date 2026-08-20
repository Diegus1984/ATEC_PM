import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Printer } from "lucide-react"
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { fetchDdpDeliveriesByDay } from "@/lib/api/ddp-manager"
import { dateToIso, formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { printDdpTables } from "./ddp-export"

// Colonne della tabella giorni. «Data» è la chiave di riga e resta sempre visibile.
const CONSEGNE_COLUMNS: { id: string; label: string }[] = [
  { id: "weekday", label: "Giorno" },
  { id: "commercialCount", label: "Righe Comm." },
  { id: "commercialValue", label: "Valore Comm." },
  { id: "officinaCount", label: "Righe Off." },
  { id: "officinaValue", label: "Valore Off." },
  { id: "totalCount", label: "Righe Tot." },
  { id: "totalValue", label: "Valore Tot." },
]
const CONSEGNE_COLUMNS_DEFAULT = Object.fromEntries(
  CONSEGNE_COLUMNS.map((column) => [column.id, true])
)

// Palette di serie validata (dataviz: croma/contrasto/CVD ok in light e dark).
const COLOR_COMMERCIALI = "#4A86C6"
const COLOR_OFFICINE = "#1F9077"

const chartConfig = {
  commerciali: { label: "DDP Commerciali", color: COLOR_COMMERCIALI },
  officine: { label: "DDP Officine", color: COLOR_OFFICINE },
} satisfies ChartConfig

type Metric = "val" | "n"

interface ChartDay {
  day: string
  label: string
  late: boolean
  commerciali: number
  officine: number
}

// Tick X: data ruotata, rossa quando il giorno di consegna è già scaduto.
function DayTick({
  x,
  y,
  payload,
  lateDays,
}: {
  x?: number
  y?: number
  payload?: { value: string }
  lateDays: Set<string>
}) {
  const value = payload?.value ?? ""
  const late = lateDays.has(value)
  return (
    <text
      x={x}
      y={y}
      dy={4}
      transform={`rotate(-45 ${x} ${y})`}
      textAnchor="end"
      fontSize={11}
      fontWeight={late ? 700 : 400}
      fill={late ? "#C0392B" : "currentColor"}
      opacity={late ? 1 : 0.65}
    >
      {value}
    </text>
  )
}

export function DdpConsegneView() {
  const [metric, setMetric] = React.useState<Metric>("val")

  const [visible, setVisible] = usePersistedColumnVisibility(
    "ddp-consegne-columns-v1",
    CONSEGNE_COLUMNS_DEFAULT
  )
  const show = (id: string) => visible[id] ?? true
  const columnToggles = CONSEGNE_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: show(id),
    onToggle: (value: boolean) =>
      setVisible((prev) => ({ ...prev, [id]: value })),
  }))

  // staleTime 0: i dati arrivano dalle distinte modificate altrove e l'hub esclude
  // l'autore della modifica — al rientro nella vista si rilegge sempre dal server.
  const query = useQuery({
    queryKey: ["ddp-deliveries-by-day"],
    queryFn: fetchDdpDeliveriesByDay,
    staleTime: 0,
  })

  useProjectHub("all", () => {
    void query.refetch()
  })

  const today = dateToIso(new Date())
  const days = React.useMemo(() => query.data ?? [], [query.data])

  const chartData: ChartDay[] = React.useMemo(
    () =>
      days.map((entry) => {
        const day = entry.day.slice(0, 10)
        return {
          day,
          label: formatDateShort(day),
          late: day < today,
          commerciali:
            metric === "val" ? (entry.commercialValue ?? 0) : entry.commercialCount,
          officine: metric === "val" ? (entry.officinaValue ?? 0) : entry.officinaCount,
        }
      }),
    [days, metric, today]
  )

  const lateDays = React.useMemo(
    () => new Set(chartData.filter((entry) => entry.late).map((entry) => entry.label)),
    [chartData]
  )

  const totals = React.useMemo(() => {
    let rows = 0
    let value = 0
    let late = 0
    for (const entry of days) {
      rows += entry.commercialCount + entry.officinaCount
      value += (entry.commercialValue ?? 0) + (entry.officinaValue ?? 0)
      if (entry.day.slice(0, 10) < today) late++
    }
    return { rows, value, late }
  }, [days, today])

  function weekday(day: string): string {
    return new Date(`${day.slice(0, 10)}T00:00:00`).toLocaleDateString("it-IT", {
      weekday: "long",
    })
  }

  function print() {
    printDdpTables(
      "Analisi Consegne — Previsioni giornaliere",
      `${days.length} giorni · ${totals.rows} righe · ${euro(totals.value)} complessivi · ${totals.late} giorni scaduti · riferimento ${formatDateShort(today)}`,
      [
        {
          title: "Consegne previste per giorno (tutte le commesse)",
          headers: [
            "Data",
            "Giorno",
            "Righe Comm.",
            "Valore Comm.",
            "Righe Off.",
            "Valore Off.",
            "Righe Tot.",
            "Valore Tot.",
            "Scaduta",
          ],
          rows: days.map((entry) => {
            const day = entry.day.slice(0, 10)
            return [
              formatDateShort(day),
              weekday(day),
              String(entry.commercialCount),
              euro(entry.commercialValue),
              String(entry.officinaCount),
              euro(entry.officinaValue),
              String(entry.commercialCount + entry.officinaCount),
              euro((entry.commercialValue ?? 0) + (entry.officinaValue ?? 0)),
              day < today ? "SÌ" : "",
            ]
          }),
        },
      ]
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="text-base font-semibold">Consegne Previste per Giorno</h2>
          <p className="text-sm text-muted-foreground">
            Consegne ancora da evadere su tutte le commesse, separate Commerciali /
            Officine.
          </p>
        </div>
        <Tabs value={metric} onValueChange={(value) => setMetric(value as Metric)}>
          <TabsList>
            <TabsTrigger value="val">Valore (€)</TabsTrigger>
            <TabsTrigger value="n">Numero righe</TabsTrigger>
          </TabsList>
        </Tabs>
        <ColumnsMenu columns={columnToggles} />
        <Button variant="outline" size="sm" onClick={print} disabled={days.length === 0}>
          <Printer className="mr-1.5 size-4" />
          Stampa PDF
        </Button>
      </div>

      {query.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : query.error ? (
        <p className="text-sm text-destructive">{(query.error as Error).message}</p>
      ) : days.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Nessuna consegna prevista nelle distinte in anagrafica.
        </p>
      ) : (
        <>
          <Card>
            <CardContent className="space-y-3 pt-4">
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
                <span>
                  <strong>{days.length}</strong> giorni di consegna —{" "}
                  <strong>{totals.rows}</strong> righe —{" "}
                  <strong>{euro(totals.value)}</strong> complessivi
                  {totals.late > 0 ? (
                    <>
                      {" "}
                      — <strong className="text-destructive">{totals.late}</strong>{" "}
                      giorni scaduti
                    </>
                  ) : null}
                </span>
                <span className="ml-auto flex items-center gap-3 text-xs text-muted-foreground">
                  <span className="flex items-center gap-1.5">
                    <span
                      className="size-3 rounded-sm"
                      style={{ backgroundColor: COLOR_COMMERCIALI }}
                    />
                    DDP Commerciali
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span
                      className="size-3 rounded-sm"
                      style={{ backgroundColor: COLOR_OFFICINE }}
                    />
                    DDP Officine
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span className="size-3 rounded-sm border border-[#C0392B] bg-[#F6D9D6]" />
                    Consegna scaduta
                  </span>
                </span>
              </div>
              <div className="overflow-x-auto">
                <ChartContainer
                  config={chartConfig}
                  className="h-[320px]"
                  style={{ minWidth: `${Math.max(560, chartData.length * 52)}px` }}
                >
                  <BarChart data={chartData} margin={{ top: 8, right: 12, bottom: 34, left: 8 }} barGap={2}>
                    <CartesianGrid vertical={false} strokeOpacity={0.35} />
                    <XAxis
                      dataKey="label"
                      tickLine={false}
                      axisLine={false}
                      interval={0}
                      height={52}
                      tick={<DayTick lateDays={lateDays} />}
                    />
                    <YAxis
                      tickLine={false}
                      axisLine={false}
                      width={64}
                      tickFormatter={(value: number) =>
                        metric === "val"
                          ? value >= 1000
                            ? `${Math.round(value / 1000)}k €`
                            : `${value} €`
                          : String(value)
                      }
                    />
                    <ChartTooltip
                      content={
                        <ChartTooltipContent
                          formatter={(value, name) => (
                            <span className="flex w-full items-center justify-between gap-3">
                              <span>
                                {chartConfig[name as keyof typeof chartConfig]?.label ??
                                  name}
                              </span>
                              <span className="font-mono font-medium tabular-nums">
                                {metric === "val" ? euro(Number(value)) : `${value} righe`}
                              </span>
                            </span>
                          )}
                        />
                      }
                    />
                    <Bar
                      dataKey="commerciali"
                      fill="var(--color-commerciali)"
                      radius={[4, 4, 0, 0]}
                      maxBarSize={18}
                    />
                    <Bar
                      dataKey="officine"
                      fill="var(--color-officine)"
                      radius={[4, 4, 0, 0]}
                      maxBarSize={18}
                    />
                  </BarChart>
                </ChartContainer>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardContent className="pt-4">
              <GridScroller>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Data</TableHead>
                      {show("weekday") && <TableHead>Giorno</TableHead>}
                      {show("commercialCount") && (
                        <TableHead className="text-right">Righe Comm.</TableHead>
                      )}
                      {show("commercialValue") && (
                        <TableHead className="text-right">Valore Comm.</TableHead>
                      )}
                      {show("officinaCount") && (
                        <TableHead className="text-right">Righe Off.</TableHead>
                      )}
                      {show("officinaValue") && (
                        <TableHead className="text-right">Valore Off.</TableHead>
                      )}
                      {show("totalCount") && (
                        <TableHead className="text-right">Righe Tot.</TableHead>
                      )}
                      {show("totalValue") && (
                        <TableHead className="text-right">Valore Tot.</TableHead>
                      )}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {days.map((entry) => {
                      const day = entry.day.slice(0, 10)
                      const late = day < today
                      return (
                        <TableRow
                          key={day}
                          className={cn(late && "bg-destructive/5")}
                        >
                          <TableCell
                            className={cn(
                              "whitespace-nowrap",
                              late && "font-semibold text-destructive"
                            )}
                          >
                            {formatDateShort(day)}
                          </TableCell>
                          {show("weekday") && (
                            <TableCell className="capitalize">{weekday(day)}</TableCell>
                          )}
                          {show("commercialCount") && (
                            <TableCell className="text-right tabular-nums">
                              {entry.commercialCount}
                            </TableCell>
                          )}
                          {show("commercialValue") && (
                            <TableCell className="text-right tabular-nums">
                              {euro(entry.commercialValue)}
                            </TableCell>
                          )}
                          {show("officinaCount") && (
                            <TableCell className="text-right tabular-nums">
                              {entry.officinaCount}
                            </TableCell>
                          )}
                          {show("officinaValue") && (
                            <TableCell className="text-right tabular-nums">
                              {euro(entry.officinaValue)}
                            </TableCell>
                          )}
                          {show("totalCount") && (
                            <TableCell className="text-right font-semibold tabular-nums">
                              {entry.commercialCount + entry.officinaCount}
                            </TableCell>
                          )}
                          {show("totalValue") && (
                            <TableCell className="text-right font-semibold tabular-nums">
                              {euro((entry.commercialValue ?? 0) + (entry.officinaValue ?? 0))}
                            </TableCell>
                          )}
                        </TableRow>
                      )
                    })}
                  </TableBody>
                  <TableFooter>
                    <TableRow>
                      <TableCell colSpan={show("weekday") ? 2 : 1}>Totale</TableCell>
                      {show("commercialCount") && (
                        <TableCell className="text-right tabular-nums">
                          {days.reduce((sum, entry) => sum + entry.commercialCount, 0)}
                        </TableCell>
                      )}
                      {show("commercialValue") && (
                        <TableCell className="text-right tabular-nums">
                          {euro(
                            days.reduce((sum, entry) => sum + (entry.commercialValue ?? 0), 0)
                          )}
                        </TableCell>
                      )}
                      {show("officinaCount") && (
                        <TableCell className="text-right tabular-nums">
                          {days.reduce((sum, entry) => sum + entry.officinaCount, 0)}
                        </TableCell>
                      )}
                      {show("officinaValue") && (
                        <TableCell className="text-right tabular-nums">
                          {euro(days.reduce((sum, entry) => sum + (entry.officinaValue ?? 0), 0))}
                        </TableCell>
                      )}
                      {show("totalCount") && (
                        <TableCell className="text-right tabular-nums">{totals.rows}</TableCell>
                      )}
                      {show("totalValue") && (
                        <TableCell className="text-right tabular-nums">
                          {euro(totals.value)}
                        </TableCell>
                      )}
                    </TableRow>
                  </TableFooter>
                </Table>
              </GridScroller>
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
