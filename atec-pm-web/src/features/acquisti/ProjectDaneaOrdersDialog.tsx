// ── Dialog «Ordina in Danea» di commessa (batch multi-RDO per fornitore) ───

import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { FileCheck2, ShoppingCart } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
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
import { createPurchaseRfqDaneaOrderMulti } from "@/lib/api/purchase-rfqs"
import type { PurchaseRfqListItem } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"

import { buildOrderGroups, type OrderSupplierGroup } from "./acquisti-shared"

/**
 * Raggruppa le RDO chiuse con vincitore per fornitore e genera l'ordine fornitore
 * in Danea (multi-riga se più RDO dello stesso fornitore). Legge le RDO dal live
 * (`allRfqs`), così i gruppi già ordinati spariscono al refetch real-time.
 */
export function ProjectDaneaOrdersDialog({
  project,
  allRfqs,
  onClose,
  onGenerated,
}: {
  project: { projectId: number; projectCode: string } | null
  allRfqs: PurchaseRfqListItem[]
  onClose: () => void
  onGenerated: () => void
}) {
  const confirm = useConfirm()
  const [expectedDate, setExpectedDate] = React.useState<string | null>(null)

  React.useEffect(() => {
    setExpectedDate(null)
  }, [project?.projectId])

  const groups = React.useMemo(() => {
    if (!project) return []
    return buildOrderGroups(allRfqs.filter((r) => r.projectId === project.projectId))
  }, [allRfqs, project])

  const mutation = useMutation({
    mutationFn: ({ rfqIds }: { rfqIds: number[]; key: string }) =>
      createPurchaseRfqDaneaOrderMulti(rfqIds, expectedDate),
    onSuccess: (num, { rfqIds }) => {
      notifyInfo(`Ordine fornitore n. ${num} creato in Danea (${rfqIds.length} RDO)`)
      onGenerated()
    },
    onError: (err: Error) => notifyError(err.message),
  })

  // Quale gruppo è in corso di generazione: lo sa già react-query (isPending +
  // variables), non serve uno stato locale da tenere sincronizzato a mano.
  const pendingKey = mutation.isPending ? mutation.variables?.key : undefined

  const handleGenerate = async (g: OrderSupplierGroup) => {
    const rfqIds = g.rfqs.map((r) => r.id)
    const ok = await confirm({
      title: "Generare l'ordine fornitore in Danea?",
      description:
        `Crea in Atec_PM UN ordine per ${g.supplierName} (commessa ${g.projectCode}) ` +
        `con ${rfqIds.length} riga/e, totale ${euro(g.total)} + IVA` +
        (expectedDate ? `, consegna prevista ${formatDateShort(expectedDate)}` : "") +
        ". Le righe distinta passano a In ordine.",
      confirmLabel: "Genera ordine",
    })
    if (ok) mutation.mutate({ rfqIds, key: g.key })
  }

  return (
    <Dialog open={project !== null} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-3 overflow-hidden sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-lg font-bold">
            <ShoppingCart className="h-5 w-5 text-primary" />
            Ordina in Danea — Commessa {project?.projectCode}
          </DialogTitle>
          <DialogDescription className="text-xs">
            RDO chiuse con vincitore, raggruppate per fornitore. Un ordine Danea per fornitore
            (più righe se più RDO dello stesso fornitore).
          </DialogDescription>
        </DialogHeader>

        {groups.length === 0 ? (
          <div className="rounded border p-6 text-center text-sm text-muted-foreground">
            Nessuna RDO pronta per l'ordine. Apri una RDO, registra i prezzi e scegli il
            «Vincitore»: il gruppo comparirà qui.
          </div>
        ) : (
          <div className="min-h-0 flex-1 overflow-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Fornitore</TableHead>
                  <TableHead>RDO</TableHead>
                  <TableHead className="text-right">Totale</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {groups.map((g) => (
                  <TableRow key={g.key}>
                    <TableCell className="font-medium">{g.supplierName}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {g.rfqs.map((r) => `#${r.id}`).join(", ")}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{euro(g.total)}</TableCell>
                    <TableCell className="text-right">
                      <Button
                        size="sm"
                        disabled={mutation.isPending}
                        onClick={() => void handleGenerate(g)}
                        className="gap-1"
                      >
                        <FileCheck2 className="h-3.5 w-3.5" />
                        {pendingKey === g.key
                          ? "Creazione…"
                          : g.rfqs.length > 1
                            ? `Ordine unico (${g.rfqs.length} RDO)`
                            : "Genera ordine"}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}

        <DialogFooter className="items-center sm:justify-between">
          <DateField
            value={expectedDate}
            onChange={setExpectedDate}
            size="sm"
            placeholder="Consegna prevista (opzionale)"
            className="h-8 w-52"
          />
          <Button variant="outline" size="sm" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
