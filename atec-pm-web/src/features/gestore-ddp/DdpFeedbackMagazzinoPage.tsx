import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ArrowLeft, EyeOff, RotateCcw } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import {
  fetchDdpFeedbackMagazzino,
  resetDdpFeedbackMagazzino,
  setDdpFeedbackMagazzinoHidden,
} from "@/lib/api/ddp-feedback"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import type { DdpFeedbackMagazzinoGroup, DdpStatusItem } from "@/lib/api/types"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { cn } from "@/lib/utils"

function typeLabel(ddpType: string): string {
  return ddpType === "OFFICINA" ? "OFFICINA" : "COMMERCIALE"
}

function StatusBadge({
  statusKey,
  statusDefs,
}: {
  statusKey: string
  statusDefs: Map<string, DdpStatusItem>
}) {
  const def = statusDefs.get(statusKey)
  return (
    <span
      className="inline-flex h-6 items-center justify-center rounded-full px-2 text-xs font-bold"
      style={{
        backgroundColor: def?.colorBg ?? "#CCCCCC",
        color: def?.colorFg ?? "#000000",
      }}
    >
      {statusKey}
    </span>
  )
}

function DdpGroupCard({
  group,
  statusDefs,
  onRefresh,
}: {
  group: DdpFeedbackMagazzinoGroup
  statusDefs: Map<string, DdpStatusItem>
  onRefresh: () => void
}) {
  const officina = group.ddpType === "OFFICINA"
  const hiddenCount = group.rows.filter((row) => row.hidden).length
  const shownCount = group.rows.length - hiddenCount

  async function toggleHidden(itemId: number, hidden: boolean) {
    await setDdpFeedbackMagazzinoHidden(
      group.projectId,
      group.ddpType,
      itemId,
      hidden
    )
    onRefresh()
  }

  async function reset() {
    await resetDdpFeedbackMagazzino(group.projectId, group.ddpType)
    onRefresh()
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
        <div className="flex items-center gap-2">
          <CardTitle className="text-sm">
            DDP {typeLabel(group.ddpType)}
          </CardTitle>
          <span className="text-xs text-muted-foreground">
            {shownCount} {shownCount === 1 ? "riga" : "righe"}
          </span>
        </div>
        {hiddenCount > 0 && (
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">
              {hiddenCount} {hiddenCount === 1 ? "riga spenta" : "righe spente"}
            </span>
            <Button variant="ghost" size="xs" onClick={reset}>
              <RotateCcw className="mr-1 size-3" />
              Reset righe
            </Button>
          </div>
        )}
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Rich.</TableHead>
              <TableHead>Descrizione</TableHead>
              <TableHead className="text-right">Q.tà</TableHead>
              {officina ? (
                <>
                  <TableHead>Materiale</TableHead>
                  <TableHead>Trattamento</TableHead>
                </>
              ) : (
                <TableHead>UM</TableHead>
              )}
              <TableHead>Fornitore</TableHead>
              {!officina && <TableHead>Produttore</TableHead>}
              <TableHead>Stato</TableHead>
              <TableHead>Rif. Danea</TableHead>
              <TableHead>Destinazione</TableHead>
              <TableHead>Specifica</TableHead>
              <TableHead>Note</TableHead>
              <TableHead className="w-9" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {group.rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={12}
                  className="text-center text-sm text-muted-foreground"
                >
                  Nessuna riga negli stati di magazzino.
                </TableCell>
              </TableRow>
            ) : (
              group.rows.map((row) => (
                <TableRow
                  key={row.itemId}
                  className={cn(row.hidden && "opacity-50")}
                >
                  <TableCell>{row.requestedBy || "—"}</TableCell>
                  <TableCell className="max-w-[240px] truncate">
                    {row.description}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {row.quantity}
                  </TableCell>
                  {officina ? (
                    <>
                      <TableCell>{row.material || "—"}</TableCell>
                      <TableCell>{row.treatment || "—"}</TableCell>
                    </>
                  ) : (
                    <TableCell>{row.unit || "—"}</TableCell>
                  )}
                  <TableCell>{row.supplierName || "—"}</TableCell>
                  {!officina && (
                    <TableCell>{row.manufacturer || "—"}</TableCell>
                  )}
                  <TableCell>
                    <StatusBadge
                      statusKey={row.itemStatus}
                      statusDefs={statusDefs}
                    />
                  </TableCell>
                  <TableCell>{row.daneaRef || "—"}</TableCell>
                  <TableCell>{row.destination || "—"}</TableCell>
                  <TableCell>{row.destinationSpec || "—"}</TableCell>
                  <TableCell className="max-w-[160px] truncate">
                    {row.notes || "—"}
                  </TableCell>
                  <TableCell>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-sm"
                          onClick={() =>
                            void toggleHidden(row.itemId, !row.hidden)
                          }
                        >
                          <EyeOff className="size-4" />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>
                        {row.hidden ? "Riattiva riga" : "Nascondi riga"}
                      </TooltipContent>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}

export function DdpFeedbackMagazzinoPage() {
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["ddp-feedback-magazzino"],
    queryFn: fetchDdpFeedbackMagazzino,
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  const statusDefs = React.useMemo(() => {
    const map = new Map<string, DdpStatusItem>()
    for (const status of statusesQuery.data ?? []) {
      map.set(status.statusKey, status)
    }
    return map
  }, [statusesQuery.data])

  useProjectHub("all", () => {
    void query.refetch()
  })

  const groupedByCode = React.useMemo(() => {
    const groups = new Map<string, DdpFeedbackMagazzinoGroup[]>()
    for (const item of query.data ?? []) {
      const key = item.code || "—"
      const list = groups.get(key)
      if (list) list.push(item)
      else groups.set(key, [item])
    }
    return Array.from(groups.entries())
      .sort((a, b) => a[0].localeCompare(b[0], "it"))
      .map(([code, items]) => ({
        code,
        customerName: items[0]?.customerName || "",
        items: items
          .slice()
          .sort(
            (a, b) =>
              (a.ddpType === "OFFICINA" ? 1 : 0) -
              (b.ddpType === "OFFICINA" ? 1 : 0)
          ),
      }))
  }, [query.data])

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="icon-sm" onClick={() => navigate("/gestore-ddp")}>
          <ArrowLeft className="size-4" />
        </Button>
        <div>
          <h1 className="text-lg font-semibold">Feedback Magazzino</h1>
          <p className="text-sm text-muted-foreground">
            Righe negli stati di magazzino (CON, COS, DISP, PAR, MOD), per
            commessa. Nascondi le righe già gestite.
          </p>
        </div>
      </div>

      {query.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : query.error ? (
        <p className="text-sm text-destructive">
          {(query.error as Error).message}
        </p>
      ) : groupedByCode.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Nessuna riga negli stati di magazzino.
        </p>
      ) : (
        <div className="space-y-6">
          {groupedByCode.map((group) => (
            <div key={group.code} className="space-y-2">
              <div className="flex items-baseline gap-2">
                <span className="font-semibold">{group.code}</span>
                <span className="text-sm text-muted-foreground">
                  {group.customerName}
                </span>
              </div>
              <div className="space-y-3">
                {group.items.map((item) => (
                  <DdpGroupCard
                    key={`${item.projectId}-${item.ddpType}`}
                    group={item}
                    statusDefs={statusDefs}
                    onRefresh={() => void query.refetch()}
                  />
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
