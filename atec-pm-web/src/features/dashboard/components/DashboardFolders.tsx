// ── Dashboard a cartelle (blocco 7) ────────────────────────────────────────
//
// La pagina d'ingresso del prototipo: una cartella per commessa, con le tre
// statistiche (milestone, avanzamento medio, periodo) già a video — nella scheda
// Milestones compaiono solo espandendo, qui no: è il senso della pagina.
//
// La spunta «In dashboard» è CONDIVISA (colonna su `projects`): chi toglie una
// commessa la toglie a tutti, quindi la scrittura vuole la chiave
// `action.toggle_dashboard_folder` e le escluse restano recuperabili dalla fascia
// di chip in fondo.

import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Folder, Hash, ReceiptText, RefreshCw } from "lucide-react"
import { Link, useNavigate } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Skeleton } from "@/components/ui/skeleton"
import { Switch } from "@/components/ui/switch"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { projectStatusMeta } from "@/features/commesse/project-status"
import {
  fetchDashboardFolders,
  saveDashboardSettings,
  setProjectInDashboard,
} from "@/lib/api/dashboard"
import { fetchSalSummary } from "@/lib/api/sal"
import type { DashboardFolder } from "@/lib/api/types"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

const FOLDERS_QUERY_KEY = "dashboard-folders"

