// ── Bilancio Commessa (cross-commessa) ─────────────────────────────────────
// Una card per commessa con i due KPI di redditività a consuntivo e l'ingresso al
// conto economico completo. Stessa struttura di /sal e /milestones: PmSidebar a
// sinistra, elenco card a destra, contenuto della card caricato solo se aperta.
//
// Regola cromatica presa dal prototipo V32: la card diventa rossa quando la
// percentuale di redditività è SOTTO la soglia (confronto stretto) OPPURE l'importo
// è negativo, e il rosso si applica a ENTRAMBI i riquadri, non solo a quello fuori
// soglia. La soglia è parametrica (default 20%) e la modifica chi ha la chiave
// `action.edit_bilancio_settings`.

import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  CircleCheck,
  ExternalLink,
  Folder,
  FolderOpen,
  Percent,
  RefreshCw,
  Scale,
} from "lucide-react"
import { Link } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import {
  PmSidebar,
  type PmContainer,
  type PmQuickView,
  type PmSidebarSection,
} from "@/components/shared/pm-sidebar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Skeleton } from "@/components/ui/skeleton"
import { projectStatusMeta } from "@/features/commesse/project-status"
import { ProjectBudgetVsActual } from "@/features/commesse/bva/ProjectBudgetVsActual"
import { fetchBilancioSummary, saveBilancioSettings } from "@/lib/api/bilancio"
import type { BilancioSummary } from "@/lib/api/types"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { euro, percent } from "@/lib/format"
import { buildPmProjectSections } from "@/lib/pm-project-sections"
import { useGlobalBudgetHub } from "@/lib/signalr/use-budget-hub"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

const SUMMARY_QUERY_KEY = "bilancio-summary"

/** Schede lasciate aperte (per utente/PC), come in /sal. */
const EXPANDED_STORAGE_KEY = "bilancio:expanded-projects:v1"

/** Sotto soglia (stretto) o importo negativo: la card va in rosso. */
function isLow(row: BilancioSummary, thresholdPct: number): boolean {
  if (row.profitabilityPct != null && row.profitabilityPct < thresholdPct) return true
  return row.profitability != null && row.profitability < 0
}

