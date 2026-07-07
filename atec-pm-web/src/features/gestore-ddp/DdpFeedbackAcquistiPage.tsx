import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ArrowLeft, EyeOff, RotateCcw } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Textarea } from "@/components/ui/textarea"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import {
  fetchDdpFeedbackAcquisti,
  resetDdpFeedbackAcquisti,
  setDdpFeedbackAcquistiHidden,
  setDdpFeedbackAcquistiNote,
} from "@/lib/api/ddp-feedback"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import type { DdpFeedbackAcquistiGroup, DdpStatusItem } from "@/lib/api/types"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { cn } from "@/lib/utils"

function typeLabel(ddpType: string): string {
  return ddpType === "OFFICINA" ? "OFFICINA" : "COMMERCIALE"
}

function DdpGroupCard({
  group,
  statusDefs,
  onRefresh,
}: {
  group: DdpFeedbackAcquistiGroup
  statusDefs: Map<string, DdpStatusItem>
  onRefresh: () => void
}) {
  const hiddenCount = group.rows.filter((row) => row.hidden).length

  async function toggleHidden(statusKey: string, hidden: boolean) {
    await setDdpFeedbackAcquistiHidden(
      group.projectId,
      group.ddpType,
      statusKey,
      hidden
    )
    onRefresh()
  }

  async function saveNote(statusKey: string, note: string) {
    await setDdpFeedbackAcquistiNote(
      group.projectId,
      group.ddpType,
      statusKey,
      note
    )
    onRefresh()
  }

  async function reset() {
    await resetDdpFeedbackAcquisti(group.projectId, group.ddpType)
    onRefresh()
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
        <CardTitle className="text-sm">
          DDP {typeLabel(group.ddpType)}
        </CardTitle>
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
      <CardContent className="space-y-2">
        {group.rows.map((row) => {
          const def = statusDefs.get(row.statusKey)
          return (
            <div
              key={`${row.statusKey}-${row.note}-${row.hidden}`}
              className={cn(
                "grid grid-cols-[80px_1fr_60px_1fr_36px] items-start gap-3 rounded-lg border p-2",
                row.hidden && "opacity-50"
              )}
            >
              <span
                className="inline-flex h-6 items-center justify-center rounded-full px-2 text-xs font-bold"
                style={{
                  backgroundColor: def?.colorBg ?? "#CCCCCC",
                  color: def?.colorFg ?? "#000000",
                }}
              >
                {row.statusKey}
              </span>
              <span className="pt-0.5 text-sm">
                {def?.label ?? row.statusKey}
              </span>
              <span
                className={cn(
                  "pt-0.5 text-right text-sm font-semibold tabular-nums",
                  row.count === 0 && "text-muted-foreground"
                )}
              >
                {row.count}
              </span>
              <Textarea
                defaultValue={row.note}
                placeholder="Note…"
                rows={1}
                className="min-h-8 resize-none py-1 text-sm"
                onBlur={(event) => {
                  const value = event.target.value
                  if (value !== row.note) void saveNote(row.statusKey, value)
                }}
              />
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => void toggleHidden(row.statusKey, !row.hidden)}
                  >
                    <EyeOff className="size-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>
                  {row.hidden ? "Riattiva riga" : "Nascondi riga"}
                </TooltipContent>
              </Tooltip>
            </div>
          )
        })}
      </CardContent>
    </Card>
  )
}

export function DdpFeedbackAcquistiPage() {
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["ddp-feedback-acquisti"],
    queryFn: fetchDdpFeedbackAcquisti,
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
    const groups = new Map<string, DdpFeedbackAcquistiGroup[]>()
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
          <h1 className="text-lg font-semibold">Feedback Acquisti</h1>
          <p className="text-sm text-muted-foreground">
            Righe negli stati di follow-up acquisti (VER, CHEK, DO, RO, PAR),
            per commessa. Nota testuale e nascondi per stato.
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
          Nessuna commessa con righe DDP.
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
              <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
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
