// ── Gestione Trasferta (blocco 6) ──────────────────────────────────────────
//
// Pagina PM come Milestones e SAL: `PmSidebar` con le commesse a sinistra, card o
// dettaglio a destra. Il modulo non esisteva: in ATEC PM «trasferta» era solo una voce di
// costo aggregata nel preventivo e un tipo di riga timesheet.

import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Check,
  ChevronRight,
  FolderOpen,
  Plane,
  Plus,
  RefreshCw,
  Trash2,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { PmSidebar } from "@/components/shared/pm-sidebar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Skeleton } from "@/components/ui/skeleton"
import { Switch } from "@/components/ui/switch"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableRow,
} from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import {
  createTravelStep,
  deleteTravelStep,
  fetchTravelPeople,
  fetchTravelPlan,
  fetchTravelSummary,
  rebuildTravelFromTimesheet,
  reorderTravelSteps,
  updateTravelStep,
  verifyTravelScarico,
} from "@/lib/api/travel"
import type {
  TravelCalcKind,
  TravelProjectSummaryDto,
  TravelStepDto,
} from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro, fmtHours } from "@/lib/format"
import { buildPmProjectSections } from "@/lib/pm-project-sections"
import { useTravelHub } from "@/lib/signalr/use-travel-hub"
import { notifyError, notifyInfo, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { TravelAggregateBar } from "./TravelAggregateBar"
import { TravelCalcDialog } from "./TravelCalcDialog"
import {
  TravelStepTable,
  TravelTableHead,
  TravelTotalsRow,
  rigaDaCompilare,
  stickyBody,
} from "./TravelStepTable"

import type { ColumnDef } from "@tanstack/react-table"

import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { getSession } from "@/lib/auth/session"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"

const COLUMN_LABELS: Record<string, string> = {
  code: "Codice",
  title: "Commessa",
  customerName: "Cliente",
  pmName: "PM",
  stepCount: "N° Step",
  days: "Giorni",
  plannedDays: "Giorni previsti",
  // #98: «Ore cantiere» si chiama «Ore Trasferta» — stessa fonte, nome deciso da Zanoni.
  travelHours: "Ore Trasferta",
  travelHoursCost: "Costo Ore Trasferta",
  plannedHoursCost: "Costo Ore Previsto",
  travelCost: "Spese Trasferta",
  plannedTravelCost: "Spese Previste",
  daVerificare: "Da verificare",
}

type ViewMode = "cards" | "table"

function viewModeStorageKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `trasferta.list.viewMode.${employeeId}`
}

function loadViewMode(): ViewMode {
  const stored = localStorage.getItem(viewModeStorageKey())
  return stored === "table" ? "table" : "cards"
}

