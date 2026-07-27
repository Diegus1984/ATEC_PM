import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Check, Plus } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchProjects } from "@/lib/api/projects"
import { fetchSuppliers } from "@/lib/api/suppliers"
import {
  fetchPriorityWorkRequests,
  fetchWorkRequests,
} from "@/lib/api/workRequests"
import type { Rfq, WorkRequest } from "@/lib/api/types"
import { isSystemProjectCode } from "@/lib/system-projects"
import { useWorkRequestsHub } from "@/lib/signalr/use-work-requests-hub"
import { notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"
import { RfqDialog } from "@/features/work-requests/components/RfqDialog"
import { WrDraftForm } from "@/features/work-requests/components/wr-draft-form"
import { WorkRequestRow } from "@/features/work-requests/components/wr-row"
import { useWorkRequestMutations } from "@/features/work-requests/use-work-request-mutations"
import {
  defaultVisibleColumnsFor,
  filterRowsByView,
  newWorkRequestPayload,
  toSaveRequest,
  WR_COLUMN_LABELS,
  WR_VIEW_HEADERS,
  type WorkRequestViewMode,
} from "@/features/work-requests/wr-shared"

export interface ProjectWorkRequestsProps {
  projectId?: number
  viewMode?: WorkRequestViewMode
}

export function ProjectWorkRequests({
  projectId = 0,
  viewMode = "project",
}: ProjectWorkRequestsProps) {
  const confirm = useConfirm()
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")

  // Filtri della tabella priorità
  const [priorityLevel, setPriorityLevel] = React.useState("all")
  const [priorityScope, setPriorityScope] = React.useState("tot")

  // Se viewMode è 'project', carichiamo per quella commessa; altrimenti carichiamo
  // tutte (e filtriamo dopo) o usiamo fetchPriorityWorkRequests.
  const queryKey =
    viewMode === "project"
      ? ["work-requests", projectId]
      : viewMode === "priorities"
        ? ["priority-work-requests", priorityLevel, priorityScope]
        : ["all-work-requests"]

  // Fornitori per il dialog RDO
  const { data: suppliers = [] } = useQuery({
    queryKey: ["suppliers"],
    queryFn: fetchSuppliers,
  })

  // Progetti per la creazione bozze (staging)
  const { data: projectsData } = useQuery({
    queryKey: ["projects"],
    queryFn: () => fetchProjects({ pageSize: 1000 }),
    enabled: viewMode === "drafts",
  })
  // INTERNA in testa al select, poi le commesse reali ordinate per codice.
  const projects = React.useMemo(() => {
    const items = projectsData?.items ?? []
    return [...items].sort((a, b) => {
      const aSys = isSystemProjectCode(a.code) ? 0 : 1
      const bSys = isSystemProjectCode(b.code) ? 0 : 1
      if (aSys !== bSys) return aSys - bSys
      return a.code.localeCompare(b.code, "it")
    })
  }, [projectsData?.items])

  const internaProjectId = React.useMemo(
    () => projects.find((p) => isSystemProjectCode(p.code))?.id ?? null,
    [projects]
  )

  const {
    data: workRequests = [],
    isLoading,
    error,
  } = useQuery({
    queryKey,
    queryFn: () => {
      if (viewMode === "project" && projectId > 0) {
        return fetchWorkRequests(projectId)
      } else if (viewMode === "priorities") {
        const scope = priorityScope === "tot" ? "" : priorityScope
        const lvl = priorityLevel === "all" ? "" : priorityLevel
        return fetchPriorityWorkRequests(scope, lvl)
      } else {
        return fetchWorkRequests(0) // Carica tutte
      }
    },
  })

  const { invalidate, create, update, remove, patchField } = useWorkRequestMutations()

  // Real-time: ricarica quando un altro utente modifica le lavorazioni
  useWorkRequestsHub(
    viewMode === "project" ? projectId > 0 : true,
    invalidate,
    viewMode === "project" ? projectId : undefined
  )

  // ── Dialog RDO ────────────────────────────────────────────────
  const [rfqDialogOpen, setRfqDialogOpen] = React.useState(false)
  const [activeRfqRequest, setActiveRfqRequest] = React.useState<WorkRequest | null>(
    null
  )

  const handleOpenRfq = (req: WorkRequest) => {
    setActiveRfqRequest(req)
    setRfqDialogOpen(true)
  }

  // RDO = solo elenco offerte tracciato a mano: non scrive più su fornitore/n° ODA
  // (quelli arrivano dalla DDP Officina per le righe collegate).
  const handleSaveRfqs = (updatedRfqs: Rfq[]) => {
    if (!activeRfqRequest) return
    update.mutate({
      id: activeRfqRequest.id,
      request: toSaveRequest(activeRfqRequest, { rfqs: updatedRfqs }),
    })
  }

  // ── Azioni di riga ────────────────────────────────────────────
  const handleDelete = async (req: WorkRequest) => {
    const ok = await confirm({
      title: "Eliminare la lavorazione?",
      description: req.description.trim() || "(lavorazione senza descrizione)",
      confirmLabel: "Elimina",
    })
    if (ok) remove.mutate(req.id)
  }

  // Cambio tipo: sulle WR da DDP l'ODA lo riallinea il sync; sulle manuali
  // Interna → ATEC / n° vuoto, uscendo da Interna si toglie solo l'ATEC ereditato.
  const handleTypeChange = (req: WorkRequest, typeVal: string) => {
    patchField(req.id, "type", typeVal)
    if (req.ddpOfficinaItemId != null) return
    if (typeVal === "Internal") {
      patchField(req.id, "po_supplier", "ATEC")
      patchField(req.id, "po_number", "")
    } else if (req.poSupplier === "ATEC") {
      patchField(req.id, "po_supplier", "")
    }
  }

  const handleConfirmDraft = (id: number) => {
    patchField(id, "is_staging", false)
    notifySuccess("Bozza confermata")
  }

  // Creazione rapida inline premendo invio
  const [newRequestDesc, setNewRequestDesc] = React.useState("")
  const handleAddNewRequest = () => {
    if (!newRequestDesc.trim()) return
    create.mutate(
      newWorkRequestPayload({
        projectId,
        description: newRequestDesc,
        requestDate: new Date().toISOString().split("T")[0],
      })
    )
    setNewRequestDesc("")
  }

  // ── Visibilità colonne: default per viewMode + persistenza localStorage ──
  const defaultVisibleColumns = React.useMemo(
    () => defaultVisibleColumnsFor(viewMode),
    [viewMode]
  )
  const [visibleColumns, setVisibleColumns] = usePersistedColumnVisibility(
    `work-requests-columns-${viewMode}`,
    defaultVisibleColumns
  )

  const columnToggles = WR_COLUMN_LABELS.map(({ id, label }) => ({
    id,
    label,
    checked: visibleColumns[id],
    onToggle: (val: boolean) =>
      setVisibleColumns((prev) => ({ ...prev, [id]: val })),
  }))
  const visibleCount = Object.values(visibleColumns).filter(Boolean).length

  const rows = React.useMemo(
    () => filterRowsByView(workRequests, viewMode),
    [workRequests, viewMode]
  )

  const handleConfirmAllDrafts = () => {
    rows.forEach((r) => patchField(r.id, "is_staging", false))
    notifySuccess("Tutte le bozze sono state confermate")
  }

  // Riga puntata da un link (?item=…): la si porta a schermo appena i dati ci sono.
  React.useEffect(() => {
    if (!highlightRowId || isLoading) return
    const timer = window.setTimeout(() => {
      const row = document.querySelector(
        `[data-row-id="${CSS.escape(highlightRowId)}"]`
      )
      row?.scrollIntoView({ block: "center", behavior: "smooth" })
    }, 150)
    return () => window.clearTimeout(timer)
  }, [highlightRowId, isLoading, rows])

  const { title: cardTitle, description: cardDescription } =
    WR_VIEW_HEADERS[viewMode]

  const headClass = "whitespace-nowrap py-2 align-top"

  if (isLoading)
    return <p className="text-sm text-muted-foreground">Caricamento lavorazioni...</p>
  if (error)
    return (
      <p className="text-sm text-destructive">
        Errore nel caricamento delle lavorazioni.
      </p>
    )

  return (
    <Card className="overflow-hidden py-0">
      <CardHeader className="flex flex-row items-center justify-between gap-3 border-b bg-muted/40 py-3">
        <div className="min-w-0 space-y-1">
          <CardTitle className="text-base">{cardTitle}</CardTitle>
          <CardDescription>{cardDescription}</CardDescription>
        </div>
        <div className="flex items-center gap-2">
          {viewMode === "priorities" && (
            <>
              <Select value={priorityLevel} onValueChange={setPriorityLevel}>
                <SelectTrigger className="w-36 h-8 text-sm">
                  <SelectValue placeholder="Priorità" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Tutte le priorità</SelectItem>
                  <SelectItem value="0">Priorità P0</SelectItem>
                  <SelectItem value="1">Priorità P1</SelectItem>
                  <SelectItem value="2">Priorità P2</SelectItem>
                </SelectContent>
              </Select>

              <Select value={priorityScope} onValueChange={setPriorityScope}>
                <SelectTrigger className="w-44 h-8 text-sm">
                  <SelectValue placeholder="Filtro tipo" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="tot">Tutte le lavorazioni</SelectItem>
                  <SelectItem value="ext">Solo Esterne</SelectItem>
                  <SelectItem value="int">Solo Interne</SelectItem>
                </SelectContent>
              </Select>
            </>
          )}
          {viewMode === "drafts" && rows.length > 0 && (
            <Button size="sm" className="h-8" onClick={handleConfirmAllDrafts}>
              <Check />
              Conferma Tutte
            </Button>
          )}
          <ColumnsMenu columns={columnToggles} />
        </div>
      </CardHeader>
      <CardContent className="p-3">
        {viewMode === "drafts" && (
          <WrDraftForm
            projects={projects}
            internaProjectId={internaProjectId}
            onCreate={(payload) => create.mutate(payload)}
          />
        )}

        <div className="overflow-hidden rounded-lg border">
          <Table className="min-w-[1320px] border-separate border-spacing-y-1.5">
            <TableHeader className="sticky top-0 z-10 bg-muted/40">
              <TableRow className="border-0 hover:bg-transparent">
                <TableHead className={cn(headClass, "w-10 text-center")}>#</TableHead>
                {visibleColumns.project && (
                  <TableHead className={cn(headClass, "w-48")}>Commessa</TableHead>
                )}
                {visibleColumns.requestDate && (
                  <TableHead className={cn(headClass, "w-32")}>Data Richiesta</TableHead>
                )}
                {visibleColumns.description && (
                  <TableHead className={cn(headClass, "min-w-[240px]")}>
                    Descrizione
                  </TableHead>
                )}
                {visibleColumns.type && (
                  <TableHead className={cn(headClass, "w-36")}>Tipo</TableHead>
                )}
                {visibleColumns.rfqs && (
                  <TableHead className={cn(headClass, "min-w-[200px]")}>
                    RDO (Offerte)
                  </TableHead>
                )}
                {visibleColumns.oda && (
                  <TableHead className={cn(headClass, "min-w-[220px]")}>
                    Dati ODA (Ordine)
                  </TableHead>
                )}
                {visibleColumns.priority && (
                  <TableHead className={cn(headClass, "w-40")}>Priorità</TableHead>
                )}
                {visibleColumns.availabilityDate && (
                  <TableHead className={cn(headClass, "w-32")}>Disponibilità</TableHead>
                )}
                {visibleColumns.notes && (
                  <TableHead className={cn(headClass, "min-w-[180px]")}>Note</TableHead>
                )}
                {visibleColumns.status && (
                  <TableHead className={cn(headClass, "w-36")}>Stato</TableHead>
                )}
                {visibleColumns.treatment && (
                  <TableHead className={cn(headClass, "w-28")}>Tratt.</TableHead>
                )}
                {visibleColumns.treatmentDate && (
                  <TableHead className={cn(headClass, "w-44")}>
                    Data Trattamento
                  </TableHead>
                )}
                {visibleColumns.treatmentNotes && (
                  <TableHead className={cn(headClass, "min-w-[180px]")}>
                    Note Trattamento
                  </TableHead>
                )}
                <TableHead className={cn(headClass, "w-12 text-right")} />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.length === 0 && (
                <TableRow>
                  <TableCell
                    colSpan={visibleCount + 2}
                    className="py-8 text-center text-muted-foreground"
                  >
                    Nessuna lavorazione trovata.
                  </TableCell>
                </TableRow>
              )}
              {rows.map((req, idx) => (
                <WorkRequestRow
                  key={req.id}
                  req={req}
                  index={idx}
                  columns={visibleColumns}
                  highlighted={highlightRowId === String(req.id)}
                  onPatch={patchField}
                  onTypeChange={handleTypeChange}
                  onOpenRfq={handleOpenRfq}
                  onConfirmDraft={handleConfirmDraft}
                  onDelete={(r) => void handleDelete(r)}
                />
              ))}

              {/* Riga di inserimento rapido inline */}
              {viewMode === "project" && (
                <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
                  <TableCell className="whitespace-normal py-2 !align-top text-center">
                    <Plus className="mx-auto size-4 text-muted-foreground" />
                  </TableCell>
                  <TableCell
                    colSpan={visibleCount + 1}
                    className="whitespace-normal py-2 !align-top"
                  >
                    <Input
                      placeholder="Aggiungi una nuova lavorazione (scrivi la descrizione e premi Invio)..."
                      className="h-8 w-full border-dashed text-sm shadow-none"
                      value={newRequestDesc}
                      onChange={(e) => setNewRequestDesc(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") handleAddNewRequest()
                      }}
                    />
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>

        {/* Dialog Gestione Offerte (RDO) */}
        <RfqDialog
          open={rfqDialogOpen}
          rfqs={activeRfqRequest?.rfqs ?? []}
          suppliers={suppliers}
          onClose={() => setRfqDialogOpen(false)}
          onSave={handleSaveRfqs}
        />
      </CardContent>
    </Card>
  )
}
