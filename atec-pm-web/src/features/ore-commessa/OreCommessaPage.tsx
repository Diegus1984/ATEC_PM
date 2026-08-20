// ── Pagina «Ore Commessa» + causale «Extra Lavoro» (segnalazioni #39, #109, #112) ──
//
// Pagina PM come Trasferta e Milestones: `PmSidebar` con le commesse a sinistra, le
// imputazioni di ore a destra. Il PM legge riga per riga chi ha scaricato ore, su quale
// fase e quanto costano, e può spostarne una o più su «Extra Lavoro»: da quel momento
// quelle ore NON pesano più sui costi della commessa.
//
// Nella tabella Extra Lavoro ogni riga ha l'interruttore «conta nella commessa»: serve a
// misurare quanto peserebbe caricare quelle ore, senza doverle togliere e rimettere.
//
// Le ore restano quelle che la persona ha scritto: qui non si riscrive il timesheet di
// nessuno, si scrive solo la decisione di contabilità (tabella laterale lato server).
//
// #112: Struttura e logica analoghe a Trasferta:
// - Modalità vista Tabella / Card con persistenza per utente in localStorage
// - Tutte le commesse visibili (non solo quelle con ore > 0)
// - Commesse con ore da verificare evidenziate in rosso
// - Tabella filtrata con DataTableCardFiltered e colonne ordinate e configurabili

import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Check, Clock, FolderOpen, RefreshCw, Undo2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { GridScroller } from "@/components/shared/grid-scroller"
import { PmSidebar } from "@/components/shared/pm-sidebar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { Switch } from "@/components/ui/switch"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
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
  backToProjectHours,
  fetchProjectHours,
  fetchProjectHoursSummary,
  moveToExtraWork,
  setExtraWorkCounts,
  verifyProjectHours,
} from "@/lib/api/project-hours"
import type { ProjectHourRow, ProjectHoursSummary } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { formatDateOrDash, formatDateShort } from "@/lib/date-iso"
import { euro, fmtHours } from "@/lib/format"
import { buildPmProjectSections } from "@/lib/pm-project-sections"
import { useBudgetHub, useGlobalBudgetHub } from "@/lib/signalr/use-budget-hub"
import { notifyError, notifyInfo, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

const COLUMN_LABELS: Record<string, string> = {
  code: "Codice",
  title: "Commessa",
  customerName: "Cliente",
  pmName: "PM",
  totalHours: "Ore scaricate",
  totalCost: "Costo",
  peopleCount: "Persone",
  firstWorkDate: "Primo giorno",
  lastWorkDate: "Ultimo giorno",
  daVerificare: "Da verificare",
}

type ViewMode = "cards" | "table"

function viewModeStorageKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `ore-commessa.list.viewMode.${employeeId}`
}

function loadViewMode(): ViewMode {
  const stored = localStorage.getItem(viewModeStorageKey())
  return stored === "table" ? "table" : "cards"
}

function totals(rows: ProjectHourRow[]) {
  return rows.reduce(
    (acc, r) => ({ hours: acc.hours + r.hours, cost: acc.cost + r.cost }),
    { hours: 0, cost: 0 }
  )
}

