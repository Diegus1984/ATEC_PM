import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, Clock, Plus, Trash2, X } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Textarea } from "@/components/ui/textarea"
import {
  approveHrAbsence,
  cancelHrAbsence,
  fetchHrAbsences,
} from "@/lib/api/hr"
import type { HrAbsence } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { NuovaRichiestaDialog } from "./NuovaRichiestaDialog"

const COLUMNS = [
  { id: "employee", label: "Dipendente" },
  { id: "department", label: "Reparto" },
  { id: "type", label: "Tipologia" },
  { id: "period", label: "Periodo" },
  { id: "duration", label: "Durata" },
  { id: "status", label: "Stato" },
  { id: "notes", label: "Note" },
  { id: "approver", label: "Approvatore" },
  { id: "created", label: "Inserita il" },
  { id: "actions", label: "Azioni" },
]
const COLUMNS_DEFAULT = Object.fromEntries(COLUMNS.map((c) => [c.id, true]))
const COLUMNS_STORAGE_KEY = "hr-richieste-columns-v1"

function tipoLabel(type: string): string {
  switch (type) {
    case "VACATION":
      return "Ferie"
    case "PERMIT":
      return "Permesso / ROL"
    case "SICKNESS":
      return "Malattia"
    case "INJURY":
      return "Infortunio"
    default:
      return type
  }
}

function tipoBadgeClass(type: string): string {
  switch (type) {
    case "VACATION":
      return "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300 border-sky-300"
    case "PERMIT":
      return "bg-indigo-100 text-indigo-800 dark:bg-indigo-950 dark:text-indigo-300 border-indigo-300"
    case "SICKNESS":
      return "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300 border-amber-300"
    case "INJURY":
      return "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300 border-rose-300"
    default:
      return "bg-muted text-muted-foreground"
  }
}

function statoBadge(status: string) {
  switch (status) {
    case "PENDING":
      return (
        <Badge
          variant="outline"
          className="bg-amber-50 text-amber-700 border-amber-300 dark:bg-amber-950 dark:text-amber-300"
        >
          <Clock className="mr-1 size-3" />
          In attesa
        </Badge>
      )
    case "APPROVED":
      return (
        <Badge
          variant="outline"
          className="bg-emerald-50 text-emerald-700 border-emerald-300 dark:bg-emerald-950 dark:text-emerald-300"
        >
          <Check className="mr-1 size-3" />
          Approvata
        </Badge>
      )
    case "REJECTED":
      return (
        <Badge
          variant="outline"
          className="bg-rose-50 text-rose-700 border-rose-300 dark:bg-rose-950 dark:text-rose-300"
        >
          <X className="mr-1 size-3" />
          Rifiutata
        </Badge>
      )
    case "CANCELLED":
      return (
        <Badge variant="outline" className="text-muted-foreground">
          Annullata
        </Badge>
      )
    default:
      return <Badge variant="outline">{status}</Badge>
  }
}

