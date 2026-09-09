import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { Check, Clock, Plus, Trash2, X } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
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
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
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
import { useHrHub } from "@/lib/signalr/use-hr-hub"
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

type Scheda = "mie" | "da_approvare" | "tutte"

/** Ogni scheda è una rotta: sottovoci del menu e schede in pagina dicono la stessa cosa (08/09). */
const PERCORSO_SCHEDA: Record<Scheda, string> = {
  mie: "/hr/richieste",
  da_approvare: "/hr/richieste/da-approvare",
  tutte: "/hr/richieste/tutte",
}

export function RichiestePage({ vista = "mie" }: { vista?: Scheda }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const canManage = canWriteFeature("nav.hr_richieste")

  // Chi non approva vede solo le sue: le altre schede non esistono per lui, qualunque rotta.
  const activeTab: Scheda = canManage ? vista : "mie"
  const confirm = useConfirm()
  const [dialogNuovaAperta, setDialogNuovaAperta] = React.useState(false)
  // Dettaglio di una richiesta: la riga dice il minimo, il clic dice tutto (Diego, 08/09).
  const [dettaglio, setDettaglio] = React.useState<HrAbsence | null>(null)

  async function annullaConConferma(r: HrAbsence) {
    const ok = await confirm({
      title: "Annullare la richiesta?",
      description: `${tipoLabel(r.absenceType)} di ${r.employeeName} del ${formatDateShort(r.dateFrom)}. La richiesta viene annullata e non conta più.`,
      confirmLabel: "Annulla richiesta",
      destructive: true,
    })
    if (ok) {
      setDettaglio(null)
      annullaMutation.mutate(r.id)
    }
  }
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

  // Filtri sulla lista (Diego, 09/09/2026): dipendente, reparto, tipo, stato. Lavorano sulla
  // lista dell'anno già scaricata: niente giri al server per un filtro.
  const [filtroDipendente, setFiltroDipendente] = React.useState<number | null>(null)
  const [filtroReparto, setFiltroReparto] = React.useState<string>("tutti")
  const [filtroTipo, setFiltroTipo] = React.useState<string>("tutti")
  const [filtroStato, setFiltroStato] = React.useState<string>("tutti")
  const filtriAttivi =
    filtroDipendente != null || filtroReparto !== "tutti" || filtroTipo !== "tutti" || filtroStato !== "tutti"

  const opzioniDipendenti: LookupComboboxOption<number>[] = React.useMemo(() => {
    const visti = new Map<number, string>()
    for (const r of richieste) visti.set(r.employeeId, r.employeeName)
    return [...visti.entries()]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name, "it"))
  }, [richieste])
  const reparti = React.useMemo(
    () => [...new Set(richieste.map((r) => r.departmentName).filter((x): x is string => Boolean(x)))].sort(),
    [richieste]
  )

  const displayedRichieste = React.useMemo(() => {
    let lista = activeTab === "da_approvare" ? richieste.filter((r) => r.status === "PENDING") : richieste
    if (filtroDipendente != null) lista = lista.filter((r) => r.employeeId === filtroDipendente)
    if (filtroReparto !== "tutti") lista = lista.filter((r) => (r.departmentName ?? "") === filtroReparto)
    if (filtroTipo !== "tutti") lista = lista.filter((r) => r.absenceType === filtroTipo)
    if (filtroStato !== "tutti") lista = lista.filter((r) => r.status === filtroStato)
    // Dalla più recente inserita: il server ordina già così, qui si tiene l'ordine anche se un
    // giorno la lista arrivasse da altrove.
    return [...lista].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id - a.id)
  }, [richieste, activeTab, filtroDipendente, filtroReparto, filtroTipo, filtroStato])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-absences"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-calendar"] })
  }

  // Real-time: una richiesta inserita, approvata o annullata da un altro utente (o un
  // import di assenze da Ecos) ricarica la lista senza aspettare il refresh.
  useHrHub(true, (change) => {
    if (change.action !== "import-progress") invalidate()
  })

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

      {/* Filtri */}
      <div className="flex flex-wrap items-center gap-2">
        <LookupCombobox<number>
          options={opzioniDipendenti}
          value={filtroDipendente}
          onValueChange={setFiltroDipendente}
          placeholder="Tutti i dipendenti"
          noneLabel="— tutti i dipendenti —"
          className="w-56"
        />
        {reparti.length > 0 && (
          <Select value={filtroReparto} onValueChange={setFiltroReparto}>
            <SelectTrigger className="h-8 w-52 text-sm">
              <SelectValue placeholder="Reparto" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="tutti">Tutti i reparti</SelectItem>
              {reparti.map((rep) => (
                <SelectItem key={rep} value={rep}>
                  {rep}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
        <Select value={filtroTipo} onValueChange={setFiltroTipo}>
          <SelectTrigger className="h-8 w-44 text-sm">
            <SelectValue placeholder="Tipo" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="tutti">Tutti i tipi</SelectItem>
            {(["VACATION", "PERMIT", "SICKNESS", "INJURY", "OTHER"] as const).map((t) => (
              <SelectItem key={t} value={t}>
                {tipoLabel(t)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={filtroStato} onValueChange={setFiltroStato}>
          <SelectTrigger className="h-8 w-40 text-sm">
            <SelectValue placeholder="Stato" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="tutti">Tutti gli stati</SelectItem>
            <SelectItem value="PENDING">In attesa</SelectItem>
            <SelectItem value="APPROVED">Approvate</SelectItem>
            <SelectItem value="REJECTED">Rifiutate</SelectItem>
            <SelectItem value="CANCELLED">Annullate</SelectItem>
          </SelectContent>
        </Select>
        {filtriAttivi && (
          <Button
            size="sm"
            variant="ghost"
            className="h-8 text-xs"
            onClick={() => {
              setFiltroDipendente(null)
              setFiltroReparto("tutti")
              setFiltroTipo("tutti")
              setFiltroStato("tutti")
            }}
          >
            Azzera filtri
          </Button>
        )}
        <span className="ml-auto text-xs text-muted-foreground">
          {displayedRichieste.length} richieste
        </span>
      </div>

      {/* Tabs */}
      <Tabs
        value={activeTab}
        onValueChange={(v) => navigate(PERCORSO_SCHEDA[v as Scheda])}
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
                  : `${r.hours ?? 0}h${r.hourFrom && r.hourTo ? ` (${r.hourFrom}–${r.hourTo})` : ""}`

                return (
                  <TableRow
                    key={r.id}
                    className="cursor-pointer hover:bg-muted/60"
                    onClick={() => setDettaglio(r)}
                  >
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
                      <TableCell
                        className="text-right space-x-1 whitespace-nowrap"
                        onClick={(e) => e.stopPropagation()}
                      >
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
                            onClick={() => void annullaConConferma(r)}
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

      {/* Dettaglio richiesta */}
      <Dialog open={dettaglio != null} onOpenChange={(open) => { if (!open) setDettaglio(null) }}>
        <DialogContent className="sm:max-w-md">
          {dettaglio && (
            <>
              <DialogHeader>
                <DialogTitle>{dettaglio.employeeName}</DialogTitle>
                <DialogDescription className="flex flex-wrap items-center gap-2">
                  <span>{tipoLabel(dettaglio.absenceType)}</span>
                  {statoBadge(dettaglio.status)}
                </DialogDescription>
              </DialogHeader>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
                <dt className="text-muted-foreground">Reparto</dt>
                <dd>{dettaglio.departmentName || "—"}</dd>
                <dt className="text-muted-foreground">Periodo</dt>
                <dd className="tabular-nums">
                  {dettaglio.dateFrom.slice(0, 10) === dettaglio.dateTo.slice(0, 10)
                    ? formatDateShort(dettaglio.dateFrom)
                    : `${formatDateShort(dettaglio.dateFrom)} → ${formatDateShort(dettaglio.dateTo)}`}
                </dd>
                <dt className="text-muted-foreground">Durata</dt>
                <dd className="tabular-nums">
                  {dettaglio.isFullDay
                    ? "Giornata intera"
                    : dettaglio.hours != null
                      ? `${dettaglio.hours}h${dettaglio.hourFrom && dettaglio.hourTo ? ` (${dettaglio.hourFrom}–${dettaglio.hourTo})` : ""}`
                      : "Ore non indicate"}
                </dd>
                <dt className="text-muted-foreground">Origine</dt>
                <dd>
                  {dettaglio.source === "ECOS"
                    ? `Ecos${dettaglio.ecosAbsenceId ? ` (richiesta ${dettaglio.ecosAbsenceId})` : ""}`
                    : "ATEC PM"}
                </dd>
                <dt className="text-muted-foreground">Note</dt>
                <dd>{dettaglio.notes || "—"}</dd>
                {dettaglio.approvedByName && (
                  <>
                    <dt className="text-muted-foreground">Approvata da</dt>
                    <dd>
                      {dettaglio.approvedByName} ({formatDateShort(dettaglio.approvedAt)})
                    </dd>
                  </>
                )}
                {dettaglio.rejectionReason && (
                  <>
                    <dt className="text-muted-foreground">Motivo del rifiuto</dt>
                    <dd>{dettaglio.rejectionReason}</dd>
                  </>
                )}
                <dt className="text-muted-foreground">Inserita il</dt>
                <dd>
                  {formatDateShort(dettaglio.createdAt)}
                  {dettaglio.createdByName ? ` da ${dettaglio.createdByName}` : ""}
                </dd>
              </dl>
              {dettaglio.ecosAbsenceId && dettaglio.status === "PENDING" && (
                <p className="rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
                  Questa richiesta vive anche su Ecos: approvarla, rifiutarla o annullarla da qui la
                  aggiorna prima là e poi qui. La durata ufficiale arriva da Ecos quando la accetta.
                </p>
              )}
              {dettaglio.status === "PENDING" && (
                <DialogFooter className="gap-2 sm:justify-between">
                  <Button
                    size="sm"
                    variant="ghost"
                    className="text-muted-foreground hover:text-destructive"
                    onClick={() => void annullaConConferma(dettaglio)}
                    disabled={annullaMutation.isPending}
                  >
                    <Trash2 className="mr-1 size-3.5" />
                    Annulla richiesta
                  </Button>
                  {canManage && (
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        variant="outline"
                        className="text-rose-600 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950"
                        onClick={() => {
                          const id = dettaglio.id
                          setDettaglio(null)
                          setRifiutoDialog({ open: true, absenceId: id, reason: "" })
                        }}
                        disabled={approvaMutation.isPending}
                      >
                        <X className="mr-1 size-3" />
                        Rifiuta
                      </Button>
                      <Button
                        size="sm"
                        className="bg-emerald-600 text-white hover:bg-emerald-700"
                        onClick={() => {
                          const id = dettaglio.id
                          setDettaglio(null)
                          approvaMutation.mutate({ id, approved: true })
                        }}
                        disabled={approvaMutation.isPending}
                      >
                        <Check className="mr-1 size-3" />
                        Approva
                      </Button>
                    </div>
                  )}
                </DialogFooter>
              )}
            </>
          )}
        </DialogContent>
      </Dialog>

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
