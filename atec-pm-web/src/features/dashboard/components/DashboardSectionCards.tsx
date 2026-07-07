import { TrendingDown, TrendingUp } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import {
  Card,
  CardAction,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { DashboardData } from "@/lib/api/types"

function formatEuro(value: number): string {
  if (value <= 0) {
    return "—"
  }
  return `${value.toLocaleString("it-IT")} €`
}

function weekShareOfMonth(hoursWeek: number, hoursMonth: number): string | null {
  if (hoursMonth <= 0) {
    return null
  }
  const pct = Math.round((hoursWeek / hoursMonth) * 100)
  return `${pct}% del mese`
}

export function DashboardSectionCards({ data }: { data: DashboardData }) {
  const showRevenue = data.totalRevenue > 0
  const weekShare = weekShareOfMonth(data.hoursThisWeek, data.hoursThisMonth)
  const activeShare =
    data.activeProjects + data.draftProjects > 0
      ? Math.round(
          (data.activeProjects / (data.activeProjects + data.draftProjects)) * 100
        )
      : 0

  return (
    <div className="grid grid-cols-1 gap-4 *:data-[slot=card]:bg-gradient-to-t *:data-[slot=card]:from-primary/5 *:data-[slot=card]:to-card *:data-[slot=card]:shadow-xs @xl/main:grid-cols-2 @5xl/main:grid-cols-4 dark:*:data-[slot=card]:bg-card">
      <Card className="@container/card">
        <CardHeader>
          <CardDescription>Commesse attive</CardDescription>
          <CardTitle className="text-2xl font-semibold tabular-nums @[250px]/card:text-3xl">
            {data.activeProjects}
          </CardTitle>
          <CardAction>
            <Badge variant="outline">
              <TrendingUp />
              {data.draftProjects} bozze
            </Badge>
          </CardAction>
        </CardHeader>
        <CardFooter className="flex-col items-start gap-1.5 text-sm">
          <div className="line-clamp-1 flex gap-2 font-medium">
            {activeShare}% operative <TrendingUp className="size-4" />
          </div>
          <div className="text-muted-foreground">
            {data.completedProjects} completate in archivio
          </div>
        </CardFooter>
      </Card>

      <Card className="@container/card">
        <CardHeader>
          <CardDescription>Ore settimana</CardDescription>
          <CardTitle className="text-2xl font-semibold tabular-nums @[250px]/card:text-3xl">
            {data.hoursThisWeek.toFixed(1)}
          </CardTitle>
          <CardAction>
            <Badge variant="outline">
              {weekShare ? (
                <>
                  <TrendingUp />
                  {weekShare}
                </>
              ) : (
                <>
                  <TrendingDown />
                  Nessun dato mese
                </>
              )}
            </Badge>
          </CardAction>
        </CardHeader>
        <CardFooter className="flex-col items-start gap-1.5 text-sm">
          <div className="line-clamp-1 flex gap-2 font-medium">
            {data.hoursThisMonth.toFixed(1)} h nel mese
          </div>
          <div className="text-muted-foreground">Timesheet consolidato aziendale</div>
        </CardFooter>
      </Card>

      <Card className="@container/card">
        <CardHeader>
          <CardDescription>Clienti attivi</CardDescription>
          <CardTitle className="text-2xl font-semibold tabular-nums @[250px]/card:text-3xl">
            {data.totalCustomers}
          </CardTitle>
          <CardAction>
            <Badge variant="outline">
              <TrendingUp />
              {data.totalEmployees} risorse
            </Badge>
          </CardAction>
        </CardHeader>
        <CardFooter className="flex-col items-start gap-1.5 text-sm">
          <div className="line-clamp-1 flex gap-2 font-medium">
            Anagrafica clienti aggiornata
          </div>
          <div className="text-muted-foreground">
            {data.totalEmployees} dipendenti attivi
          </div>
        </CardFooter>
      </Card>

      <Card className="@container/card">
        <CardHeader>
          <CardDescription>
            {showRevenue ? "Fatturato commesse" : "Portfolio commesse"}
          </CardDescription>
          <CardTitle className="text-2xl font-semibold tabular-nums @[250px]/card:text-3xl">
            {showRevenue
              ? formatEuro(data.totalRevenue)
              : data.activeProjects + data.draftProjects}
          </CardTitle>
          <CardAction>
            <Badge variant="outline">
              <TrendingUp />
              {showRevenue ? "Attive + chiuse" : "In lavorazione"}
            </Badge>
          </CardAction>
        </CardHeader>
        <CardFooter className="flex-col items-start gap-1.5 text-sm">
          <div className="line-clamp-1 flex gap-2 font-medium">
            {showRevenue ? "Dati economici visibili" : "KPI operativi"}
          </div>
          <div className="text-muted-foreground">
            {showRevenue
              ? "Solo commesse attive e completate"
              : "Il fatturato richiede permesso dedicato"}
          </div>
        </CardFooter>
      </Card>
    </div>
  )
}