export function BilancioPage() {
  const queryClient = useQueryClient()
  // La soglia di redditività è un parametro condiviso: la tocca solo chi ha la chiave.
  const canEditThreshold = canWriteFeature("action.edit_bilancio_settings")

  // #97: le COMPLETED entrano nel payload solo con la vista «Commesse chiuse» attiva —
  // caricamento pigro voluto, ogni chiusa costa 4 sottoquery al server.
  const [closedView, setClosedView] = React.useState(false)
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)

  const [expandedProjects, setExpandedProjects] = React.useState<Record<number, boolean>>(
    () => {
      try {
        const raw = localStorage.getItem(EXPANDED_STORAGE_KEY)
        if (raw) return JSON.parse(raw) as Record<number, boolean>
      } catch {
        // ignore
      }
      return {}
    }
  )

  const toggleExpanded = React.useCallback((projectId: number) => {
    setExpandedProjects((prev) => {
      const next = { ...prev, [projectId]: !prev[projectId] }
      const trimmed = Object.fromEntries(
        Object.entries(next).filter(([, open]) => open)
      )
      try {
        localStorage.setItem(EXPANDED_STORAGE_KEY, JSON.stringify(trimmed))
      } catch {
        // ignore
      }
      return next
    })
  }, [])

  const summaryQuery = useQuery({
    queryKey: [SUMMARY_QUERY_KEY, closedView],
    queryFn: () => fetchBilancioSummary(closedView),
  })

  const refetchAll = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: [SUMMARY_QUERY_KEY] })
  }, [queryClient])

  useGlobalBudgetHub(true, refetchAll)

  const rows = React.useMemo(
    () => summaryQuery.data?.projects ?? [],
    [summaryQuery.data]
  )
  const thresholdPct = summaryQuery.data?.thresholdPct ?? 20

  // #97: aperte e chiuse tenute separate. A `closedView` spento il payload non dovrebbe
  // contenere COMPLETED, ma il filtro client resta per sicurezza.
  const openRows = React.useMemo(
    () => rows.filter((r) => r.status !== "COMPLETED"),
    [rows]
  )
  const closedRows = React.useMemo(
    () => rows.filter((r) => r.status === "COMPLETED"),
    [rows]
  )

  const quickViews: PmQuickView[] = [
    {
      key: "all",
      selected: !closedView && selectedProjectId === null,
      onClick: () => {
        setClosedView(false)
        setSelectedProjectId(null)
      },
      icon: <Scale />,
      label: "Tutte le commesse",
      count: openRows.length,
    },
    {
      key: "chiuse",
      selected: closedView && selectedProjectId === null,
      onClick: () => {
        setClosedView(true)
        setSelectedProjectId(null)
      },
      icon: <CircleCheck />,
      label: "Commesse chiuse",
      // Caricamento pigro: prima del primo ingresso nella vista il numero non si sa —
      // niente badge, uno «0» direbbe il falso.
      count: closedView && summaryQuery.data ? closedRows.length : undefined,
    },
  ]

  const sections = React.useMemo(() => {
    const built = buildPmProjectSections(
      openRows.map((r) => ({
        code: r.code,
        container: {
          key: `p${r.projectId}`,
          selected: selectedProjectId === r.projectId,
          onClick: () => setSelectedProjectId(r.projectId),
          label: r.title ? `${r.code} — ${r.title}` : r.code,
          count: 0,
          dots: isLow(r, thresholdPct)
            ? [{ dotClass: "bg-red-500", label: "Redditività sotto soglia" }]
            : [],
        } satisfies PmContainer,
      }))
    )
    // #97: terza sezione con le chiuse, visibile solo quando la vista le ha caricate.
    // L'ordine è quello del server (data poi codice); il collasso lo dà già PmSidebar.
    if (closedRows.length > 0) {
      built.push({
        key: "chiuse",
        label: "Commesse chiuse",
        containers: closedRows.map(
          (r) =>
            ({
              key: `p${r.projectId}`,
              selected: selectedProjectId === r.projectId,
              onClick: () => setSelectedProjectId(r.projectId),
              label: r.title ? `${r.code} — ${r.title}` : r.code,
              count: 0,
              dots: isLow(r, thresholdPct)
                ? [{ dotClass: "bg-red-500", label: "Redditività sotto soglia" }]
                : [],
            }) satisfies PmContainer
        ),
        emptyLabel: "Nessuna commessa chiusa",
      } satisfies PmSidebarSection)
    }
    return built
  }, [openRows, closedRows, selectedProjectId, thresholdPct])

  const visibleRows = React.useMemo(() => {
    if (selectedProjectId !== null) {
      return rows.filter((r) => r.projectId === selectedProjectId)
    }
    return closedView ? closedRows : openRows
  }, [rows, openRows, closedRows, closedView, selectedProjectId])

  // Selezionare una commessa dalla sidebar apre la sua scheda.
  React.useEffect(() => {
    if (selectedProjectId !== null) {
      setExpandedProjects((prev) => ({ ...prev, [selectedProjectId]: true }))
    }
  }, [selectedProjectId])

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">Bilancio Commessa</h1>
        <p className="text-sm text-muted-foreground">
          Redditività a consuntivo di tutte le commesse: Totale Ordine meno i costi
          realmente sostenuti.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar storageKey="bilancio" quickViews={quickViews} sections={sections} />

        <main className="flex min-w-0 flex-1 flex-col gap-4 p-4 min-h-0">
          <header className="flex shrink-0 flex-row flex-wrap items-center justify-end gap-3">
            {canEditThreshold ? <ThresholdEditor thresholdPct={thresholdPct} /> : null}

            <Button
              variant="outline"
              size="sm"
              className="h-9"
              onClick={() => void summaryQuery.refetch()}
              disabled={summaryQuery.isFetching}
            >
              <RefreshCw className={summaryQuery.isFetching ? "animate-spin" : ""} />
              Aggiorna
            </Button>
          </header>

          <section className="flex-1 space-y-3 overflow-y-auto pr-1">
            {summaryQuery.isError ? (
              <PageErrorAlert
                message={
                  (summaryQuery.error as Error).message ||
                  "Errore nel caricamento del bilancio."
                }
              />
            ) : summaryQuery.isLoading ? (
              <div className="space-y-3">
                <Skeleton className="h-24 w-full" />
                <Skeleton className="h-24 w-full" />
              </div>
            ) : visibleRows.length === 0 ? (
              <div className="rounded-lg border border-dashed bg-muted/10 py-16 text-center">
                <p className="text-sm font-medium text-muted-foreground">
                  {closedView
                    ? "Nessuna commessa chiusa."
                    : "Nessuna commessa da mostrare."}
                </p>
              </div>
            ) : (
              visibleRows.map((row) => (
                <ProjectBilancioCard
                  key={row.projectId}
                  row={row}
                  thresholdPct={thresholdPct}
                  expanded={!!expandedProjects[row.projectId]}
                  onToggleExpanded={() => toggleExpanded(row.projectId)}
                />
              ))
            )}
          </section>
        </main>
      </div>
    </div>
  )
}

/** Campo soglia in testata, visibile solo a chi può scrivere `action.edit_bilancio_settings`. */
function ThresholdEditor({ thresholdPct }: { thresholdPct: number }) {
  const queryClient = useQueryClient()
  const [text, setText] = React.useState(String(thresholdPct))

  React.useEffect(() => {
    setText(String(thresholdPct))
  }, [thresholdPct])

  const saveMutation = useMutation({
    mutationFn: (value: number) => saveBilancioSettings({ thresholdPct: value }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [SUMMARY_QUERY_KEY] })
    },
    onError: (err: Error) => notifyError(err),
  })

  function commit() {
    const parsed = Number(text.replace(",", "."))
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) {
      setText(String(thresholdPct))
      return
    }
    if (parsed === thresholdPct || saveMutation.isPending) return
    saveMutation.mutate(parsed)
  }

  return (
    <div className="flex items-center gap-1.5">
      <Percent className="size-3.5 text-muted-foreground" />
      <Label htmlFor="bilancio-threshold" className="text-xs font-normal">
        Soglia
      </Label>
      <Input
        id="bilancio-threshold"
        value={text}
        inputMode="decimal"
        disabled={saveMutation.isPending}
        className="h-9 w-16 text-right font-mono tabular-nums"
        onChange={(e) => setText(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
        }}
      />
    </div>
  )
}

