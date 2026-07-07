import * as React from "react"
import { Area, AreaChart, CartesianGrid, XAxis } from "recharts"

import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  ToggleGroup,
  ToggleGroupItem,
} from "@/components/ui/toggle-group"
import { useIsMobile } from "@/hooks/use-mobile"
import type { DashboardDailyHoursPoint } from "@/lib/api/types"

const chartConfig = {
  hours: {
    label: "Ore",
    color: "var(--primary)",
  },
} satisfies ChartConfig

type ChartRow = {
  date: string
  hours: number
}

function filterByRange(
  rows: ChartRow[],
  rangeKey: "7d" | "30d" | "90d"
): ChartRow[] {
  const days = rangeKey === "7d" ? 7 : rangeKey === "30d" ? 30 : 90
  const cutoff = new Date()
  cutoff.setHours(0, 0, 0, 0)
  cutoff.setDate(cutoff.getDate() - days)

  return rows.filter((row) => new Date(row.date) >= cutoff)
}

function toChartRows(points: DashboardDailyHoursPoint[]): ChartRow[] {
  return points.map((point) => ({
    date: point.workDate.slice(0, 10),
    hours: point.hours,
  }))
}

export function DashboardHoursChart({
  dailyHours,
}: {
  dailyHours: DashboardDailyHoursPoint[]
}) {
  const isMobile = useIsMobile()
  const [timeRange, setTimeRange] = React.useState<"7d" | "30d" | "90d">("90d")
  const chartRows = React.useMemo(() => toChartRows(dailyHours), [dailyHours])
  const filteredData = React.useMemo(
    () => filterByRange(chartRows, timeRange),
    [chartRows, timeRange]
  )

  React.useEffect(() => {
    if (isMobile) {
      setTimeRange("7d")
    }
  }, [isMobile])

  return (
    <Card className="@container/card">
      <CardHeader>
        <CardTitle>Ore registrate</CardTitle>
        <CardDescription>
          Totale ore timesheet — ultimi{" "}
          {timeRange === "7d" ? "7 giorni" : timeRange === "30d" ? "30 giorni" : "3 mesi"}
        </CardDescription>
        <CardAction>
          <ToggleGroup
            type="single"
            value={timeRange}
            onValueChange={(value) => {
              if (value === "7d" || value === "30d" || value === "90d") {
                setTimeRange(value)
              }
            }}
            variant="outline"
            className="hidden *:data-[slot=toggle-group-item]:px-4! @[767px]/card:flex"
          >
            <ToggleGroupItem value="90d">3 mesi</ToggleGroupItem>
            <ToggleGroupItem value="30d">30 giorni</ToggleGroupItem>
            <ToggleGroupItem value="7d">7 giorni</ToggleGroupItem>
          </ToggleGroup>
          <Select
            value={timeRange}
            onValueChange={(value) => {
              if (value === "7d" || value === "30d" || value === "90d") {
                setTimeRange(value)
              }
            }}
          >
            <SelectTrigger
              className="flex w-40 **:data-[slot=select-value]:block **:data-[slot=select-value]:truncate @[767px]/card:hidden"
              size="sm"
              aria-label="Intervallo temporale"
            >
              <SelectValue placeholder="Intervallo" />
            </SelectTrigger>
            <SelectContent align="end">
              <SelectItem value="90d">Ultimi 3 mesi</SelectItem>
              <SelectItem value="30d">Ultimi 30 giorni</SelectItem>
              <SelectItem value="7d">Ultimi 7 giorni</SelectItem>
            </SelectContent>
          </Select>
        </CardAction>
      </CardHeader>
      <CardContent className="px-2 pt-4 sm:px-6 sm:pt-6">
        {filteredData.length === 0 ? (
          <p className="py-12 text-center text-sm text-muted-foreground">
            Nessuna ora registrata nel periodo selezionato.
          </p>
        ) : (
          <ChartContainer
            config={chartConfig}
            className="aspect-auto h-[250px] w-full"
          >
            <AreaChart data={filteredData}>
              <defs>
                <linearGradient id="fillHours" x1="0" y1="0" x2="0" y2="1">
                  <stop
                    offset="5%"
                    stopColor="var(--color-hours)"
                    stopOpacity={1}
                  />
                  <stop
                    offset="95%"
                    stopColor="var(--color-hours)"
                    stopOpacity={0.1}
                  />
                </linearGradient>
              </defs>
              <CartesianGrid vertical={false} />
              <XAxis
                dataKey="date"
                tickLine={false}
                axisLine={false}
                tickMargin={8}
                minTickGap={32}
                tickFormatter={(value: string) => {
                  const date = new Date(value)
                  return date.toLocaleDateString("it-IT", {
                    month: "short",
                    day: "numeric",
                  })
                }}
              />
              <ChartTooltip
                cursor={false}
                content={
                  <ChartTooltipContent
                    labelFormatter={(value) => {
                      return new Date(String(value)).toLocaleDateString("it-IT", {
                        weekday: "short",
                        month: "short",
                        day: "numeric",
                      })
                    }}
                    indicator="dot"
                  />
                }
              />
              <Area
                dataKey="hours"
                type="natural"
                fill="url(#fillHours)"
                stroke="var(--color-hours)"
              />
            </AreaChart>
          </ChartContainer>
        )}
      </CardContent>
    </Card>
  )
}