export function RichiestePage() {
  const queryClient = useQueryClient()
  const canManage = canWriteFeature("nav.hr_richieste")

  const [activeTab, setActiveTab] = React.useState<"mie" | "da_approvare" | "tutte">("mie")
  const [dialogNuovaAperta, setDialogNuovaAperta] = React.useState(false)
  const [rifiutoDialog, setRifiutoDialog] = React.useState<{
    open: boolean
    absenceId: number | null
    reason: string
  }>({ open: false, absenceId: null, reason: "" })

  const [filtroAnno, setFiltroAnno] = React.useState<number>(() => new Date().getFullYear())

  const [visible, setVisible] = usePersistedColumnVisibility(
    COLUMNS_STORAGE_KEY,
    COLUMNS_DEFAULT
  )
  const columnToggles = COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: visible[id] ?? true,
    onToggle: (value: boolean) => setVisible((prev) => ({ ...prev, [id]: value })),
  }))
  const show = (id: string) => visible[id] ?? true

  const { data: richieste = [], isLoading } = useQuery({
    queryKey: ["hr-absences", filtroAnno],
    queryFn: () => fetchHrAbsences({ year: filtroAnno }),
  })

  const daApprovareCount = React.useMemo(
    () => richieste.filter((r) => r.status === "PENDING").length,
    [richieste]
  )

  const displayedRichieste = React.useMemo(() => {
    if (activeTab === "da_approvare") {
      return richieste.filter((r) => r.status === "PENDING")
    }
    return richieste
  }, [richieste, activeTab])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-absences"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-calendar"] })
  }

  const approvaMutation = useMutation({
    mutationFn: ({ id, approved, reason }: { id: number; approved: boolean; reason?: string }) =>
      approveHrAbsence(id, { approved, rejectionReason: reason }),
    onSuccess: (_, vars) => {
      notifySuccess(vars.approved ? "Richiesta approvata" : "Richiesta rifiutata")
      invalidate()
      setRifiutoDialog({ open: false, absenceId: null, reason: "" })
    },
    onError: (e) => notifyError((e as Error).message),
  })

  const annullaMutation = useMutation({
    mutationFn: (id: number) => cancelHrAbsence(id),
    onSuccess: () => {
      notifySuccess("Richiesta annullata")
      invalidate()
    },
    onError: (e) => notifyError((e as Error).message),
  })

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-lg font-semibold">Ferie e Permessi</h1>
          <p className="text-sm text-muted-foreground">
            Richieste di assenza, permessi orari e autorizzazioni reparto.
          </p>
        </div>

        <div className="ml-auto flex flex-wrap items-center gap-2">
          <div className="flex items-center gap-1">
            <Label className="text-xs text-muted-foreground">Anno:</Label>
            <Input
              type="number"
              value={filtroAnno}
              onChange={(e) => setFiltroAnno(Number(e.target.value))}
              className="w-20 h-8 text-sm"
            />
          </div>
          <Button size="sm" onClick={() => setDialogNuovaAperta(true)}>
            <Plus className="mr-1 size-3.5" />
            Nuova richiesta
          </Button>
          <ColumnsMenu columns={columnToggles} />
        </div>
      </div>

      {/* Tabs */}
      <Tabs
        value={activeTab}
        onValueChange={(v) => setActiveTab(v as "mie" | "da_approvare" | "tutte")}
      >
        <TabsList>
          <TabsTrigger value="mie">Le mie richieste</TabsTrigger>
          {canManage && (
            <TabsTrigger value="da_approvare" className="relative">
              Da approvare
              {daApprovareCount > 0 && (
                <span className="ml-1.5 rounded-full bg-amber-500 text-white text-[10px] px-1.5 py-0.2 font-medium">
                  {daApprovareCount}
                </span>
              )}
            </TabsTrigger>
          )}
          {canManage && <TabsTrigger value="tutte">Tutte le richieste</TabsTrigger>}
        </TabsList>
      </Tabs>

      {/* Table */}
      <GridScroller>
        <Table>
          <TableHeader>
            <TableRow>
              {show("employee") && <TableHead>Dipendente</TableHead>}
              {show("department") && <TableHead>Reparto</TableHead>}
              {show("type") && <TableHead>Tipologia</TableHead>}
              {show("period") && <TableHead>Periodo</TableHead>}
              {show("duration") && <TableHead>Durata</TableHead>}
              {show("status") && <TableHead>Stato</TableHead>}
              {show("notes") && <TableHead>Note / Motivo</TableHead>}
              {show("approver") && <TableHead>Approvatore</TableHead>}
              {show("created") && <TableHead>Inserita il</TableHead>}
              {show("actions") && <TableHead className="text-right">Azioni</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableRow>
                <TableCell colSpan={COLUMNS.length} className="h-24 text-center text-muted-foreground">
                  Caricamento richieste…
                </TableCell>
              </TableRow>
            ) : displayedRichieste.length === 0 ? (
              <TableRow>
                <TableCell colSpan={COLUMNS.length} className="h-24 text-center text-muted-foreground">
                  Nessuna richiesta trovata per i criteri selezionati.
                </TableCell>
              </TableRow>
            ) : (
              displayedRichieste.map((r: HrAbsence) => {
                const isSingleDay = r.dateFrom.slice(0, 10) === r.dateTo.slice(0, 10)
                const periodText = isSingleDay
                  ? formatDateShort(r.dateFrom)
                  : `${formatDateShort(r.dateFrom)} → ${formatDateShort(r.dateTo)}`
                const durationText = r.isFullDay
                  ? isSingleDay
                    ? "1 giorno"
                    : "Più giorni"
                  : `${r.hours ?? 0}h`

                return (
                  <TableRow key={r.id}>
                    {show("employee") && (
                      <TableCell className="font-medium">{r.employeeName}</TableCell>
                    )}
                    {show("department") && (
                      <TableCell className="text-muted-foreground">
                        {r.departmentName || "—"}
                      </TableCell>
                    )}
                    {show("type") && (
                      <TableCell>
                        <span
                          className={cn(
                            "inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border",
                            tipoBadgeClass(r.absenceType)
                          )}
                        >
                          {tipoLabel(r.absenceType)}
                        </span>
                      </TableCell>
                    )}
                    {show("period") && <TableCell>{periodText}</TableCell>}
                    {show("duration") && (
                      <TableCell className="text-muted-foreground">{durationText}</TableCell>
                    )}
                    {show("status") && <TableCell>{statoBadge(r.status)}</TableCell>}
                    {show("notes") && (
                      <TableCell className="max-w-xs truncate text-xs text-muted-foreground">
                        {r.notes || (r.rejectionReason ? `Rifiuto: ${r.rejectionReason}` : "—")}
                      </TableCell>
                    )}
                    {show("approver") && (
                      <TableCell className="text-xs text-muted-foreground">
                        {r.approvedByName ? `${r.approvedByName} (${formatDateShort(r.approvedAt)})` : "—"}
                      </TableCell>
                    )}
                    {show("created") && (
                      <TableCell className="text-xs text-muted-foreground">
                        {formatDateShort(r.createdAt)}
                      </TableCell>
                    )}
                    {show("actions") && (
                      <TableCell className="text-right space-x-1 whitespace-nowrap">
                        {canManage && r.status === "PENDING" && (
                          <>
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-7 text-xs text-emerald-600 hover:text-emerald-700 hover:bg-emerald-50 dark:hover:bg-emerald-950"
                              onClick={() => approvaMutation.mutate({ id: r.id, approved: true })}
                              disabled={approvaMutation.isPending}
                            >
                              <Check className="mr-1 size-3" />
                              Approva
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-7 text-xs text-rose-600 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950"
                              onClick={() =>
                                setRifiutoDialog({ open: true, absenceId: r.id, reason: "" })
                              }
                              disabled={approvaMutation.isPending}
                            >
                              <X className="mr-1 size-3" />
                              Rifiuta
                            </Button>
                          </>
                        )}
                        {r.status === "PENDING" && (
                          <Button
                            size="sm"
                            variant="ghost"
                            className="h-7 text-xs text-muted-foreground hover:text-destructive"
                            onClick={() => annullaMutation.mutate(r.id)}
                            disabled={annullaMutation.isPending}
                            title="Annulla richiesta"
                          >
                            <Trash2 className="size-3.5" />
                          </Button>
                        )}
                      </TableCell>
                    )}
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
      </GridScroller>

      {/* Dialog Nuova Richiesta */}
      <NuovaRichiestaDialog
        open={dialogNuovaAperta}
        onOpenChange={setDialogNuovaAperta}
        canManage={canManage}
      />

      {/* Dialog Motivo Rifiuto */}
      <Dialog
        open={rifiutoDialog.open}
        onOpenChange={(open) =>
          setRifiutoDialog((prev) => ({ ...prev, open, absenceId: open ? prev.absenceId : null }))
        }
      >
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Rifiuta richiesta</DialogTitle>
            <DialogDescription>
              Specifica il motivo del rifiuto della richiesta di assenza.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2 py-2">
            <Label>Motivo del rifiuto</Label>
            <Textarea
              value={rifiutoDialog.reason}
              onChange={(e) =>
                setRifiutoDialog((prev) => ({ ...prev, reason: e.target.value }))
              }
              placeholder="Es. Mancanza copertura reparto..."
              rows={3}
            />
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setRifiutoDialog({ open: false, absenceId: null, reason: "" })}
            >
              Annulla
            </Button>
            <Button
              variant="destructive"
              disabled={approvaMutation.isPending}
              onClick={() => {
                if (rifiutoDialog.absenceId) {
                  approvaMutation.mutate({
                    id: rifiutoDialog.absenceId,
                    approved: false,
                    reason: rifiutoDialog.reason,
                  })
                }
              }}
            >
              Conferma rifiuto
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