export function DashboardFolders() {
  const queryClient = useQueryClient()
  // La spunta «In dashboard» è CONDIVISA (vale per tutti), il numero massimo di cartelle è un
  // parametro di sistema: due permessi distinti, non due gradini della stessa scala.
  const canToggle = canWriteFeature("action.toggle_dashboard_folder")
  const canEditSettings = canWriteFeature("action.edit_dashboard_settings")

  const [includeClosed, setIncludeClosed] = React.useState(false)

  const foldersQuery = useQuery({
    queryKey: [FOLDERS_QUERY_KEY, includeClosed],
    queryFn: () => fetchDashboardFolders(includeClosed),
  })

  // Ambiente condiviso: la spunta di un collega (o una commessa nuova) si vede subito.
  // L'hub commesse è già montato una volta nella shell (`useProjectsRealtime`) e questa
  // chiave è nel suo elenco: qui basta il refetch locale dopo le proprie scritture.
  const refetchAll = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: [FOLDERS_QUERY_KEY] })
  }, [queryClient])

  const toggleMutation = useMutation({
    mutationFn: ({ projectId, value }: { projectId: number; value: boolean }) =>
      setProjectInDashboard(projectId, value),
    onSuccess: refetchAll,
    onError: (err: Error) => notifyError(err),
  })

  const data = foldersQuery.data
  const shown = data?.projects ?? []
  const hidden = data?.hidden ?? []
  const maxCards = data?.maxCards ?? 10
  // Il limite taglia le cartelle a video, non l'elenco: le commesse oltre il taglio
  // non finiscono fra le escluse, restano semplicemente fuori schermo finché non si
  // libera un posto (è la regola del prototipo, con la stessa nota esplicativa).
  const visible = shown.slice(0, maxCards)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold tracking-tight">
            Commesse in dashboard
          </h2>
          <p className="text-sm text-muted-foreground">
            Una cartella per commessa: apri la scheda con un clic, togli la spunta
            per liberare spazio.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <SalShortcut />

          <div className="flex items-center gap-2">
            <Switch
              id="dashboard-closed"
              checked={includeClosed}
              onCheckedChange={setIncludeClosed}
            />
            <Label htmlFor="dashboard-closed" className="text-xs font-normal">
              Mostra anche le completate
            </Label>
          </div>

          {canEditSettings ? <MaxCardsEditor maxCards={maxCards} /> : null}

          <Button
            variant="outline"
            size="sm"
            className="h-9"
            onClick={() => void foldersQuery.refetch()}
            disabled={foldersQuery.isFetching}
          >
            <RefreshCw className={foldersQuery.isFetching ? "animate-spin" : ""} />
            Aggiorna
          </Button>
        </div>
      </div>

      {foldersQuery.isError ? (
        <PageErrorAlert
          message={
            (foldersQuery.error as Error).message ||
            "Errore nel caricamento delle commesse."
          }
        />
      ) : foldersQuery.isLoading ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 3 }).map((_, index) => (
            <Skeleton key={index} className="h-44 rounded-xl" />
          ))}
        </div>
      ) : visible.length === 0 ? (
        <div className="rounded-lg border border-dashed bg-muted/10 py-16 text-center">
          <p className="text-sm font-medium text-muted-foreground">
            {shown.length === 0 && hidden.length === 0
              ? "Nessuna commessa aperta in anagrafica."
              : "Nessuna commessa in dashboard: rimettine una dalla fascia qui sotto."}
          </p>
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {visible.map((project) => (
            <ProjectFolderCard
              key={project.projectId}
              project={project}
              canToggle={canToggle}
              busy={toggleMutation.isPending}
              onToggle={(value) =>
                toggleMutation.mutate({ projectId: project.projectId, value })
              }
            />
          ))}
        </div>
      )}

      {shown.length > maxCards ? (
        <p className="rounded-lg border border-dashed bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
          {maxCards === 1
            ? `Visualizzata la prima commessa di ${shown.length}.`
            : `Visualizzate le prime ${maxCards} commesse di ${shown.length}.`}{" "}
          Togli la spunta a una cartella per liberare spazio e mostrarne un'altra.
        </p>
      ) : null}

      {hidden.length > 0 ? (
        <div className="space-y-2">
          <h3 className="text-sm font-semibold">
            Commesse non in dashboard{" "}
            <span className="font-normal text-muted-foreground">
              ({hidden.length})
            </span>
          </h3>
          <div className="flex flex-wrap gap-2">
            {hidden.map((project) => (
              <label
                key={project.projectId}
                className={cn(
                  "flex items-center gap-2 rounded-full border bg-card px-3 py-1.5 text-xs",
                  canToggle ? "cursor-pointer hover:bg-muted/50" : "opacity-70"
                )}
                title={
                  canToggle
                    ? "Rimetti la commessa in dashboard"
                    : "Non hai il permesso di cambiare le commesse in dashboard"
                }
              >
                <Checkbox
                  checked={false}
                  disabled={!canToggle || toggleMutation.isPending}
                  onCheckedChange={() =>
                    toggleMutation.mutate({
                      projectId: project.projectId,
                      value: true,
                    })
                  }
                />
                <span className="font-mono font-medium">{project.code}</span>
                {project.title ? (
                  <span className="text-muted-foreground">— {project.title}</span>
                ) : null}
              </label>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}

/**
 * Cartella «Pagamenti SAL» che si colora sulle scadenze, come nel prototipo:
 * rossa se c'è almeno una scadenza raggiunta o un incasso scaduto, gialla se ci
 * sono solo pre-warning. Il conteggio è la somma delle righe che chiedono
 * attenzione, cioè quello che si trova aprendo /sal.
 */
function SalShortcut() {
  const canSeeSal = canAccessFeature("nav.sal")

  const summaryQuery = useQuery({
    queryKey: ["sal-summary"],
    queryFn: fetchSalSummary,
    enabled: canSeeSal,
  })

  if (!canSeeSal) return null

  const rows = summaryQuery.data ?? []
  const warn = rows.reduce((acc, row) => acc + row.warn + row.incasso, 0)
  const pre = rows.reduce((acc, row) => acc + row.pre, 0)
  const total = warn + pre

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          asChild
          variant="outline"
          size="sm"
          className={cn(
            "h-9",
            warn > 0 &&
              "border-red-300 bg-red-50 text-red-800 hover:bg-red-100 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200",
            warn === 0 &&
              pre > 0 &&
              "border-amber-300 bg-amber-50 text-amber-900 hover:bg-amber-100 dark:border-amber-900/60 dark:bg-amber-950/40 dark:text-amber-200"
          )}
        >
          <Link to="/sal">
            <ReceiptText />
            Pagamenti SAL
            {total > 0 ? (
              <Badge
                variant="secondary"
                className="ml-1 h-5 min-w-5 justify-center px-1 tabular-nums"
              >
                {total}
              </Badge>
            ) : null}
          </Link>
        </Button>
      </TooltipTrigger>
      <TooltipContent>
        {warn > 0
          ? `${warn} fra scadenze raggiunte e incassi scaduti${pre > 0 ? `, ${pre} imminenti` : ""}`
          : pre > 0
            ? `${pre} scadenze di fatturazione imminenti`
            : "Nessuna scadenza di fatturazione da segnalare"}
      </TooltipContent>
    </Tooltip>
  )
}

/** Numero massimo di cartelle a video: il DASH_MAX cablato del prototipo, qui governabile. */
function MaxCardsEditor({ maxCards }: { maxCards: number }) {
  const queryClient = useQueryClient()
  const [text, setText] = React.useState(String(maxCards))

  React.useEffect(() => {
    setText(String(maxCards))
  }, [maxCards])

  const saveMutation = useMutation({
    mutationFn: (value: number) => saveDashboardSettings({ maxCards: value }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [FOLDERS_QUERY_KEY] })
    },
    onError: (err: Error) => notifyError(err),
  })

  function commit() {
    const parsed = Number(text.trim())
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > 100) {
      setText(String(maxCards))
      return
    }
    if (parsed === maxCards || saveMutation.isPending) return
    saveMutation.mutate(parsed)
  }

  return (
    <div className="flex items-center gap-1.5">
      <Hash className="size-3.5 text-muted-foreground" />
      <Label htmlFor="dashboard-max-cards" className="text-xs font-normal">
        Cartelle
      </Label>
      <Input
        id="dashboard-max-cards"
        value={text}
        inputMode="numeric"
        disabled={saveMutation.isPending}
        title="Numero massimo di cartelle mostrate in dashboard (1–100)"
        className="h-9 w-16 text-right font-mono tabular-nums"
        onChange={(event) => setText(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === "Enter") event.currentTarget.blur()
        }}
      />
    </div>
  )
}