export function OreCommessaPage() {
  const queryClient = useQueryClient()
  const [selected, setSelected] = React.useState<number | null>(null)
  const [includeClosed, setIncludeClosed] = React.useState(false)
  /** Vista rapida «Da verificare» (#109): filtro vero, non una scorciatoia a «Tutte». */
  const [onlyPending, setOnlyPending] = React.useState(false)
  const [viewMode, setViewModeState] = React.useState<ViewMode>(loadViewMode)

  const setViewMode = React.useCallback((mode: ViewMode) => {
    setViewModeState(mode)
    localStorage.setItem(viewModeStorageKey(), mode)
  }, [])

  // #109 / #112: un solo riepilogo alimenta tabella, card e barra laterale, come fa la Trasferta.
  const summaryQuery = useQuery({
    queryKey: ["ore-commessa", "summary", includeClosed],
    queryFn: () => fetchProjectHoursSummary(includeClosed),
  })
  const projects = React.useMemo(() => summaryQuery.data ?? [], [summaryQuery.data])
  const daVerificare = React.useMemo(
    () => projects.filter((p) => p.needsVerification),
    [projects]
  )
  const visibili = onlyPending ? daVerificare : projects

  const refresh = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["ore-commessa"] })
  }, [queryClient])
  // Ambiente condiviso: le ore che un collega scarica adesso accendono la card senza
  // ricaricare la pagina. `BudgetChanged` sul gruppo globale è l'evento che il server
  // emette già a ogni scrittura di ore.
  useGlobalBudgetHub(true, refresh)

  const verifyMutation = useMutation({
    mutationFn: (projectId: number) => verifyProjectHours(projectId),
    onSuccess: (message) => {
      notifySuccess(message)
      refresh()
      void queryClient.invalidateQueries({ queryKey: ["ore-commessa", "pending-count"] })
    },
    onError: (err: Error) => notifyError(err),
  })

  const columns = React.useMemo<ColumnDef<ProjectHoursSummary>[]>(
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
        accessorKey: "totalHours",
        header: COLUMN_LABELS.totalHours,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {fmtHours(row.original.totalHours)} h
          </span>
        ),
      },
      {
        accessorKey: "totalCost",
        header: COLUMN_LABELS.totalCost,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {euro(row.original.totalCost)}
          </span>
        ),
      },
      {
        accessorKey: "peopleCount",
        header: COLUMN_LABELS.peopleCount,
        cell: ({ row }) => (
          <span className="font-medium tabular-nums">
            {row.original.peopleCount}
          </span>
        ),
      },
      {
        accessorKey: "firstWorkDate",
        header: COLUMN_LABELS.firstWorkDate,
        cell: ({ row }) => (
          <span className="tabular-nums">
            {formatDateOrDash(row.original.firstWorkDate)}
          </span>
        ),
      },
      {
        accessorKey: "lastWorkDate",
        header: COLUMN_LABELS.lastWorkDate,
        cell: ({ row }) => (
          <span className="tabular-nums">
            {formatDateOrDash(row.original.lastWorkDate)}
          </span>
        ),
      },
      {
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
        storageKey="ore-commessa"
        quickViews={[
          {
            key: "all",
            selected: selected === null && !onlyPending,
            onClick: () => {
              setSelected(null)
              setOnlyPending(false)
            },
            icon: <FolderOpen className="size-4" />,
            label: "Tutte le commesse",
            count: projects.length,
          },
          {
            key: "pending",
            selected: selected === null && onlyPending,
            onClick: () => {
              setSelected(null)
              setOnlyPending(true)
            },
            icon: <Clock className="size-4" />,
            label: "Da verificare",
            count: daVerificare.length,
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
              // Il conteggio adesso è un dato vero e arriva dal riepilogo
              count: p.peopleCount,
              dots: p.needsVerification
                ? [{ dotClass: "bg-red-500", label: "Ore da verificare" }]
                : [],
            },
          })),
          { includeOther: true }
        )}
      />

      <div className="min-w-0 flex-1 space-y-4">
        {selected != null ? (
          <ProjectHours
            projectId={selected}
            project={projects.find((p) => p.projectId === selected)}
            onBack={() => setSelected(null)}
          />
        ) : summaryQuery.isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : visibili.length === 0 ? (
          <Empty className="p-8">
            <EmptyHeader>
              <EmptyTitle>
                {onlyPending
                  ? "Nessuna commessa da verificare"
                  : "Nessuna commessa"}
              </EmptyTitle>
              <EmptyDescription>
                {onlyPending
                  ? "Tutto lo scarico ore arrivato finora è stato guardato."
                  : "Le bozze e le commesse annullate restano sempre fuori."}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : viewMode === "table" ? (
          <DataTableCardFiltered
            title="Ore Commessa"
            description="Riepilogo ore scaricate per commessa."
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
            // #112: Chiave di visibilità colonne versionata per la vista tabella Ore Commessa
            visibilityStorageKey="table-visibility-ore-commessa-summary-v1"
            toolbarActions={
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2">
                  <Switch
                    id="ore-closed"
                    checked={includeClosed}
                    onCheckedChange={setIncludeClosed}
                  />
                  <Label htmlFor="ore-closed" className="text-xs">
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
              <div>
                <h2 className="text-lg font-semibold">Ore Commessa</h2>
                <p className="text-sm text-muted-foreground">
                  Le commesse con le relative imputazioni di ore. Apri per leggerle
                  riga per riga e spostarne una parte su «Extra Lavoro».
                </p>
              </div>
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2">
                  <Switch
                    id="ore-closed-card"
                    checked={includeClosed}
                    onCheckedChange={setIncludeClosed}
                  />
                  <Label htmlFor="ore-closed-card" className="text-xs">
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
                <OreCommessaCard
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

/**
 * Card di commessa (#109), sorella di quella della Trasferta: quante ore sono arrivate,
 * da quante persone, in che finestra di date, e se qualcuno le ha già guardate.
 * Rossa finché il PM non preme «Verifica effettuata».
 */
function OreCommessaCard({
  project,
  onOpen,
  onVerify,
  verifying,
}: {
  project: ProjectHoursSummary
  onOpen: () => void
  onVerify: () => void
  verifying: boolean
}) {
  const daVerificare = project.needsVerification
  const finestra =
    project.firstWorkDate && project.lastWorkDate
      ? `${formatDateShort(project.firstWorkDate)} – ${formatDateShort(project.lastWorkDate)}`
      : "—"
  return (
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
              {project.peopleCount > 0 ? (
                <span className="text-[11px] text-muted-foreground">
                  {project.peopleCount} {project.peopleCount === 1 ? "persona" : "persone"}
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
          <OreKpi
            label="Ore scaricate"
            value={`${fmtHours(project.totalHours)} h`}
            sub={euro(project.totalCost)}
          />
          <OreKpi
            label="Persone"
            value={String(project.peopleCount)}
            sub={project.peopleCount === 1 ? "collega" : "colleghi"}
          />
          <div className="col-span-2">
            <OreKpi label="Dal primo all’ultimo giorno" value={finestra} />
          </div>
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

function OreKpi({
  label,
  value,
  sub,
}: {
  label: string
  value: string
  sub?: string
}) {
  return (
    <div className="rounded-md border px-2 py-1.5">
      <p className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="font-medium tabular-nums">{value}</p>
      {sub ? <p className="text-xs text-muted-foreground tabular-nums">{sub}</p> : null}
    </div>
  )
}

function ProjectHours({
  projectId,
  project,
  onBack,
}: {
  projectId: number
  project?: ProjectHoursSummary
  onBack?: () => void
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [checked, setChecked] = React.useState<Set<number>>(new Set())

  const hoursQuery = useQuery({
    queryKey: ["project-hours", projectId],
    queryFn: () => fetchProjectHours(projectId),
    enabled: projectId > 0,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["project-hours", projectId] })
    // I costi della commessa cambiano: il Bilancio aperto in un'altra scheda deve seguire.
    void queryClient.invalidateQueries({ queryKey: ["budget-vs-actual", projectId] })
    void queryClient.invalidateQueries({ queryKey: ["bilancio-summary"] })
  }, [queryClient, projectId])

  // Ambiente condiviso: se un altro PM sposta ore (o qualcuno ne imputa di nuove) la
  // pagina si riallinea da sola. È lo stesso evento che muove il Bilancio.
  useBudgetHub(projectId > 0, invalidate, projectId)

  const rows = React.useMemo(() => hoursQuery.data ?? [], [hoursQuery.data])
  const normali = React.useMemo(() => rows.filter((r) => !r.isExtra), [rows])
  const extra = React.useMemo(() => rows.filter((r) => r.isExtra), [rows])

  const moveMutation = useMutation({
    mutationFn: (ids: number[]) => moveToExtraWork(projectId, ids),
    onSuccess: (n) => {
      setChecked(new Set())
      invalidate()
      notifyInfo(n === 1 ? "1 riga spostata su Extra Lavoro" : `${n} righe spostate su Extra Lavoro`)
    },
    onError: (err: Error) => notifyError(err),
  })

  const backMutation = useMutation({
    mutationFn: (ids: number[]) => backToProjectHours(projectId, ids),
    onSuccess: () => {
      invalidate()
      notifyInfo("Righe riportate nella commessa")
    },
    onError: (err: Error) => notifyError(err),
  })

  const countsMutation = useMutation({
    mutationFn: ({ entryId, counts }: { entryId: number; counts: boolean }) =>
      setExtraWorkCounts(projectId, entryId, counts),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })

  /**
   * Spostare ore su Extra Lavoro cambia il costo della commessa e la redditività, quindi
   * si chiede conferma dicendo di quante ore e quanti euro si tratta: da qui in poi quei
   * numeri non sono più quelli che il PM aveva visto un momento prima.
   */
  async function chiediESposta() {
    const scelte = rows.filter((r) => checked.has(r.entryId))
    const t = totals(scelte)
    const ok = await confirm({
      title: "Sposta su Extra Lavoro",
      description:
        `${scelte.length === 1 ? "1 riga" : `${scelte.length} righe`} — ` +
        `${fmtHours(t.hours)} h · ${euro(t.cost)}. ` +
        "Da questo momento queste ore non pesano più sui costi della commessa e la " +
        "redditività cambia. Si rimettono dentro quando vuoi dalla tabella Extra Lavoro.",
      confirmLabel: "Sposta",
      destructive: false,
    })
    if (ok) moveMutation.mutate([...checked])
  }

  function toggle(entryId: number) {
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(entryId)) next.delete(entryId)
      else next.add(entryId)
      return next
    })
  }

  if (hoursQuery.isLoading) return <Skeleton className="h-64 w-full" />

  const tNormali = totals(normali)
  // Nel totale della commessa entrano anche le righe di Extra Lavoro rimesse dentro.
  const tRimesse = totals(extra.filter((r) => r.countsInProject))
  const tExtra = totals(extra)

  return (
    <div className="space-y-4">
      {onBack ? (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/30 px-3 py-2">
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={onBack}>
              ← Tutte le commesse
            </Button>
            {project ? (
              <span className="text-sm">
                <span className="font-mono text-xs font-bold">{project.code}</span>
                <span className="ml-2 font-medium">{project.title}</span>
                {project.customerName ? (
                  <span className="text-muted-foreground"> · {project.customerName}</span>
                ) : null}
              </span>
            ) : null}
          </div>
        </div>
      ) : null}

      <Card>
        <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle className="text-base">Ore della commessa</CardTitle>
            <p className="text-xs text-muted-foreground">
              Ogni riga è una imputazione del Timesheet. Seleziona le righe da spostare
              sulla causale «Extra Lavoro»: da lì non peseranno più sui costi.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => void hoursQuery.refetch()}
              disabled={hoursQuery.isFetching}
            >
              <RefreshCw className={hoursQuery.isFetching ? "animate-spin" : ""} />
            </Button>
            <Button
              size="sm"
              disabled={checked.size === 0 || moveMutation.isPending}
              onClick={() => void chiediESposta()}
            >
              <Clock className="size-3.5" />
              Sposta su Extra Lavoro{checked.size > 0 ? ` (${checked.size})` : ""}
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <GridScroller className="rounded-lg border">
            <Table className="w-max min-w-full">
              <TableHeader className="bg-muted/40">
                <TableRow className="hover:bg-transparent">
                  <TableHead className="w-10" aria-label="Seleziona" />
                  <TableHead className="min-w-44 text-xs">Nominativo</TableHead>
                  <TableHead className="w-28 text-xs">Data</TableHead>
                  <TableHead className="min-w-48 text-xs">Fase</TableHead>
                  <TableHead className="min-w-44 text-xs">Sezione di costo</TableHead>
                  <TableHead className="w-24 text-right text-xs">Ore</TableHead>
                  <TableHead className="w-28 text-right text-xs">Costo orario</TableHead>
                  <TableHead className="w-28 text-right text-xs">Costo</TableHead>
                  <TableHead className="min-w-40 text-xs">Note</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {normali.length === 0 ? (
                  <TableRow className="hover:bg-transparent">
                    <TableCell colSpan={9} className="py-6 text-center text-sm text-muted-foreground">
                      Nessuna ora imputata su questa commessa.
                    </TableCell>
                  </TableRow>
                ) : (
                  normali.map((row) => (
                    <TableRow key={row.entryId}>
                      <TableCell>
                        <Checkbox
                          checked={checked.has(row.entryId)}
                          onCheckedChange={() => toggle(row.entryId)}
                          aria-label={`Seleziona le ore di ${row.employeeName}`}
                        />
                      </TableCell>
                      <TableCell className="text-sm font-medium">{row.employeeName}</TableCell>
                      <TableCell className="text-sm tabular-nums">
                        {formatDateOrDash(row.workDate)}
                      </TableCell>
                      <TableCell className="text-sm">{row.phaseName}</TableCell>
                      <TableCell className="text-sm">
                        <div className="flex items-center gap-1.5">
                          <span>{row.costSectionName || "—"}</span>
                          {row.costSectionType === "DA_CLIENTE" ? (
                            <Tooltip>
                              <TooltipTrigger asChild>
                                <Badge
                                  variant="outline"
                                  className="border-blue-500/40 text-blue-600"
                                >
                                  CANTIERE
                                </Badge>
                              </TooltipTrigger>
                              <TooltipContent>
                                Sezione «da cliente»: queste ore generano anche la riga di
                                trasferta
                              </TooltipContent>
                            </Tooltip>
                          ) : null}
                        </div>
                      </TableCell>
                      <TableCell className="text-right text-sm tabular-nums">
                        {fmtHours(row.hours)}
                      </TableCell>
                      <TableCell className="text-right text-sm tabular-nums">
                        {euro(row.hourlyCost)}
                      </TableCell>
                      <TableCell className="text-right text-sm tabular-nums">
                        {euro(row.cost)}
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {row.notes}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
              <TableFooter>
                <TableRow className="hover:bg-transparent">
                  <TableCell colSpan={5} className="text-xs font-semibold uppercase">
                    Totale nella commessa
                  </TableCell>
                  <TableCell className="text-right font-semibold tabular-nums">
                    {fmtHours(tNormali.hours + tRimesse.hours)}
                  </TableCell>
                  <TableCell />
                  <TableCell className="text-right font-semibold tabular-nums">
                    {euro(tNormali.cost + tRimesse.cost)}
                  </TableCell>
                  <TableCell />
                </TableRow>
              </TableFooter>
            </Table>
          </GridScroller>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            Extra Lavoro
            {extra.length > 0 ? (
              <span className="ml-2 text-xs font-normal text-muted-foreground">
                {fmtHours(tExtra.hours)} h · {euro(tExtra.cost)}
              </span>
            ) : null}
          </CardTitle>
          <p className="text-xs text-muted-foreground">
            Ore tolte dalla contabilità della commessa. L'interruttore le rimette dentro
            una per una: serve a vedere quanto peserebbe caricarle davvero.
          </p>
        </CardHeader>
        <CardContent>
          {extra.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Nessuna riga su Extra Lavoro.
            </p>
          ) : (
            <GridScroller className="rounded-lg border">
              <Table className="w-max min-w-full">
                <TableHeader className="bg-muted/40">
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="min-w-44 text-xs">Nominativo</TableHead>
                    <TableHead className="w-28 text-xs">Data</TableHead>
                    <TableHead className="min-w-48 text-xs">Fase</TableHead>
                    <TableHead className="w-24 text-right text-xs">Ore</TableHead>
                    <TableHead className="w-28 text-right text-xs">Costo</TableHead>
                    <TableHead className="w-44 text-center text-xs">
                      Conta nella commessa
                    </TableHead>
                    <TableHead className="w-36 text-xs">Spostata da</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {extra.map((row) => (
                    <TableRow key={row.entryId}>
                      <TableCell className="text-sm font-medium">{row.employeeName}</TableCell>
                      <TableCell className="text-sm tabular-nums">
                        {formatDateOrDash(row.workDate)}
                      </TableCell>
                      <TableCell className="text-sm">{row.phaseName}</TableCell>
                      <TableCell className="text-right text-sm tabular-nums">
                        {fmtHours(row.hours)}
                      </TableCell>
                      <TableCell className="text-right text-sm tabular-nums">
                        {euro(row.cost)}
                      </TableCell>
                      <TableCell className="text-center">
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span>
                              <Switch
                                checked={row.countsInProject}
                                disabled={countsMutation.isPending}
                                onCheckedChange={(next) =>
                                  countsMutation.mutate({
                                    entryId: row.entryId,
                                    counts: next,
                                  })
                                }
                              />
                            </span>
                          </TooltipTrigger>
                          <TooltipContent>
                            {row.countsInProject
                              ? "Queste ore stanno pesando sui costi della commessa"
                              : "Queste ore NON pesano sui costi della commessa"}
                          </TooltipContent>
                        </Tooltip>
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">
                        {row.movedByName || "—"}
                      </TableCell>
                      <TableCell>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label="Riporta nella commessa"
                              disabled={backMutation.isPending}
                              onClick={() => backMutation.mutate([row.entryId])}
                            >
                              <Undo2 />
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>
                            Toglie la riga da Extra Lavoro e la rimette fra le ore normali
                          </TooltipContent>
                        </Tooltip>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
                <TableFooter>
                  <TableRow className="hover:bg-transparent">
                    <TableCell colSpan={3} className="text-xs font-semibold uppercase">
                      Totale Extra Lavoro
                    </TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">
                      {fmtHours(tExtra.hours)}
                    </TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">
                      {euro(tExtra.cost)}
                    </TableCell>
                    <TableCell colSpan={3} className="text-xs text-muted-foreground">
                      di cui rimesse nella commessa: {fmtHours(tRimesse.hours)} h ·{" "}
                      {euro(tRimesse.cost)}
                    </TableCell>
                  </TableRow>
                </TableFooter>
              </Table>
            </GridScroller>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