/** I due riquadri KPI della card: verdi, oppure rossi entrambi se fuori soglia. */
function KpiBox({
  label,
  value,
  low,
  negative,
}: {
  label: string
  value: string
  low: boolean
  negative: boolean
}) {
  return (
    <div
      className={cn(
        "rounded-lg border px-3 py-2",
        low
          ? "border-red-200 bg-red-50 dark:border-red-900/60 dark:bg-red-950/40"
          : "border-emerald-200 bg-emerald-50 dark:border-emerald-900/60 dark:bg-emerald-950/40"
      )}
    >
      <div
        className={cn(
          "text-[10px] font-semibold uppercase tracking-wide",
          low
            ? "text-red-700 dark:text-red-300"
            : "text-emerald-700 dark:text-emerald-300"
        )}
      >
        {label}
      </div>
      <div
        className={cn(
          "text-lg font-bold tabular-nums",
          low
            ? "text-red-700 dark:text-red-300"
            : "text-emerald-700 dark:text-emerald-300",
          negative && "text-[#B23A4B] dark:text-red-400"
        )}
      >
        {value}
      </div>
    </div>
  )
}

function ProjectBilancioCard({
  row,
  thresholdPct,
  expanded,
  onToggleExpanded,
}: {
  row: BilancioSummary
  thresholdPct: number
  expanded: boolean
  onToggleExpanded: () => void
}) {
  const low = isLow(row, thresholdPct)
  const status = projectStatusMeta(row.status)

  return (
    <Card className="overflow-hidden py-0">
      <CardHeader
        onClick={onToggleExpanded}
        className={cn(
          // `!` in coda (Tailwind v4): batte la variante di serie di CardHeader
          // `[.border-b]:pb-(--card-spacing)`, che a card espansa darebbe 24px sotto e 12 sopra.
          "flex cursor-pointer select-none flex-row items-center justify-between bg-muted/20 px-4 py-3 transition-colors hover:bg-muted/40 [.border-b]:pb-3!",
          expanded && "border-b"
        )}
      >
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded border bg-muted px-1.5 py-0.5 font-mono text-xs font-bold">
              {row.code}
            </span>
            <CardTitle className="truncate text-sm font-semibold">
              {row.title}
            </CardTitle>
            {/* #97: badge di stato standard (icona + etichetta), sempre visibile —
                via il tag grezzo che stampava il codice ON_HOLD com'era sul DB. */}
            <Badge
              variant="outline"
              className={cn("gap-1 text-[10px]", status.className, status.borderClassName)}
            >
              <status.icon className="size-3" />
              {status.label}
            </Badge>
          </div>
          <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-0.5 text-xs text-muted-foreground">
            <span>
              Cliente: <strong className="text-foreground">{row.customerName}</strong>
            </span>
            <span>
              PM: <strong className="text-foreground">{row.pmName}</strong>
            </span>
            <span>
              Ordine: <strong className="text-foreground">{euro(row.orderTotal)}</strong>
            </span>
            <span>
              Costi: <strong className="text-foreground">{euro(row.actualTotalCost)}</strong>
            </span>
          </div>
        </div>

        <div
          className="ml-4 flex shrink-0 items-center gap-3"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="hidden gap-2 sm:grid sm:grid-cols-2">
            <KpiBox
              label="Consuntivo Redditività"
              value={euro(row.profitability)}
              low={low}
              negative={row.profitability != null && row.profitability < 0}
            />
            <KpiBox
              label="Consuntivo % Redditività"
              value={percent(row.profitabilityPct)}
              low={low}
              negative={row.profitabilityPct != null && row.profitabilityPct < 0}
            />
          </div>

          {canAccessFeature("nav.commesse") ? (
            <Button asChild variant="outline" size="sm" className="h-8 gap-1">
              <Link
                to={`/commesse/${row.projectId}/budget_vs_actual`}
                state={{ fromGlobal: "/bilancio" }}
                title="Apri il bilancio economico"
              >
                <ExternalLink className="size-3.5" />
                Apri
              </Link>
            </Button>
          ) : null}

          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={onToggleExpanded}
            aria-label={expanded ? "Chiudi la scheda" : "Apri il bilancio economico"}
          >
            {expanded ? (
              <FolderOpen className="size-4 text-muted-foreground" />
            ) : (
              <Folder className="size-4 text-muted-foreground" />
            )}
          </Button>
        </div>
      </CardHeader>

      {expanded && (
        <CardContent className="bg-zinc-50/50 p-4 dark:bg-zinc-900/30">
          <ProjectBudgetVsActual projectId={row.projectId} />
        </CardContent>
      )}
    </Card>
  )
}
