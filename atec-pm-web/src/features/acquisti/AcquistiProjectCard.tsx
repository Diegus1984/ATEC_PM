// ── Card di commessa con la griglia dei fabbisogni ────────────────────────

import type { ColumnDef } from "@tanstack/react-table"
import { BriefcaseBusiness, FileCheck2, ShoppingCart } from "lucide-react"

import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { AcquistiInboxItem } from "@/lib/api/types"
import { euro } from "@/lib/format"

import { COLUMN_LABELS, type GroupByProject } from "./acquisti-shared"

export function AcquistiProjectCard({
  group,
  columns,
  rowStyle,
  onRequestRfq,
  onOrderDanea,
}: {
  group: GroupByProject
  columns: ColumnDef<AcquistiInboxItem>[]
  rowStyle: (row: AcquistiInboxItem) => React.CSSProperties | undefined
  onRequestRfq: (items: AcquistiInboxItem[]) => void
  onOrderDanea: (project: { projectId: number; projectCode: string }) => void
}) {
  return (
    <Card className="border shadow-md overflow-hidden bg-card">
      <CardHeader className="bg-muted/50 p-4 border-b flex flex-col md:flex-row md:items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="p-2 bg-primary/10 rounded-lg text-primary">
            <BriefcaseBusiness className="h-5 w-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <CardTitle className="text-base font-bold text-foreground">
                Commessa {group.projectCode}
              </CardTitle>
              {group.customerName && (
                <Badge variant="outline" className="text-xs font-normal">
                  {group.customerName}
                </Badge>
              )}
            </div>
            <CardDescription className="text-xs text-muted-foreground line-clamp-1 mt-0.5">
              {group.projectTitle || "Nessun titolo"}
            </CardDescription>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <div className="text-right text-xs">
            <div className="font-bold text-foreground text-sm">{euro(group.totalEstCost)}</div>
            <div className="text-muted-foreground">
              {group.items.length} articoli ({group.totalQty} pz tot.)
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              className="h-8 text-xs gap-1 font-medium"
              onClick={() => onRequestRfq(group.items)}
            >
              <FileCheck2 className="h-3.5 w-3.5" />
              Richiedi RDO
            </Button>
            <Button
              size="sm"
              className="h-8 text-xs gap-1 font-medium"
              onClick={() =>
                onOrderDanea({
                  projectId: group.projectId,
                  projectCode: group.projectCode,
                })
              }
            >
              <ShoppingCart className="h-3.5 w-3.5" />
              Ordina Danea
            </Button>
          </div>
        </div>
      </CardHeader>

      <CardContent className="p-0">
        <DataTableCardFiltered
          title={`Fabbisogni ${group.projectCode}`}
          columns={columns}
          data={group.items}
          columnLabels={COLUMN_LABELS}
          visibilityStorageKey={`table-visibility-acquisti-ag-commessa-${group.projectCode}`}
          rowStyle={rowStyle}
        />
      </CardContent>
    </Card>
  )
}