export function TrasfertaPage() {
  const queryClient = useQueryClient()
  const [selected, setSelected] = React.useState<number | null>(null)
  const [includeClosed, setIncludeClosed] = React.useState(false)
  // Vista rapida «Con trasferte» (#70): prima era un'etichetta con un numero e basta —
  // il click faceva `setSelected(null)` come «Tutte le commesse», quindi l'elenco non
  // cambiava mai. Ora è un filtro vero, applicato ovunque si mostrino le commesse.
  const [onlyWithTravel, setOnlyWithTravel] = React.useState(false)
  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadViewMode)

  const setViewMode = React.useCallback((mode: ViewMode) => {
    setViewModeState(mode)
    localStorage.setItem(viewModeStorageKey(), mode)
  }, [])

  const summaryQuery = useQuery({
    queryKey: ["travel-summary", includeClosed],
    queryFn: () => fetchTravelSummary(includeClosed),
  })

  const projects = React.useMemo(
    () => summaryQuery.data ?? [],
    [summaryQuery.data]
  )

  // Ambiente condiviso: le card si riallineano appena qualcuno tocca una trasferta.
  const refreshSummary = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["travel-summary"] })
  }, [queryClient])
  useTravelHub(true, refreshSummary, null)

  /**
   * «Verifica effettuata» (#102): spegne il rosso della card e il pallino del menu.
   * Niente conferma — non si perde nulla e si rifà premendo di nuovo dopo il prossimo
   * scarico. Insieme al riepilogo si rinfresca il contatore del menu, che ha una chiave
   * a parte.
   */
  const verifyMutation = useMutation({
    mutationFn: (projectId: number) => verifyTravelScarico(projectId),
    onSuccess: (message) => {
      notifySuccess(message)
      refreshSummary()
      void queryClient.invalidateQueries({ queryKey: ["travel", "pending-count"] })
    },
    onError: (err: Error) => notifyError(err),
  })

  const withTravel = React.useMemo(
    () => projects.filter((p) => p.stepCount > 0),
    [projects]
  )
  /** Le commesse effettivamente mostrate: tabella, card ed elenco della barra laterale. */
  const visibili = onlyWithTravel ? withTravel : projects

  const columns = React.useMemo<ColumnDef<TravelProjectSummaryDto>[]>(
    () => [
      {
        accessorKey: "code",
        header: COLUMN_LABELS.code,
        cell: ({ row }) => (
          <Badge variant="outline" className="font-mono text-xs font-bold">
            {row.original.code}
          </Badge>
        ),
      },
      {
        accessorKey: "title",
        header: COLUMN_LABELS.title,
        cell: ({ row }) => (
          <button
            type="button"
            className="max-w-xs truncate text-left font-medium hover:underline"
            onClick={() => setSelected(row.original.projectId)}
          >
            {row.original.title}
          </button>
        ),
      },
      {
        accessorKey: "customerName",
        header: COLUMN_LABELS.customerName,
        cell: ({ row }) => row.original.customerName || "—",
      },
      {
        accessorKey: "pmName",
        header: COLUMN_LABELS.pmName,
        cell: ({ row }) => row.original.pmName || "—",
      },
      {
        accessorKey: "stepCount",
        header: COLUMN_LABELS.stepCount,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">{row.original.stepCount}</span>
        ),
      },
      {
        // Dalla #98 i giorni possono valere mezze giornate (0,5): formato italiano.
        accessorKey: "days",
        header: COLUMN_LABELS.days,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {fmtHours(row.original.days)}
          </span>
        ),
      },
      {
        // #98: i giorni di trasferta PREVENTIVATI, dalle risorse pianificate del Bilancio.
        accessorKey: "plannedDays",
        header: COLUMN_LABELS.plannedDays,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {fmtHours(row.original.plannedDays)}
          </span>
        ),
      },
      // «Ore» e «Costi Personale» sono usciti anche da qui (#52): nella trasferta non si
      // imputano più, quindi sulle commesse nuove sarebbero due colonne sempre a zero.
      // I valori restano nel database sulle righe vecchie, semplicemente non si mostrano.
      {
        // #96: ore di cantiere a consuntivo dal timesheet (stesso perimetro del costo sotto).
        accessorKey: "travelHours",
        header: COLUMN_LABELS.travelHours,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {fmtHours(row.original.travelHours)} h
          </span>
        ),
      },
      {
        // #92: costo delle ore di cantiere dal timesheet — informativo, non va al Bilancio.
        accessorKey: "travelHoursCost",
        header: COLUMN_LABELS.travelHoursCost,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {euro(row.original.travelHoursCost)}
          </span>
        ),
      },
      {
        // #96: costo preventivato delle ore delle fasi cantiere, come lo calcola il Bilancio.
        accessorKey: "plannedHoursCost",
        header: COLUMN_LABELS.plannedHoursCost,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {euro(row.original.plannedHoursCost)}
          </span>
        ),
      },
      {
        accessorKey: "travelCost",
        header: COLUMN_LABELS.travelCost,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">{euro(row.original.travelCost)}</span>
        ),
      },
      {
        // #96: «Spese Trasferta» a preventivo — la voce del Riepilogo Costi del Bilancio.
        accessorKey: "plannedTravelCost",
        header: COLUMN_LABELS.plannedTravelCost,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {euro(row.original.plannedTravelCost)}
          </span>
        ),
      },
      {
        // #102: la stessa informazione delle card anche in vista tabella, o chi lavora
        // con l'elenco non saprebbe mai che è arrivato qualcosa.
        id: "daVerificare",
        accessorFn: (r) => r.pendingPeople,
        header: COLUMN_LABELS.daVerificare,
        cell: ({ row }) =>
          row.original.needsVerification ? (
            <Badge
              variant="outline"
              className="border-red-400/60 text-red-700 dark:text-red-300"
              title={`${fmtHours(row.original.pendingHours)} h non verificate`}
            >
              {row.original.pendingPeople}{" "}
              {row.original.pendingPeople === 1 ? "persona" : "persone"}
            </Badge>
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        id: "actions",
        enableHiding: false,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <Button
              variant="outline"
              size="sm"
              className="h-8"
              onClick={() => setSelected(row.original.projectId)}
            >
              Apri
            </Button>
          </div>
        ),
      },
    ],
    []
  )

  const viewToggle = (
    <Select
      value={viewMode}
      onValueChange={(val) => setViewMode(val as ViewMode)}
    >
      <SelectTrigger size="sm" className="w-[130px]">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="table">Tabella</SelectItem>
        <SelectItem value="cards">Card</SelectItem>
      </SelectContent>
    </Select>
  )

  return (
    <div className="flex gap-4">
      <PmSidebar
        storageKey="trasferta"
        quickViews={[
          {
            key: "all",
            selected: selected === null && !onlyWithTravel,
            onClick: () => {
              setSelected(null)
              setOnlyWithTravel(false)
            },
            icon: <FolderOpen className="size-4" />,
            label: "Tutte le commesse",
            count: projects.length,
          },
          {
            key: "with",
            selected: selected === null && onlyWithTravel,
            onClick: () => {
              setSelected(null)
              setOnlyWithTravel(true)
            },
            icon: <Plane className="size-4" />,
            label: "Con trasferte",
            count: withTravel.length,
          },
        ]}
        sections={buildPmProjectSections(
          visibili.map((p) => ({
            code: p.code,
            container: {
              key: String(p.projectId),
              selected: selected === p.projectId,
              onClick: () => setSelected(p.projectId),
              label: `${p.code} — ${p.title}`,
              count: p.stepCount,
              // #102: pallino rosso sulle commesse con scarico ore da guardare, così
              // si vedono anche quando l'elenco è lungo e la card è fuori schermo.
              dots: p.needsVerification
                ? [{ dotClass: "bg-red-500", label: "Scarico ore da verificare" }]
                : [],
            },
          }))
        )}
      />

      <div className="min-w-0 flex-1 space-y-4">
        {selected != null ? (
          <TravelProjectDetail
            project={projects.find((p) => p.projectId === selected)}
            projectId={selected}
            onBack={() => setSelected(null)}
            onChanged={() => {
              void queryClient.invalidateQueries({ queryKey: ["travel-summary"] })
            }}
          />
        ) : summaryQuery.isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : visibili.length === 0 ? (
          <Empty className="p-8">
            <EmptyHeader>
              <EmptyTitle>Nessuna commessa</EmptyTitle>
              <EmptyDescription>
                {onlyWithTravel
                  ? "Nessuna commessa ha ancora una trasferta: torna a «Tutte le commesse» per vederle tutte."
                  : "Le bozze e le commesse annullate restano sempre fuori."}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : viewMode === "table" ? (
          <DataTableCardFiltered
            title="Gestione Trasferta"
            description="Riepilogo trasferte per commessa."
            columns={columns}
            data={visibili}
            columnLabels={COLUMN_LABELS}
            isLoading={summaryQuery.isLoading}
            isFetching={summaryQuery.isFetching}
            onRefresh={() => summaryQuery.refetch()}
            searchPlaceholder="Cerca commessa, cliente, PM…"
            rowNoun="commesse"
            emptyMessage="Nessuna commessa trovata."
            getRowId={(row) => String(row.projectId)}
            onRowDoubleClick={(row) => setSelected(row.projectId)}
            // Chiave VERSIONATA: v2 quando sparirono due colonne (#52), v3 con la nuova
            // «Costo Ore Trasferta» (#92), v4 con «Ore cantiere» e i due previsti (#96),
            // v5 con «Giorni previsti» e la rinomina in «Ore Trasferta» (#98).
            // È la regola del progetto: colonne cambiate → chiave nuova, o chi ha già usato
            // il menù «Colonne» resta con la scelta vecchia.
            visibilityStorageKey="table-visibility-trasferta-summary-v6"
            toolbarActions={
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2">
                  <Switch
                    id="travel-closed"
                    checked={includeClosed}
                    onCheckedChange={setIncludeClosed}
                  />
                  <Label htmlFor="travel-closed" className="text-xs">
                    Mostra anche le completate
                  </Label>
                </div>
                {viewToggle}
              </div>
            }
          />
        ) : (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h1 className="text-lg font-semibold">Gestione Trasferta</h1>
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2">
                  <Switch
                    id="travel-closed-card"
                    checked={includeClosed}
                    onCheckedChange={setIncludeClosed}
                  />
                  <Label htmlFor="travel-closed-card" className="text-xs">
                    Mostra anche le completate
                  </Label>
                </div>
                {viewToggle}
                <Button
                  variant="outline"
                  size="sm"
                  disabled={summaryQuery.isFetching}
                  onClick={() => summaryQuery.refetch()}
                >
                  <RefreshCw className={summaryQuery.isFetching ? "animate-spin" : ""} />
                  Aggiorna
                </Button>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {visibili.map((p) => (
                <TravelProjectCard
                  key={p.projectId}
                  project={p}
                  onOpen={() => setSelected(p.projectId)}
                  onVerify={() => verifyMutation.mutate(p.projectId)}
                  verifying={verifyMutation.isPending}
                />
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

/** Card di commessa con i KPI: giorni, ore e costi di cantiere (#92) e spese trasferta,
 *  ognuno con sotto il suo previsto dal Bilancio (#96). */
function TravelProjectCard({
  project,
  onOpen,
  onVerify,
  verifying,
}: {
  project: TravelProjectSummaryDto
  onOpen: () => void
  onVerify: () => void
  verifying: boolean
}) {
  const daVerificare = project.needsVerification
  return (
    // #102: rossa finché il PM non dichiara di aver guardato lo scarico ore arrivato
    // dal Timesheet. Non è un errore: è «qui c'è del lavoro nuovo da controllare».
    <Card
      className={cn(
        "gap-3",
        daVerificare &&
          "border-red-300 bg-red-50 dark:border-red-900/60 dark:bg-red-950/40"
      )}
    >
      <CardHeader className="gap-1">
        <div className="flex items-start gap-2">
          <FolderOpen className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
          <div className="min-w-0 flex-1">
            <CardTitle className="truncate text-sm">{project.title}</CardTitle>
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              <Badge variant="outline" className="font-mono text-[10px]">
                {project.code}
              </Badge>
              {project.stepCount > 0 ? (
                <span className="text-[11px] text-muted-foreground">
                  {project.stepCount} step
                </span>
              ) : null}
            </div>
          </div>
        </div>
        <p className="truncate text-xs text-muted-foreground">
          {project.customerName || "—"}
          {project.pmName ? ` · PM ${project.pmName}` : ""}
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid grid-cols-2 gap-2 text-xs">
          {/* Ore e costo del personale non si imputano più nella trasferta (#52). La #92 ha
              aggiunto il «Costo Ore Trasferta» (ore di cantiere dal timesheet, informativo:
              non va al Bilancio) e rinominato «Costi trasferta» in «Spese Trasferta».
              La #96 ha messo il PREVISTO delle fasi cantiere sotto ogni KPI (stesse fonti
              del Bilancio); la #98 aggiunge i giorni previsti, ribattezza «Ore cantiere» in
              «Ore Trasferta» e FISSA L'ORDINE dei riquadri: giorni, ore, costo ore, spese. */}
          <Kpi
            label="Giorni trasferta"
            value={fmtHours(project.days)}
            sub={`previsti ${fmtHours(project.plannedDays)}`}
          />
          <Kpi
            label="Ore Trasferta"
            value={`${fmtHours(project.travelHours)} h`}
            sub={`su ${fmtHours(project.plannedHours)} h previste`}
          />
          <Kpi
            label="Costo Ore Trasferta"
            value={euro(project.travelHoursCost)}
            sub={`previsto ${euro(project.plannedHoursCost)}`}
          />
          <Kpi
            label="Spese Trasferta"
            value={euro(project.travelCost)}
            sub={`previsto ${euro(project.plannedTravelCost)}`}
          />
        </div>
        {daVerificare ? (
          <p className="rounded-md border border-red-300 bg-background/70 px-2 py-1.5 text-[11px] text-red-700 dark:border-red-900/60 dark:text-red-300">
            <span className="font-semibold">
              {project.pendingPeople === 1
                ? "1 persona ha scaricato ore"
                : `${project.pendingPeople} persone hanno scaricato ore`}
            </span>{" "}
            da verificare: {fmtHours(project.pendingHours)} h
            {project.pendingFrom
              ? ` · dal ${formatDateShort(project.pendingFrom)} al ${formatDateShort(
                  project.pendingTo ?? project.pendingFrom
                )}`
              : ""}
          </p>
        ) : project.verifiedAt ? (
          <p className="px-0.5 text-[11px] text-muted-foreground">
            Verificata il {formatDateShort(project.verifiedAt)}
            {project.verifiedByName ? ` da ${project.verifiedByName}` : ""}
          </p>
        ) : null}
        <Button variant="outline" size="sm" className="w-full" onClick={onOpen}>
          Apri
        </Button>
        {daVerificare ? (
          <Button
            variant="secondary"
            size="sm"
            className="w-full"
            disabled={verifying}
            onClick={onVerify}
          >
            <Check className="size-4" />
            Verifica effettuata
          </Button>
        ) : null}
      </CardContent>
    </Card>
  )
}

function Kpi({
  label,
  value,
  sub,
}: {
  label: string
  value: string
  /** Riga muted sotto il valore: il previsto dal Bilancio (#96). */
  sub?: string
}) {
  return (
    <div className="rounded-md border px-2 py-1.5">
      <p className="text-[10px] uppercase tracking-wide text-muted-foreground">
        {label}
      </p>
      <p className="font-medium tabular-nums">{value}</p>
      {sub ? (
        <p className="text-xs text-muted-foreground tabular-nums">{sub}</p>
      ) : null}
    </div>
  )
}

/** Dettaglio: N step collassabili + tabella «Riepilogo Trasferta». */
function TravelProjectDetail({
  project,
  projectId,
  onBack,
  onChanged,
}: {
  project?: TravelProjectSummaryDto
  projectId: number
  onBack: () => void
  onChanged: () => void
}) {
  const queryClient = useQueryClient()
  const [collapsed, setCollapsed] = React.useState<Set<number>>(new Set())
  const [selectedIds, setSelectedIds] = React.useState<Set<number>>(new Set())
  const [calc, setCalc] = React.useState<{
    rowId: number
    kind: TravelCalcKind
    personName: string
    days: number | null
  } | null>(null)

  const planQuery = useQuery({
    queryKey: ["travel-plan", projectId],
    queryFn: () => fetchTravelPlan(projectId),
  })

  const peopleQuery = useQuery({
    queryKey: ["travel-people"],
    queryFn: fetchTravelPeople,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["travel-plan", projectId] })
    // Il Bilancio legge la voce «Spese Trasferta»: se è aperto in un altro tab deve seguire.
    void queryClient.invalidateQueries({ queryKey: ["project-bva", projectId] })
    onChanged()
  }, [queryClient, projectId, onChanged])

  // Griglia editabile a più mani: chi la guarda vede arrivare le modifiche degli altri.
  // La guardia sul fuoco dentro la riga impedisce che il refetch cancelli quello che
  // l'utente sta scrivendo (trappola n.1 del blocco 4).
  useTravelHub(true, invalidate, projectId)

  const addStepMutation = useMutation({
    mutationFn: () => createTravelStep(projectId, { description: "", rowVersion: null }),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })

  // Rete di sicurezza della derivazione (#37/#52): le righe di cantiere nascono da sole a
  // ogni ora salvata, ma se cambia il tag «da cliente» di una sezione di costo — che sta
  // fuori dalla commessa — le commesse già aperte non se ne accorgono finché qualcuno non
  // ritocca un'imputazione. Da qui si riallinea in un clic.
  const rebuildMutation = useMutation({
    mutationFn: () => rebuildTravelFromTimesheet(projectId),
    onSuccess: (messaggio) => {
      invalidate()
      notifyInfo(messaggio)
    },
    onError: (err: Error) => notifyError(err),
  })

  const plan = planQuery.data

  // Cambio commessa: la selezione delle righe non ha senso fuori dal contesto.
  React.useEffect(() => {
    setSelectedIds(new Set())
  }, [projectId])

  function toggle(stepId: number) {
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(stepId)) next.delete(stepId)
      else next.add(stepId)
      return next
    })
  }

  function toggleSelect(rowId: number, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (checked) next.add(rowId)
      else next.delete(rowId)
      return next
    })
  }

  function toggleSelectAll(rowIds: number[], checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      for (const id of rowIds) {
        if (checked) next.add(id)
        else next.delete(id)
      }
      return next
    })
  }

  if (planQuery.isLoading) return <Skeleton className="h-60 w-full" />
  if (planQuery.isError) {
    return (
      <p className="text-sm text-destructive">
        {(planQuery.error as Error).message || "Errore nel caricamento della trasferta."}
      </p>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/30 px-3 py-2">
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" onClick={onBack}>
            ← Tutte le commesse
          </Button>
          {project ? (
            <span className="text-sm">
              <span className="font-mono text-xs">{project.code}</span>
              <span className="ml-2 font-medium">{project.title}</span>
            </span>
          ) : null}
        </div>
        <div className="flex items-center gap-2">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="sm"
                disabled={rebuildMutation.isPending}
                onClick={() => rebuildMutation.mutate()}
              >
                <RefreshCw
                  className={cn("size-3.5 mr-1", rebuildMutation.isPending && "animate-spin")}
                />
                Aggiorna dal Timesheet
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              Rilegge le ore di cantiere e rifà le righe derivate. Le righe scritte a mano
              non si toccano.
            </TooltipContent>
          </Tooltip>
          <Button
            size="sm"
            disabled={addStepMutation.isPending}
            onClick={() => addStepMutation.mutate()}
          >
            <Plus className="size-3.5 mr-1" />
            Step trasferta
          </Button>
        </div>
      </div>

      {plan && plan.steps.length === 0 ? (
        <Empty className="p-8">
          <EmptyHeader>
            <EmptyTitle>Nessuno step di trasferta</EmptyTitle>
            <EmptyDescription>
              Aggiungi il primo step: ogni step è un periodo di trasferta con le sue persone.
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : null}

      {plan && plan.steps.length > 0 ? (
        <TravelAggregateBar
          projectId={projectId}
          selectedIds={[...selectedIds]}
          onDeselect={() => setSelectedIds(new Set())}
          onApplied={() => {
            setSelectedIds(new Set())
            invalidate()
          }}
        />
      ) : null}

      {plan?.steps.map((step, index) => (
        <TravelStepCard
          key={step.id}
          projectId={projectId}
          step={step}
          index={index}
          open={!collapsed.has(step.id)}
          onToggle={() => toggle(step.id)}
          people={peopleQuery.data ?? []}
          allIds={plan.steps.map((s) => s.id)}
          onChanged={invalidate}
          onOpenCalc={(rowId, kind) => {
            const row = step.rows.find((r) => r.id === rowId)
            setCalc({
              rowId,
              kind,
              personName: row?.personName ?? "",
              days: row?.days ?? null,
            })
          }}
          selectedIds={selectedIds}
          onToggleSelect={toggleSelect}
          onToggleSelectAll={toggleSelectAll}
        />
      ))}

      {plan && plan.steps.length > 0 ? (
        <TravelSummaryTable plan={plan} />
      ) : null}

      {calc ? (
        <TravelCalcDialog
          projectId={projectId}
          rowId={calc.rowId}
          personName={calc.personName}
          kind={calc.kind}
          expectedDays={calc.days}
          onClose={() => setCalc(null)}
          onSaved={() => {
            setCalc(null)
            invalidate()
          }}
        />
      ) : null}
    </div>
  )
}

function TravelStepCard({
  projectId,
  step,
  index,
  open,
  onToggle,
  people,
  allIds,
  onChanged,
  onOpenCalc,
  selectedIds,
  onToggleSelect,
  onToggleSelectAll,
}: {
  projectId: number
  step: TravelStepDto
  index: number
  open: boolean
  onToggle: () => void
  people: Parameters<typeof TravelStepTable>[0]["people"]
  allIds: number[]
  onChanged: () => void
  onOpenCalc: (rowId: number, kind: TravelCalcKind) => void
  selectedIds: Set<number>
  onToggleSelect: (rowId: number, checked: boolean) => void
  onToggleSelectAll: (rowIds: number[], checked: boolean) => void
}) {
  const confirm = useConfirm()
  const [description, setDescription] = React.useState(step.description)
  const boxRef = React.useRef<HTMLInputElement>(null)

  /**
   * #103 — evidenza delle righe da compilare, CONGELATA.
   *
   * L'insieme si ricalcola solo quando si preme «Lista aggiornata» (o quando si
   * cambia step): se seguisse i dati in tempo reale, la riga tornerebbe bianca al
   * primo importo digitato e il PM perderebbe il segno di dove stava lavorando.
   * Stesso mestiere di `useDeferredItemOrder`, che congela l'ordine per lo stesso
   * motivo.
   */
  const [epoca, setEpoca] = React.useState(0)
  const idsDaCompilare = React.useMemo(() => {
    void epoca
    return new Set(step.rows.filter(rigaDaCompilare).map((r) => r.id))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [epoca, step.id])
  const quanteDaCompilare = idsDaCompilare.size

  /** Step nato da una fase di cantiere (#37/#52): titolo e vita li decide il Timesheet. */
  const isFromPhase = step.projectPhaseId != null

  /**
   * Uno step derivato si può cancellare solo quando è rimasto senza ore vere dietro: o è
   * vuoto, o tutte le sue righe sono segnalate «senza ore» (la fase non è più di cantiere,
   * oppure le ore sono state cancellate). Finché le ore ci sono rinascerebbe subito.
   * Senza questa via d'uscita uno step nato per sbaglio restava a video per sempre: il
   * pulsante era disabilitato su tutti gli step derivati, e la rigenerazione non cancella
   * mai niente di proposito.
   */
  const canDeleteStep = !isFromPhase || step.rows.every((r) => r.hoursMissing)

  React.useEffect(() => {
    if (document.activeElement === boxRef.current) return
    setDescription(step.description)
  }, [step.description, step.rowVersion])

  const saveMutation = useMutation({
    mutationFn: (value: string) =>
      updateTravelStep(projectId, step.id, {
        description: value,
        rowVersion: step.rowVersion,
      }),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteTravelStep(projectId, step.id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const reorderMutation = useMutation({
    mutationFn: (ids: number[]) => reorderTravelSteps(projectId, ids),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  async function handleDelete() {
    const label = step.description.trim() || `Step trasferta ${index + 1}`
    const ok = await confirm({
      title: "Elimina step di trasferta",
      description: `Eliminare «${label}» e tutte le sue righe-persona?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate()
  }

  function move(delta: number) {
    const ids = [...allIds]
    const from = ids.indexOf(step.id)
    const to = from + delta
    if (from < 0 || to < 0 || to >= ids.length) return
    ids.splice(to, 0, ids.splice(from, 1)[0])
    reorderMutation.mutate(ids)
  }

  return (
    <div className="rounded-lg border">
      <div className="flex flex-wrap items-center gap-2 border-b bg-muted/40 px-3 py-2">
        <button
          type="button"
          className="flex items-center gap-1.5 text-left"
          onClick={onToggle}
          aria-label={open ? "Comprimi lo step" : "Espandi lo step"}
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
              open && "rotate-90"
            )}
          />
          <span className="text-sm font-semibold whitespace-nowrap">
            Step {index + 1}
          </span>
        </button>

        {isFromPhase ? (
          // Step nato dal Timesheet (#37/#52): il titolo È la fase e la segue se viene
          // rinominata in commessa. Riscriverlo qui durerebbe fino al prossimo salvataggio
          // di ore, quindi si legge e basta.
          <div className="flex min-w-52 flex-1 items-center gap-2">
            <Tooltip>
              <TooltipTrigger asChild>
                <Badge
                  variant="outline"
                  className="border-blue-500/40 text-blue-600 whitespace-nowrap"
                >
                  FASE
                </Badge>
              </TooltipTrigger>
              <TooltipContent>
                Step creato dalle ore imputate su questa fase di cantiere: il titolo segue
                il nome della fase
              </TooltipContent>
            </Tooltip>
            <span className="truncate text-sm font-medium">{step.description}</span>
          </div>
        ) : (
          <Input
            ref={boxRef}
            value={description}
            placeholder="Descrizione dell'attività della trasferta"
            className="h-8 min-w-52 flex-1 bg-background"
            onChange={(e) => setDescription(e.target.value)}
            onBlur={() => {
              if (description.trim() === step.description.trim()) return
              saveMutation.mutate(description.trim())
            }}
            onKeyDown={(e) => {
              if (e.key === "Enter") e.currentTarget.blur()
            }}
          />
        )}

        {/* #103 — «Lista aggiornata», al centro fra il titolo dello step e la
            finestra dei totali. Ricalcola quali righe sono ancora senza costi:
            quelle compilate nel frattempo perdono il rosso e il grassetto. */}
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              className="h-7 gap-1.5 text-xs"
              onClick={() => setEpoca((e) => e + 1)}
            >
              <RefreshCw className="size-3.5" />
              Lista aggiornata
              {quanteDaCompilare > 0 ? (
                <Badge
                  variant="outline"
                  className="border-red-400/50 px-1 py-0 text-[10px] font-semibold text-red-600 dark:text-red-400"
                >
                  {quanteDaCompilare}
                </Badge>
              ) : null}
            </Button>
          </TooltipTrigger>
          <TooltipContent>
            {quanteDaCompilare > 0
              ? `${quanteDaCompilare} righe senza nessun costo compilato, in rosso. Premi dopo aver compilato: le righe a posto tornano bianche.`
              : "Nessuna riga senza costi. Premi per ricontrollare dopo aver modificato la tabella."}
          </TooltipContent>
        </Tooltip>

        <div className="flex flex-wrap items-center gap-1.5 text-xs">
          {/* Segnalazione #38: quale dei tre finisce nel Bilancio non era scritto da nessuna
              parte, e leggendo «Totale costi step» ci si aspettava di ritrovarlo lì.
              Nel Bilancio va SOLO la metà «trasferta»: le ore del personale ci arrivano già
              dal Timesheet, sotto «Risorse Atec», e contarle anche qui le raddoppierebbe.
              Confermato da Paolo il 06/08/2026. */}
          {/* Dalla #52 il costo del personale non si imputa più qui: resta solo sulle righe
              vecchie, e il riquadro si mostra solo se ha davvero un valore — altrimenti
              sarebbero due riquadri identici e uno a zero. */}
          {step.totals.personnelCost !== 0 ? (
            <StepBadge
              label="Totale costi personale"
              value={step.totals.personnelCost}
              note="righe vecchie — dal Timesheet, non entra nel Bilancio"
            />
          ) : null}
          <StepBadge
            label="Totale costi trasferta"
            value={step.totals.travelCost}
            note="→ voce «Spese Trasferta / indennità» del Bilancio"
          />
          {step.totals.personnelCost !== 0 ? (
            <StepBadge label="Totale costi step" value={step.totals.totalCost} strong />
          ) : null}
        </div>

        <div className="flex items-center gap-0.5">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Sposta su"
                disabled={index === 0 || reorderMutation.isPending}
                onClick={() => move(-1)}
              >
                ↑
              </Button>
            </TooltipTrigger>
            <TooltipContent>Sposta su</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Sposta giù"
                disabled={index === allIds.length - 1 || reorderMutation.isPending}
                onClick={() => move(1)}
              >
                ↓
              </Button>
            </TooltipTrigger>
            <TooltipContent>Sposta giù</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Elimina step"
                  // Uno step nato da una fase rinascerebbe al primo salvataggio di ore:
                  // per farlo sparire si tolgono le ore, o si spegne la fase.
                  disabled={deleteMutation.isPending || !canDeleteStep}
                  onClick={() => void handleDelete()}
                >
                  <Trash2 className="text-destructive" />
                </Button>
              </span>
            </TooltipTrigger>
            <TooltipContent>
              {canDeleteStep
                ? isFromPhase
                  ? "Step senza più ore dietro: si può togliere"
                  : "Elimina step"
                : "Step creato dal Timesheet: si toglie togliendo le ore su quella fase"}
            </TooltipContent>
          </Tooltip>
        </div>
      </div>

      <Collapsible open={open}>
        <div className="p-3">
          <TravelStepTable
            projectId={projectId}
            step={step}
            people={people}
            onChanged={onChanged}
            onOpenCalc={onOpenCalc}
            selectedIds={selectedIds}
            onToggleSelect={onToggleSelect}
            onToggleSelectAll={onToggleSelectAll}
            idsDaCompilare={idsDaCompilare}
          />
        </div>
      </Collapsible>
    </div>
  )
}

function StepBadge({
  label,
  value,
  strong,
  note,
}: {
  label: string
  value: number
  strong?: boolean
  /** Riga piccola sotto l'importo: dove va a finire (o non finire) questo numero. */
  note?: string
}) {
  return (
    <span
      className={cn(
        "inline-flex flex-col rounded-md border px-2 py-0.5 tabular-nums",
        strong ? "border-primary/40 bg-primary/5 font-semibold" : "bg-background"
      )}
      title={note ? `${label} — ${note}` : label}
    >
      <span>
        <span className="text-[10px] font-normal text-muted-foreground">{label}</span>{" "}
        {euro(value)}
      </span>
      {note ? (
        <span className="text-[9px] font-normal leading-tight text-muted-foreground">
          {note}
        </span>
      ) : null}
    </span>
  )
}

/**
 * «Riepilogo Trasferta»: stessa struttura a 14 colonne, un rigo per nominativo distinto
 * più il «Totale Riepilogo». Nel prototipo le celle dei nominativi restano vuote; qui i
 * totali per persona ci sono, e mostrarli è più utile che lasciare l'elenco muto.
 */
function TravelSummaryTable({
  plan,
}: {
  plan: NonNullable<Awaited<ReturnType<typeof fetchTravelPlan>>>
}) {
  return (
    <div className="rounded-lg border">
      {/* #101 — la testata del Riepilogo aveva il solo titolo: mancava la finestra
          dei totali che ogni step ha già. Stessi `StepBadge`, stesse regole (il
          costo del personale si mostra solo se c'è davvero, vedi #52). */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/40 px-3 py-2">
        <span className="text-sm font-semibold">Riepilogo Trasferta</span>
        <div className="flex flex-wrap items-center gap-1.5 text-xs">
          {plan.grandTotals.personnelCost !== 0 ? (
            <StepBadge
              label="Totale costi personale"
              value={plan.grandTotals.personnelCost}
              note="righe vecchie — dal Timesheet, non entra nel Bilancio"
            />
          ) : null}
          <StepBadge
            label="Totale costi trasferta"
            value={plan.grandTotals.travelCost}
            note="→ voce «Spese Trasferta / indennità» del Bilancio"
          />
          {plan.grandTotals.personnelCost !== 0 ? (
            <StepBadge
              label="Totale costi commessa"
              value={plan.grandTotals.totalCost}
              strong
            />
          ) : null}
        </div>
      </div>
      <div className="p-3">
        <GridScroller className="rounded-lg border">
          <Table className="w-max min-w-full">
            <TravelTableHead />
            <TableBody>
              {plan.summary.length === 0 ? (
                <TableRow className="hover:bg-transparent">
                  <TableCell colSpan={14} className="py-4 text-center text-sm text-muted-foreground">
                    Nessun nominativo negli step.
                  </TableCell>
                </TableRow>
              ) : (
                plan.summary.map((person) => (
                  // Fondo pieno: le tre celle congelate (#101) lo ereditano e
                  // devono coprire le colonne che scorrono loro sotto.
                  <TableRow key={person.personName} className="bg-background">
                    <TableCell className={stickyBody[0]} />
                    <TableCell className={stickyBody[1]} />
                    <TableCell className={cn(stickyBody[2], "font-medium")}>
                      {person.personName}
                    </TableCell>
                    <TableCell />
                    <TableCell />
                    <TableCell className="text-center tabular-nums">
                      {fmtHours(person.totals.days)}
                    </TableCell>
                    {/* Ore e Costi Personale non esistono più come colonne (#52): questa
                        tabella riusa la stessa intestazione delle griglie degli step e
                        deve avere esattamente lo stesso numero di celle. */}
                    <TableCell />
                    <TableCell />
                    <TableCell className="text-right tabular-nums">
                      {euro(person.totals.lodgingCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(person.totals.mealCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(person.totals.allowanceCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(person.totals.carCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(person.totals.transportCost)}
                    </TableCell>
                    <TableCell />
                  </TableRow>
                ))
              )}
            </TableBody>
            <TableFooter>
              <TravelTotalsRow label="Totale Riepilogo" totals={plan.grandTotals} />
            </TableFooter>
          </Table>
        </GridScroller>
      </div>
    </div>
  )
}