function Stat({
  label,
  children,
}: {
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="rounded-md border px-2 py-1.5">
      <p className="text-[10px] uppercase tracking-wide text-muted-foreground">
        {label}
      </p>
      <div className="text-sm font-medium tabular-nums">{children}</div>
    </div>
  )
}

function ProjectFolderCard({
  project,
  canToggle,
  busy,
  onToggle,
}: {
  project: DashboardFolder
  canToggle: boolean
  busy: boolean
  onToggle: (value: boolean) => void
}) {
  const navigate = useNavigate()
  const status = projectStatusMeta(project.status)
  const period = project.periodStart
    ? `${formatDateShort(project.periodStart)} → ${formatDateShort(project.periodEnd)}`
    : "—"

  return (
    <Card
      className="group relative cursor-pointer gap-3 transition-colors hover:border-primary/40 hover:bg-muted/30"
      onClick={() => navigate(`/commesse/${project.projectId}`)}
    >
      <CardHeader className="gap-1">
        <div className="flex items-start gap-2">
          <Folder className="mt-0.5 size-4 shrink-0 text-muted-foreground group-hover:text-primary" />
          <div className="min-w-0 flex-1">
            <CardTitle className="truncate text-sm">
              {/* Link stirato: tutta la cartella è cliccabile ma resta raggiungibile da tastiera. */}
              <Link
                to={`/commesse/${project.projectId}`}
                className="after:absolute after:inset-0 after:rounded-xl focus-visible:outline-none focus-visible:after:ring-2 focus-visible:after:ring-ring"
                onClick={(event) => event.stopPropagation()}
              >
                {project.title || project.code}
              </Link>
            </CardTitle>
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              <Badge variant="outline" className="font-mono text-[10px]">
                {project.code}
              </Badge>
              {project.status !== "ACTIVE" ? (
                <Badge
                  variant="outline"
                  className={cn("gap-1 text-[10px]", status.className, status.borderClassName)}
                >
                  <status.icon className="size-3" />
                  {status.label}
                </Badge>
              ) : null}
            </div>
          </div>

          {/* Sopra il link stirato (z-10) e fuori dal clic della cartella. */}
          <label
            className={cn(
              "relative z-10 flex shrink-0 items-center gap-1.5 text-[11px]",
              canToggle ? "cursor-pointer" : "cursor-default opacity-70"
            )}
            title={
              canToggle
                ? "Togli la spunta per rimuovere la commessa dalla dashboard (vale per tutti)"
                : "Non hai il permesso di cambiare le commesse in dashboard"
            }
            onClick={(event) => event.stopPropagation()}
          >
            <Checkbox
              checked
              disabled={!canToggle || busy}
              onCheckedChange={() => onToggle(false)}
            />
            In dashboard
          </label>
        </div>

        <p className="truncate text-xs text-muted-foreground">
          {project.customerName || "—"}
          {project.pmName ? ` · PM ${project.pmName}` : ""}
        </p>
      </CardHeader>

      <CardContent>
        <div className="grid grid-cols-2 gap-2">
          <Stat label="Milestones">{project.milestoneCount}</Stat>
          <Stat label="Avanzamento medio">
            <span className="flex items-center gap-2">
              <span
                className={cn(project.avgProgress === 100 && "text-teal-600")}
              >
                {project.avgProgress == null ? "—" : `${project.avgProgress}%`}
              </span>
              {project.avgProgress != null ? (
                <span className="h-1.5 w-12 overflow-hidden rounded bg-muted">
                  <span
                    className={cn(
                      "block h-full",
                      project.avgProgress >= 100 ? "bg-teal-500" : "bg-primary"
                    )}
                    style={{ width: `${project.avgProgress}%` }}
                  />
                </span>
              ) : null}
            </span>
          </Stat>
          <div className="col-span-2">
            <Stat label="Periodo">{period}</Stat>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
