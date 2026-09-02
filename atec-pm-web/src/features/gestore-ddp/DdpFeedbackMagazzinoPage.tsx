import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ArrowLeft, EyeOff, RotateCcw } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { ColumnsMenu } from "@/components/shared/columns-menu"
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
import { GridScroller } from "@/components/shared/grid-scroller"
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
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"
import { ddpTypeLabel } from "@/features/commesse/ddp-constants"

/**
 * Colonne della griglia (unione DDP Commerciale + Officina): «Materiale/Trattamento»
 * esistono solo in Officina, «UM/Produttore» solo in Commerciale — il menu le elenca
 * tutte, ogni card mostra quelle che le competono. Standard menu «Colonne»: vedi
 * BLOCKS-RULES.md.
 */
const MAG_COLUMNS: { id: string; label: string }[] = [
  // Stesso nome delle DDP di commessa (segnalazione #61).
  { id: "requestedBy", label: "Inserito da" },
  { id: "description", label: "Descrizione" },
  { id: "quantity", label: "Q.tà" },
  { id: "material", label: "Materiale (Officina)" },
  { id: "treatment", label: "Trattamento (Officina)" },
  { id: "unit", label: "UM (Commerciale)" },
  { id: "supplier", label: "Fornitore" },
  { id: "manufacturer", label: "Produttore (Commerciale)" },
  { id: "status", label: "Stato" },
  { id: "daneaRef", label: "Rif. Danea" },
  { id: "destination", label: "Destinazione" },
  { id: "destinationSpec", label: "Specifica" },
  { id: "notes", label: "Note" },
]
const MAG_COLUMNS_DEFAULT = Object.fromEntries(
  MAG_COLUMNS.map((column) => [column.id, true])
)
const MAG_COLUMNS_STORAGE_KEY = "ddp-feedback-magazzino-columns-v1"

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
  visible,
  onRefresh,
}: {
  group: DdpFeedbackMagazzinoGroup
  statusDefs: Map<string, DdpStatusItem>
  visible: Record<string, boolean>
  onRefresh: () => void
}) {
  const officina = group.ddpType === "OFFICINA"
  // Colonna mostrata solo se accesa nel menu E pertinente al tipo di DDP.
  const show = (id: string) => {
    if (!visible[id]) return false
    if (id === "material" || id === "treatment") return officina
    if (id === "unit" || id === "manufacturer") return !officina
    return true
  }
  const visibleCount = MAG_COLUMNS.filter((column) => show(column.id)).length
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
            DDP {ddpTypeLabel(group.ddpType)}
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
        <GridScroller className="rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              {show("requestedBy") && <TableHead>Inserito da</TableHead>}
              {show("description") && <TableHead>Descrizione</TableHead>}
              {show("quantity") && (
                <TableHead className="text-right">Q.tà</TableHead>
              )}
              {show("material") && <TableHead>Materiale</TableHead>}
              {show("treatment") && <TableHead>Trattamento</TableHead>}
              {show("unit") && <TableHead>UM</TableHead>}
              {show("supplier") && <TableHead>Fornitore</TableHead>}
              {show("manufacturer") && <TableHead>Produttore</TableHead>}
              {show("status") && <TableHead>Stato</TableHead>}
              {show("daneaRef") && <TableHead>Rif. Danea</TableHead>}
              {show("destination") && <TableHead>Destinazione</TableHead>}
              {show("destinationSpec") && <TableHead>Specifica</TableHead>}
              {show("notes") && <TableHead>Note</TableHead>}
              <TableHead className="w-9" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {group.rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={visibleCount + 1}
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
                  {show("requestedBy") && (
                    <TableCell>{row.requestedBy || "—"}</TableCell>
                  )}
                  {show("description") && (
                    <TableCell className="min-w-[280px] max-w-[420px]">
                      <span
                        className="block line-clamp-2 whitespace-normal break-words leading-snug"
                        title={row.description}
                      >
                        {row.description || "—"}
                      </span>
                    </TableCell>
                  )}
                  {show("quantity") && (
                    <TableCell className="text-right tabular-nums">
                      {row.quantity}
                    </TableCell>
                  )}
                  {show("material") && (
                    <TableCell>{row.material || "—"}</TableCell>
                  )}
                  {show("treatment") && (
                    <TableCell>{row.treatment || "—"}</TableCell>
                  )}
                  {show("unit") && <TableCell>{row.unit || "—"}</TableCell>}
                  {show("supplier") && (
                    <TableCell>{row.supplierName || "—"}</TableCell>
                  )}
                  {show("manufacturer") && (
                    <TableCell>{row.manufacturer || "—"}</TableCell>
                  )}
                  {show("status") && (
                    <TableCell>
                      <StatusBadge
                        statusKey={row.itemStatus}
                        statusDefs={statusDefs}
                      />
                    </TableCell>
                  )}
                  {show("daneaRef") && (
                    <TableCell>{row.daneaRef || "—"}</TableCell>
                  )}
                  {show("destination") && (
                    <TableCell>{row.destination || "—"}</TableCell>
                  )}
                  {show("destinationSpec") && (
                    <TableCell>{row.destinationSpec || "—"}</TableCell>
                  )}
                  {show("notes") && (
                    <TableCell className="max-w-[160px] whitespace-normal break-words">
                      {row.notes || "—"}
                    </TableCell>
                  )}
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
        </GridScroller>
      </CardContent>
    </Card>
  )
}

export function DdpFeedbackMagazzinoPage() {
  const navigate = useNavigate()

  const [visible, setVisible] = usePersistedColumnVisibility(
    MAG_COLUMNS_STORAGE_KEY,
    MAG_COLUMNS_DEFAULT
  )
  const columnToggles = MAG_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: visible[id] ?? true,
    onToggle: (value: boolean) =>
      setVisible((prev) => ({ ...prev, [id]: value })),
  }))

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
        title: items[0]?.title || "",
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
        <div className="ml-auto">
          <ColumnsMenu columns={columnToggles} />
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
              <div className="flex flex-wrap items-baseline gap-2">
                <span className="font-semibold">{group.code}</span>
                {group.title ? <span className="text-sm">{group.title}</span> : null}
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
                    visible={visible}
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
